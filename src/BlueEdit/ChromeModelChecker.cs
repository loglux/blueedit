using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Chrome's proofreader model without Chrome (#104): the "Tinymodel Proofreader" Chrome downloads (117 MB, a
/// LiteRT-LM file, English only) run by Google's LiteRT-LM (the `litert-lm` package from PyPI, Apache-2.0) — no
/// browser, no flags, no experiment configuration. The model comes from the user's own Chrome profile (it is Google's,
/// shipped with Chrome: BlueEdit never ships it). A small script (tools/chrome-model/proofread.py, built into the
/// editor and written next to the virtual environment) loads it once and answers lines of JSON on stdin; the prompt is
/// the bare text and the model's turn mark, the answer picked among the likeliest with a fixed seed, as measured (the
/// script's notes, docs/tech/browser-ai-research.md).
/// The model answers with the text corrected; what changed is found by comparing, as for Edge and Chrome. Like Chrome
/// it drops a final full stop, which is put back.
/// </summary>
sealed class ChromeModelChecker : IGrammarChecker
{
    public string Name => "Chrome model";
    const int MaxParagraph = 3000;

    readonly string _home;
    readonly ConcurrentDictionary<string, IReadOnlyList<GrammarIssue>> _done = new();
    readonly ConcurrentDictionary<string, byte> _queued = new();
    readonly BlockingCollection<string> _work = new();
    readonly Thread? _thread;
    Process? _runner;
    IntPtr _job;
    int _version;
    volatile string? _status;
    volatile bool _broken, _disposed;

    public static string DefaultHome => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit", "litert");

    /// <summary>The Python of the virtual environment install.ps1 -ChromeModel makes in <paramref name="home"/>, when
    /// it has litert-lm; null otherwise.</summary>
    public static string? PythonIn(string home)
    {
        var (python, tool) = OperatingSystem.IsWindows()
            ? (Path.Combine(home, "venv", "Scripts", "python.exe"), Path.Combine(home, "venv", "Scripts", "litert-lm.exe"))
            : (Path.Combine(home, "venv", "bin", "python"), Path.Combine(home, "venv", "bin", "litert-lm"));
        return File.Exists(python) && File.Exists(tool) ? python : null;
    }

    /// <summary>The newest proofreader model in the user's Chrome profile, or null when Chrome has not downloaded one
    /// (its flags off, or the proofreader never used).</summary>
    public static string? ModelInChrome()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var userData in new[]
                 {
                     Path.Combine(local, "Google", "Chrome", "User Data"),
                     Path.Combine(home, ".config", "google-chrome"),
                     Path.Combine(home, "Library", "Application Support", "Google", "Chrome"),
                 })
        {
            var dir = Path.Combine(userData, "OptGuideManifestModel");
            if (!Directory.Exists(dir)) continue;
            var found = Directory.EnumerateFiles(dir, "manifest.json", SearchOption.AllDirectories)
                .Where(m => File.ReadAllText(m).Contains("Proofreader", StringComparison.Ordinal))
                .Select(m => Path.Combine(Path.GetDirectoryName(m)!, "weights.bin"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (found != null) return found;
        }
        return null;
    }

    public ChromeModelChecker(string? home = null)
    {
        _home = home ?? DefaultHome;
        if (PythonIn(_home) == null) { _status = "LiteRT-LM not installed (tools\\proofing-setup\\install.ps1 -ChromeModel)"; return; }
        if (ModelInChrome() == null) { _status = "no proofreader model in Chrome: turn its flags on and use it once (docs/proofing-setup.md)"; return; }
        _status = "starting";
        _thread = new Thread(Loop) { IsBackground = true, Name = "Chrome model" };
        _thread.Start();
    }

    public bool Available => _thread != null && !_broken;
    public string? Status => _status;
    public string? LastFailure { get; private set; }
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


    void Loop()
    {
        try { Start(); _status = null; }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException or JsonException)
        {
            if (_disposed) return;
            _status = "Chrome model did not start: " + ex.Message;
            _broken = true;
            Interlocked.Increment(ref _version);
            return;
        }
        Interlocked.Increment(ref _version);
        foreach (var text in _work.GetConsumingEnumerable())
        {
            try
            {
                var corrected = BrowserProofreader.KeepFinalStop(text, Ask(text));
                var answer = new JsonObject { ["corrected"] = corrected, ["errors"] = new JsonArray() }.ToJsonString();
                _done[text] = LlmGrammarChecker.IssuesFrom(text, answer, "en", Name);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                _done[text] = Array.Empty<GrammarIssue>();
                LastFailure = "Chrome model: " + ex.Message;
            }
            _queued.TryRemove(text, out _);
            Interlocked.Increment(ref _version);
        }
    }

    void Start()
    {
        Directory.CreateDirectory(_home);
        var script = Path.Combine(_home, "proofread.py");
        using (var res = typeof(ChromeModelChecker).Assembly.GetManifestResourceStream("proofread.py")
                         ?? throw new InvalidOperationException("the proofread.py script is not built in"))
        using (var file = File.Create(script)) res.CopyTo(file);
        var psi = new ProcessStartInfo(PythonIn(_home)!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
        };
        // a copy named .litertlm: LiteRT-LM goes by the extension and would not open Chrome's weights.bin; copied again
        // when Chrome has a newer model (another size or a later date)
        var source = ModelInChrome() ?? throw new InvalidOperationException("no proofreader model in Chrome");
        var model = Path.Combine(_home, "chrome-proofreader.litertlm");
        if (!File.Exists(model) || new FileInfo(model).Length != new FileInfo(source).Length || File.GetLastWriteTimeUtc(model) < File.GetLastWriteTimeUtc(source))
            File.Copy(source, model, overwrite: true);
        foreach (var a in new[] { "-u", script, model })
            psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        _runner = Process.Start(psi) ?? throw new InvalidOperationException("Python did not start");
        _runner.ErrorDataReceived += (_, _) => { };                // LiteRT's own log
        _runner.BeginErrorReadLine();
        _job = JobObject.KillOnClose(_runner);
        var ready = Read(TimeSpan.FromSeconds(60)) ?? throw new InvalidOperationException("the model did not load in 60 s");
        if (ready["ready"] is null) throw new InvalidOperationException((string?)ready["error"] ?? "the model did not load");
    }

    string Ask(string text)
    {
        _runner!.StandardInput.WriteLine(new JsonObject { ["text"] = text }.ToJsonString());
        _runner.StandardInput.Flush();
        var answer = Read(TimeSpan.FromSeconds(60)) ?? throw new InvalidOperationException("no answer in 60 s");
        if ((string?)answer["error"] is { } error) throw new InvalidOperationException(error);
        return (string?)answer["corrected"] ?? text;
    }

    // one line of JSON from the script, or null when it did not come in time (or the script ended)
    JsonNode? Read(TimeSpan wait)
    {
        var line = _runner!.StandardOutput.ReadLineAsync();
        if (!line.Wait(wait) || line.Result is null)
        {
            if (_runner.HasExited) throw new InvalidOperationException($"the script ended (code {_runner.ExitCode})");
            return null;
        }
        return JsonNode.Parse(line.Result);
    }

    public void Dispose()
    {
        _disposed = true;
        _work.CompleteAdding();
        try { if (_runner is { HasExited: false }) _runner.Kill(true); } catch (InvalidOperationException) { }
        _runner?.Dispose();
        if (_job != IntPtr.Zero) { JobObject.Close(_job); _job = IntPtr.Zero; }
    }
}
