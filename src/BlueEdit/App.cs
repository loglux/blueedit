using System.Text.Json.Nodes;
using Tui;

/// <summary>The editor: its documents, checkers and language servers, the menus and keys, wired up in the
/// constructor; <see cref="Run"/> runs it until it quits. Spread over App.*.cs by area.</summary>
sealed partial class App : IDisposable
{
    readonly string[] args;

    readonly ConsoleHost host = new();
    readonly TuiApp app;
    bool hostStarted; bool graphicsAsked; bool formulaErrorShown;    // formula pictures (AskTerminalForGraphics)
    // the first start: no settings.json yet (BLUEEDIT_WELCOME=0 skips the welcome, as the UI tests do)
    bool firstRun = !File.Exists(Settings.FilePath) && Environment.GetEnvironmentVariable("BLUEEDIT_WELCOME") != "0";
    Settings settings; string? settingsError;
    // Far's F4 runs the editor for one file; there Esc closes it, as Far's own editor does ("escapeQuits": "auto")
    bool startedByFar = ParentProcess.StartedByFar(ParentProcess.Ancestors());
    bool escapeQuits;
    Action? escapeClose;    // set once the document list exists (below)
    // "windows" (default, instant), "office" (Word's dictionaries, asked in the background) or "hunspell" (LibreOffice's
    // dictionaries; the only one on Linux and macOS); all share the editor's word list
    readonly UserDictionary userWords = new(UserDictionary.DefaultPath);
    string[] spellWanted;    // what the provider was asked for (it may have fewer)
    ISpellProvider spell;
    // grammar checkers, each started only while it is turned on; `grammar` puts the ones in use together
    LanguageToolChecker? languageTool;
    LlmGrammarChecker? llm;
    LanguageServers? lsp;    // language servers (errors and warnings in code), by setting "languageServers"
    // a WorkspaceEdit a server sent by itself (after a quick fix's command); set once the documents exist
    Action<JsonNode>? serverEdit;
    string lspConfigKey = "";
    LlmServer? llmServer;    // one llama.cpp server shared by grammar checking and style suggestions
    bool completing;    // a completion list is open (typing into it must not open another)
    (int Row, int Start)? noCompletionFor;    // the word being typed that had nothing to offer
    int completionRequest;    // the latest request to a language server (older answers are dropped)
    bool dotWhileListed;    // a dot typed while a list was open: ask again when it closes
    string? signature;    // the parameters of the function being called, shown in the status line
    int signatureRow = -1;    // ... while the caret stays on this line
    readonly Stack<(string Path, int Row, int Col)> definitionTrail = new();    // where F12 jumped from (Shift+F12 goes back)
    BrowserProofreader? edgeProof;
    HarperChecker? harper;
    BrowserProofreader? chromeProof;
    ChromeModelChecker? chromeModel;
    GrammarCheckers? grammar;
    readonly EditorView editor;
    MainView? mainView;
    // the menu, built again when the interface's language changes (Ui.cs)
    Func<MenuBar>? buildMenu;
    // ---------- documents ----------
    // The editor view always shows the active document; the others are parked in `docs` as captured state.
    readonly List<EditorView.Doc> docs = new();
    int cur;
    // Command line:  be [-r] [+LINE] file[:LINE[:COL]]...
    //   -r / --read-only;  +N or --line N: open the last file at line N;  file:12 or file:12:5 as compilers, grep and
    //   other tools print locations (only when no file with that literal name exists).
    int startLine;
    readonly List<string> files = new();
    // ---------- recovery (Recovery.cs) ----------
    // Every "autoRecoverSeconds" the documents with unsaved edits are copied apart from their files, in the background;
    // an error that ends the editor copies them at once. The next start offers what an editor left that way.
    readonly Recovery recovery = Recovery.ForThisProcess(Recovery.DefaultDir);
    long recoveryDue;
    // ---------- status ----------
    string note = "";
    bool noteFresh;
    // the word count of the status line ("wordCount"): counted half a second after the last edit, never while typing
    int wordsShown = -1; int wordsVersion = -1;
    // why a checker did not start, said once in the message line (the status line only names it)
    readonly HashSet<string> grammarFailuresShown = new();
    // Tools > Code tools: the language servers and formatters, each found (where) or not, installed into the editor's own
    // folder on Enter (CodeTools.cs): in the background, the steps in the status line; the servers are taken up after.
    bool installingTools;
    // A code file whose language has a server the editor can install, but none is installed: said once per language.
    readonly HashSet<string> codeToolHints = new(StringComparer.OrdinalIgnoreCase);
    // Each tick: the current file's text to its language server when it was edited (or just opened); a server that did
    // not start says why once.
    readonly HashSet<string> lspFailuresShown = new();
    // ---------- terminal font ----------
    // "auto": keep the Windows Terminal profile in step with the font settings and the theme (only when running in
    // Windows Terminal, and only rewriting the file when something changed). "hint": never write anything.
    const string RestartHint = "restart Windows Terminal and open \"BlueEdit\" from the ˅ menu";
    // ---------- search ----------
    // The options of the last search are offered again; a selection within one line is offered as the text to find.
    string lastReplace = "";
    string? folderRoot;
    string folderMasks = "*";
    FolderSearch.Result? folderResult;
    string folderResultRoot = "";
    int folderPick;
    SearchOptions? folderOptions;
    string folderWhat = "matches";    // what the list holds: "matches" (Alt+F7) or "references to x" (Alt+F12)
    // ---------- Tools > Set up grammar checking (ProofingTools.cs) ----------
    // One place to get grammar checking working: a language model here (a video card helps), a model server at an
    // address (nothing to install here), LanguageTool or Harper (no video card needed). Installed in the background, the
    // steps in the status line; the checker is turned on when it is there.
    bool installingProofing;

    public App(string[] args)
    {
        this.args = args;
        app = new TuiApp(host);
        (settings, settingsError) = Settings.Load(Settings.FilePath);
        spellWanted = SpellLanguagesFor(settings);
        spell = CreateSpell(settings, spellWanted);
        editor = new EditorView { Clipboard = OperatingSystem.IsWindows() ? new WindowsClipboard() : new UnixClipboard(host.Write), Spell = spell };
        editor.Diagnostics = p => lsp?.DiagnosticsFor(p) ?? Array.Empty<LspDiagnostic>();
        ApplySettings(settings);
        docs.Add(editor.CaptureDoc());
        serverEdit = e => app.Post(() => Note(ApplyServerEdit(e, T("the language server"))));
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "-r" or "--read-only") editor.ReadOnlyFile = true;
            else if (a.Length > 1 && a[0] == '+' && int.TryParse(a[1..], out var n1)) startLine = n1;
            else if (a == "--line" && i + 1 < args.Length && int.TryParse(args[++i], out var n2)) startLine = n2;
            else files.Add(a);
        }
        // A host that forgot to quote a name with spaces (Far's !.! without quotes) delivers it in pieces: "note", "one.md".
        // Rejoin when no piece exists on its own and the rest look like a continuation (no path separators).
        if (files.Count > 1 && !files.Any(File.Exists) && files.Skip(1).All(p => !p.AsSpan().ContainsAny('\\', '/', ':')))
            files = new List<string> { string.Join(" ", files) };
        foreach (var f in files)
        {
            var (path, line, col) = Location.Parse(f);
            OpenPath(path);
            if (line > 0) editor.GoToLine(line, Math.Max(col, 1));
        }
        if (startLine > 0) editor.GoToLine(startLine);
        editor.CursorMoved += () => { if (noteFresh) noteFresh = false; else note = ""; if (editor.Row != signatureRow) signature = null; };
        SyncTerminalProfile();
        if (settingsError != null) Note(Tf("Settings: {0}", settingsError));
        app.Tick = () => { CheckDisk(); ReportGrammarFailures(); SyncLanguageServer(); StoreRecovery(); };
        escapeClose = EscapeClose;
        editor.EscapeAction = escapeClose;
        // Esc closes: with "escapeQuits", and the settings file opened over another one, which is a page to go back from
        editor.EscapeCloses = () => escapeQuits || (docs.Count > 1 && IsSettingsFile(editor.Path));
        // The language model that tells apart languages of one script (ParagraphLanguage.cs): loaded in the background, then
        // the screen drawn again, as paragraphs may now be checked in another language
        _ = Task.Run(() => { if (ParagraphLanguage.Load()) app.Post(() => { }); });
        host.GraphicsChanged += () => app.Post(() => FormulaImages.SetTerminal(host.Sixel, host.CellPixels));
        FormulaImages.Changed = () => app.Post(() =>
        {
            FormulaImages.Taken();
            editor.FormulaPicturesChanged();
            if (FormulaImages.LoadError is { } error && !formulaErrorShown) { formulaErrorShown = true; Note(Tf("Formula pictures: {0}", error)); }
        });
        editor.WordTyped += AutoComplete;
        SetUpKeysAndMenu();
        app.Root = mainView = new MainView(editor)
        {
            Theme = Themes.Get(settings.Theme, settings.Colors),
            Tabs = () => docs.Select((d, i) => (NameOf(PathOf(i)), IsModified(i), i == cur)).ToList(),
            Status = StatusText,
            Hints = new[] { ("F1", "Help"), ("F2", "Save"), ("F3", "Open"), ("F4", "Column"), ("F5", "Next"), ("F6", "Window"), ("^F", "Find"),
                            ("^R", "Replace"), ("^G", "Goto"), ("F7", "Spell"), ("F9", "Menu"), ("F8", "Spell on/off"), ("Alt+Z", "Wrap"),
                            ("^E", "Read"), ("^O", "Outline"), ("F10", "Quit") },
            SwitchTo = Switch,
        };
        // the window's title names the file, as Notepad does; the console's own title is put back on exit (ConsoleHost)
        app.Title = () => $"{NameOf(editor.Path)}{(editor.Modified ? " *" : "")} - BlueEdit";
        editor.ShowTiming = Environment.GetEnvironmentVariable("BLUEEDIT_TIMING") == "1";
    }

    public void Dispose() => host.Dispose();

    /// <summary>Runs the editor until it quits; the terminal is given back by <see cref="Dispose"/>.</summary>
    public void Run()
    {
        host.Start();
        hostStarted = true;
        AskTerminalForGraphics();
        app.Post(OfferRecovery);
        if (firstRun) app.Post(Welcome);
        try { app.Run(); }
        catch (Exception) when (settings.AutoRecoverSeconds > 0)
        {
            // An error nothing else caught ends the editor: the unsaved text is copied first, then the terminal is given
            // back and the error printed as before.
            Sync();
            recovery.StoreNow(RecoveryItems());
            host.Dispose();
            Console.Error.WriteLine($"BlueEdit stopped on an error. Unsaved text is kept in {recovery.Dir}; the next start offers it back.");
            throw;
        }
        recovery.Clear();
        spell.Dispose();                     // closes Office's hidden document (and the Word it started)
        grammar?.Dispose();
        llmServer?.Dispose();                  // stops the LanguageTool and llama.cpp servers
        lsp?.Dispose();                        // and the language servers
    }

    internal static string T(string english) => Ui.T(english);

    internal static string Tf(string english, params object?[] args) => Ui.T(english, args);

    internal static string Tn(long count, string one, string other, params object?[] more) => Ui.N(count, one, other, more);

    // whether .NET will find ICU: it loads libicuuc.so.NN, the newest version first (versions 50 to 99 here); the names are
    // built from digits by hand, since formatting a number would already need the culture
    internal static bool IcuPresent()
    {
        for (int v = 99; v >= 50; v--)
            if (System.Runtime.InteropServices.NativeLibrary.TryLoad("libicuuc.so." + (char)('0' + v / 10) + (char)('0' + v % 10), out var h))
            {
                System.Runtime.InteropServices.NativeLibrary.Free(h);
                return true;
            }
        return System.Runtime.InteropServices.NativeLibrary.TryLoad("libicuuc.so", out _);
    }
}
