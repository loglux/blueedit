using System.Text.Json.Nodes;
using Tui;

sealed partial class App
{
    // Settings take effect immediately: at start, after saving settings.json, and when a theme is picked from the menu.
    void ApplySettings(Settings s)
    {
        var theme = Themes.Get(s.Theme, s.Colors);
        editor.ApplyTheme(theme);
        app.Theme = theme.Ui;
        if (mainView != null) mainView.Theme = theme;
        if (Ui.Use(s.UiLanguage, Settings.Directory) && buildMenu != null) app.Menu = buildMenu();
        editor.TabSize = s.TabSize;
        editor.RightMargin = s.RightMargin;
        editor.AutoWrap = s.AutoWrap;
        editor.AutoIndent = s.AutoIndent;
        editor.AutoClose = s.AutoClose;
        editor.CompleteWhileTyping = s.CompleteWhileTyping;
        editor.WrapProse = s.WrapProse;
        editor.WrapCode = s.WrapCode;
        editor.LineNumbersOn = s.LineNumbers;
        editor.ShowTiming = s.ShowTiming || Environment.GetEnvironmentVariable("BLUEEDIT_TIMING") == "1";
        editor.DefaultMdView = s.MarkdownView switch { "source" => MdView.Source, "reading" => MdView.Reading, _ => MdView.Live };
        var langs = SpellLanguagesFor(s);
        var provider = !OperatingSystem.IsWindows() || s.SpellProvider == "hunspell" ? "Hunspell"
            : s.SpellProvider == "office" && OfficeSpell.Installed ? "Office" : "Windows";
        if (!langs.SequenceEqual(spellWanted) || spell.Name != provider || editor.Spell == null)
        {
            spell.Dispose();
            spellWanted = langs;
            spell = CreateSpell(s, langs);
            editor.Spell = spell;
        }
        editor.SpellOn = s.SpellCheck && spell.Available;
        editor.LatexCheckOn = s.LatexCheck;
        editor.LatexStyleOn = s.LatexStyle;
        host.Screen.UseScrollRegions = s.FastScroll;
        // BLUEEDIT_KEY_EVENTS=1 or 0 turns it on or off whatever the settings
        host.UseKeyEvents(Environment.GetEnvironmentVariable("BLUEEDIT_KEY_EVENTS") switch { "1" => true, "0" => false, _ => s.KeyEvents });
        if (Latex.BasicSymbols != (s.FormulaSymbols == "basic"))
        {
            Latex.BasicSymbols = s.FormulaSymbols == "basic";
            Conceal.ClearCaches();
            editor.FormulaPicturesChanged();
        }
        if (FormulaImages.Wanted != s.FormulaImages || FormulaImages.ImagesWanted != s.Images)
        {
            FormulaImages.Wanted = s.FormulaImages;
            FormulaImages.ImagesWanted = s.Images;
            AskTerminalForGraphics();
            editor.FormulaPicturesChanged();
        }
        escapeQuits = s.EscapeQuits == "on" || (s.EscapeQuits == "auto" && startedByFar);
        editor.EscapeAction = escapeClose;
        bool wantLt = s.Grammar.Contains("languagetool"), wantLlm = s.Grammar.Contains("llm"), wantEdge = s.Grammar.Contains("edge"), wantHarper = s.Grammar.Contains("harper"), wantChrome = s.Grammar.Contains("chrome"), wantChromeModel = s.Grammar.Contains("chromemodel");
        if (languageTool != null && (!wantLt || LanguageToolHome(s) != LanguageToolHome(settings) || languageTool.Options.Key != s.LanguageToolOptions.Key))
        { languageTool.Dispose(); languageTool = null; }
        if (wantLt && languageTool == null) languageTool = new LanguageToolChecker(LanguageToolHome(s), s.LanguageToolOptions);
        bool llmMoved = LlmHome(s) != LlmHome(settings) || s.LlmModel != settings.LlmModel || s.LlmServer != settings.LlmServer
            || s.LlmServerModel != settings.LlmServerModel || s.LlmApiKey != settings.LlmApiKey;
        if (llm != null && (!wantLlm || llmMoved)) { llm.Dispose(); llm = null; }
        // the model leaves video memory with grammar checking (style starts it again when asked) or when another is chosen
        if (llmServer != null && (!wantLlm || llmMoved)) { llmServer.Dispose(); llmServer = null; }
        if (wantLlm && llm == null) llm = new LlmGrammarChecker(SharedLlm(s), LlmHome(s));
        if (edgeProof != null && (!wantEdge || s.EdgePath != settings.EdgePath)) { edgeProof.Dispose(); edgeProof = null; }
        if (wantEdge && edgeProof == null) edgeProof = new BrowserProofreader(s.EdgePath);
        if (harper != null && !wantHarper) { harper.Dispose(); harper = null; }
        if (wantHarper && harper == null) harper = new HarperChecker(HarperChecker.DefaultHome);
        if (chromeProof != null && !wantChrome) { chromeProof.Dispose(); chromeProof = null; }
        if (wantChrome && chromeProof == null) chromeProof = BrowserProofreader.Chrome(null);
        if (chromeModel != null && !wantChromeModel) { chromeModel.Dispose(); chromeModel = null; }
        if (wantChromeModel && chromeModel == null) chromeModel = new ChromeModelChecker();
        var inUse = new IGrammarChecker?[] { languageTool, llm, edgeProof, harper, chromeProof, chromeModel }.OfType<IGrammarChecker>().ToList();
        grammar = inUse.Count == 0 ? null : new GrammarCheckers(inUse);
        editor.Grammar = grammar;
        editor.GrammarOn = grammar != null;
        editor.GrammarLanguages = spell.Languages;
        editor.IgnoredRules = s.IgnoredRules.ToHashSet(StringComparer.Ordinal);
        // language servers: the defaults and the settings' own, only those installed (a missing one is not an error)
        var servers = s.Lsp ? LspConfig(s) : new Dictionary<string, ServerCommand>();
        var key = string.Join(";", servers.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value.Command + " " + string.Join(" ", x.Value.Args)));
        if (key != lspConfigKey)
        {
            lsp?.Dispose();
            lsp = servers.Count > 0 ? new LanguageServers(servers) : null;
            // edits a server sends after running a command (a quick fix's): applied on the editor's thread
            if (lsp != null) lsp.EditRequested += e => serverEdit?.Invoke(e);
            lspConfigKey = key;
        }
        settings = s;
    }

    static bool IsSettingsFile(string? path) =>
        path != null && string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(Settings.FilePath), StringComparison.OrdinalIgnoreCase);

    // The first start: a short welcome with the keys to begin with, what works already, the way to grammar checking and
    // the interface's language. Whatever is chosen (Esc too) writes settings.json, so it is shown once.
    void Welcome()
    {
        static string Native(string tag)
        {
            try { var n = System.Globalization.CultureInfo.GetCultureInfo(tag).NativeName; return char.ToUpper(n[0], System.Globalization.CultureInfo.CurrentCulture) + n[1..]; }
            catch (System.Globalization.CultureNotFoundException) { return tag; }
        }
        while (true)
        {
            var spelling = spell.Available
                ? Tf("Spelling works already: {0}.", string.Join(", ", spell.Languages.Select(Native))) + "\n" + T("F8 turns it on and off, F7 on a word shows the suggestions.")
                : T("Spelling needs dictionaries: Tools > Get spelling dictionaries.");
            var message = string.Join("\n",
                T("F9 or a click on the top bar opens the menu; F1 lists every key."),
                T("F2 saves, F3 opens a file, Ctrl+N starts a new one, F10 quits."),
                "",
                spelling,
                T("Grammar and style checking is not set up: a language model (here, in"),
                T("Ollama or LM Studio), LanguageTool or Harper, installed when you pick it."),
                "",
                T("Shown once; all of this is in the Tools menu later."));
            int choice = Dialogs.Ask(app, T("Welcome to BlueEdit"), message, [T("Begin"), T("Grammar..."), T("Language...")], 0);
            Settings.EnsureFile(Settings.FilePath, settings);
            if (choice == 2)
            {
                // the interface's languages, each named in its own words; English unless another is picked
                var languages = Ui.Available(Settings.Directory);
                var items = languages.Select(Native).ToList();
                int current = Math.Max(0, languages.ToList().IndexOf(Ui.Language));
                if (Dialogs.Pick(app, T("Interface language"), items, current, current: current) is int k && languages[k] != Ui.Language)
                {
                    Settings.SetValue(Settings.FilePath, "uiLanguage", JsonString(languages[k]));
                    ReloadSettings();
                }
                continue;
            }
            note = "";
            if (choice == 1) SetUpGrammar();
            return;
        }
    }

    void ReloadSettings()
    {
        var (s, error) = Settings.Load(Settings.FilePath);
        ApplySettings(s);
        Note(error == null ? T("Settings applied") : Tf("Settings: {0}", error));
        SyncTerminalProfile();
    }

    bool WriteProfile() =>
        Environment.ProcessPath is { } exe && TerminalProfile.Write(exe, settings.Font, settings.FontSize, Themes.Get(settings.Theme, settings.Colors));

    void SyncTerminalProfile()
    {
        if (settings.FontSetup != "auto" || !TerminalProfile.InWindowsTerminal) return;
        bool isNew = !TerminalProfile.Installed;
        try
        {
            if (WriteProfile() && isNew) Note(Tf("Windows Terminal profile \"BlueEdit\" added ({0} {1}): {2}", settings.Font, settings.FontSize, T(RestartHint)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Note(Tf("Terminal profile: {0}", ex.Message)); }
    }

    void TerminalFont()
    {
        // the profile is for Windows Terminal starting be.exe; elsewhere the terminal's own settings set the font
        if (!OperatingSystem.IsWindows())
        {
            Dialogs.ShowText(app, T("Terminal font"), new[]
            {
                T("The font and its size are set in your terminal's own settings: it draws the text."),
                Tf("Fonts that suit BlueEdit: {0} or another monospaced font with box-drawing", settings.Font),
                T("characters (DejaVu Sans Mono, JetBrains Mono, Fira Code). Colours come from the theme"),
                T("(Tools > Settings)."),
            });
            return;
        }
        var hint = TerminalProfile.Hint(settings.Font, settings.FontSize);
        if (settings.FontSetup == "hint" && !TerminalProfile.Installed) { Dialogs.ShowText(app, T("Terminal font"), hint); return; }
        var state = TerminalProfile.Installed ? Ui.Tc("profile", "installed") : Ui.Tc("profile", "not installed");
        var answer = Dialogs.Ask(app, T("Terminal font"),
            Tf("Windows Terminal profile \"BlueEdit\": {0}.\nFont {1}, size {2}, colours of the theme.\nYour other profiles are not changed. Font settings: Tools > Settings.", state, settings.Font, settings.FontSize),
            new[] { T("Install/update"), T("Remove"), T("How to by hand"), T("Close") }, 3);
        try
        {
            switch (answer)
            {
                case 0: WriteProfile(); Note(Tf("Profile saved: {0}", T(RestartHint))); break;
                case 1: TerminalProfile.Remove(); Note(T("Windows Terminal profile removed")); break;
                case 2: Dialogs.ShowText(app, T("Terminal font"), hint); break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Note(Tf("Terminal profile: {0}", ex.Message)); }
    }

    // Tools > Command be in any console: the editor's folder put in the user's PATH, or taken out (UserPath.cs), so a
    // copy unpacked into Downloads starts as "be" in a new console; on Linux and macOS, how to link it
    void CommandEverywhere()
    {
        if (Environment.ProcessPath is not { } exe || Path.GetDirectoryName(exe) is not { } dir) return;
        var name = Path.GetFileNameWithoutExtension(exe);
        if (!OperatingSystem.IsWindows())
        {
            Dialogs.ShowText(app, T("Command in any console"), new[] { "", "  " + T("Link it into a folder on PATH, for example:"), $"    ln -s {exe} ~/.local/bin/{name}", "" });
            return;
        }
        bool on = UserPath.Contains(dir);
        var answer = Dialogs.Ask(app, T("Command in any console"),
            on ? Tf("{0} is in your PATH: \"{1}\" starts the editor in a new console.\nTake it out?", dir, name)
               : Tf("Put {0} in your PATH, so that \"{1}\" starts the editor\nin any new console? Your own PATH: no administrator rights.", dir, name),
            on ? new[] { T("Take it out"), T("Cancel") } : new[] { T("Put it in"), T("Cancel") }, 1);
        if (answer != 0) return;
        try
        {
            UserPath.Set(dir, !on);
            Note(on ? Tf("Taken out of PATH: {0}", dir) : Tf("In PATH: \"{0}\" starts the editor in new consoles (the open ones keep their PATH)", name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Note(Tf("PATH: {0}", ex.Message)); }
    }

    // Alt+G: for every file, and remembered in the settings
    void ToggleLineNumbers()
    {
        Settings.SetValue(Settings.FilePath, "lineNumbers", settings.LineNumbers ? "false" : "true");
        ReloadSettings();
        Note(editor.LineNumbersOn ? T("Line numbers on (Alt+G turns them off)") : T("Line numbers off"));
    }

    // Alt+Z: kept in the settings for this kind of file (text or code), as Alt+G keeps line numbers, so that the View
    // menu's √ and Tools > Settings never disagree
    void ToggleSoftWrap()
    {
        editor.ToggleSoftWrap();
        Settings.SetValue(Settings.FilePath, editor.IsProseFile ? "wrapProse" : "wrapCode", editor.WrapOn ? "true" : "false");
        ReloadSettings();
        Note(editor.WrapOn ? T("Soft wrap on") : T("Soft wrap off"));
    }

    // View > Formula pictures, at the right while they are on: why none are shown ("" when they are, or are off)
    string FormulaPicturesMissing() =>
        !settings.FormulaImages || FormulaImages.Active ? ""
        : FormulaImages.ModuleDir == null && FormulaImages.Renderer == null ? T("no module")
        : FormulaImages.LoadError != null ? T("module failed")
        : T("not in this terminal");

    // View > Images, at the right while they are on: why none are shown ("" when they are, or are off)
    string ImagesMissing() =>
        !settings.Images || FormulaImages.ImagesActive ? ""
        : FormulaImages.ModuleDir == null && FormulaImages.Decoder == null ? T("no module")
        : FormulaImages.LoadError != null ? T("module failed")
        : FormulaImages.ImageError != null ? T("module too old")
        : T("not in this terminal");

    // View > Images: kept in the settings
    void ToggleImages()
    {
        Settings.SetValue(Settings.FilePath, "images", settings.Images ? "false" : "true");
        ReloadSettings();
        Note(!settings.Images ? T("Images off: shown as text")
            : FormulaImages.ImagesActive ? T("Images on: a line that is only an image shows the picture (with soft wrap)")
            : Tf("Images on, but not shown: {0}", ImagesMissing()));
    }

    // View > Clock goes round off, time, date and time; kept in the settings, as the Tools > Settings row is
    void CycleClock()
    {
        var next = settings.Clock switch { "off" => "time", "time" => "date", _ => "off" };
        Settings.SetValue(Settings.FilePath, "clock", $"\"{next}\"");
        ReloadSettings();
        Note(next switch { "time" => T("Clock: the time"), "date" => T("Clock: the date and the time"), _ => T("Clock off") });
    }

    void ToggleAutoWrap()
    {
        Settings.SetValue(Settings.FilePath, "autoWrap", settings.AutoWrap ? "false" : "true");
        ReloadSettings();
        Note(editor.AutoWrap ? Tf("Auto-wrap on: words typed past column {0} go to a new line", editor.RightMargin) : T("Auto-wrap off"));
    }

    // Tools > Settings: the everyday settings as a list (Far's way); its last row opens settings.json, which has them all
    // with comments. What is changed is written into that file (comments and the rest kept) and applied at once.
    void ShowSettings()
    {
        static string On(bool b) => b ? "on" : "off";
        static string[] Numbers(int current, params int[] usual) =>
            usual.Append(current).Distinct().Order().Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var s = settings;
        // label, key in the file, values shown, the value as written in the file for each, current, help
        var rows = new List<(string Label, string Key, string[] Shown, string[] Json, string Current, string Help)>
        {
            ("Colour scheme", "theme", Themes.Names, Themes.Names.Select(n => $"\"{n}\"").ToArray(), s.Theme, "The editor's colours."),
            ("Interface language", "uiLanguage", Ui.Available(Settings.Directory).Prepend("auto").ToArray(),
                Ui.Available(Settings.Directory).Prepend("auto").Select(c => $"\"{c}\"").ToArray(), s.UiLanguage,
                "Menus in this language: auto follows the system's. A table lang/<code>.json in the settings folder adds one."),
            ("Markdown opens in", "markdownView", ["live", "reading", "source"], ["\"live\"", "\"reading\"", "\"source\""], s.MarkdownView,
                "Live: markup hidden except on the caret's line; reading: as a page; source: as written. Ctrl+E switches."),
            ("Formula pictures", "formulaImages", ["on", "off"], ["true", "false"], On(s.FormulaImages),
                "Display formulas typeset, where the terminal shows Sixel graphics and the mathimages module is installed."),
            ("Images", "images", ["on", "off"], ["true", "false"], On(s.Images),
                "A Markdown line that is only an image shows the picture, as formula pictures do (the same module and terminal)."),
            ("Formula symbols", "formulaSymbols", ["full", "basic"], ["\"full\"", "\"basic\""], s.FormulaSymbols,
                "Basic: without the characters most fonts lack (ₙ, ⩽), for a terminal that shows boxes for them (MobaXterm)."),
            ("Formula errors", "latexCheck", ["on", "off"], ["true", "false"], On(s.LatexCheck),
                "Formulas that would not typeset (an unclosed brace, \\left without \\right) underlined; Shift+F7 goes to them."),
            ("Formula style hints", "latexStyle", ["on", "off"], ["true", "false"], On(s.LatexStyle),
                "With formula errors on: cos without \\, * for \\times, <= for \\le, 5 cm in italics as warnings; F7 fixes."),
            ("Spell checking", "spellCheck", ["on", "off"], ["true", "false"], On(s.SpellCheck), "Misspelt words underlined; F7 suggests (F8 switches)."),
            ("Line numbers", "lineNumbers", ["on", "off"], ["true", "false"], On(s.LineNumbers), "Line numbers at the left (Alt+G)."),
            ("Soft wrap in text", "wrapProse", ["on", "off"], ["true", "false"], On(s.WrapProse), "Long lines of text and Markdown wrap at the window's edge (Alt+Z)."),
            ("Soft wrap in code", "wrapCode", ["on", "off"], ["true", "false"], On(s.WrapCode), "The same for code files."),
            ("Wrap while typing", "autoWrap", ["on", "off"], ["true", "false"], On(s.AutoWrap), "Real line breaks at the right margin as you type, in text and Markdown."),
            ("Right margin", "rightMargin", Numbers(s.RightMargin, 72, 78, 80, 100, 120), Numbers(s.RightMargin, 72, 78, 80, 100, 120),
                s.RightMargin.ToString(System.Globalization.CultureInfo.InvariantCulture), "Where Format > Reformat and wrapping while typing break lines."),
            ("Tab size", "tabSize", Numbers(s.TabSize, 2, 4, 8), Numbers(s.TabSize, 2, 4, 8),
                s.TabSize.ToString(System.Globalization.CultureInfo.InvariantCulture), "Spaces inserted by the Tab key."),
            ("Auto indent", "autoIndent", ["on", "off"], ["true", "false"], On(s.AutoIndent), "Enter keeps the indentation of the line above, one step deeper after \":\" in Python or \"{\" in C#, JavaScript…"),
            ("Brackets in pairs", "autoClose", ["code", "always", "off"], ["\"code\"", "\"always\"", "\"off\""], s.AutoClose,
                "Typing ( adds ), in code files or everywhere."),
            ("Completion while typing", "completeWhileTyping", ["code", "always", "off"], ["\"code\"", "\"always\"", "\"off\""], s.CompleteWhileTyping,
                "The completion list opens by itself after three letters (Ctrl+Space always opens it)."),
            ("Word count", "wordCount", ["on", "off"], ["true", "false"], On(s.WordCount), "The number of words in the status line."),
            ("Clock", "clock", ["off", "time", "date"], ["\"off\"", "\"time\"", "\"date\""], s.Clock,
                "At the right of the menu bar, as in Far: the time, or the date and the time."),
            ("Language servers", "lsp", ["on", "off"], ["true", "false"], On(s.Lsp), "Errors and warnings in code, from the language's server."),
            ("Esc closes the file", "escapeQuits", ["auto", "on", "off"], ["\"auto\"", "\"on\"", "\"off\""], s.EscapeQuits,
                "Auto: only when Far Manager opened the editor, as Far's own editor does. Esc in the settings file always goes back."),
        };
        if (OperatingSystem.IsWindows())
        {
            rows.Insert(4, ("Spelling dictionaries", "spellProvider", ["windows", "office", "hunspell"], ["\"windows\"", "\"office\"", "\"hunspell\""],
                s.SpellProvider, "Windows (instant), Microsoft Word's (in the background) or LibreOffice's Hunspell dictionaries."));
        }
        rows.Add(("Keys in any layout", "keyEvents", ["on", "off"], ["true", "false"], On(s.KeyEvents),
            "Shortcuts found by the key, whatever the keyboard layout: Ctrl+/ with Russian, Ctrl+1. Off: keys read as text."));
        var d = app.RunModal(new OptionsDialog(T("Settings"),
            rows.Select(r => new OptionRow(T(r.Label), r.Shown.Select(T).ToList(), Math.Max(0, Array.IndexOf(r.Shown, r.Current)), T(r.Help))).ToList(),
            T("Edit settings.json…  (all settings, with comments)")));
        if (d.Chosen is not { } chosen) return;
        bool changed = false;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Shown[chosen[i]] == rows[i].Current) continue;
            Settings.EnsureFile(Settings.FilePath, settings);
            Settings.SetValue(Settings.FilePath, rows[i].Key, rows[i].Json[chosen[i]]);
            changed = true;
        }
        if (changed) ReloadSettings();
        if (d.More) OpenSettings();
    }

    void OpenSettings()
    {
        Settings.EnsureFile(Settings.FilePath, settings);
        OpenPath(Settings.FilePath);
        Note(docs.Count > 1 ? T("Settings: F2 saves and applies them; Esc goes back to the text") : T("Settings: F2 saves and applies them"));
    }

    void SetTheme(string name)
    {
        Settings.SetValue(Settings.FilePath, "theme", $"\"{name}\"");
        ReloadSettings();
        Note(Tf("Theme: {0}", name));
    }
}
