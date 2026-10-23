using System.Collections.Concurrent;
using System.Text.Json.Nodes;

/// <summary>
/// Grammar by Harper (github.com/Automattic/harper, Apache-2.0): a rule-based checker for English written in Rust, run
/// as its language server harper-ls over stdio. Offline and fast (about 10–30 ms a paragraph once warm); English only,
/// in the dialect of the paragraph's language (en-GB → British, en-US → American...). Each paragraph is opened as a
/// document of its own: its diagnostics are the findings, each one's quick fixes the replacements. Harper's own spell
/// check is off: spelling is the spelling provider's job, as with LanguageTool. The server is harper-ls in
/// %LOCALAPPDATA%\BlueEdit\harper (or ~/.local/share/BlueEdit/harper), otherwise on PATH.
/// </summary>
sealed class HarperChecker : IGrammarChecker
{
    public string Name => "Harper";

    readonly string? _exe;
    readonly bool _spelling;
    readonly ConcurrentDictionary<string, IReadOnlyList<GrammarIssue>> _done = new();
    readonly ConcurrentDictionary<string, byte> _queued = new();
    readonly BlockingCollection<(string Text, string Dialect, string Key)> _work = new();
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonArray>> _diagnostics = new();
    readonly Thread? _thread;
    LspClient? _client;
    volatile string _dialect = "American";
    int _version, _doc;
    volatile string? _status;
    volatile bool _broken, _disposed;

    const int MaxParagraph = 10000;
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    public static string DefaultHome => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit", "harper");

    /// <summary>harper-ls in <paramref name="home"/>, otherwise on PATH; null when neither.</summary>
    public static string? ServerIn(string home)
    {
        var own = Path.Combine(home, OperatingSystem.IsWindows() ? "harper-ls.exe" : "harper-ls");
        return File.Exists(own) ? own : LspClient.Resolve("harper-ls");
    }

    public HarperChecker(string home, bool spelling = false)
    {
        _exe = ServerIn(home);
        _spelling = spelling;
        if (_exe == null) { _status = "harper-ls not found"; return; }
        _status = "starting";
        _thread = new Thread(Loop) { IsBackground = true, Name = "Harper" };
        _thread.Start();
    }

    // off when the server could not start or stopped; one paragraph's timeout is LastFailure
    public bool Available => _thread != null && !_broken;
    public string? Status => _status;
    public string? LastFailure { get; private set; }
    public int Version => Volatile.Read(ref _version);

    /// <summary>Harper's name for the English of a language tag ("en-GB" → "British"), null for other languages.</summary>
    internal static string? DialectOf(string language)
    {
        if (!language.StartsWith("en", StringComparison.OrdinalIgnoreCase) || (language.Length > 2 && language[2] != '-' && language[2] != '_')) return null;
        return (language.Length > 3 ? language[3..].ToUpperInvariant() : "") switch
        {
            "GB" or "UK" or "IE" or "NZ" => "British",
            "AU" => "Australian",
            "CA" => "Canadian",
            _ => "American",
        };
    }

    public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true)
    {
        if (!Available || DialectOf(language) is not { } dialect || paragraph.Trim().Length == 0 || paragraph.Length > MaxParagraph)
            return Array.Empty<GrammarIssue>();
        var key = dialect + "\n" + paragraph;
        if (_done.TryGetValue(key, out var issues))
            return issues;
        if (queue && _queued.TryAdd(key, 0) && !_work.IsAddingCompleted) _work.Add((paragraph, dialect, key));
        return null;
    }


    // what harper-ls asks for with workspace/configuration (it asks again before checking each document)
    JsonObject Config() => new()
    {
        ["harper-ls"] = new JsonObject { ["dialect"] = _dialect, ["linters"] = new JsonObject { ["SpellCheck"] = _spelling } },
    };

    // ---------- the worker thread ----------
    void Loop()
    {
        try
        {
            _client = new LspClient(_exe!, new[] { "--stdio" }, Path.GetDirectoryName(_exe!)!);
            _client.Configuration = section => section == null ? Config() : Config()[section]?.DeepClone();
            _client.Notification += OnNotification;
            _client.Exited += () =>
            {
                if (_disposed) return;
                _status = "Harper stopped" + (_client.LastError.Length > 0 ? ": " + _client.LastError : "");
                _broken = true;
                Interlocked.Increment(ref _version);
            };
            var init = _client.Request("initialize", new JsonObject
            {
                ["processId"] = Environment.ProcessId,
                ["rootUri"] = null,
                ["capabilities"] = new JsonObject { ["workspace"] = new JsonObject { ["configuration"] = true } },
                ["initializationOptions"] = Config()["harper-ls"]!.DeepClone(),
            });
            if (!init.Wait(Wait)) throw new TimeoutException("no answer to initialize");
            _client.Notify("initialized", new JsonObject());
            _status = null;
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or System.ComponentModel.Win32Exception
                                   or InvalidOperationException or TimeoutException or AggregateException)
        {
            if (_disposed) return;
            _status = "Harper did not start: " + (ex is AggregateException a ? a.InnerException?.Message : ex.Message);
            _broken = true;
            Interlocked.Increment(ref _version);
            return;
        }
        Interlocked.Increment(ref _version);
        foreach (var (text, dialect, key) in _work.GetConsumingEnumerable())
        {
            try { _done[key] = Check(text, dialect); }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or AggregateException)
            {
                _done[key] = Array.Empty<GrammarIssue>();
                LastFailure = "Harper: " + (ex is AggregateException a ? a.InnerException?.Message : ex.Message);
            }
            _queued.TryRemove(key, out _);
            Interlocked.Increment(ref _version);
        }
    }

    List<GrammarIssue> Check(string text, string dialect)
    {
        var client = _client!;
        if (dialect != _dialect)
        {
            _dialect = dialect;
            client.Notify("workspace/didChangeConfiguration", new JsonObject { ["settings"] = Config() });
        }
        var uri = $"file:///blueedit/harper/{Interlocked.Increment(ref _doc)}.md";
        var published = new TaskCompletionSource<JsonArray>(TaskCreationOptions.RunContinuationsAsynchronously);
        _diagnostics[uri] = published;
        client.Notify("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "markdown", ["version"] = 1, ["text"] = text },
        });
        try
        {
            if (!published.Task.Wait(Wait)) throw new TimeoutException("no answer in " + Wait.TotalSeconds + " s");
            var issues = new List<GrammarIssue>();
            foreach (var d in published.Task.Result.OfType<JsonObject>())
            {
                int start = OffsetOf(text, d["range"]?["start"]), end = OffsetOf(text, d["range"]?["end"]);
                var rule = d["code"]?.ToString() ?? "Harper";
                if (!_spelling && rule == "SpellCheck") continue;
                issues.Add(new GrammarIssue(start, Math.Max(0, end - start), (string?)d["message"] ?? "", Fixes(client, uri, d), rule, Name));
            }
            return issues;
        }
        finally
        {
            _diagnostics.TryRemove(uri, out _);
            client.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } });
        }
    }

    // the replacements of a diagnostic: its quick fixes ("Replace with: …"), without "Ignore Harper error" and the like
    static List<string> Fixes(LspClient client, string uri, JsonObject diagnostic)
    {
        var actions = client.Request("textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["range"] = diagnostic["range"]!.DeepClone(),
            ["context"] = new JsonObject { ["diagnostics"] = new JsonArray(diagnostic.DeepClone()) },
        });
        if (!actions.Wait(Wait)) throw new TimeoutException("no quick fixes in " + Wait.TotalSeconds + " s");
        var fixes = new List<string>();
        foreach (var a in (actions.Result as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            if (a["edit"]?["changes"]?[uri] is JsonArray { Count: 1 } edits && (string?)edits[0]?["newText"] is { } fix && !fixes.Contains(fix))
                fixes.Add(fix);
        return fixes;
    }

    // a position (line, UTF-16 character) in the text
    internal static int OffsetOf(string text, JsonNode? position)
    {
        int line = (int?)position?["line"] ?? 0, character = (int?)position?["character"] ?? 0, at = 0;
        for (int l = 0; l < line; l++)
        {
            at = text.IndexOf('\n', at);
            if (at < 0) return text.Length;
            at++;
        }
        return Math.Min(text.Length, at + character);
    }

    void OnNotification(string method, JsonNode? parameters)
    {
        if (method == "textDocument/publishDiagnostics" && (string?)parameters?["uri"] is { } uri
            && _diagnostics.TryRemove(uri, out var published))
            published.TrySetResult(parameters?["diagnostics"]?.DeepClone() as JsonArray ?? new JsonArray());
    }

    public void Dispose()
    {
        _disposed = true;
        _work.CompleteAdding();
        _client?.Dispose();
    }
}
