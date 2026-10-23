using Tui;
using Xunit;

public sealed class LspTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-lsp-" + Guid.NewGuid().ToString("N"));
    public LspTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    [Fact]
    public void Paths_and_uris_go_both_ways_whatever_the_server_writes()
    {
        var p = Path.Combine(_dir, "a b.py");
        Assert.Equal(Path.GetFullPath(p), LanguageServers.PathOf(LanguageServers.UriOf(p)));
        if (!OperatingSystem.IsWindows()) return;
        // VS Code style: lower-case drive, the colon escaped
        var vs = "file:///" + char.ToLowerInvariant(p[0]) + "%3A" + p[2..].Replace('\\', '/').Replace(" ", "%20", StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(p), LanguageServers.PathOf(vs), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_project_root_is_the_nearest_folder_with_a_marker()
    {
        var proj = Path.Combine(_dir, "proj");
        var src = Path.Combine(proj, "src", "deep");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(proj, "pyproject.toml"), "");
        Assert.Equal(proj, LanguageServers.RootOf("Python", Path.Combine(src, "m.py")));
        File.WriteAllText(Path.Combine(proj, "x.csproj"), "<Project />");
        Assert.Equal(proj, LanguageServers.RootOf("C#", Path.Combine(src, "c.cs")));
    }

    [Fact]
    public void The_editor_underlines_and_walks_through_diagnostics()
    {
        var p = Path.Combine(_dir, "a.py");
        File.WriteAllText(p, "x = 1\ny = undefined_name\n");
        var diags = new[] { new LspDiagnostic(1, 4, 1, 18, 1, "\"undefined_name\" is not defined", "Pyright") };
        var e = new EditorView { Diagnostics = _ => diags };
        e.Load(p);
        e.SetArea(new Rect(0, 0, 80, 5));
        var s = new Screen(80, 5);
        e.Draw(s);
        Assert.True(s.StyleAt(4, 1).Attr.HasFlag(Attr.Underline));
        Assert.False(s.StyleAt(2, 1).Attr.HasFlag(Attr.Underline));
        Assert.Equal("Error (Pyright): \"undefined_name\" is not defined", e.GoToNextProblem());
        Assert.Single(e.DiagnosticsAtCaret());
    }

    [Fact]
    public void Language_server_settings_add_replace_and_turn_off()
    {
        var (s, error) = Settings.Parse("""{ "lsp": true, "languageServers": { "Python": { "command": "pylsp", "args": ["-v"] }, "C#": "off" } }""");
        Assert.Null(error);
        Assert.Equal("pylsp", s.LanguageServers["Python"]!.Command);
        Assert.Equal(new[] { "-v" }, s.LanguageServers["Python"]!.Args);
        Assert.Null(s.LanguageServers["C#"]);
        Assert.NotNull(Settings.Parse("""{ "languageServers": { "Go": 5 } }""").Error);
    }

    [Fact]
    public void Completion_answers_become_candidates_in_the_servers_order()
    {
        var answer = System.Text.Json.Nodes.JsonNode.Parse("""
            { "isIncomplete": false, "items": [
              { "label": "upper", "kind": 2, "sortText": "b" },
              { "label": "path", "kind": 9, "sortText": "a" },
              { "label": "join(…)", "kind": 3, "sortText": "c", "insertText": "join(${1:a}, ${2:b})$0", "insertTextFormat": 2 },
              { "label": "Length", "kind": 10, "sortText": "d", "textEdit": { "newText": "Length", "range": {} } } ] }
            """);
        var items = LanguageServers.CompletionsFrom(answer);
        Assert.Equal(new[] { "path", "upper", "join(…)", "Length" }, items.Select(i => i.Label));
        Assert.Equal(("mod", "path"), (items[0].Tag, items[0].Insert));
        Assert.Equal("join(a, b)", items[2].Insert);                                      // a snippet without its tab stops

        var pool = Completion.FromServer(items);
        Assert.Equal(4, Completion.Filter(pool, "").Count);                                // right after a dot: all of them
        var j = Assert.Single(Completion.Filter(pool, "jo"));
        Assert.Equal(("join(…)", "f", "join(a, b)"), (j.Word, j.Tag, j.Insert));
        Assert.Empty(Completion.Filter(Completion.Collect(["alpha"], 1, 0, [], []), ""));   // words still need a prefix

        // a server that knows little does not hide the text's words and the language's keywords: they come after
        var merged = Completion.Merge(Completion.FromServer([("Namespace1", "cls", "Namespace1")]),
            Completion.Collect(["names here"], 5, 0, [], ["namespace"]));
        Assert.Equal(new[] { "Namespace1", "namespace" }, Completion.Filter(merged, "Names").Select(c => c.Word));
        Assert.True(Completion.Filter(merged, "names").Single(c => c.Word == "namespace").Keyword);
    }

    [Fact]
    public void Definitions_hovers_and_signatures_are_read_in_every_shape_servers_send()
    {
        var p = Path.Combine(_dir, "lib.py");
        var uri = LanguageServers.UriOf(p);
        static System.Text.Json.Nodes.JsonNode? J(string s) => System.Text.Json.Nodes.JsonNode.Parse(s);
        var range = """{ "start": { "line": 4, "character": 6 }, "end": { "line": 4, "character": 9 } }""";
        Assert.Equal((Path.GetFullPath(p), 4, 6), LanguageServers.LocationsFrom(J($$"""{ "uri": "{{uri}}", "range": {{range}} }"""))[0]);
        Assert.Single(LanguageServers.LocationsFrom(J($$"""[{ "uri": "{{uri}}", "range": {{range}} }]""")));
        Assert.Equal(4, LanguageServers.LocationsFrom(J($$"""[{ "targetUri": "{{uri}}", "targetRange": {{range}}, "targetSelectionRange": {{range}} }]"""))[0].Line);
        Assert.Empty(LanguageServers.LocationsFrom(null));

        Assert.Equal("def f(x: int) -> str\n\nDoc.", LanguageServers.HoverText(J("""{ "contents": { "kind": "markdown", "value": "```python\ndef f(x: int) -> str\n```\n\n\n\nDoc." } }""")));
        Assert.Equal("a\n\nb", LanguageServers.HoverText(J("""{ "contents": ["a", { "language": "python", "value": "b" }] }""")));

        // the editor's one "JavaScript" told apart by extension for tsserver
        Assert.Equal("typescript", LanguageServers.LanguageId("JavaScript", "a.ts"));
        Assert.Equal("typescriptreact", LanguageServers.LanguageId("JavaScript", "a.TSX"));
        Assert.Equal("javascriptreact", LanguageServers.LanguageId("JavaScript", "a.jsx"));
        Assert.Equal("javascript", LanguageServers.LanguageId("JavaScript", "a.mjs"));
        Assert.Equal("csharp", LanguageServers.LanguageId("C#", "a.cs"));

        var s = LanguageServers.SignatureFrom(J("""{ "signatures": [{ "label": "f(a, b)", "parameters": [{ "label": [2, 3] }, { "label": [5, 6] }] }], "activeParameter": 1 }"""))!.Value;
        Assert.Equal("b", s.Label[s.Start..s.End]);
        var named = LanguageServers.SignatureFrom(J("""{ "signatures": [{ "label": "g(first, second)", "parameters": [{ "label": "first" }, { "label": "second" }], "activeParameter": 0 }] }"""))!.Value;
        Assert.Equal("first", named.Label[named.Start..named.End]);
        Assert.Null(LanguageServers.SignatureFrom(J("""{ "signatures": [] }""")));
    }

    [Fact]
    public void A_completion_goes_in_right_after_a_dot()
    {
        var p = Path.Combine(_dir, "a.py");
        File.WriteAllText(p, "os.\n");
        var e = new EditorView();
        e.Load(p);
        e.GoToLine(1, 4);
        e.CompleteWith("path");
        Assert.Equal("os.path", e.Lines[0]);
        Assert.True(e.AutoCompleteAfterDot() == false);                                    // the caret is past the dot now
    }

    // ---------- real servers, when installed ----------
    static IReadOnlyList<LspDiagnostic> WaitFor(LanguageServers ls, string path, string lang, string text, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        ls.Sync(path, lang, 1, () => text);
        while (DateTime.UtcNow < deadline)
        {
            var d = ls.DiagnosticsFor(path);
            if (d.Count > 0) return d;
            Thread.Sleep(250);
        }
        throw new TimeoutException("no diagnostics: " + ls.StatusFor(path, lang));
    }

    [Fact]
    public void Pyright_reports_an_undefined_name_and_an_edit_clears_it()
    {
        if (LspClient.Resolve("pyright-langserver") == null) return;
        var p = Path.Combine(_dir, "a.py");
        const string bad = "x = 1\ny = undefined_name\n";
        File.WriteAllText(p, bad);
        using var ls = new LanguageServers(LanguageServers.Defaults);
        var d = WaitFor(ls, p, "Python", bad, 60);
        var u = Assert.Single(d, x => x.Message.Contains("undefined_name", StringComparison.Ordinal));
        Assert.Equal((1, 4), (u.Line, u.Col));
        ls.Sync(p, "Python", 2, () => "x = 1\ny = x\n");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && ls.DiagnosticsFor(p).Count > 0) Thread.Sleep(250);
        Assert.Empty(ls.DiagnosticsFor(p));
    }

    [Fact]
    public void Pyright_lists_members_after_a_dot()
    {
        if (LspClient.Resolve("pyright-langserver") == null) return;
        var p = Path.Combine(_dir, "m.py");
        const string text = "import os\nos.\n";
        File.WriteAllText(p, text);
        using var ls = new LanguageServers(LanguageServers.Defaults);
        ls.Sync(p, "Python", 1, () => text);
        var items = ls.Complete(p, "Python", 1, 3, '.').Result;
        Assert.Contains(items, i => i.Label == "path");
    }

    [Fact]
    public void Pyright_finds_a_definition_in_another_file_explains_it_and_shows_its_parameters()
    {
        if (LspClient.Resolve("pyright-langserver") == null) return;
        File.WriteAllText(Path.Combine(_dir, "pyrightconfig.json"), "{}");               // one project: both files
        var lib = Path.Combine(_dir, "lib.py");
        File.WriteAllText(lib, "def greet(name: str, times: int = 1) -> str:\n    \"\"\"Says hello.\"\"\"\n    return name\n");
        var main = Path.Combine(_dir, "main.py");
        const string text = "from lib import greet\ngreet(\"a\", \n";
        File.WriteAllText(main, text);
        using var ls = new LanguageServers(LanguageServers.Defaults);
        ls.Sync(main, "Python", 1, () => text);

        var where = LanguageServers.LocationsFrom(ls.Ask(main, "Python", "textDocument/definition", 1, 1).Result);
        var d = Assert.Single(where);
        Assert.Equal((Path.GetFullPath(lib), 0), (d.Path, d.Line));

        var hover = LanguageServers.HoverText(ls.Ask(main, "Python", "textDocument/hover", 1, 1).Result);
        Assert.Contains("greet", hover, StringComparison.Ordinal);
        Assert.Contains("Says hello.", hover, StringComparison.Ordinal);

        var sig = LanguageServers.SignatureFrom(ls.Ask(main, "Python", "textDocument/signatureHelp", 1, 11).Result);
        Assert.NotNull(sig);
        Assert.Contains("times", sig.Value.Label[sig.Value.Start..sig.Value.End], StringComparison.Ordinal);   // the second argument
    }

    [Fact]
    public void A_rename_answer_gives_edits_by_file_and_none_when_it_moves_files()
    {
        var a = Path.GetFullPath(Path.Combine(_dir, "a.py"));
        var b = Path.GetFullPath(Path.Combine(_dir, "b.py"));
        string Edit(int line, int col, string text) =>
            $$"""{ "range": { "start": { "line": {{line}}, "character": {{col}} }, "end": { "line": {{line}}, "character": {{col + 3}} } }, "newText": "{{text}}" }""";
        var changes = LanguageServers.WorkspaceEditFrom(System.Text.Json.Nodes.JsonNode.Parse(
            $$"""{ "changes": { "{{LanguageServers.UriOf(a)}}": [ {{Edit(0, 4, "new")}} ], "{{LanguageServers.UriOf(b)}}": [ {{Edit(1, 0, "new")}}, {{Edit(2, 0, "new")}} ] } }"""));
        Assert.Equal(1, changes![a].Count);
        Assert.Equal(2, changes[b].Count);
        var docs = LanguageServers.WorkspaceEditFrom(System.Text.Json.Nodes.JsonNode.Parse(
            $$"""{ "documentChanges": [ { "textDocument": { "uri": "{{LanguageServers.UriOf(a)}}", "version": 3 }, "edits": [ {{Edit(0, 4, "new")}} ] } ] }"""));
        Assert.Equal(new LspEdit(0, 4, 0, 7, "new"), Assert.Single(docs![a]));
        Assert.Null(LanguageServers.WorkspaceEditFrom(System.Text.Json.Nodes.JsonNode.Parse(
            $$"""{ "documentChanges": [ { "kind": "rename", "oldUri": "{{LanguageServers.UriOf(a)}}", "newUri": "{{LanguageServers.UriOf(b)}}" } ] }""")));
    }

    [Fact]
    public void An_outline_from_symbol_trees_or_flat_lists_leaves_out_what_is_inside_functions()
    {
        static string Range(int line, int col) =>
            $$"""{ "start": { "line": {{line}}, "character": {{col}} }, "end": { "line": {{line}}, "character": {{col + 1}} } }""";
        var tree = LanguageServers.OutlineFrom(System.Text.Json.Nodes.JsonNode.Parse($$"""
            [ { "name": "C", "kind": 5, "range": {{Range(0, 0)}}, "selectionRange": {{Range(0, 6)}}, "children": [
                { "name": "m", "kind": 6, "range": {{Range(1, 4)}}, "selectionRange": {{Range(1, 8)}}, "children": [
                  { "name": "local", "kind": 13, "range": {{Range(2, 8)}}, "selectionRange": {{Range(2, 8)}} } ] } ] },
              { "name": "f", "kind": 12, "range": {{Range(4, 0)}}, "selectionRange": {{Range(4, 4)}} } ]
            """));
        Assert.Equal(new[] { (0, "C", "class", 0, 6), (1, "m", "method", 1, 8), (0, "f", "function", 4, 4) }, tree.ToArray());
        var flat = LanguageServers.OutlineFrom(System.Text.Json.Nodes.JsonNode.Parse($$"""
            [ { "name": "m", "kind": 6, "containerName": "C", "location": { "uri": "file:///a.cs", "range": {{Range(1, 4)}} } },
              { "name": "local", "kind": 13, "containerName": "m", "location": { "uri": "file:///a.cs", "range": {{Range(2, 8)}} } },
              { "name": "C", "kind": 5, "location": { "uri": "file:///a.cs", "range": {{Range(0, 0)}} } } ]
            """));
        Assert.Equal(new[] { (0, "C", "class", 0, 0), (1, "m", "method", 1, 4) }, flat.ToArray());
    }

    [Fact]
    public void Pyright_outlines_classes_methods_and_functions()
    {
        if (LspClient.Resolve("pyright-langserver") == null) return;
        var p = Path.Combine(_dir, "o.py");
        const string text = "class Greeter:\n    def greet(self, name):\n        word = name\n        return word\n\ndef main():\n    pass\n";
        File.WriteAllText(p, text);
        using var ls = new LanguageServers(LanguageServers.Defaults);
        ls.Sync(p, "Python", 1, () => text);
        var outline = LanguageServers.OutlineFrom(ls.AskFile(p, "Python", "textDocument/documentSymbol", new System.Text.Json.Nodes.JsonObject()).Result);
        Assert.Equal(new[] { (0, "Greeter", "class", 0, 6), (1, "greet", "method", 1, 8), (0, "main", "function", 5, 4) }, outline.ToArray());
    }

    [Fact]
    public void Pyright_renames_a_function_in_both_files_that_use_it()
    {
        if (LspClient.Resolve("pyright-langserver") == null) return;
        File.WriteAllText(Path.Combine(_dir, "pyrightconfig.json"), "{}");
        var lib = Path.Combine(_dir, "lib.py");
        File.WriteAllText(lib, "def greet(name):\n    return name\n");
        var main = Path.Combine(_dir, "main.py");
        const string text = "from lib import greet\ngreet(\"a\")\n";
        File.WriteAllText(main, text);
        using var ls = new LanguageServers(LanguageServers.Defaults);
        ls.Sync(main, "Python", 1, () => text);
        ls.Ask(main, "Python", "textDocument/hover", 1, 1).Wait();                // the server has started
        Assert.True(ls.Can(main, "Python", "renameProvider"));
        Assert.True(ls.CanPrepareRename(main, "Python"));
        Assert.NotNull(ls.Ask(main, "Python", "textDocument/prepareRename", 1, 1).Result);
        Assert.Null(ls.Ask(main, "Python", "textDocument/prepareRename", 1, 7).Result);     // in the string "a"
        var byFile = LanguageServers.WorkspaceEditFrom(ls.Ask(main, "Python", "textDocument/rename", 1, 1,
            new System.Text.Json.Nodes.JsonObject { ["newName"] = "hello" }).Result)!;
        Assert.Equal(2, byFile[Path.GetFullPath(main)].Count);                 // the import and the call
        var e = new EditorView();
        e.Load(lib);
        Assert.True(e.ApplyEdits(byFile[Path.GetFullPath(lib)]));
        Assert.Equal("def hello(name):", e.Lines[0]);

        // where greet is used: its definition, the import and the call, sorted by file and line
        var refs = LanguageServers.ReferencesFrom(ls.Ask(main, "Python", "textDocument/references", 1, 1,
            new System.Text.Json.Nodes.JsonObject { ["context"] = new System.Text.Json.Nodes.JsonObject { ["includeDeclaration"] = true } }).Result);
        Assert.Equal(new[] { (Path.GetFullPath(lib), 0, 4, 5), (Path.GetFullPath(main), 0, 16, 5), (Path.GetFullPath(main), 1, 0, 5) }, refs.ToArray());
    }

    [Fact]
    public void Csharp_ls_lists_members_of_a_string()
    {
        if (LspClient.Resolve("csharp-ls") == null) return;
        File.WriteAllText(Path.Combine(_dir, "t.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup></Project>");
        var p = Path.Combine(_dir, "C.cs");
        const string text = "class C { int M(string s) { return s.\n; } }\n";
        File.WriteAllText(p, text);
        using var ls = new LanguageServers(LanguageServers.Defaults);
        ls.Sync(p, "C#", 1, () => text);
        var deadline = DateTime.UtcNow.AddSeconds(120);
        IReadOnlyList<(string Label, string Tag, string Insert)> items = [];
        while (DateTime.UtcNow < deadline && !items.Any(i => i.Label == "Length"))
        {
            items = ls.Complete(p, "C#", 0, 37, '.').Result;                             // the project may still be loading
            if (!items.Any(i => i.Label == "Length")) Thread.Sleep(1000);
        }
        Assert.Contains(items, i => i.Label == "Length");
    }

    [Fact]
    public void Csharp_ls_formats_a_file_and_pyright_says_it_does_not_format()
    {
        if (LspClient.Resolve("csharp-ls") == null) return;
        File.WriteAllText(Path.Combine(_dir, "t.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup></Project>");
        var p = Path.Combine(_dir, "C.cs");
        const string text = "class C\n{\nint M( ){\nreturn 1;\n}\n}\n";      // Roslyn keeps one-line blocks on one line
        File.WriteAllText(p, text);
        using var ls = new LanguageServers(LanguageServers.Defaults);
        ls.Sync(p, "C#", 1, () => text);
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline && ls.Can(p, "C#", "documentFormattingProvider") == null) Thread.Sleep(250);
        Assert.True(ls.Can(p, "C#", "documentFormattingProvider"));
        var options = new System.Text.Json.Nodes.JsonObject { ["options"] = new System.Text.Json.Nodes.JsonObject { ["tabSize"] = 4, ["insertSpaces"] = true } };
        IReadOnlyList<LspEdit> edits = [];
        while (DateTime.UtcNow < deadline && edits.Count == 0)                 // the project may still be loading
        {
            edits = LanguageServers.EditsFrom(ls.AskFile(p, "C#", "textDocument/formatting", options).Result);
            if (edits.Count == 0) Thread.Sleep(1000);
        }
        var e = new EditorView();
        e.Load(p);
        Assert.True(e.ApplyEdits(edits));
        Assert.Contains("    int M()", e.Lines);
        Assert.Contains("        return 1;", e.Lines);

        if (LspClient.Resolve("pyright-langserver") == null) return;
        var py = Path.Combine(_dir, "m.py");
        File.WriteAllText(py, "x=1\n");
        ls.Sync(py, "Python", 1, () => "x=1\n");
        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until && ls.Can(py, "Python", "documentFormattingProvider") == null) Thread.Sleep(250);
        Assert.False(ls.Can(py, "Python", "documentFormattingProvider"));
    }

    [Fact]
    public void Actions_that_can_be_taken_come_with_the_preferred_first()
    {
        var acts = LanguageServers.ActionsFrom(System.Text.Json.Nodes.JsonNode.Parse("""
            [ { "title": "Generate class" },
              { "title": "Not now", "disabled": { "reason": "no" } },
              { "title": "using System.Text;", "kind": "quickfix", "isPreferred": true },
              { "title": "Organize imports", "command": "source.organizeImports" } ]
            """));
        Assert.Equal(new[] { "using System.Text;", "Generate class", "Organize imports" }, acts.Select(a => a.Title).ToArray());
        Assert.Equal("quickfix", acts[0].Kind);
    }

    [Fact]
    public void Csharp_ls_offers_the_missing_using_and_its_edit_adds_it()
    {
        if (LspClient.Resolve("csharp-ls") == null) return;
        File.WriteAllText(Path.Combine(_dir, "t.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup></Project>");
        var p = Path.Combine(_dir, "C.cs");
        const string bad = "class C { StringBuilder b = new(); }\n";
        File.WriteAllText(p, bad);
        using var ls = new LanguageServers(LanguageServers.Defaults);
        var d = WaitFor(ls, p, "C#", bad, 180).First(x => x.Message.Contains("StringBuilder", StringComparison.Ordinal));
        Assert.NotNull(d.Raw);                                                 // handed back to the server as it came
        var acts = LanguageServers.ActionsFrom(ls.CodeActions(p, "C#", d.Line, d.Col, d.EndLine, d.EndCol, [d]).Result);
        Assert.Equal("using System.Text;", acts[0].Title);                     // the preferred one first
        Assert.Null(acts[0].Raw["edit"]);                                      // csharp-ls leaves the edit for codeAction/resolve
        var resolved = ls.Request(p, "C#", "codeAction/resolve", acts[0].Raw).Result;
        var byFile = LanguageServers.WorkspaceEditFrom(resolved?["edit"])!;
        var e = new EditorView();
        e.Load(p);
        Assert.True(e.ApplyEdits(byFile[Path.GetFullPath(p)]));
        Assert.StartsWith("using System.Text;", e.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Csharp_ls_reports_a_type_error_in_a_project()
    {
        if (LspClient.Resolve("csharp-ls") == null) return;
        File.WriteAllText(Path.Combine(_dir, "t.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup></Project>");
        var p = Path.Combine(_dir, "C.cs");
        const string bad = "class C { int M() { return \"text\"; } }\n";
        File.WriteAllText(p, bad);
        using var ls = new LanguageServers(LanguageServers.Defaults);
        var d = WaitFor(ls, p, "C#", bad, 180);
        Assert.Contains(d, x => x.Severity == 1 && x.Line == 0);
    }
}
