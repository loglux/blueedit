using System.Text.Json.Nodes;
using Tui;

sealed partial class App
{
    /// <summary>The keys and the menu bar (F9).</summary>
    void SetUpKeysAndMenu()
    {
        editor.Shortcuts.AddRange(new (KeyInput, Action)[]
        {
            (K(Key.F2), Save),
            (Ctrl('s'), Save),
            (Ctrl(' '), Complete),
            (K(Key.F10, Mods.Shift), SaveAllAndQuit),
            (K(Key.F2, Mods.Shift), SaveAs),
            (K(Key.F3), Open),
            (K(Key.F4), () => editor.ToggleColumnMode()),
            (K(Key.F5), () => FindAgain(false)),
            (K(Key.F5, Mods.Shift), () => FindAgain(true)),
            (K(Key.F7, Mods.Alt), FindInFolder),
            (K(Key.F7, Mods.Shift), () => NextProblem(false)),
            (K(Key.F7, Mods.Ctrl), StyleSuggest),
            (K(Key.F12), GoToDefinition),
            (K(Key.F12, Mods.Ctrl), FormatCode),
            (K(Key.F2, Mods.Ctrl), RenameSymbol),
            (K(Key.F12, Mods.Alt), FindReferences),
            (K(Key.F12, Mods.Shift), GoBack),
            (K(Key.F1, Mods.Alt), ShowHover),
            (K(Key.F6), NextDoc),
            (K(Key.PageDown, Mods.Ctrl), NextDoc),
            (K(Key.PageUp, Mods.Ctrl), PrevDoc),
            (K(Key.F6, Mods.Shift), PrevDoc),
            (Ctrl('n'), NewDoc),
            (Ctrl('w'), CloseDoc),
            (Ctrl('f'), Find),
            (Ctrl('g'), GoTo),
            (Ctrl('r'), Replace),
            (Ctrl('q'), Quit),
            (K(Key.F10), Quit),
            (K(Key.F1), ShowHelp),
            (K(Key.F7), SpellFix),
            (K(Key.F8), ToggleSpell),
            (Alt('z'), ToggleSoftWrap),
            (Alt('g'), ToggleLineNumbers),
            (Ctrl('e'), () => editor.ToggleReading()),
            (Ctrl('o'), ShowOutline),
            (Ctrl('b'), () => editor.Bold()),
            (Alt('i'), () => editor.Italic()),
            (Alt('c'), () => editor.InlineCode()),
            (Alt('d'), () => editor.Strike()),
            (Alt('m'), () => editor.InlineMath()),
            (Ctrl('k'), () => editor.InsertLink()),
            (Alt('h'), () => editor.CycleHeading()),
            (Alt('l'), () => editor.BulletList()),
            (Alt('n'), () => editor.NumberedList()),
            (Alt('q'), () => editor.Quote()),
            (Alt('x'), () => editor.ToggleCheckbox()),
            (Alt('r'), () => Note(editor.Reformat())),
        });
        for (int d = 1; d <= 9; d++)
        {
            int index = d - 1;
            editor.Shortcuts.Add((Alt((char)('0' + d)), () => Switch(index)));
        }
        // ---------- menu ----------
        buildMenu = () => new MenuBar(new[]
        {
            new Menu(T("_File"), new[]
            {
                new MenuItem(T("_New"), "Ctrl+N", NewDoc),
                new MenuItem(T("_Open..."), "F3", Open),
                new MenuItem(T("_Save"), "F2, Ctrl+S", Save),
                new MenuItem(T("Save _as..."), "Shift+F2", SaveAs),
                new MenuItem(T("Save a_ll"), "", SaveAll),
                new MenuItem(T("Reopen with _encoding..."), "", ReopenWithEncoding),
                new MenuItem(T("Con_vert encoding..."), "", ConvertEncoding),
                new MenuItem(T("Line en_dings"), "", ToggleLineEndings) { Value = () => editor.CrLf ? "CRLF" : "LF" },
                new MenuItem(T("_Info..."), "", ShowInfo),
                new MenuItem(T("_Close"), "Ctrl+W", CloseDoc),
                new MenuItem(T("Save all and _quit"), "Shift+F10", SaveAllAndQuit),
                new MenuItem(T("E_xit"), "Ctrl+Q", Quit),
            }),
            new Menu(T("_Edit"), new[]
            {
                new MenuItem(T("_Undo"), "Ctrl+Z", () => editor.Undo()),
                new MenuItem(T("_Redo"), "Ctrl+Y", () => editor.Redo()),
                new MenuItem(T("Cu_t"), "Ctrl+X", () => editor.Cut()),
                new MenuItem(T("_Copy"), "Ctrl+C", () => editor.Copy()),
                new MenuItem(T("_Paste"), "Ctrl+V", () => editor.Paste()),
                new MenuItem(T("Select _all"), "Ctrl+A", () => editor.SelectAll()),
                new MenuItem(T("Delete _line"), "Ctrl+L", () => editor.DeleteLinePublic()),
                new MenuItem(T("Duplicat_e line"), "Ctrl+D", () => editor.DuplicateLinePublic()),
                new MenuItem(T("Complete _word"), "Ctrl+Space", Complete),
                new MenuItem(T("Re_name symbol..."), "Ctrl+F2", RenameSymbol),
            }),
            new Menu(T("_Search"), new[]
            {
                new MenuItem(T("_Find..."), "Ctrl+F", Find),
                new MenuItem(T("Find _next"), "F5", () => FindAgain(false)),
                new MenuItem(T("Find _previous"), "Shift+F5", () => FindAgain(true)),
                new MenuItem(T("_Replace..."), "Ctrl+R", Replace),
                new MenuItem(T("Find in f_older..."), "Alt+F7", FindInFolder),
                new MenuItem(T("Folder resul_ts..."), "", ShowFolderResults),
                new MenuItem(T("S_ymbol references..."), "Alt+F12", FindReferences),
                new MenuItem(T("_Go to line..."), "Ctrl+G", GoTo),
            }),
            // Alt+V, then S, L or R: a Markdown view
            new Menu(T("_View"), new[]
            {
                new MenuItem(T("Soft _wrap"), "Alt+Z", ToggleSoftWrap) { On = () => editor.WrapOn },
                new MenuItem(T("Line _numbers"), "Alt+G", ToggleLineNumbers) { On = () => editor.LineNumbersOn },
                new MenuItem(T("_Outline"), "Ctrl+O", ShowOutline),
                new MenuItem(T("_Formula pictures"), "", ToggleFormulaImages) { On = () => settings.FormulaImages, Value = FormulaPicturesMissing },
                new MenuItem(T("Ima_ges"), "", ToggleImages) { On = () => settings.Images, Value = ImagesMissing },
                new MenuItem(T("Markdown: _source"), "", () => editor.SetView(MdView.Source)) { On = () => editor.IsMarkdown && editor.View == MdView.Source, Choice = true },
                new MenuItem(T("Markdown: _live preview"), "", () => editor.SetView(MdView.Live)) { On = () => editor.IsMarkdown && editor.View == MdView.Live, Choice = true },
                new MenuItem(T("Markdown: _reading"), "Ctrl+E", () => editor.ToggleReading()) { On = () => editor.IsMarkdown && editor.View == MdView.Reading, Choice = true },
                new MenuItem(T("Theme: _DOS blue"), "", () => SetTheme("dos-blue")) { On = () => settings.Theme is not ("dark" or "light"), Choice = true },
                new MenuItem(T("Theme: dar_k"), "", () => SetTheme("dark")) { On = () => settings.Theme == "dark", Choice = true },
                new MenuItem(T("Theme: lig_ht"), "", () => SetTheme("light")) { On = () => settings.Theme == "light", Choice = true },
                new MenuItem(T("_Clock"), "", CycleClock) { On = () => settings.Clock != "off", Value = () => settings.Clock == "off" ? "" : T(settings.Clock) },
            }),
            new Menu(T("F_ormat"), new[]
            {
                new MenuItem(T("_Bold"), "Ctrl+B", () => editor.Bold()),
                new MenuItem(T("_Italic"), "Alt+I", () => editor.Italic()),
                new MenuItem(T("Inline _code"), "Alt+C", () => editor.InlineCode()),
                new MenuItem(T("S_trikethrough"), "Alt+D", () => editor.Strike()),
                new MenuItem(T("Inline _math  $...$"), "Alt+M", () => editor.InlineMath()),
                new MenuItem(T("_Link"), "Ctrl+K", () => editor.InsertLink()),
                new MenuItem(T("_Heading level"), "Alt+H", () => editor.CycleHeading()),
                new MenuItem(T("B_ullet list"), "Alt+L", () => editor.BulletList()),
                new MenuItem(T("_Numbered list"), "Alt+N", () => editor.NumberedList()),
                new MenuItem(T("_Quote"), "Alt+Q", () => editor.Quote()),
                new MenuItem(T("Chec_kbox"), "Alt+X", () => editor.ToggleCheckbox()),
                new MenuItem(T("_Reformat paragraph"), "Alt+R", () => Note(editor.Reformat())),
                new MenuItem(T("Paragraph: align le_ft"), "", () => Note(editor.Align(Alignment.Left))),
                new MenuItem(T("Paragraph: align ri_ght"), "", () => Note(editor.Align(Alignment.Right))),
                new MenuItem(T("Paragraph: c_entre"), "", () => Note(editor.Align(Alignment.Centre))),
                new MenuItem(T("Paragraph: _justify"), "", () => Note(editor.Align(Alignment.Justify))),
                new MenuItem(T("Auto-_wrap at the margin"), "", ToggleAutoWrap) { On = () => settings.AutoWrap },
                new MenuItem(T("Format c_ode"), "Ctrl+F12", FormatCode),
            }),
            new Menu(T("_Tools"), new[]
            {
                new MenuItem(T("_Spelling suggestions"), "F7", SpellFix),
                new MenuItem(T("Ne_xt problem"), "Shift+F7", () => NextProblem(false)),
                new MenuItem(T("Pre_vious problem"), "", () => NextProblem(true)),
                new MenuItem(T("S_tyle suggestions"), "Ctrl+F7", StyleSuggest),
                new MenuItem(T("St_yle options..."), "", StyleOptions),
                new MenuItem(T("Spell _check"), "F8", ToggleSpell) { On = () => editor.SpellOn },
                new MenuItem(T("Spelling l_anguages..."), "", SpellingLanguages),
                new MenuItem(T("Spelling _provider..."), "", SpellingProvider),
                new MenuItem(T("Get spelling _dictionaries..."), "", GetDictionaries),
                new MenuItem(T("Set _up grammar checking..."), "", SetUpGrammar),
                new MenuItem(T("Gra_mmar checking..."), "", GrammarSettings),
                new MenuItem(T("Grammar mod_el..."), "", GrammarModel),
                new MenuItem(T("Ignored _rules..."), "", IgnoredRulesDialog),
                new MenuItem(T("Settin_gs..."), "", ShowSettings),
                new MenuItem(T("Code too_ls..."), "", ShowCodeTools),
                new MenuItem(T("Terminal _font..."), "", TerminalFont),
                new MenuItem(T("Command _be in any console..."), "", CommandEverywhere),
            }),
            new Menu(T("_Block"), new[]
            {
                new MenuItem(T("_Column mode"), "F4", () => editor.ToggleColumnMode()) { On = () => editor.ColumnMode },
                new MenuItem(T("_Indent lines"), "Tab", () => editor.IndentBlock()),
                new MenuItem(T("_Unindent lines"), "Shift+Tab", () => editor.UnindentBlock()),
            }),
            new Menu(T("_Window"), new[]
            {
                new MenuItem(T("_Next"), "F6", NextDoc),
                new MenuItem(T("_Previous"), "Shift+F6", PrevDoc),
            }),
            new Menu(T("Hel_p"), new[]
            {
                new MenuItem(T("_Keys and commands"), "F1", ShowHelp),
                new MenuItem(T("_About"), "", About),
            }),
        })
        {
            Opened = ProofingTools.LookForVideoCard,     // so that Tools > Set up grammar checking finds the answer ready
            Right = ClockText,
        };
        app.Menu = buildMenu();
    }

    // ---------- keys ----------
    internal static KeyInput K(Key key, Mods mods = Mods.None) => new(key, '\0', mods);

    internal static KeyInput Ctrl(char c) => new(Key.Character, c, Mods.Ctrl);

    internal static KeyInput Alt(char c) => new(Key.Character, c, Mods.Alt);
}
