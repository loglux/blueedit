using System.Text.Json.Nodes;
using Tui;

sealed partial class App
{
    SearchOptions LastOptions() => editor.LastSearch ?? new SearchOptions("");

    static FormToggle[] SearchToggles(SearchOptions o) => new[]
    {
        new FormToggle(T("Match _case"), o.MatchCase), new FormToggle(T("_Whole word"), o.WholeWord), new FormToggle(T("_Regular expression"), o.UseRegex),
    };

    static SearchOptions OptionsFrom(string pattern, IReadOnlyList<bool> t) => new(pattern, t[0], t[1], t[2]);

    void ReportFind(bool found, SearchOptions o)
    {
        if (editor.SearchError != null) Note(Tf("Search: {0}", editor.SearchError));
        else if (!found) Note(Tf("Not found: {0}", o.Describe()));
        else if (editor.LastFindWrapped) Note(T("Search wrapped around"));
    }

    void Find()
    {
        var last = LastOptions();
        var form = app.RunModal(new FormDialog(T("Find"), new[] { new FormField(T("Find:"), editor.SelectionOnOneLine ?? last.Pattern) }, SearchToggles(last),
            T("Enter = find   F5 = next   Shift+F5 = previous   Esc = cancel")));
        if (form.Values is not { } v || v[0].Length == 0) return;
        var o = OptionsFrom(v[0], form.Toggles);
        var from = Here();
        ReportFind(editor.Find(o), o);
        jumps.Record(from, Here(), "search");
    }

    void FindAgain(bool backward)
    {
        if (editor.LastSearch == null) { Find(); return; }
        var from = Here();
        ReportFind(editor.FindNext(backward), editor.LastSearch);
        jumps.Record(from, Here(), "search");
    }

    void Replace()
    {
        if (editor.ReadOnlyView) { Note(T("Read-only")); return; }
        var last = LastOptions();
        var form = app.RunModal(new FormDialog(T("Replace"),
            new[] { new FormField(T("Find:"), editor.SelectionOnOneLine ?? last.Pattern), new FormField(T("Replace with:"), lastReplace) }, SearchToggles(last),
            T("Enter = go through the matches   Tab = next field   Esc = cancel")));
        if (form.Values is not { } v || v[0].Length == 0) return;
        var o = OptionsFrom(v[0], form.Toggles);
        lastReplace = v[1];
        if (o.UseRegex && o.Build().Error is { } bad) { Note(Tf("Search: {0}", bad)); return; }
        int n = editor.ReplaceInteractive(o, lastReplace,
            () => app.RunModal(new KeyPrompt(T("Replace this match?   Y = yes   N = no   A = all the rest   Esc = stop"), "yna")).Result);
        Note(editor.SearchError != null ? Tf("Search: {0}", editor.SearchError) : n == 0 ? Tf("Nothing replaced: {0}", o.Describe()) : Tf("Replaced {0}", n));
    }

    void FindInFolder()
    {
        var last = LastOptions();
        var root = folderRoot ?? (editor.Path != null ? System.IO.Path.GetDirectoryName(editor.Path)! : Directory.GetCurrentDirectory());
        var form = app.RunModal(new FormDialog(T("Find in folder"),
            new[] { new FormField(T("Find:"), editor.SelectionOnOneLine ?? last.Pattern), new FormField(T("Folder:"), root), new FormField(T("Files:"), folderMasks) },
            SearchToggles(last), T("Enter = search   Files: *.md;*.cs   Tab = next field   Esc = cancel")));
        if (form.Values is not { } v || v[0].Length == 0) return;
        var o = OptionsFrom(v[0], form.Toggles);
        folderRoot = v[1].Trim().Trim('"'); folderMasks = v[2].Trim().Length == 0 ? "*" : v[2].Trim();
        Note(T("Searching...")); app.Render();
        var result = FolderSearch.Run(folderRoot, folderMasks, o);
        if (result.Error != null) { Note(Tf("Search: {0}", result.Error)); return; }
        if (result.Hits.Count == 0) { Note(Tn(result.FilesSearched, "Not found in {0} file: {1}", "Not found in {0} files: {1}", o.Describe())); return; }
        folderResult = result; folderResultRoot = folderRoot; folderPick = 0; folderOptions = o; folderWhat = "matches";
        ShowFolderResults();
    }

    void ShowFolderResults()
    {
        if (folderResult == null) { Note(T("No folder search or references yet (Alt+F7, Alt+F12)")); return; }
        var r = folderResult;
        int width = Math.Max(40, app.Screen.Width - 8);
        var items = r.Hits.Select(h => FolderSearch.Format(h, folderResultRoot, width)).ToList();
        int fileCount = r.Hits.Select(h => h.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var title = $"{r.Hits.Count}{(r.Truncated ? "+" : "")} {folderWhat} in {fileCount} file{(fileCount == 1 ? "" : "s")}";
        if (Dialogs.Pick(app, title, items, folderPick) is not int i) return;
        folderPick = i;
        var hit = r.Hits[i];
        var from = Here();
        OpenPath(hit.Path);
        editor.SelectAt(hit.Line, hit.Col, hit.Length);
        jumps.Record(from, Here());
        if (folderOptions != null) editor.RememberSearch(folderOptions);
    }

    void ShowOutline()
    {
        // code: the language server's symbols (classes, functions, methods…); prose: the headings
        if (lsp != null && editor.Path is { } path && editor.LangName is { } lang && lsp.Handles(lang) && !editor.IsProseFile)
        {
            if (lsp.StatusFor(path, lang) is { } st && st.Contains(' ', StringComparison.Ordinal)) { Note(Tf("Language server: {0}", st)); return; }
            if (lsp.Can(path, lang, "documentSymbolProvider") != true) { Note(Tf("{0} gives no outline (docs/lsp.md)", lsp.StatusFor(path, lang))); return; }
            lsp.Sync(path, lang, editor.EditVersion, () => string.Join('\n', editor.Lines));
            Note(T("Asking the language server for the outline..."));
            lsp.AskFile(path, lang, "textDocument/documentSymbol", new JsonObject())
                .WaitAsync(TimeSpan.FromSeconds(15))
                .ContinueWith(r => app.Post(() =>
                {
                    if (!r.IsCompletedSuccessfully) { Note(Tf("No answer from the language server in {0} s", 15)); return; }
                    if (editor.Path != path) return;
                    var symbols = LanguageServers.OutlineFrom(r.Result);
                    if (symbols.Count == 0) { Note(T("No classes or functions in this file")); return; }
                    int width = symbols.Max(s => s.Level * 2 + s.Name.Length) + 3;
                    var items = symbols.Select(s => (new string(' ', s.Level * 2) + s.Name).PadRight(width) + s.Kind).ToList();
                    int current = Math.Max(0, symbols.ToList().FindLastIndex(s => s.Line <= editor.Row));
                    Note("");
                    if (Dialogs.Pick(app, Tn(symbols.Count, "Outline: {0} symbol", "Outline: {0} symbols"), items, current) is int i)
                    {
                        var from = Here();
                        editor.GoToLine(symbols[i].Line + 1, symbols[i].Col + 1);
                        jumps.Record(from, Here());
                    }
                }), TaskScheduler.Default);
            return;
        }
        var heads = editor.Outline();
        if (heads.Count == 0) { Note(T("No headings (Markdown or LaTeX files)")); return; }
        var items = heads.Select(h => new string(' ', (h.Level - 1) * 2) + h.Title).ToList();
        int current = Math.Max(0, heads.FindLastIndex(h => h.Line <= editor.Row));
        if (Dialogs.Pick(app, T("Outline"), items, current) is int idx)
        {
            var from = Here();
            editor.GoToLine(heads[idx].Line + 1);
            jumps.Record(from, Here());
        }
    }

    void GoToMatchingBracket()
    {
        var from = Here();
        if (editor.GoToMatchingBracket() is { } why) Note(why);
        jumps.Record(from, Here(), "bracket");                       // to and fro counts as one
    }

    void GoTo()
    {
        var s = Dialogs.Prompt(app, T("Go to line"), T("Line number:"));
        if (!int.TryParse(s, out var n)) return;
        var from = Here();
        editor.GoToLine(n);
        jumps.Record(from, Here());
    }
}
