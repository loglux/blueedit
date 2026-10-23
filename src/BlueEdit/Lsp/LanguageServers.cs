using System.Collections.Concurrent;
using System.Text.Json.Nodes;

/// <summary>An error, warning or hint a language server reported: where (0-based lines and UTF-16 columns, which are
/// .NET string indices), how bad (1 error, 2 warning, 3 information, 4 hint), what, and which server said it. Raw: the
/// diagnostic as the server sent it, handed back when asking for its fixes (servers match fixes by its code and data).
/// Rule and Fix: the editor's own formula check (LatexCheck.Style) names its rule and the text for the range.</summary>
sealed record LspDiagnostic(int Line, int Col, int EndLine, int EndCol, int Severity, string Message, string Source, JsonNode? Raw = null,
    string Rule = "", string? Fix = null);

/// <summary>A change a language server asks for (a TextEdit): the range to replace (0-based lines, UTF-16 columns) and
/// the new text, line breaks as "\n".</summary>
sealed record LspEdit(int Line, int Col, int EndLine, int EndCol, string Text);

/// <summary>A fix or refactoring a server offers (a CodeAction, or a bare Command): its title, kind ("quickfix"…),
/// whether the server prefers it, and the object itself (its edit, command, data).</summary>
sealed record LspAction(string Title, string Kind, bool Preferred, JsonObject Raw);

/// <summary>How to start a language server: a command (looked up on PATH) and its arguments.</summary>
sealed record ServerCommand(string Command, IReadOnlyList<string> Args);

/// <summary>
/// The language servers in use (setting "languageServers"): one per language and project root, started when a file
/// of that language is first shown, stopped with the editor. Keeps every open file's text in sync with its server
/// (the whole text, at most every editor tick and only after an edit) and the diagnostics each server publishes.
/// Everything that talks to a server runs on that server's own thread: the editor never waits for one.
/// </summary>
sealed class LanguageServers : IDisposable
{
    /// <summary>Servers used when the settings name none: free, installed separately (docs/lsp.md).</summary>
    public static IReadOnlyDictionary<string, ServerCommand> Defaults => new Dictionary<string, ServerCommand>(StringComparer.OrdinalIgnoreCase)
    {
        ["Python"] = new("pyright-langserver", ["--stdio"]),
        ["C#"] = new("csharp-ls", []),
        ["JavaScript"] = new(CodeTools.TypeScriptExe, ["--lsp", "--stdio"]),    // .js .ts .jsx .tsx: TypeScript 7's own server
    };

    static readonly Dictionary<string, string> LanguageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Python"] = "python", ["C#"] = "csharp", ["PowerShell"] = "powershell", ["C/C++"] = "cpp", ["JavaScript"] = "javascript",
        ["Go"] = "go", ["Rust"] = "rust", ["Java"] = "java", ["JSON"] = "json", ["SQL"] = "sql", ["HTML/XML"] = "html",
    };

    // files that mark a project's root, nearest first; the file's own folder when there is none
    static readonly Dictionary<string, string[]> RootMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C#"] = ["*.sln", "*.slnx", "*.csproj"],
        ["Python"] = ["pyproject.toml", "pyrightconfig.json", "setup.py", "setup.cfg", "requirements.txt"],
        ["JavaScript"] = ["tsconfig.json", "jsconfig.json", "package.json"],
    };

    /// <summary>The LSP language id of a file: by its language, and for the editor's one "JavaScript" by the
    /// extension, since TypeScript's server reads .ts as TypeScript and .tsx/.jsx with JSX only when told so.</summary>
    internal static string LanguageId(string language, string path) =>
        language.Equals("JavaScript", StringComparison.OrdinalIgnoreCase)
            ? Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".ts" or ".mts" or ".cts" => "typescript",
                ".tsx" => "typescriptreact",
                ".jsx" => "javascriptreact",
                _ => "javascript",
            }
            : LanguageIds.GetValueOrDefault(language, language.ToLowerInvariant());

    readonly IReadOnlyDictionary<string, ServerCommand> _config;
    readonly Dictionary<(string Lang, string Root), Server> _servers = new();
    readonly Dictionary<string, int> _synced = new(StringComparer.OrdinalIgnoreCase);       // path → editor's edit version sent
    readonly ConcurrentDictionary<string, IReadOnlyList<LspDiagnostic>> _diagnostics = new(StringComparer.OrdinalIgnoreCase);
    int _version;

    /// <summary>Goes up whenever diagnostics or a server's state changed (the screen may need redrawing).</summary>
    public int Version => Volatile.Read(ref _version);

    public LanguageServers(IReadOnlyDictionary<string, ServerCommand> config) => _config = config;

    public bool Handles(string? language) => language != null && _config.ContainsKey(language);

    /// <summary>Tells the file's server about its current text, if it changed since the last call (cheap otherwise:
    /// <paramref name="text"/> is only called when something is sent). Starts the server on first use.</summary>
    public void Sync(string path, string? language, int editVersion, Func<string> text)
    {
        if (!Handles(language)) return;
        path = Path.GetFullPath(path);
        if (_synced.TryGetValue(path, out var v) && v == editVersion) return;
        _synced[path] = editVersion;
        ServerFor(language!, path).Update(path, text());
    }

    /// <summary>The server's completions at a position (0-based line, UTF-16 column), after the text was synced:
    /// label, a short tag (m method, f function, fld field, cls class...) and the text to insert. Empty when the
    /// language has no server or it does not answer. <paramref name="trigger"/>: the character that asked ('.').</summary>
    public Task<IReadOnlyList<(string Label, string Tag, string Insert)>> Complete(string path, string? language, int line, int col, char? trigger)
    {
        if (!Handles(language)) return Task.FromResult<IReadOnlyList<(string, string, string)>>(Array.Empty<(string, string, string)>());
        return ServerFor(language!, Path.GetFullPath(path)).Complete(Path.GetFullPath(path), line, col, trigger);
    }

    /// <summary>A request about a position in the file (after its text was synced): the server's raw result, or null
    /// when there is no server, it is not running, or it failed. <paramref name="method"/> e.g. textDocument/hover.</summary>
    public Task<JsonNode?> Ask(string path, string? language, string method, int line, int col, JsonObject? extra = null)
    {
        if (!Handles(language)) return Task.FromResult<JsonNode?>(null);
        return ServerFor(language!, Path.GetFullPath(path)).Ask(Path.GetFullPath(path), method, line, col, extra);
    }

    /// <summary>A request about the whole file (after its text was synced), e.g. textDocument/formatting: the
    /// server's raw result, or null as for <see cref="Ask"/>. <paramref name="extra"/> goes into the parameters.</summary>
    public Task<JsonNode?> AskFile(string path, string? language, string method, JsonObject extra)
    {
        if (!Handles(language)) return Task.FromResult<JsonNode?>(null);
        return ServerFor(language!, Path.GetFullPath(path)).AskFile(Path.GetFullPath(path), method, extra);
    }

    /// <summary>Whether the file's server offers a capability of its initialize answer ("documentFormattingProvider"…):
    /// null while it is starting or when there is none.</summary>
    public bool? Can(string path, string? language, string capability)
    {
        if (!Handles(language)) return null;
        if (!_servers.TryGetValue((language!, RootOf(language!, Path.GetFullPath(path))), out var s) || s.Capabilities is not { } caps) return null;
        // true, or an options object; absent or false: not offered
        return caps[capability] is { } v && !(v is JsonValue jv && jv.TryGetValue<bool>(out var b) && !b);
    }

    /// <summary>Whether the server answers textDocument/prepareRename (renameProvider: { prepareProvider: true }).</summary>
    public bool CanPrepareRename(string path, string? language) =>
        Handles(language) && _servers.TryGetValue((language!, RootOf(language!, Path.GetFullPath(path))), out var s)
        && s.Capabilities?["renameProvider"] is JsonObject r && (bool?)r["prepareProvider"] == true;      // true alone: no prepare

    /// <summary>The edits of a formatting answer (a list of TextEdits), "\r\n" in their text made "\n".</summary>
    internal static IReadOnlyList<LspEdit> EditsFrom(JsonNode? result)
    {
        var list = new List<LspEdit>();
        if (result is not JsonArray a) return list;
        foreach (var e in a)
        {
            if (e?["range"] is not { } r || r["start"] is not { } s || r["end"] is not { } end) continue;
            var text = ((string?)e["newText"] ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            list.Add(new LspEdit((int)s["line"]!, (int)s["character"]!, (int)end["line"]!, (int)end["character"]!, text));
        }
        return list;
    }

    /// <summary>The edits of a WorkspaceEdit (a rename's answer) by file: its "changes" map or its "documentChanges"
    /// list. Null when it also creates, renames or deletes files, which the editor does not do.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<LspEdit>>? WorkspaceEditFrom(JsonNode? result)
    {
        var byFile = new Dictionary<string, IReadOnlyList<LspEdit>>(StringComparer.OrdinalIgnoreCase);
        if (result?["documentChanges"] is JsonArray docs)
        {
            foreach (var d in docs)
            {
                if (d?["kind"] != null) return null;                           // create / rename / delete a file
                if ((string?)d?["textDocument"]?["uri"] is not { } uri || PathOf(uri) is not { } path) continue;
                byFile[path] = [.. byFile.GetValueOrDefault(path, []), .. EditsFrom(d!["edits"])];
            }
        }
        else if (result?["changes"] is JsonObject changes)
        {
            foreach (var (uri, edits) in changes)
                if (PathOf(uri) is { } path) byFile[path] = EditsFrom(edits);
        }
        return byFile;
    }

    /// <summary>Where a symbol is defined (textDocument/definition: a Location, a list, or LocationLinks): files with
    /// 0-based line and column.</summary>
    internal static IReadOnlyList<(string Path, int Line, int Col)> LocationsFrom(JsonNode? result)
    {
        var list = new List<(string, int, int)>();
        IEnumerable<JsonNode?> items = result switch { JsonArray a => a, JsonObject o => [o], _ => [] };
        foreach (var it in items)
        {
            var uri = (string?)it?["targetUri"] ?? (string?)it?["uri"];
            var range = it?["targetSelectionRange"] ?? it?["targetRange"] ?? it?["range"];
            if (uri == null || range?["start"] is not { } start || PathOf(uri) is not { } path) continue;
            list.Add((path, (int)start["line"]!, (int)start["character"]!));
        }
        return list;
    }

    /// <summary>The places of a references answer (a list of Locations): file, 0-based line and column, and the length
    /// on that line (0 when the range spans lines).</summary>
    internal static IReadOnlyList<(string Path, int Line, int Col, int Length)> ReferencesFrom(JsonNode? result)
    {
        var list = new List<(string, int, int, int)>();
        if (result is not JsonArray a) return list;
        foreach (var it in a)
        {
            if ((string?)it?["uri"] is not { } uri || PathOf(uri) is not { } path || it!["range"] is not { } r) continue;
            int line = (int)r["start"]!["line"]!, col = (int)r["start"]!["character"]!;
            int endLine = (int)r["end"]!["line"]!, endCol = (int)r["end"]!["character"]!;
            list.Add((path, line, col, endLine == line ? Math.Max(0, endCol - col) : 0));
        }
        return list.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Item2).ThenBy(x => x.Item3).ToList();
    }

    static readonly Dictionary<int, string> SymbolKinds = new()
    {
        [2] = "module", [3] = "namespace", [4] = "package", [5] = "class", [6] = "method", [7] = "property", [8] = "field",
        [9] = "constructor", [10] = "enum", [11] = "interface", [12] = "function", [13] = "variable", [14] = "constant",
        [22] = "enum member", [23] = "struct", [24] = "event", [25] = "operator",
    };

    /// <summary>The symbols of a documentSymbol answer as an outline, in file order: nesting level (0 at the top),
    /// name, kind ("class", "method"…) and 0-based line and column of the name. What is inside a function or method
    /// (its local variables) is left out. Takes both forms: DocumentSymbol trees and flat SymbolInformation lists.</summary>
    internal static IReadOnlyList<(int Level, string Name, string Kind, int Line, int Col)> OutlineFrom(JsonNode? result)
    {
        var list = new List<(int, string, string, int, int)>();
        if (result is not JsonArray a) return list;
        static bool Callable(int kind) => kind is 6 or 9 or 12;           // method, constructor, function
        // flat SymbolInformation: the level from the container's name; what a function or method contains is dropped
        var kindOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var it in a.OfType<JsonObject>())
            if (it["location"] != null && (string?)it["name"] is { } n) kindOf.TryAdd(n, (int?)it["kind"] ?? 0);
        void Walk(JsonArray items, int level)
        {
            foreach (var it in items.OfType<JsonObject>())
            {
                int kind = (int?)it["kind"] ?? 0;
                var at = it["selectionRange"]?["start"] ?? it["range"]?["start"] ?? it["location"]?["range"]?["start"];
                if (at == null) continue;
                int lv = level;
                if (it["location"] != null && (string?)it["containerName"] is { Length: > 0 } container)
                {
                    if (Callable(kindOf.GetValueOrDefault(container))) continue;
                    lv = 1;
                }
                list.Add((lv, ((string?)it["name"] ?? "").Trim(), SymbolKinds.GetValueOrDefault(kind, ""), (int)at["line"]!, (int)at["character"]!));
                if (!Callable(kind) && it["children"] is JsonArray ch) Walk(ch, level + 1);
            }
        }
        Walk(a, 0);
        return list.OrderBy(x => x.Item4).ThenBy(x => x.Item5).ToList();
    }

    /// <summary>The text of a hover (MarkupContent, MarkedString or a list of them), Markdown fences and blank runs
    /// trimmed; empty when there is none.</summary>
    internal static string HoverText(JsonNode? result)
    {
        static string Part(JsonNode? c) => c switch
        {
            JsonValue v => v.ToString(),
            JsonObject o => (string?)o["value"] ?? "",
            _ => "",
        };
        var contents = result?["contents"];
        var text = contents is JsonArray a ? string.Join("\n\n", a.Select(Part)) : Part(contents);
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n').Where(l => !l.TrimStart().StartsWith("```", StringComparison.Ordinal));
        return System.Text.RegularExpressions.Regex.Replace(string.Join('\n', lines).Trim(), @"\n{3,}", "\n\n");
    }

    /// <summary>The active signature of a signatureHelp result and where its active parameter is in the label (or
    /// -1, -1); null when there is none.</summary>
    internal static (string Label, int Start, int End)? SignatureFrom(JsonNode? result)
    {
        if (result?["signatures"] is not JsonArray sigs || sigs.Count == 0) return null;
        int active = Math.Clamp((int?)result["activeSignature"] ?? 0, 0, sigs.Count - 1);
        var sig = sigs[active]!;
        var label = (string?)sig["label"] ?? "";
        int p = (int?)sig["activeParameter"] ?? (int?)result["activeParameter"] ?? -1;
        if (sig["parameters"] is JsonArray ps && p >= 0 && p < ps.Count)
        {
            var pl = ps[p]?["label"];
            if (pl is JsonArray range && range.Count == 2) return (label, (int)range[0]!, (int)range[1]!);   // [start, end] offsets
            if ((string?)pl is { Length: > 0 } name && label.IndexOf(name, StringComparison.Ordinal) is int at and >= 0) return (label, at, at + name.Length);
        }
        return (label, -1, -1);
    }

    static readonly Dictionary<int, string> KindTags = new()
    {
        [2] = "m", [3] = "f", [4] = "ctor", [5] = "fld", [6] = "var", [7] = "cls", [8] = "ifc", [9] = "mod", [10] = "prop",
        [12] = "val", [13] = "enum", [14] = "kw", [15] = "snip", [20] = "enum", [21] = "const", [22] = "str", [23] = "evt", [25] = "type",
    };

    /// <summary>Completion items of a server's answer (a list, or { items: [...] }), in the server's order.</summary>
    internal static IReadOnlyList<(string Label, string Tag, string Insert)> CompletionsFrom(JsonNode? result)
    {
        var items = result switch { JsonArray a => a, JsonObject o when o["items"] is JsonArray a => a, _ => new JsonArray() };
        return items.OfType<JsonObject>()
            .Select((it, i) => (It: it, I: i))
            .OrderBy(x => (string?)x.It["sortText"] ?? (string?)x.It["label"] ?? "", StringComparer.Ordinal).ThenBy(x => x.I)
            .Select(x =>
            {
                var label = ((string?)x.It["label"] ?? "").Trim();
                var insert = (string?)x.It["textEdit"]?["newText"] ?? (string?)x.It["insertText"] ?? label;
                if ((int?)x.It["insertTextFormat"] == 2) insert = Unsnippet(insert);      // ${1:name} → name, $0 → ""
                // a label like "append(object)" or "Count { get; }": the word itself is what the user types
                if (insert.Length == 0 || insert.Contains('\n', StringComparison.Ordinal)) insert = label;
                return (label, KindTags.GetValueOrDefault((int?)x.It["kind"] ?? 0, ""), insert);
            })
            .Where(x => x.label.Length > 0)
            .ToList();
    }

    static string Unsnippet(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\$\{\d+(?::([^}]*))?\}|\$\d+", m => m.Groups[1].Success ? m.Groups[1].Value : "");

    /// <summary>A WorkspaceEdit the server sent by itself (workspace/applyEdit, after running a command), on the
    /// server's thread: the editor applies it on its own.</summary>
    public event Action<JsonNode>? EditRequested;

    /// <summary>The fixes and refactorings for a range (textDocument/codeAction), with the diagnostics there handed back.</summary>
    public Task<JsonNode?> CodeActions(string path, string? language, int line, int col, int endLine, int endCol, IEnumerable<LspDiagnostic> diagnostics)
    {
        static JsonObject Pos(int l, int c) => new() { ["line"] = l, ["character"] = c };
        return AskFile(path, language, "textDocument/codeAction", new JsonObject
        {
            ["range"] = new JsonObject { ["start"] = Pos(line, col), ["end"] = Pos(endLine, endCol) },
            ["context"] = new JsonObject
            {
                ["diagnostics"] = new JsonArray(diagnostics.Select(d => d.Raw?.DeepClone()).Where(d => d != null).ToArray()),
                ["triggerKind"] = 1,                                           // asked for, not automatic
            },
        });
    }

    /// <summary>A request that is not about a position (codeAction/resolve, workspace/executeCommand) to the file's
    /// server, after the text it is about.</summary>
    public Task<JsonNode?> Request(string path, string? language, string method, JsonNode parameters)
    {
        if (!Handles(language)) return Task.FromResult<JsonNode?>(null);
        return ServerFor(language!, Path.GetFullPath(path)).Request(method, parameters);
    }

    /// <summary>The actions of a codeAction answer that can be taken, the server's preferred ones first.</summary>
    internal static IReadOnlyList<LspAction> ActionsFrom(JsonNode? result)
    {
        if (result is not JsonArray a) return [];
        return a.OfType<JsonObject>()
            .Where(x => x["disabled"] == null && (string?)x["title"] is { Length: > 0 })
            .Select((x, i) => (A: new LspAction(((string?)x["title"])!.Trim(), (string?)x["kind"] ?? "", (bool?)x["isPreferred"] == true, x), I: i))
            .OrderBy(x => x.A.Preferred ? 0 : 1).ThenBy(x => x.I)
            .Select(x => x.A).ToList();
    }

    /// <summary>The file was closed in the editor.</summary>
    public void Close(string path)
    {
        path = Path.GetFullPath(path);
        _synced.Remove(path);
        foreach (var s in _servers.Values) s.Close(path);
        if (_diagnostics.TryRemove(path, out _)) Interlocked.Increment(ref _version);
    }

    public IReadOnlyList<LspDiagnostic> DiagnosticsFor(string? path) =>
        path != null && _diagnostics.TryGetValue(Path.GetFullPath(path), out var d) ? d : Array.Empty<LspDiagnostic>();

    /// <summary>"pyright-langserver", "pyright-langserver starting", "pyright-langserver failed: …", or null when the
    /// language has no server.</summary>
    public string? StatusFor(string? path, string? language)
    {
        if (path == null || !Handles(language)) return null;
        var root = RootOf(language!, Path.GetFullPath(path));
        if (!_servers.TryGetValue((language!, root), out var s)) return Name(_config[language!]) + " starting";
        return s.Status;
    }

    static string Name(ServerCommand c) => Path.GetFileNameWithoutExtension(c.Command);

    Server ServerFor(string language, string path)
    {
        var root = RootOf(language, path);
        if (!_servers.TryGetValue((language, root), out var s))
        {
            s = new Server(this, language, root, _config[language]);
            _servers[(language, root)] = s;
        }
        return s;
    }

    internal static string RootOf(string language, string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var markers = RootMarkers.TryGetValue(language, out var m) ? m : [];
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            try
            {
                if (markers.Any(x => d.EnumerateFiles(x).Any())) return d.FullName;
                if (Directory.Exists(Path.Combine(d.FullName, ".git"))) return d.FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { break; }
        }
        return dir;
    }

    internal static string UriOf(string path) => new Uri(path).AbsoluteUri;

    internal static string? PathOf(string uri)
    {
        try
        {
            var u = new Uri(Uri.UnescapeDataString(uri));
            if (!u.IsFile) return null;
            var p = Path.GetFullPath(u.LocalPath);
            // pyright writes "c:/…": the drive letter in capitals, as Windows shows it everywhere else
            return p.Length > 1 && p[1] == ':' ? char.ToUpperInvariant(p[0]) + p[1..] : p;
        }
        catch (UriFormatException) { return null; }
    }

    void Publish(JsonNode? p, string source)
    {
        if (p?["uri"] is not { } uriNode || PathOf((string)uriNode!) is not { } path) return;
        var list = new List<LspDiagnostic>();
        if (p["diagnostics"] is JsonArray arr)
            foreach (var d in arr)
            {
                if (d?["range"] is not { } r) continue;
                list.Add(new LspDiagnostic(
                    (int)r["start"]!["line"]!, (int)r["start"]!["character"]!, (int)r["end"]!["line"]!, (int)r["end"]!["character"]!,
                    (int?)d["severity"] ?? 1, ((string?)d["message"] ?? "").Trim(), (string?)d["source"] ?? source, d.DeepClone()));
            }
        _diagnostics[path] = list;
        Interlocked.Increment(ref _version);
    }

    void Changed() => Interlocked.Increment(ref _version);

    public void Dispose()
    {
        foreach (var s in _servers.Values) s.Dispose();
        _servers.Clear();
    }

    /// <summary>One server process for one language and root, with a worker thread that sends everything in order
    /// (the initialize handshake first).</summary>
    sealed class Server : IDisposable
    {
        readonly LanguageServers _owner;
        readonly string _language, _root;
        readonly ServerCommand _command;
        readonly BlockingCollection<Action> _work = new();
        readonly Dictionary<string, int> _open = new(StringComparer.OrdinalIgnoreCase);   // path → version sent
        LspClient? _client;
        volatile string _status;

        public Server(LanguageServers owner, string language, string root, ServerCommand command)
        {
            _owner = owner; _language = language; _root = root; _command = command;
            _status = Name(command) + " starting";
            new Thread(Loop) { IsBackground = true, Name = "LSP " + language }.Start();
        }

        public string Status => _status;

        /// <summary>The server's capabilities from its initialize answer; null until it answered.</summary>
        public JsonObject? Capabilities => _capabilities;
        volatile JsonObject? _capabilities;

        public void Update(string path, string text) { if (!_work.IsAddingCompleted) _work.Add(() => Send(path, text)); }

        public Task<IReadOnlyList<(string Label, string Tag, string Insert)>> Complete(string path, int line, int col, char? trigger)
        {
            var context = trigger is char t
                ? new JsonObject { ["triggerKind"] = 2, ["triggerCharacter"] = t.ToString() }
                : new JsonObject { ["triggerKind"] = 1 };
            return Ask(path, "textDocument/completion", line, col, new JsonObject { ["context"] = context })
                .ContinueWith(r => CompletionsFrom(r.Result), TaskScheduler.Default);
        }

        /// <summary>A position request, queued after the text it is about so the server answers for what is on the
        /// screen; null when it cannot be asked or fails.</summary>
        public Task<JsonNode?> Ask(string path, string method, int line, int col, JsonObject? extra)
        {
            var p = new JsonObject { ["position"] = new JsonObject { ["line"] = line, ["character"] = col } };
            if (extra != null) foreach (var (k, v) in extra) p[k] = v?.DeepClone();
            return AskFile(path, method, p);
        }

        /// <summary>A request about the file, queued like <see cref="Ask"/>; the file's uri is added to
        /// <paramref name="extra"/>.</summary>
        public Task<JsonNode?> AskFile(string path, string method, JsonObject extra)
        {
            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_work.IsAddingCompleted) { tcs.SetResult(null); return tcs.Task; }
            _work.Add(() =>
            {
                if (_client is not { Running: true } c || !_open.ContainsKey(path)) { tcs.TrySetResult(null); return; }
                var p = new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = UriOf(path) } };
                foreach (var (k, v) in extra) p[k] = v?.DeepClone();
                c.Request(method, p).ContinueWith(r => tcs.TrySetResult(r.IsCompletedSuccessfully ? r.Result : null), TaskScheduler.Default);
            });
            return tcs.Task;
        }

        /// <summary>A request with its own parameters, queued like the others; null when it cannot be asked or fails.</summary>
        public Task<JsonNode?> Request(string method, JsonNode parameters)
        {
            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_work.IsAddingCompleted) { tcs.SetResult(null); return tcs.Task; }
            _work.Add(() =>
            {
                if (_client is not { Running: true } c) { tcs.TrySetResult(null); return; }
                c.Request(method, parameters.DeepClone()).ContinueWith(r => tcs.TrySetResult(r.IsCompletedSuccessfully ? r.Result : null), TaskScheduler.Default);
            });
            return tcs.Task;
        }

        public void Close(string path) { if (!_work.IsAddingCompleted) _work.Add(() => { if (_open.Remove(path)) _client?.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = UriOf(path) } }); }); }

        void Loop()
        {
            try
            {
                var client = _client = new LspClient(_command.Command, _command.Args, _root);
                _client.Notification += (method, p) => { if (method == "textDocument/publishDiagnostics") _owner.Publish(p, Name(_command)); };
                _client.ApplyEdit = e => { if (e == null || _owner.EditRequested is not { } h) return false; h(e); return true; };
                _client.Exited += () => { if (_client != client) return; _status = Name(_command) + " stopped" + (client.LastError is { Length: > 0 } e ? ": " + e : ""); _owner.Changed(); };
                var init = _client.Request("initialize", new JsonObject
                {
                    ["processId"] = Environment.ProcessId,
                    ["rootUri"] = UriOf(_root),
                    ["rootPath"] = _root,
                    ["workspaceFolders"] = new JsonArray(new JsonObject { ["uri"] = UriOf(_root), ["name"] = Path.GetFileName(_root) }),
                    ["capabilities"] = new JsonObject
                    {
                        ["textDocument"] = new JsonObject
                        {
                            ["synchronization"] = new JsonObject { ["didSave"] = false, ["dynamicRegistration"] = false },
                            ["publishDiagnostics"] = new JsonObject { ["relatedInformation"] = false },
                            ["diagnostic"] = new JsonObject { ["dynamicRegistration"] = false, ["relatedDocumentSupport"] = false },
                            // trees with the name's own range; without this pyright sends a flat list
                            ["documentSymbol"] = new JsonObject { ["hierarchicalDocumentSymbolSupport"] = true },
                            // CodeAction objects, not bare Commands; their edit may come later (codeAction/resolve)
                            ["codeAction"] = new JsonObject
                            {
                                ["codeActionLiteralSupport"] = new JsonObject
                                {
                                    ["codeActionKind"] = new JsonObject
                                    {
                                        ["valueSet"] = new JsonArray("", "quickfix", "refactor", "refactor.extract", "refactor.inline", "refactor.rewrite", "source", "source.organizeImports"),
                                    },
                                },
                                ["isPreferredSupport"] = true, ["disabledSupport"] = true, ["dataSupport"] = true,
                                ["resolveSupport"] = new JsonObject { ["properties"] = new JsonArray("edit") },
                            },
                        },
                        ["workspace"] = new JsonObject { ["workspaceFolders"] = true, ["configuration"] = true, ["applyEdit"] = true },
                    },
                });
                if (!init.Wait(TimeSpan.FromSeconds(60))) throw new InvalidOperationException("no answer to initialize in 60 s");
                _capabilities = init.Result?["capabilities"] as JsonObject ?? new JsonObject();
                _client.Notify("initialized", new JsonObject());
                _status = Name(_command);
                _owner.Changed();
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or AggregateException
                                       or System.ComponentModel.Win32Exception or IOException)
            {
                var why = ex is AggregateException a ? a.InnerException?.Message ?? a.Message : ex.Message;
                _status = Name(_command) + " failed: " + why;
                _owner.Changed();
                _work.CompleteAdding();
                // requests already queued answer null (no client) rather than never: a caller may be waiting on one
                var dead = _client;
                _client = null;                                    // first: its Exited then leaves the status alone
                try { dead?.Dispose(); } catch (Exception e) when (e is IOException or InvalidOperationException) { }
                foreach (var job in _work.GetConsumingEnumerable())
                    try { job(); } catch (Exception e) when (e is IOException or InvalidOperationException) { }
                return;
            }
            foreach (var job in _work.GetConsumingEnumerable())
                try { job(); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        }

        void Send(string path, string text)
        {
            if (_client is not { Running: true } c) return;
            var uri = UriOf(path);
            if (!_open.TryGetValue(path, out var version))
            {
                _open[path] = 1;
                c.Notify("textDocument/didOpen", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = LanguageId(_language, path), ["version"] = 1, ["text"] = text },
                });
            }
            else
            {
                _open[path] = ++version;
                // the whole text in one change without a range: allowed whatever sync kind the server prefers
                c.Notify("textDocument/didChange", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
                    ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }),
                });
            }
            PullDiagnostics(c, path);
        }

        // A server that only answers when asked (LSP 3.17 pull diagnostics; TypeScript 7 publishes nothing by itself,
        // measured): its findings for the edited file, and for the other open ones when one file's errors can depend
        // on another's ("interFileDependencies"). The answers go where pushed ones go.
        void PullDiagnostics(LspClient c, string edited)
        {
            if (_capabilities?["diagnosticProvider"] is not JsonObject provider) return;
            var paths = (bool?)provider["interFileDependencies"] == true ? _open.Keys.OrderBy(p => p != edited).ToList() : [edited];
            foreach (var path in paths)
            {
                var uri = UriOf(path);
                c.Request("textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } })
                    .ContinueWith(r =>
                    {
                        // "unchanged" keeps what is shown; a failed request (the file closed meanwhile) changes nothing
                        if (r.IsCompletedSuccessfully && r.Result is JsonObject { } report && (string?)report["kind"] == "full")
                            _owner.Publish(new JsonObject { ["uri"] = uri, ["diagnostics"] = report["items"]?.DeepClone() }, Name(_command));
                    }, TaskScheduler.Default);
            }
        }

        public void Dispose()
        {
            _work.CompleteAdding();
            _client?.Dispose();
        }
    }
}
