using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// A model server at an address, which BlueEdit does not start (settings "llmServer", "llmServerModel", "llmApiKey"):
/// llama-server or LM Studio on another computer, Ollama, a cloud API — anything that speaks OpenAI's chat completions
/// (#95). <see cref="Url"/> is its base address as the user gave it (…/v1 or not).
/// </summary>
sealed record LlmEndpoint(string Url, string Model, string? Key)
{
    /// <summary>The key from the setting: as it is, "env:NAME" for an environment variable or "file:PATH" for a file; trimmed of spaces and of
    /// a byte order mark (Notepad writes one; then every request failed in the Obsidian plugin). Null when empty.</summary>
    public static string? KeyFrom(string setting, string? folder = null)
    {
        // "file:PATH": a file holding the key (~ for the home folder, a bare name in the settings folder), e.g.
        // file:~/keys/openai.txt, or llm-key that SaveKey writes
        string ReadFile(string path)
        {
            path = path.Trim();
            if (path.StartsWith('~')) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
            else if (!Path.IsPathRooted(path)) path = Path.Combine(folder ?? Settings.Directory, path);
            return KeyFile.Read(path);
        }
        var key = setting.StartsWith("env:", StringComparison.Ordinal) ? Environment.GetEnvironmentVariable(setting[4..].Trim()) ?? ""
            : setting.StartsWith("file:", StringComparison.Ordinal) ? ReadFile(setting[5..])
            : setting;
        key = key.Trim().Trim('﻿').Trim();
        // a file of several lines (two keys, a key and a note): the first is the key; a key has no line break
        key = key.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return key.Length == 0 ? null : key;
    }

    /// <summary>The file a key typed in the editor goes to, in the settings folder: settings.json then holds only
    /// "file:llm-key", so the key is not on screen when the settings are opened, nor in a copy of them someone shares,
    /// and on Windows a copy of the file is of no use elsewhere (docs/tech/proofing-internals.md).</summary>
    public const string KeyFileName = "llm-key";

    /// <summary>What "llmApiKey" is set to for what was typed: "env:NAME", "file:PATH" and nothing stay as they are;
    /// a key is written to <see cref="KeyFileName"/> (<see cref="KeyFile"/>: encrypted on Windows, mode 600 elsewhere) and named.</summary>
    public static string SaveKey(string typed, string? folder = null)
    {
        typed = typed.Trim();
        folder ??= Settings.Directory;
        var path = Path.Combine(folder, KeyFileName);
        if (typed.Length == 0 || typed.StartsWith("env:", StringComparison.Ordinal) || typed.StartsWith("file:", StringComparison.Ordinal))
        {
            // no key of ours any more: the old one is not left behind
            if (typed != "file:" + KeyFileName) File.Delete(path);
            return typed;
        }
        KeyFile.Write(path, typed);
        return "file:" + KeyFileName;
    }

    string Base => Url.Trim().TrimEnd('/');
    public string ChatUrl => Base.EndsWith("/chat/completions", StringComparison.Ordinal) ? Base
        // …/v1, …/v1beta, and Google's OpenAI form under /v1beta/openai
        : System.Text.RegularExpressions.Regex.IsMatch(Base, @"/v\d+\w*(/openai)?$") ? Base + "/chat/completions" : Base + "/v1/chat/completions";
    public string ModelsUrl => IsAnthropic ? Api + "/models?limit=1000" : IsGoogleNative ? Api + "/models?pageSize=1000"
        : ChatUrl[..^"/chat/completions".Length] + "/models";
    public string Host => Uri.TryCreate(Base, UriKind.Absolute, out var u) ? u.Host : Base;

    // The cloud services asked in their own way (LlmServer.Cloud.cs), known by their addresses
    bool At(string host) => Host.Equals(host, StringComparison.OrdinalIgnoreCase);
    public bool IsOpenAi => At("api.openai.com");
    /// <summary>Anthropic's own API (Messages), not OpenAI's form.</summary>
    public bool IsAnthropic => At("api.anthropic.com");
    public bool IsGoogle => At("generativelanguage.googleapis.com");
    /// <summary>Google's own API (Interactions): its address without "/openai", which is its OpenAI form.</summary>
    public bool IsGoogleNative => IsGoogle && !System.Text.RegularExpressions.Regex.IsMatch(Base, "/openai(/|$)");
    public bool IsDeepSeek => At("api.deepseek.com");
    /// <summary>The own API's base, whatever path was given after the host: Anthropic's …/v1, Google's …/v1beta.</summary>
    public string Api => (Uri.TryCreate(Base, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Authority) : Base) + (IsGoogle ? "/v1beta" : "/v1");

    /// <summary>The key as the server wants it: Anthropic's x-api-key with its API version, Google's x-goog-api-key, or a
    /// bearer token.</summary>
    public void AddKey(HttpRequestMessage req)
    {
        if (IsAnthropic) req.Headers.Add("anthropic-version", "2023-06-01");
        if (Key is not { } key) return;
        // a key a header cannot carry (a line break inside) is that request's failure, said as such: before, the
        // exception ended the editor from the checker's thread
        try
        {
            if (IsAnthropic) req.Headers.Add("x-api-key", key);
            else if (IsGoogleNative) req.Headers.Add("x-goog-api-key", key);
            else req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        }
        catch (FormatException) { throw new HttpRequestException("the key has a character a request cannot carry (a line break?): check it"); }
    }

    /// <summary>On this computer or the local network: paragraphs sent there stay at home; anything else is the
    /// internet, and the user is told.</summary>
    public bool Private
    {
        get
        {
            if (!Uri.TryCreate(Base, UriKind.Absolute, out var u)) return false;
            if (u.IsLoopback || u.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || !u.Host.Contains('.', StringComparison.Ordinal)) return true;
            if (!IPAddress.TryParse(u.Host, out var ip)) return false;
            var b = ip.GetAddressBytes();
            return ip.AddressFamily == AddressFamily.InterNetwork
                ? b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254)
                : ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
        }
    }
}

/// <summary>
/// The language model everything that asks one goes to (grammar checking, style suggestions), shared, so a model sits in
/// video memory once. Normally one llama.cpp server (llm\bin\llama-server.exe) with one .gguf model, started here on
/// first use by whoever needs it first (the others wait) and stopped with the editor — a Windows job object ends it
/// even if the editor is killed. Or a server at an address (<see cref="LlmEndpoint"/>): chosen, or the fallback when
/// llama.cpp is missing or does not start.
/// </summary>
sealed partial class LlmServer : IDisposable
{
    /// <summary>A program's file name: name.exe on Windows, the name itself elsewhere.</summary>
    public static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    readonly string _dir;
    readonly object _lock = new();
    // long: a server at an address may load the model on the first request (Ollama took minutes in the plugin)
    readonly HttpClient _http = Net.Client(TimeSpan.FromSeconds(300));
    Process? _process;
    IntPtr _job;

    /// <summary>The llama-server's process id once it was started (tests: it must be gone after Dispose).</summary>
    internal int? ProcessId { get { try { return _process?.Id; } catch (InvalidOperationException) { return null; } } }
    string? _url;
    volatile string? _error;
    volatile LlmEndpoint? _remote;
    string? _answeredBy;
    // what this server turned out not to take, learned from its 400 answers (#95)
    readonly HashSet<string> _omit = new(StringComparer.Ordinal);
    bool _completionTokens;
    bool _modelAsked;

    public string Model { get; }
    public string ModelName => _remote is { } r ? _answeredBy ?? r.Model : LlmGrammarChecker.DisplayName(Model);
    /// <summary>Why it cannot start, or null.</summary>
    public string? Error => _error;
    public bool Running => _url != null;
    /// <summary>The server at an address to use when llama.cpp does not start; null: none.</summary>
    public LlmEndpoint? Fallback { get; set; }
    /// <summary>Why the server at an address is used instead of llama.cpp here, or null.</summary>
    public string? FallbackReason { get; private set; }
    /// <summary>The server at an address in use, or null for llama.cpp here.</summary>
    public LlmEndpoint? Remote => _remote;

    public LlmServer(string dir, string modelPath) { _dir = dir; Model = modelPath; }

    /// <summary>A server at an address; <paramref name="instead"/>: why it stands in for llama.cpp here.</summary>
    public static LlmServer At(LlmEndpoint endpoint, string? instead = null) =>
        new("", endpoint.Model) { _remote = endpoint, _url = endpoint.Url, FallbackReason = instead };

    /// <summary>The server's address, starting it first if needed (a few seconds; blocks the caller).</summary>
    public string Url()
    {
        lock (_lock)
        {
            if (_url != null) return _url;
            if (_error != null) throw new InvalidOperationException(_error);
            try { _url = Start(); return _url; }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                if (Fallback is { } f)                           // llama.cpp did not start: the server at an address
                {
                    _remote = f;
                    FallbackReason = "llama.cpp did not start: " + ex.Message;
                    _url = f.Url;
                    return _url;
                }
                _error = ex.Message;
                throw new InvalidOperationException(ex.Message, ex);
            }
        }
    }

    /// <summary>The models a server at an address offers (its /models), sorted; an empty list when it does not answer.</summary>
    public static List<string> ModelsAt(LlmEndpoint endpoint)
    {
        using var http = Net.Client(TimeSpan.FromSeconds(5));
        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint.ModelsUrl);
        endpoint.AddKey(req);
        try
        {
            using var resp = http.Send(req);
            if (!resp.IsSuccessStatusCode) return new();
            return ModelsIn(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new();
        }
    }

    string Start()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
        var psi = new ProcessStartInfo(Path.Combine(_dir, "bin", Exe("llama-server")))
        {
            WorkingDirectory = Path.Combine(_dir, "bin"),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[] { "-m", Model, "-ngl", "99", "-c", "4096", "--host", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            psi.ArgumentList.Add(a);
        _process = Process.Start(psi) ?? throw new InvalidOperationException("llama-server did not start");
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
        _job = JobObject.KillOnClose(_process);
        var baseUrl = $"http://127.0.0.1:{port}";
        var sw = Stopwatch.StartNew();
        while (true)                                             // ready when /health says so (a few seconds)
        {
            if (_process.HasExited) throw new InvalidOperationException($"llama-server exited with code {_process.ExitCode}");
            try
            {
                using var r = _http.GetAsync(new Uri(baseUrl + "/health")).GetAwaiter().GetResult();
                if (r.IsSuccessStatusCode) return baseUrl;
            }
            catch (HttpRequestException) { }
            if (sw.Elapsed > TimeSpan.FromSeconds(180)) throw new InvalidOperationException("llama-server did not get ready");
            Thread.Sleep(250);
        }
    }

    /// <summary>One chat request: the system instructions, the text, an answer held to a JSON schema (llama.cpp
    /// constrains generation to it). Returns the answer's JSON text. Blocks until the model has answered.</summary>
    public string Chat(string instructions, string text, string schemaName, JsonNode schema, int maxTokens, double temperature = 0)
    {
        var url = Url();
        var remote = _remote;
        // no model named (the installer keeps none for a server of the user's own: nobody types a model's name): the
        // one it has, the first of its list; Tools > Grammar model chooses another
        if (remote != null && remote.Model.Trim().Length == 0 && !_modelAsked)
        {
            _modelAsked = true;
            if (ModelsAt(remote) is [var first, ..]) _remote = remote = remote with { Model = first };
        }
        if (remote != null) Know(remote);
        if (PausedUntil is { } until && until > DateTime.Now) throw new LimitReachedException(until, _limitMessage);
        for (int attempt = 0; ; attempt++)
        {
            var (to, body) = remote == null ? (url + "/v1/chat/completions", Body(null, instructions, text, schemaName, schema, maxTokens, temperature))
                : Request(remote, instructions, text, schemaName, schema, maxTokens, temperature);
            using var req = new HttpRequestMessage(HttpMethod.Post, to)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            remote?.AddKey(req);
            using var resp = _http.Send(req);
            if (!resp.IsSuccessStatusCode)
            {
                var answer = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (remote != null && attempt < 6 && StrictToolFailed(remote, resp.StatusCode, answer)) continue;
                // a server at an address that refuses a field we can do without: leave it out and ask again
                if (resp.StatusCode == HttpStatusCode.BadRequest && remote != null && attempt < 6 && Adapt(answer)) continue;
                if (remote != null) Refused(remote, resp, answer);
                var said = ErrorMessage(answer);
                throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {(said.Length > 200 ? said[..200] : said)}");
            }
            using var doc = JsonDocument.Parse(resp.Content.ReadAsStream());
            var (content, cutOff, who) = Completion(remote, doc.RootElement);
            // the model ran to the token limit: the JSON is cut off. Seen with Gemma 3 12B writing "\n" on and on after a
            // display formula (found by the benchmark of BlueProof, the Obsidian plugin)
            if (cutOff) throw new CutOffException($"the model's answer was cut off at the token limit ({maxTokens})");
            // the model that answered, by name: llama-server ignores the name asked for and answers with its own .gguf
            if (remote != null && who != null)
                _answeredBy = who.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? LlmGrammarChecker.DisplayName(who) : who;
            return _omit.Contains("response_format") || _jsonObject ? Unfenced(content) : content;
        }
    }

    // The request. llama-server here gets what it was measured with; a server at an address also the model's name and
    // reasoning_effort "none" (Ollama's switch for thinking: without it Qwen spent the whole limit thinking and gave an
    // empty answer, in the plugin), less what it refused before, and max_completion_tokens where it wants that.
    JsonObject Body(LlmEndpoint? remote, string instructions, string text, string schemaName, JsonNode schema, int maxTokens, double temperature)
    {
        var body = new JsonObject
        {
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = instructions },
                new JsonObject { ["role"] = "user", ["content"] = text }),
        };
        if (remote != null && remote.Model.Length > 0) body["model"] = remote.Model;
        body[_completionTokens ? "max_completion_tokens" : "max_tokens"] = maxTokens + (_thinks ? ThinkingRoom : 0);
        var format = _jsonObject ? new JsonObject { ["type"] = "json_object" }
            : new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = _strictSchema
                    ? new JsonObject { ["name"] = schemaName, ["strict"] = true, ["schema"] = Strict(schema) }
                    : new JsonObject { ["name"] = schemaName, ["schema"] = schema.DeepClone() },
            };
        var optional = new (string Name, JsonNode Value)[]
        {
            ("temperature", temperature),
            ("response_format", format),
            ("chat_template_kwargs", new JsonObject { ["enable_thinking"] = false }),       // Qwen3 would "think" first
        };
        foreach (var (name, value) in optional)
            if (!_omit.Contains(name)) body[name] = value;
        // a model that cannot stop thinking is asked to think little ("none" it may refuse)
        if (remote != null && !_omit.Contains("reasoning_effort")) body["reasoning_effort"] = _thinks ? "low" : "none";
        if (_strictTool == true && remote is { IsDeepSeek: true })
        {
            body.Remove("response_format");
            body["tools"] = new JsonArray(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = schemaName, ["description"] = "The answer.", ["strict"] = true, ["parameters"] = Strict(schema) },
            });
            if (!_omit.Contains("tool_choice"))
                body["tool_choice"] = new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = schemaName } };
        }
        return body;
    }

    // Without a schema a model may wrap its JSON in a Markdown code fence.
    static string Unfenced(string content)
    {
        var t = content.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal)) return content;
        int start = t.IndexOf('\n', StringComparison.Ordinal), end = t.LastIndexOf("```", StringComparison.Ordinal);
        return start > 0 && end > start ? t[(start + 1)..end].Trim() : content;
    }

    public void Dispose()
    {
        try { if (_process is { HasExited: false }) _process.Kill(); } catch (InvalidOperationException) { }
        _process?.Dispose();
        if (_job != IntPtr.Zero) { JobObject.Close(_job); _job = IntPtr.Zero; }
        _http.Dispose();
    }
}

/// <summary>The service's limit of requests was reached: nothing is sent until <see cref="Until"/>.</summary>
sealed class LimitReachedException(DateTime until, string said)
    : HttpRequestException($"the service's limit of requests was reached; checking goes on at {until:HH:mm}. {said}")
{
    public DateTime Until { get; } = until;
}

/// <summary>The model ran to the token limit and its answer is cut off (it looped while copying a formula, #74, #126).</summary>
sealed class CutOffException : InvalidOperationException
{
    public CutOffException() { }
    public CutOffException(string message) : base(message) { }
    public CutOffException(string message, Exception inner) : base(message, inner) { }
}
