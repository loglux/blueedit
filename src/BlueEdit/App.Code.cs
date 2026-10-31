using System.Text.Json.Nodes;
using Tui;

sealed partial class App
{
    // The language servers named in the settings plus the defaults, keeping only those whose command is installed.
    internal static Dictionary<string, ServerCommand> LspConfig(Settings s)
    {
        var all = new Dictionary<string, ServerCommand>(LanguageServers.Defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var (lang, cmd) in s.LanguageServers)
            if (cmd == null) all.Remove(lang); else all[lang] = cmd;
        return all.Where(x => LspClient.Resolve(x.Value.Command) != null).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }

    void ShowCodeTools()
    {
        var tools = CodeTools.All;
        // a table to read (what there is, where), the actions as buttons under it: as rows of one list the tools and
        // the actions read as one kind of thing, and Enter on a tool's row installing it was not to be guessed
        static string Short(string path) => path.Length > 34 ? "…" + path[^33..] : path;
        string Row(CodeTools.Tool t) => $"{t.Name,-11}{T(t.Purpose),-38} " + (CodeTools.Find(t) is { } f
            ? (f.Ours ? T("installed here") : Tf("found: {0}", Short(f.Path))) : Tf("not installed, {0}", T(t.Needs)));
        var table = string.Join("\n", tools.Select(Row));
        var pick = Dialogs.Fix(app, Tf("Code tools — in {0}", CodeTools.Root), [], table,
            [T("Install the missing"), T("Remove what was installed here"), T("Close")], "", none: "", table: true);
        if (pick is not (0 or 1)) return;
        if (installingTools) { Note(T("Code tools: an installation is running")); return; }
        if (pick == 1)
        {
            lsp?.Dispose(); lsp = null; lspConfigKey = "";                // the servers hold their files open
            try { CodeTools.Remove(null); Note(Tf("Code tools removed from {0}", CodeTools.Root)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Note(Tf("Code tools: {0}", ex.Message)); }
            ApplySettings(settings);
            return;
        }
        var todo = tools.Where(t => CodeTools.Find(t) == null).ToArray();
        if (todo.Length == 0) { Note(T("Code tools: all are installed")); return; }
        installingTools = true;
        Task.Run(async () =>
        {
            var done = new List<string>();
            foreach (var t in todo)
            {
                var error = await CodeTools.Install(t, s => app.Post(() => Note(Tf("Code tools: {0}", s))));
                done.Add(error == null ? Tf("{0} installed", t.Name) : Tf("{0} failed: {1}", t.Name, error));
            }
            app.Post(() =>
            {
                installingTools = false;
                lspConfigKey = "";                                              // the servers found now are started
                ApplySettings(settings);
                Note(Tf("Code tools: {0}", string.Join("; ", done)));
            });
        });
    }

    void HintCodeTools()
    {
        if (!settings.Lsp || editor.LangName is not { } lang || !LanguageServers.Defaults.TryGetValue(lang, out var cmd)) return;
        if (lsp?.Handles(lang) == true || !codeToolHints.Add(lang) || settings.LanguageServers.ContainsKey(lang)) return;
        Note(Tf("{0}: no language server installed (errors, completion…): Tools > Code tools", lang));
    }

    void SyncLanguageServer()
    {
        HintCodeTools();
        if (lsp == null || editor.Path == null) return;
        lsp.Sync(editor.Path, editor.LangName, editor.EditVersion, () => string.Join('\n', editor.Lines));
        if (lsp.StatusFor(editor.Path, editor.LangName) is { } st && (st.Contains(" failed", StringComparison.Ordinal) || st.Contains(" stopped", StringComparison.Ordinal)) && lspFailuresShown.Add(st))
            Note(Tf("Language server: {0}", st));
    }

    // F7 on an error or warning from the language server: what it says, in full.
    void ShowDiagnostics(IReadOnlyList<LspDiagnostic> ds)
    {
        var lines = new List<string> { "" };
        foreach (var d in ds)
        {
            lines.Add("  " + Tf("{0} ({1}), line {2}:", EditorView.SeverityName(d.Severity), d.Source, d.Line + 1));
            foreach (var l in d.Message.Split('\n')) lines.Add("    " + l.TrimEnd('\r'));
            lines.Add("");
        }
        Note($"{EditorView.SeverityName(ds[0].Severity)} ({ds[0].Source}): {EditorView.FirstLine(ds[0].Message)}");
        Dialogs.ShowText(app, ds.Count == 1 ? EditorView.SeverityName(ds[0].Severity) : Tn(ds.Count, "{0} finding here", "{0} findings here"), lines);
    }

    // Ctrl+Space: the language server's completions when the file has one (waiting for it at most a second), otherwise
    // words of all open documents and the language's keywords that start like the word before the caret. A single
    // candidate is put in at once; otherwise a list opens under the word.
    void Complete()
    {
        if (editor.ReadOnlyView || completing) return;
        var w = editor.WordBeforeCaret();
        var server = ServerPool(TimeSpan.FromMilliseconds(400));
        if (w is not { } word)
        {
            // right after a dot: the server's members, or nothing to go on
            if (server != null && Completion.Filter(server, "").Count > 0) OpenCompletion(server, auto: false);
            else Note(T("Completion: type the start of a word, then Ctrl+Space"));
            return;
        }
        var pool = WordPool(word.Start);
        if (server != null) pool = Completion.Merge(server, pool);          // the server's first, then words and keywords
        var items = Completion.Filter(pool, word.Prefix);
        if (items.Count == 0) { Note(Tf("No words starting with \"{0}\"", word.Prefix)); return; }
        if (items.Count == 1) { editor.CompleteWith(items[0].Insert ?? items[0].Word); return; }
        OpenCompletion(pool, auto: false);
    }

    // Words of all open documents and the language's keywords (the word at the caret, starting at `start`, left out).
    Completion.Pool WordPool(int start) =>
        Completion.Collect(editor.Lines, editor.Row, start, docs.Where((_, i) => i != cur).Select(EditorView.LinesOf), editor.LanguageKeywords, editor.HyphenWords);

    // The language server's completions at the caret, waiting at most `wait`; null without a server or an answer. A
    // server still starting (or loading its project) is not waited for at all: Ctrl+Space must not stall the editor.
    Completion.Pool? ServerPool(TimeSpan wait)
    {
        if (lsp == null || editor.Path is not { } path || !lsp.Handles(editor.LangName)) return null;
        if (lsp.StatusFor(path, editor.LangName) is not { } st || st.Contains(' ', StringComparison.Ordinal)) return null;   // "… starting", "… failed: …"
        lsp.Sync(path, editor.LangName, editor.EditVersion, () => string.Join('\n', editor.Lines));
        var task = lsp.Complete(path, editor.LangName, editor.Row, editor.Col, null);
        return task.Wait(wait) && task.Result.Count > 0 ? Completion.FromServer(task.Result) : null;
    }

    void OpenCompletion(Completion.Pool pool, bool auto)
    {
        completing = true;
        try { app.RunModal(new CompletionPopup(editor, pool, () => mainView!.Theme, auto)); }
        finally { completing = false; }
        if (dotWhileListed) { dotWhileListed = false; AutoComplete('.'); }      // "a.b." — the dot typed in the list asks again
    }

    // The list opening by itself (setting completeWhileTyping): after a dot, or the third letter of a word. With a
    // language server the request goes in the background and the list opens when the answer comes — if the caret has
    // not moved on meanwhile; typing never waits. Without one, words of the text: collected once per word, and a word
    // with nothing to offer is not looked up again while it is being typed on.
    void AutoComplete(char typed)
    {
        const int MinChars = 3;
        if (typed is '(' or ',') { AskSignature(); return; }
        if (typed == ')') { signature = null; return; }
        if (completing) { if (typed == '.') dotWhileListed = true; return; }
        bool dot = typed == '.';
        if (dot ? !editor.AutoCompleteAfterDot() : !editor.AutoCompleteHere(MinChars)) return;
        if (lsp != null && editor.Path is { } path && lsp.Handles(editor.LangName))
        {
            lsp.Sync(path, editor.LangName, editor.EditVersion, () => string.Join('\n', editor.Lines));
            int asked = ++completionRequest, row = editor.Row, col = editor.Col, version = editor.EditVersion;
            lsp.Complete(path, editor.LangName, row, col, dot ? '.' : null).ContinueWith(r => app.Post(() =>
            {
                if (asked != completionRequest || completing || editor.Path != path || editor.Row != row || editor.Col != col || editor.EditVersion != version) return;
                var w = editor.WordBeforeCaret();
                var pool = r.Result.Count > 0 ? Completion.FromServer(r.Result) : null;
                if (pool != null && w != null) pool = Completion.Merge(pool, WordPool(w.Value.Start));   // words after the server's
                if (pool != null && Completion.Filter(pool, w?.Prefix ?? "") is var items && items.Count > 0 && !items.All(c => c.Word == w?.Prefix))
                    OpenCompletion(pool, auto: true);
                else if (!dot) AutoCompleteWords();
            }), TaskScheduler.Default);
            return;
        }
        if (!dot) AutoCompleteWords();
    }

    void AutoCompleteWords()
    {
        if (completing || editor.WordBeforeCaret() is not { } w) return;
        if (noCompletionFor == (editor.Row, w.Start)) return;
        var pool = WordPool(w.Start);
        var items = Completion.Filter(pool, w.Prefix);
        if (items.Count == 0 || items.All(c => c.Word == w.Prefix)) { noCompletionFor = (editor.Row, w.Start); return; }
        noCompletionFor = null;
        OpenCompletion(pool, auto: true);
    }

    // ( or , typed in code: the language server's signature of the function being called goes to the status line, the
    // parameter being typed in brackets — asked in the background, shown while the caret stays on the line.
    void AskSignature()
    {
        if (lsp == null || editor.Path is not { } path || !lsp.Handles(editor.LangName)) return;
        lsp.Sync(path, editor.LangName, editor.EditVersion, () => string.Join('\n', editor.Lines));
        int row = editor.Row;
        lsp.Ask(path, editor.LangName, "textDocument/signatureHelp", row, editor.Col).ContinueWith(r => app.Post(() =>
        {
            if (editor.Path != path || editor.Row != row) return;
            if (LanguageServers.SignatureFrom(r.Result) is not { } s) { signature = null; return; }
            var label = s.Start >= 0 && s.End > s.Start && s.End <= s.Label.Length
                ? s.Label[..s.Start] + "[" + s.Label[s.Start..s.End] + "]" + s.Label[s.End..]
                : s.Label;
            signature = label.Length > 100 ? label[..99] + "…" : label;
            signatureRow = row;
        }), TaskScheduler.Default);
    }

    // F12: to where the symbol at the caret is defined — this file or another (opened); several: a list to pick from.
    void GoToDefinition()
    {
        if (lsp == null || editor.Path is not { } path || !lsp.Handles(editor.LangName))
        {
            Note(T("Go to definition needs a language server for this file (docs/lsp.md)"));
            return;
        }
        if (lsp.StatusFor(path, editor.LangName) is { } st && st.Contains(' ', StringComparison.Ordinal)) { Note(Tf("Language server: {0}", st)); return; }
        lsp.Sync(path, editor.LangName, editor.EditVersion, () => string.Join('\n', editor.Lines));
        Note(T("Looking for the definition...")); app.Render();
        var task = lsp.Ask(path, editor.LangName, "textDocument/definition", editor.Row, editor.Col);
        if (!task.Wait(TimeSpan.FromSeconds(5))) { Note(Tf("No answer from the language server in {0} s", 5)); return; }
        var places = LanguageServers.LocationsFrom(task.Result);
        if (places.Count == 0) { Note(T("No definition found here")); return; }
        int pick = 0;
        if (places.Count > 1)
        {
            var items = places.Select(p => $"{System.IO.Path.GetFileName(p.Path)}:{p.Line + 1}   {p.Path}").ToList();
            if (Dialogs.Pick(app, T("Definitions"), items) is not int i) return;
            pick = i;
        }
        var (to, line, col) = places[pick];
        var from = Here();
        if (!string.Equals(to, path, StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(to)) { Note(Tf("The definition is in {0}, which is not a file here", to)); return; }
            OpenPath(to);
        }
        editor.GoToLine(line + 1, col + 1);
        jumps.Record(from, Here());
        Note(Tf("Definition: {0}:{1} (Shift+F12 goes back)", System.IO.Path.GetFileName(to), line + 1));
    }

    // Ctrl+F12: formats the file — by the formatter named in the settings, else by the language server when it can (the
    // selected lines only, when it can format a range), else by a formatting program found installed (Python: ruff,
    // black; Formatters.cs). The answer comes in the background and is dropped when the text changed meanwhile. Not
    // Ctrl+Alt+L (JetBrains): the Windows console takes Ctrl+Alt for AltGr and passes on Ctrl+L, which deletes the line
    // (docs/tech/editing.md).
    void FormatCode()
    {
        if (editor.Path is not { } path || editor.LangName is not { } lang)
        {
            Note(T("Formatting is for code files (docs/lsp.md)"));
            return;
        }
        var program = Formatters.For(lang, settings.Formatters);
        bool serverCan = lsp != null && lsp.Can(path, lang, "documentFormattingProvider") == true;
        if (program is { } fp && (fp.Configured || !serverCan)) { FormatWith(fp.Command, path); return; }
        if (lsp == null || !lsp.Handles(lang) || lsp.Can(path, lang, "documentFormattingProvider") == false)
        {
            var why = settings.Formatters.TryGetValue(lang, out var off) && off == null ? T("formatting is off in settings.json")
                : Formatters.InstallHint(lang) is { } hint ? hint : T("no formatter known");
            Note($"{lang}: {why} (docs/lsp.md)");
            return;
        }
        if (lsp.StatusFor(path, lang) is { } st && st.Contains(' ', StringComparison.Ordinal)) { Note(Tf("Language server: {0}", st)); return; }
        var span = editor.SelectionSpan;
        bool range = span != null && lsp.Can(path, lang, "documentRangeFormattingProvider") == true;
        lsp.Sync(path, lang, editor.EditVersion, () => string.Join('\n', editor.Lines));
        var p = new JsonObject { ["options"] = new JsonObject { ["tabSize"] = editor.TabSize, ["insertSpaces"] = true } };
        if (range)
        {
            var ((sl, _), (el, ec)) = span!.Value;
            if (ec == 0 && el > sl) el--;                                       // a selection ending at a line's start: not that line
            p["range"] = new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = sl, ["character"] = 0 },
                ["end"] = new JsonObject { ["line"] = el, ["character"] = editor.Lines[el].Length },
            };
        }
        int version = editor.EditVersion;
        Note(range ? T("Formatting the selected lines...") : T("Formatting..."));
        lsp.AskFile(path, lang, range ? "textDocument/rangeFormatting" : "textDocument/formatting", p)
            .WaitAsync(TimeSpan.FromSeconds(30))
            .ContinueWith(r => app.Post(() =>
            {
                if (!r.IsCompletedSuccessfully) { Note(Tf("No answer from the language server in {0} s", 30)); return; }
                ApplyFormatting(path, version, LanguageServers.EditsFrom(r.Result), "the server");
            }), TaskScheduler.Default);
    }

    // The whole file through a formatting program: its output replaces the text, as one edit around what changed.
    void FormatWith(ServerCommand command, string path)
    {
        string text = string.Join('\n', editor.Lines);
        int version = editor.EditVersion;
        var name = Formatters.Name(command);
        Note(editor.HasSelection ? Tf("Formatting the whole file with {0} (it formats files, not lines)...", name) : Tf("Formatting with {0}...", name));
        Formatters.Run(command, path, text, TimeSpan.FromSeconds(30)).ContinueWith(r => app.Post(() =>
        {
            var (output, error) = r.Result;
            if (output == null) { Note(error ?? Tf("{0} failed", name)); return; }
            ApplyFormatting(path, version, Formatters.Diff(text, output), name);
        }), TaskScheduler.Default);
    }

    void ApplyFormatting(string path, int version, IReadOnlyList<LspEdit> edits, string by)
    {
        if (editor.Path != path || editor.EditVersion != version) { Note(T("Not formatted: the text changed meanwhile")); return; }
        int before = editor.EditVersion;
        var old = editor.Lines.ToList();
        if (!editor.ApplyEdits(edits)) { Note(Tf("The changes from {0} do not fit the text; nothing changed", by)); return; }
        // how many lines it touched, so that a press shows what it did (a formatted file looks much the same)
        int changed = ChangedLines(old, editor.Lines);
        Note(editor.EditVersion == before || changed == 0 ? Tf("Already formatted: {0} changed nothing", by)
            : Tf("Formatted by {0}, lines changed: {1} (Ctrl+Z undoes it)", by, changed));
    }

    // The lines the new text has that the old one had not, or the lines that went, whichever is more (blank lines
    // removed are changes too): a count without a full diff, each line matched once
    internal static int ChangedLines(IEnumerable<string> before, IEnumerable<string> after)
    {
        var left = before.GroupBy(l => l, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        int added = 0;
        foreach (var line in after)
            if (left.TryGetValue(line, out int n) && n > 0) left[line] = n - 1;
            else added++;
        return Math.Max(added, left.Values.Sum());
    }

    // Ctrl+F2: renames the symbol at the caret everywhere the language server knows it (F2 in VS Code; F2 saves here).
    // The server's changes go into the files as edits: open files get them as one undo step each, files not open are
    // opened with them, unsaved — File > Save all writes them.
    void RenameSymbol()
    {
        if (lsp == null || editor.Path is not { } path || editor.LangName is not { } lang || !lsp.Handles(lang))
        {
            Note(T("Renaming needs a language server for this file (docs/lsp.md)"));
            return;
        }
        if (lsp.StatusFor(path, lang) is { } st && st.Contains(' ', StringComparison.Ordinal)) { Note(Tf("Language server: {0}", st)); return; }
        if (lsp.Can(path, lang, "renameProvider") != true) { Note(Tf("{0} does not rename (docs/lsp.md)", lsp.StatusFor(path, lang))); return; }
        lsp.Sync(path, lang, editor.EditVersion, () => string.Join('\n', editor.Lines));
        int row = editor.Row, col = editor.Col;
        string? name = editor.WordAtCursor()?.Word;
        if (lsp.CanPrepareRename(path, lang))
        {
            // the server says whether this can be renamed, and what the name is (a range or a placeholder)
            Note(T("Asking the language server...")); app.Render();
            var prep = lsp.Ask(path, lang, "textDocument/prepareRename", row, col);
            if (!prep.Wait(TimeSpan.FromSeconds(5))) { Note(Tf("No answer from the language server in {0} s", 5)); return; }
            // null: nothing to rename here — or, measured on the CI runner, pyright not ready yet: a name in code still
            // gets the dialog, and the rename's own answer decides
            if (prep.Result is null && !editor.CaretInCode) { Note(T("Nothing to rename here")); return; }
            if ((string?)prep.Result?["placeholder"] is { Length: > 0 } ph) name = ph;
            else if ((prep.Result?["range"] ?? prep.Result) is JsonObject r && r["start"]?["line"] is { } l && (int)l == (int?)r["end"]?["line"])
            {
                int s = (int)r["start"]!["character"]!, e = (int)r["end"]!["character"]!;
                if (0 <= s && s < e && e <= editor.Lines[row].Length) name = editor.Lines[row][s..e];
            }
        }
        if (name == null) { Note(T("Nothing to rename here: put the caret on a name")); return; }
        var to = Dialogs.Prompt(app, T("Rename"), Tf("Rename {0} to:", name), name, replaceOnType: true)?.Trim();
        if (string.IsNullOrEmpty(to) || to == name) return;
        int version = editor.EditVersion;
        Note(Tf("Renaming {0} to {1}...", name, to));
        lsp.Ask(path, lang, "textDocument/rename", row, col, new JsonObject { ["newName"] = to })
            .WaitAsync(TimeSpan.FromSeconds(30))
            .ContinueWith(r => app.Post(() =>
            {
                if (!r.IsCompletedSuccessfully) { Note(Tf("No answer from the language server in {0} s", 30)); return; }
                if (editor.Path != path || editor.EditVersion != version) { Note(T("Not renamed: the text changed meanwhile")); return; }
                if (r.Result is null) { Note(Tf("{0} cannot be renamed here", name)); return; }
                if (LanguageServers.WorkspaceEditFrom(r.Result) is not { } byFile) { Note(T("The server wants to create or move files too; nothing changed")); return; }
                ApplyRename(path, byFile, name, to);
            }), TaskScheduler.Default);
    }

    void ApplyRename(string from, IReadOnlyDictionary<string, IReadOnlyList<LspEdit>> byFile, string name, string to) =>
        Note(ApplyWorkspaceEdit(from, byFile) is { } done ? $"{name} → {to}: {done}" : Tf("Nothing to rename for {0}", name));

    // A server's changes to files (a WorkspaceEdit's, by file) through the editor, each file one undo step: open files in
    // place, the others opened with them unsaved; back to the document `from` after. What was done, or null for nothing.
    string? ApplyWorkspaceEdit(string from, IReadOnlyDictionary<string, IReadOnlyList<LspEdit>> byFile)
    {
        int changes = 0, files = 0, opened = 0;
        var failed = new List<string>();
        int back = cur;
        // this file first, then the others in name order; each through the editor, so each has its own undo step
        foreach (var (file, edits) in byFile.OrderBy(x => !string.Equals(x.Key, from, StringComparison.OrdinalIgnoreCase)).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (edits.Count == 0) continue;
            bool open = docs.Exists(d => d.Path != null && string.Equals(d.Path, file, StringComparison.OrdinalIgnoreCase));
            if (!open && !File.Exists(file)) { failed.Add(System.IO.Path.GetFileName(file)); continue; }
            OpenPath(file);
            if (!editor.ApplyEdits(edits)) { failed.Add(System.IO.Path.GetFileName(file)); continue; }
            changes += edits.Count; files++;
            if (!open) opened++;
        }
        Switch(docs.FindIndex(d => d.Path != null && string.Equals(d.Path, from, StringComparison.OrdinalIgnoreCase)) is int i and >= 0 ? i : back);
        if (changes == 0 && failed.Count == 0) return null;
        var msg = Tn(changes, "{0} change", "{0} changes") + " " + Tn(files, "in {0} file", "in {0} files");
        if (opened > 0) msg += ", " + Tf("{0} opened (File > Save all)", opened);
        if (failed.Count > 0) msg += "; " + Tf("not changed (their text differs from the server's): {0}", string.Join(", ", failed));
        return msg;
    }

    // A WorkspaceEdit as JSON (from an action or sent by the server): applied, and a note on what was done.
    string ApplyServerEdit(JsonNode edit, string by)
    {
        if (LanguageServers.WorkspaceEditFrom(edit) is not { } byFile) return Tf("{0} wants to create or move files too; nothing changed", by);
        var from = editor.Path ?? "";
        return ApplyWorkspaceEdit(from, byFile) is { } done ? Tf("Changed by {0}: {1}", by, done) : T("Nothing to change");
    }

    // F7 on an error or warning in code: the fixes the language server offers for it (csharp-ls: "using System.Text;",
    // generate a type…), its preferred one first, the whole message as the last row. Without fixes, the message.
    // Not Ctrl+. (VS Code: a terminal cannot send it) nor Alt+Enter (JetBrains: Windows Terminal's full screen).
    void QuickFix(IReadOnlyList<LspDiagnostic> ds)
    {
        if (ds.FirstOrDefault(d => d.Fix != null) is { } rule) { LatexFix(rule); return; }
        if (lsp == null || editor.Path is not { } path || editor.LangName is not { } lang || lsp.Can(path, lang, "codeActionProvider") != true
            || ds.All(d => d.Source == "LaTeX"))
        {
            ShowDiagnostics(ds);
            return;
        }
        lsp.Sync(path, lang, editor.EditVersion, () => string.Join('\n', editor.Lines));
        Note(T("Asking the language server for fixes...")); app.Render();
        var d0 = ds[0];
        var task = lsp.CodeActions(path, lang, d0.Line, d0.Col, d0.EndLine, d0.EndCol, ds);
        if (!task.Wait(TimeSpan.FromSeconds(5))) { ShowDiagnostics(ds); return; }
        var actions = LanguageServers.ActionsFrom(task.Result);
        if (actions.Count == 0) { ShowDiagnostics(ds); return; }
        var first = EditorView.FirstLine(d0.Message);
        var title = $"{EditorView.SeverityName(d0.Severity)}: {(first.Length > 60 ? first[..59] + "…" : first)}";
        var items = actions.Select(a => a.Title).Append("[ The whole message ]").ToList();
        Note("");
        if (Dialogs.Pick(app, title, items) is not int i) return;
        if (i == actions.Count) { ShowDiagnostics(ds); return; }
        TakeAction(path, lang, actions[i]);
    }

    // F7 on a formula's rule of writing (LatexCheck.Style): its fix, or never this rule again.
    void LatexFix(LspDiagnostic d)
    {
        Note($"{d.Message}   [{d.Rule}]");
        string[] fixes = d.Fix is { } fix ? [fix] : [];
        switch (Dialogs.Fix(app, "LaTeX", fixes, d.Message, [T("Never show this")], "LaTeX · " + d.Rule) is int i ? i - fixes.Length : (int?)null)
        {
            case < 0: editor.FixLatex(d); break;
            case 0:
                Settings.SetValue(Settings.FilePath, "ignoredRules", Settings.JsonList(settings.IgnoredRules.Union([d.Rule])));
                ReloadSettings();
                Note(Tf("Never shown again: {0} (Tools > Ignored rules brings it back)", d.Rule));
                break;
        }
    }

    // An action: its edit (asked for with codeAction/resolve when the server left it out, as csharp-ls does), then its
    // command, which the server runs and which may send edits back (EditRequested).
    void TakeAction(string path, string lang, LspAction action)
    {
        var a = action.Raw;
        if (a["command"] is JsonValue)                                          // a bare Command
        {
            RunCommand(path, lang, a);
            return;
        }
        if (a["edit"] == null && a["data"] != null)
        {
            Note($"{action.Title}..."); app.Render();
            var resolved = lsp!.Request(path, lang, "codeAction/resolve", a);
            if (!resolved.Wait(TimeSpan.FromSeconds(10)) || resolved.Result is not JsonObject r) { Note(Tf("No answer from the language server in {0} s", 10)); return; }
            a = r;
        }
        if (a["edit"] is { } edit) Note(ApplyServerEdit(edit, action.Title));
        if (a["command"] is JsonObject cmd) RunCommand(path, lang, cmd);
    }

    void RunCommand(string path, string lang, JsonObject command) =>
        lsp?.Request(path, lang, "workspace/executeCommand", new JsonObject
        {
            ["command"] = command["command"]?.DeepClone(),
            ["arguments"] = command["arguments"]?.DeepClone() ?? new JsonArray(),
        });

    // Alt+F12: where the symbol at the caret is used, from the language server, in the list Alt+F7 shows (Search > Folder
    // results opens it again); Enter goes there. Shift+F12 is taken (back from F12), Shift+Alt+F12 (VS Code) is awkward.
    void FindReferences()
    {
        if (lsp == null || editor.Path is not { } path || editor.LangName is not { } lang || !lsp.Handles(lang))
        {
            Note(T("References need a language server for this file (docs/lsp.md)"));
            return;
        }
        if (lsp.StatusFor(path, lang) is { } st && st.Contains(' ', StringComparison.Ordinal)) { Note(Tf("Language server: {0}", st)); return; }
        if (lsp.Can(path, lang, "referencesProvider") != true) { Note(Tf("{0} does not find references (docs/lsp.md)", lsp.StatusFor(path, lang))); return; }
        var name = editor.WordAtCursor()?.Word ?? "the symbol";
        lsp.Sync(path, lang, editor.EditVersion, () => string.Join('\n', editor.Lines));
        Note(Tf("Looking for references to {0}...", name));
        lsp.Ask(path, lang, "textDocument/references", editor.Row, editor.Col, new JsonObject { ["context"] = new JsonObject { ["includeDeclaration"] = true } })
            .WaitAsync(TimeSpan.FromSeconds(30))
            .ContinueWith(r => app.Post(() =>
            {
                if (!r.IsCompletedSuccessfully) { Note(Tf("No answer from the language server in {0} s", 30)); return; }
                var places = LanguageServers.ReferencesFrom(r.Result);
                if (places.Count == 0) { Note(Tf("No references to {0} found", name)); return; }
                // each place with its line: from the open document when the file is open (unsaved text), else from disk
                Sync();
                var texts = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
                IReadOnlyList<string> LinesOf(string file)
                {
                    if (texts.TryGetValue(file, out var l)) return l;
                    var doc = docs.Find(d => d.Path != null && string.Equals(d.Path, file, StringComparison.OrdinalIgnoreCase));
                    try { l = doc != null ? doc.Lines : File.ReadAllLines(file); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { l = []; }
                    return texts[file] = l;
                }
                var hits = places.Select(p => new FolderHit(p.Path, p.Line, p.Col, p.Length,
                    p.Line < LinesOf(p.Path).Count ? LinesOf(p.Path)[p.Line] : "")).ToList();
                folderResult = new FolderSearch.Result(hits, hits.Select(h => h.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count(), false, null);
                folderResultRoot = LanguageServers.RootOf(lang, System.IO.Path.GetFullPath(path));
                folderOptions = null; folderWhat = $"references to {name}";
                // the list starts at the reference under the caret
                int row = editor.Row, col = editor.Col;
                folderPick = Math.Max(0, hits.FindIndex(h => string.Equals(h.Path, path, StringComparison.OrdinalIgnoreCase)
                    && h.Line == row && h.Col <= col && col <= h.Col + h.Length));
                ShowFolderResults();
            }), TaskScheduler.Default);
    }

    Place Here() => new(editor.Path, editor.Row, editor.Col);

    // Alt+Left, Shift+F12: to where the last jump left from; Alt+Right undoes it. A place in a file that is gone is passed over.
    void GoBack()
    {
        var here = Here();
        while (jumps.Back(here) is { } place)
            if (GoToPlace(place)) return;
        Note(T("Nothing to go back to"));
    }

    void GoForward()
    {
        var here = Here();
        while (jumps.Forward(here) is { } place)
            if (GoToPlace(place)) return;
        Note(T("Nothing to go forward to"));
    }

    bool GoToPlace(Place place)
    {
        if (place.Path == null)
        {
            // a file not saved yet has no name to tell it by: the first such one
            if (editor.Path != null)
            {
                int unsaved = Enumerable.Range(0, docs.Count).FirstOrDefault(i => PathOf(i) == null, -1);
                if (unsaved < 0) return false;
                Switch(unsaved);
            }
        }
        else if (!string.Equals(editor.Path, place.Path, StringComparison.OrdinalIgnoreCase))
        {
            if (!docs.Exists(d => string.Equals(d.Path, place.Path, StringComparison.OrdinalIgnoreCase)) && !File.Exists(place.Path)) return false;
            OpenPath(place.Path);
        }
        editor.GoToLine(place.Row + 1, place.Col + 1);
        return true;
    }

    // Alt+F1: what the language server knows about the symbol at the caret (its type, signature, documentation).
    void ShowHover()
    {
        if (lsp == null || editor.Path is not { } path || !lsp.Handles(editor.LangName))
        {
            Note(T("Symbol information needs a language server for this file (docs/lsp.md)"));
            return;
        }
        lsp.Sync(path, editor.LangName, editor.EditVersion, () => string.Join('\n', editor.Lines));
        var task = lsp.Ask(path, editor.LangName, "textDocument/hover", editor.Row, editor.Col);
        if (!task.Wait(TimeSpan.FromSeconds(3))) { Note(Tf("No answer from the language server in {0} s", 3)); return; }
        var text = LanguageServers.HoverText(task.Result);
        if (text.Length == 0) { Note(T("Nothing known about this")); return; }
        var lines = new List<string> { "" };
        foreach (var l in text.Split('\n')) lines.Add("  " + l);
        lines.Add("");
        Dialogs.ShowText(app, editor.WordAtCursor()?.Word ?? "Symbol", lines);
    }
}
