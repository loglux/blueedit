using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Proofreading of English by Edge's built-in proofreader (the on-device Aion model behind the Proofreader web API).
/// The API is not on by default: a hidden Edge (Canary by default) is started with the switches that turn it on,
/// with a profile of its own (%LOCALAPPDATA%\BlueEdit\edge-profile, where Edge keeps the downloaded model, ~1.2 GB) and a
/// local blank page, and driven over the DevTools protocol. Edge returns the corrected text; what changed is found by
/// comparing the texts, as for the language model. Other languages are left alone (Edge has English only). An
/// unofficial way in: an Edge update may drop it, and then this checker reports that it is unavailable.
/// </summary>
sealed class BrowserProofreader : IGrammarChecker
{
    public string Name { get; } = "Edge";

    readonly string? _exe;
    readonly string _profile;
    // Chrome: copy into the profile what turns the proofreader on there (see Chrome's constructor)
    readonly Action<string>? _prepare;
    readonly ConcurrentDictionary<string, IReadOnlyList<GrammarIssue>> _done = new();
    readonly ConcurrentDictionary<string, byte> _queued = new();
    readonly BlockingCollection<string> _work = new();
    readonly Thread? _thread;
    readonly HttpClient _http = Net.Client(TimeSpan.FromSeconds(10));
    readonly CancellationTokenSource _stop = new();
    ClientWebSocket? _ws;
    Process? _edge;
    IntPtr _job;
    int _version, _id;
    volatile string? _status;

    const int MaxParagraph = 3000;

    public static string DefaultProfile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit", "edge-profile");

    /// <summary>The msedge.exe to use: the one in the settings, otherwise Edge Canary, otherwise Edge; null when none.</summary>
    public static string? EdgeIn(string? wanted)
    {
        if (!string.IsNullOrWhiteSpace(wanted)) return File.Exists(wanted) ? wanted : null;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return new[]
        {
            Path.Combine(local, "Microsoft", "Edge SxS", "Application", "msedge.exe"),
            Path.Combine(x86, "Microsoft", "Edge", "Application", "msedge.exe"),
        }.FirstOrDefault(File.Exists);
    }

    public BrowserProofreader(string? edgePath, string? profile = null) : this("Edge", EdgeIn(edgePath), profile ?? DefaultProfile, null) { }

    BrowserProofreader(string name, string? exe, string profile, Action<string>? prepare)
    {
        Name = name;
        _exe = exe;
        _profile = profile;
        _prepare = prepare;
        if (_exe == null) { _status = name == "Edge" ? "Microsoft Edge not found" : "Google Chrome not found"; return; }
        _status = "starting";
        _thread = new Thread(Loop) { IsBackground = true, Name = name + " proofreader" };
        _thread.Start();
    }

    /// <summary>Chrome's model drops the full stop at the end of a paragraph (20 of its 26 false alarms on the British
    /// academic set, #42; seen in the research too): a paragraph that ended with one keeps it.</summary>
    internal static string KeepFinalStop(string text, string corrected)
    {
        var t = text.TrimEnd();
        var c = corrected.TrimEnd();
        if (t.EndsWith('.') && c.Length > 0 && !".!?…".Contains(c[^1], StringComparison.Ordinal))
            return c + "." + corrected[c.Length..];
        return corrected;
    }

    // ---------- Chrome (#42) ----------
    public static string ChromeProfile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit", "chrome-profile");

    static string ChromeUserData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "Google", "Chrome", "User Data");

    /// <summary>The chrome.exe to use: the one given, otherwise the installed Chrome; null when none.</summary>
    public static string? ChromeIn(string? wanted)
    {
        if (!string.IsNullOrWhiteSpace(wanted)) return File.Exists(wanted) ? wanted : null;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"),
        }.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Chrome's proofreader (a "tiny model", 117 MB, English only). Chrome 154 turns it on only with its flags *and* the
    /// experiment configuration Google sends some users (the variations seed): a fresh profile gets neither. So, as
    /// measured in docs/tech/browser-ai-research.md, the user's own Chrome must have it working (the flags set there),
    /// and before every start the `variations_*` keys and the flag list of its Local State are copied into BlueEdit's
    /// profile — nothing else: no history, cookies or passwords.
    /// </summary>
    public static BrowserProofreader Chrome(string? chromePath, string? profile = null) =>
        new("Chrome", ChromeIn(chromePath), profile ?? ChromeProfile, CopyChromeSwitches);

    static void CopyChromeSwitches(string profile)
    {
        var from = Path.Combine(ChromeUserData, "Local State");
        if (!File.Exists(from)) throw new InvalidOperationException("no Chrome profile to take its settings from (set Chrome's flags first: docs/proofing-setup.md)");
        // Chrome may hold its Local State open: read it shared
        string json;
        using (var fs = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(fs)) json = reader.ReadToEnd();
        var user = JsonNode.Parse(json)!.AsObject();
        var target = Path.Combine(profile, "Local State");
        var mine = File.Exists(target) && JsonNode.Parse(File.ReadAllText(target)) is JsonObject o ? o : new JsonObject();
        foreach (var (key, value) in user)
            if (key.StartsWith("variations", StringComparison.Ordinal)) mine[key] = value?.DeepClone();
        if (user["browser"]?["enabled_labs_experiments"] is JsonArray flags)
        {
            if (mine["browser"] is not JsonObject browser) mine["browser"] = browser = new JsonObject();
            browser["enabled_labs_experiments"] = flags.DeepClone();
        }
        Directory.CreateDirectory(profile);
        File.WriteAllText(target, mine.ToJsonString());
    }

    // off when Edge could not start or its worker ended (a request that timed out); one paragraph's error is LastFailure
    public bool Available => _thread != null && !_broken;
    volatile bool _broken;
    public string? LastFailure { get; private set; }
    public string? Status => _status;
    public int Version => Volatile.Read(ref _version);

    public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true)
    {
        if (!Available || !language.StartsWith("en", StringComparison.OrdinalIgnoreCase) || paragraph.Trim().Length == 0 || paragraph.Length > MaxParagraph)
            return Array.Empty<GrammarIssue>();
        if (_done.TryGetValue(paragraph, out var issues))
            return issues;
        if (queue && _queued.TryAdd(paragraph, 0) && !_work.IsAddingCompleted) _work.Add(paragraph);
        return null;
    }


    // ---------- the worker thread ----------
    void Loop()
    {
        try { Work(); }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            if (_stop.IsCancellationRequested) return;                  // the editor is closing
            _status = Name + " proofreader: no answer (" + ex.Message + ")";      // a request timed out: the worker ends
            _broken = true;
            Interlocked.Increment(ref _version);
        }
    }

    void Work()
    {
        try { Start(); _status = null; }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException
                                   or HttpRequestException or WebSocketException or JsonException)
        {
            if (_stop.IsCancellationRequested) return;
            _status = Name + " proofreader did not start: " + ex.Message;
            _broken = true;
            Interlocked.Increment(ref _version);
            return;
        }
        Interlocked.Increment(ref _version);
        foreach (var text in _work.GetConsumingEnumerable())
        {
            try
            {
                var corrected = Evaluate("(async () => { for (let a = 0; ; a++) try { return (await window.__proofreader.proofread("
                    + JsonSerializer.Serialize(text) + ")).correctedInput; } catch (e) { if (a >= 2) throw e; } })()");
                corrected = KeepFinalStop(text, corrected);
                var answer = new JsonObject { ["corrected"] = corrected, ["errors"] = new JsonArray() }.ToJsonString();
                _done[text] = LlmGrammarChecker.IssuesFrom(text, answer, "en", Name);
            }
            catch (Exception ex) when (ex is InvalidOperationException or WebSocketException or JsonException)
            {
                _done[text] = Array.Empty<GrammarIssue>();
                LastFailure = Name + " proofreader: " + ex.Message;
            }
            _queued.TryRemove(text, out _);
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>The hidden browser's command line. Its output goes to pipes, not to the editor's terminal: Chrome logs to
    /// stderr ("ERROR:google_apis\gcm\…" where its push service cannot reach Google, "Created TensorFlow Lite XNNPACK
    /// delegate for CPU"), and with the terminal inherited those lines were drawn over the editor (seen on a work
    /// computer, 6 October 2026).</summary>
    internal static ProcessStartInfo StartInfo(string exe, string profile, string page)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[]
                 {
                     "--headless=new", "--remote-debugging-port=0", "--user-data-dir=" + profile, "--no-first-run",
                     "--no-default-browser-check", "--enable-features=AIProofreadingAPI", "--enable-blink-features=AIProofreaderAPI",
                     new Uri(page).AbsoluteUri,
                 })
            psi.ArgumentList.Add(a);
        return psi;
    }

    void Start()
    {
        Directory.CreateDirectory(_profile);
        _prepare?.Invoke(_profile);
        // a page of our own: the API needs a secure context, and a local file is one (no network needed)
        var page = Path.Combine(_profile, "blueedit.html");
        File.WriteAllText(page, "<!doctype html><title>BlueEdit</title>");
        var portFile = Path.Combine(_profile, "DevToolsActivePort");
        File.Delete(portFile);
        _edge = Process.Start(StartInfo(_exe!, _profile, page)) ?? throw new InvalidOperationException(Name + " did not start");
        // its log read and dropped: a full pipe would stop it
        _edge.OutputDataReceived += (_, _) => { };
        _edge.ErrorDataReceived += (_, _) => { };
        _edge.BeginOutputReadLine();
        _edge.BeginErrorReadLine();
        _job = JobObject.KillOnClose(_edge);

        // Edge writes the port it chose into the profile
        var sw = Stopwatch.StartNew();
        string? port = null;
        while (port == null)
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(30)) throw new InvalidOperationException(Name + " did not open its DevTools port");
            try { port = File.ReadLines(portFile).FirstOrDefault(); } catch (IOException) { }
            if (port == null) Thread.Sleep(200);
        }
        string? wsUrl = null;
        while (wsUrl == null)
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(30)) throw new InvalidOperationException(Name + " did not open the page");
            var list = JsonNode.Parse(_http.GetStringAsync(new Uri($"http://127.0.0.1:{port}/json")).GetAwaiter().GetResult())!.AsArray();
            wsUrl = list.Select(t => t!).FirstOrDefault(t => (string?)t["type"] == "page" && ((string?)t["url"] ?? "").EndsWith("blueedit.html", StringComparison.Ordinal))?["webSocketDebuggerUrl"]?.ToString();
            if (wsUrl == null) Thread.Sleep(200);
        }
        _ws = new ClientWebSocket();
        _ws.ConnectAsync(new Uri(wsUrl), _stop.Token).GetAwaiter().GetResult();

        // the page may still be loading when it is listed: wait for its document
        while (Evaluate("document.readyState + '|' + document.title") != "complete|BlueEdit")
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(30)) throw new InvalidOperationException(Name + " did not load the page");
            Thread.Sleep(100);
        }
        // stable Edge 154 has the object but never answers availability(): give it 30 s
        var availability = Evaluate("typeof Proofreader === 'undefined' ? 'missing' : Promise.race([Proofreader.availability({ expectedInputLanguages: ['en'] }), "
            + "new Promise(r => setTimeout(() => r('no answer'), 30000))])");
        if (availability == "missing") throw new InvalidOperationException(Name == "Edge" ? "this Edge has no proofreader (try Edge Canary)" : "Chrome's proofreader is not on: set its flags in Chrome (docs/proofing-setup.md)");
        if (availability == "no answer") throw new InvalidOperationException($"this {Name}'s proofreader does not answer" + (Name == "Edge" ? " (try Edge Canary)" : ""));
        if (availability == "unavailable") throw new InvalidOperationException(Name + " says its proofreader is unavailable on this computer");
        if (availability != "available") { _status = "downloading its model"; Interlocked.Increment(ref _version); }
        Evaluate("(async () => { window.__proofreader = await Proofreader.create({ expectedInputLanguages: ['en'] }); return 'ok'; })()");
    }

    // Runs JavaScript in the page and returns its value as a string; creating the proofreader needs a user gesture.
    string Evaluate(string expression)
    {
        int id = ++_id;
        var msg = new JsonObject
        {
            ["id"] = id, ["method"] = "Runtime.evaluate",
            ["params"] = new JsonObject { ["expression"] = expression, ["awaitPromise"] = true, ["returnByValue"] = true, ["userGesture"] = true },
        }.ToJsonString();
        _ws!.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, _stop.Token).GetAwaiter().GetResult();
        var buf = new byte[1 << 16];
        while (true)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                r = _ws.ReceiveAsync(buf, _stop.Token).GetAwaiter().GetResult();
                if (r.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException(Name + " closed the connection");
                ms.Write(buf, 0, r.Count);
            } while (!r.EndOfMessage);
            var reply = JsonNode.Parse(ms.ToArray())!;
            if ((int?)reply["id"] != id) continue;                  // events and other replies
            if (reply["error"] is { } err) throw new InvalidOperationException((string?)err["message"] ?? "DevTools error");
            var result = reply["result"]!;
            if (result["exceptionDetails"] is { } ex)
                throw new InvalidOperationException((string?)ex["exception"]?["description"] ?? (string?)ex["text"] ?? "script error");
            return result["result"]?["value"]?.ToString() ?? "";
        }
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _stop.Cancel();
        try { if (_edge is { HasExited: false }) _edge.Kill(true); } catch (InvalidOperationException) { }
        _edge?.Dispose();
        if (_job != IntPtr.Zero) { JobObject.Close(_job); _job = IntPtr.Zero; }
        _ws?.Dispose();
        _http.Dispose();
        _stop.Dispose();
    }
}
