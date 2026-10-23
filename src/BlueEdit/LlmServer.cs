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
    public static string? KeyFrom(string setting)
    {
        // "file:PATH": a file holding the key (~ for the home folder), e.g. the Obsidian plugin's ~/.blueproof/llm-key.txt
        string ReadFile(string path)
        {
            path = path.Trim();
            if (path.StartsWith('~')) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
            try { return File.ReadAllText(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return ""; }
        }
        var key = setting.StartsWith("env:", StringComparison.Ordinal) ? Environment.GetEnvironmentVariable(setting[4..].Trim()) ?? ""
            : setting.StartsWith("file:", StringComparison.Ordinal) ? ReadFile(setting[5..])
            : setting;
        key = key.Trim().Trim('﻿').Trim();
        return key.Length == 0 ? null : key;
    }

    string Base => Url.Trim().TrimEnd('/');
    public string ChatUrl => Base.EndsWith("/chat/completions", StringComparison.Ordinal) ? Base
        : System.Text.RegularExpressions.Regex.IsMatch(Base, @"/v\d+$") ? Base + "/chat/completions" : Base + "/v1/chat/completions";
    public string ModelsUrl => ChatUrl[..^"/chat/completions".Length] + "/models";
    public string Host => Uri.TryCreate(Base, UriKind.Absolute, out var u) ? u.Host : Base;

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
sealed class LlmServer : IDisposable
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
        if (endpoint.Key is { } key) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        try
        {
            using var resp = http.Send(req);
            if (!resp.IsSuccessStatusCode) return new();
            using var doc = JsonDocument.Parse(resp.Content.ReadAsStream());
            return doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(m => m.TryGetProperty("id", out var id) ? id.GetString()?.Trim() ?? "" : "").Where(m => m.Length > 0)
                .Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
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
        for (int attempt = 0; ; attempt++)
        {
            var body = Body(remote, instructions, text, schemaName, schema, maxTokens, temperature);
            using var req = new HttpRequestMessage(HttpMethod.Post, remote?.ChatUrl ?? url + "/v1/chat/completions")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            if (remote?.Key is { } key) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var resp = _http.Send(req);
            if (!resp.IsSuccessStatusCode)
            {
                var answer = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                // a server at an address that refuses a field we can do without: leave it out and ask again
                if (resp.StatusCode == HttpStatusCode.BadRequest && remote != null && attempt < 6 && Adapt(answer)) continue;
                throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {(answer.Length > 200 ? answer[..200] : answer)}");
            }
            using var doc = JsonDocument.Parse(resp.Content.ReadAsStream());
            var choice = doc.RootElement.GetProperty("choices")[0];
            // the model ran to the token limit: the JSON is cut off. Seen with Gemma 3 12B writing "\n" on and on after a
            // display formula (found by the benchmark of BlueProof, the Obsidian plugin)
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.GetString() == "length")
                throw new CutOffException($"the model's answer was cut off at the token limit ({maxTokens})");
            // the model that answered, by name: llama-server ignores the name asked for and answers with its own .gguf
            if (remote != null && doc.RootElement.TryGetProperty("model", out var m) && m.GetString() is { Length: > 0 } who)
                _answeredBy = who.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? LlmGrammarChecker.DisplayName(who) : who;
            var content = choice.GetProperty("message").GetProperty("content").GetString() ?? "";
            return _omit.Contains("response_format") ? Unfenced(content) : content;
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
        body[_completionTokens ? "max_completion_tokens" : "max_tokens"] = maxTokens;
        var optional = new (string Name, JsonNode Value)[]
        {
            ("temperature", temperature),
            ("response_format", new JsonObject { ["type"] = "json_schema", ["json_schema"] = new JsonObject { ["name"] = schemaName, ["schema"] = schema.DeepClone() } }),
            ("chat_template_kwargs", new JsonObject { ["enable_thinking"] = false }),       // Qwen3 would "think" first
        };
        foreach (var (name, value) in optional)
            if (!_omit.Contains(name)) body[name] = value;
        if (remote != null && !_omit.Contains("reasoning_effort")) body["reasoning_effort"] = "none";
        return body;
    }

    // The fields a server may refuse that we can do without (not the model, the messages or the token limit).
    static readonly HashSet<string> Optional = new(StringComparer.Ordinal) { "temperature", "response_format", "chat_template_kwargs", "reasoning_effort" };

    /// <summary>Learns from a 400 answer which field the server refused (OpenAI: "Unknown parameter:
    /// 'chat_template_kwargs'", with error.param); true when the request is worth sending again.</summary>
    internal bool Adapt(string answer)
    {
        string? param = null;
        try
        {
            using var doc = JsonDocument.Parse(answer);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("param", out var p) && p.ValueKind == JsonValueKind.String) param = p.GetString();
        }
        catch (JsonException) { }
        param ??= System.Text.RegularExpressions.Regex.Match(answer, @"(?:unknown|unsupported|unrecognized)[^'""]*['""]([a-z_]+)['""]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } mm ? mm.Groups[1].Value : null;
        if (param == null) return false;
        if (param == "max_tokens" && !_completionTokens) { _completionTokens = true; return true; }
        return Optional.Contains(param) && _omit.Add(param);
    }

    // Without a schema a model may wrap its JSON in a Markdown code fence.
    static string Unfenced(string content)
    {
        var t = content.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal)) return content;
        int start = t.IndexOf('\n', StringComparison.Ordinal), end = t.LastIndexOf("```", StringComparison.Ordinal);
        return start > 0 && end > start ? t[(start + 1)..end] : content;
    }

    public void Dispose()
    {
        try { if (_process is { HasExited: false }) _process.Kill(); } catch (InvalidOperationException) { }
        _process?.Dispose();
        if (_job != IntPtr.Zero) { JobObject.Close(_job); _job = IntPtr.Zero; }
        _http.Dispose();
    }
}

/// <summary>The model ran to the token limit and its answer is cut off (it looped while copying a formula, #74, #126).</summary>
sealed class CutOffException : InvalidOperationException
{
    public CutOffException() { }
    public CutOffException(string message) : base(message) { }
    public CutOffException(string message, Exception inner) : base(message, inner) { }
}
