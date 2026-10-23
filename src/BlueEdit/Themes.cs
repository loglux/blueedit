using Tui;

/// <summary>All colours of the editor and its chrome. Built-in themes plus per-name overrides from the settings.</summary>
sealed class EditorTheme
{
    public required string Name { get; init; }
    public required int Background { get; init; }
    public required int Text { get; init; }
    public required int Keyword { get; init; }
    public required int String { get; init; }
    public required int Comment { get; init; }
    public required int Number { get; init; }
    public required int Heading { get; init; }
    public required int Code { get; init; }
    public required int Emphasis { get; init; }
    public required int Math { get; init; }
    public required int SelectionText { get; init; }
    public required int Selection { get; init; }
    public required int Misspelled { get; init; }
    // chrome: tab strip, status line, key hints
    public required Style TabNormal { get; init; }
    public required Style TabActive { get; init; }
    public required Style Status { get; init; }
    public required Style HintKey { get; init; }
    public required Style HintText { get; init; }
    public required Theme Ui { get; init; }

    /// <summary>Token styles in <see cref="Tok"/> order.</summary>
    public Style[] Palette() => new[] { Text, Keyword, String, Comment, Number, Heading, Code, Emphasis, Math }
        .Select(fg => Style.Rgb(fg, Background)).ToArray();

    public Style SelectionStyle => Style.Rgb(SelectionText, Selection);
    public Style MisspelledStyle => Style.Rgb(Misspelled, Background, Attr.Underline);
    /// <summary>Grammar and style findings: underlined like typos, in a colour of their own.</summary>
    public int Grammar { get; init; } = 0x3794FF;
    public Style GrammarStyle => Style.Rgb(Grammar, Background, Attr.Underline);
    /// <summary>Language server findings: errors underlined like typos, warnings in a colour of their own.</summary>
    public int Warning { get; init; } = 0xCCA700;
    public Style ErrorStyle => Style.Rgb(Misspelled, Background, Attr.Underline);
    public Style WarningStyle => Style.Rgb(Warning, Background, Attr.Underline);
}

static class Themes
{
    public static readonly string[] Names = { "dos-blue", "dark", "light" };

    public static readonly string[] ColorNames =
        { "background", "text", "keyword", "string", "comment", "number", "heading", "code", "emphasis", "math", "selection", "misspelled", "grammar", "warning" };

    static readonly EditorTheme DosBlue = new()
    {
        Name = "dos-blue", Background = 0x0000AA, Text = 0xAAAAAA, Keyword = 0xFFFFFF, String = 0x55FFFF, Comment = 0x55FF55,
        Number = 0xFF55FF, Heading = 0xFFFF55, Code = 0x55FF55, Emphasis = 0xFFAA55, Math = 0x82B4FF,
        SelectionText = 0x000000, Selection = 0x00AAAA, Misspelled = 0xFF5555, Grammar = 0xFFFF55, Warning = 0xFFAA00,
        TabNormal = Style.Rgb(0xAAAAAA, 0x000000), TabActive = Style.Rgb(0x000000, 0x00AAAA),
        Status = Style.Rgb(0x000000, 0xAAAAAA), HintKey = Style.Rgb(0xFFFFFF, 0x000000), HintText = Style.Rgb(0x000000, 0x00AAAA),
        Ui = new Theme(),
    };

    static readonly EditorTheme Dark = new()
    {
        Name = "dark", Background = 0x1E1E1E, Text = 0xD4D4D4, Keyword = 0x569CD6, String = 0xCE9178, Comment = 0x6A9955,
        Number = 0xB5CEA8, Heading = 0x4FC1FF, Code = 0xD7BA7D, Emphasis = 0xC586C0, Math = 0x9CDCFE,
        SelectionText = 0xFFFFFF, Selection = 0x264F78, Misspelled = 0xF14C4C,
        TabNormal = Style.Rgb(0x969696, 0x252526), TabActive = Style.Rgb(0xFFFFFF, 0x1E1E1E),
        Status = Style.Rgb(0xFFFFFF, 0x007ACC), HintKey = Style.Rgb(0xFFFFFF, 0x3C3C3C), HintText = Style.Rgb(0xCCCCCC, 0x252526),
        Ui = new Theme
        {
            Bar = Style.Rgb(0xCCCCCC, 0x3C3C3C), BarHotkey = Style.Rgb(0x4FC1FF, 0x3C3C3C), BarSelected = Style.Rgb(0xFFFFFF, 0x094771),
            Menu = Style.Rgb(0xCCCCCC, 0x252526), MenuHotkey = Style.Rgb(0x4FC1FF, 0x252526), MenuSelected = Style.Rgb(0xFFFFFF, 0x094771),
            MenuShortcut = Style.Rgb(0x858585, 0x252526),
            Dialog = Style.Rgb(0xCCCCCC, 0x252526), DialogTitle = Style.Rgb(0xFFFFFF, 0x252526, Attr.Bold),
            Input = Style.Rgb(0xFFFFFF, 0x3C3C3C), ListSelected = Style.Rgb(0xFFFFFF, 0x094771),
            Button = Style.Rgb(0xCCCCCC, 0x3C3C3C), ButtonFocused = Style.Rgb(0xFFFFFF, 0x0E639C, Attr.Bold), Shadow = Style.Rgb(0x555555, 0x000000),
        },
    };

    static readonly EditorTheme Light = new()
    {
        Name = "light", Background = 0xFFFFFF, Text = 0x1F1F1F, Keyword = 0x0000FF, String = 0xA31515, Comment = 0x008000,
        Number = 0x098658, Heading = 0x800000, Code = 0x795E26, Emphasis = 0xAF00DB, Math = 0x0070C1,
        SelectionText = 0x000000, Selection = 0xADD6FF, Misspelled = 0xE51400, Grammar = 0x0050B0, Warning = 0xBF8803,
        TabNormal = Style.Rgb(0x6F6F6F, 0xECECEC), TabActive = Style.Rgb(0x1F1F1F, 0xFFFFFF),
        Status = Style.Rgb(0xFFFFFF, 0x007ACC), HintKey = Style.Rgb(0xFFFFFF, 0x6F6F6F), HintText = Style.Rgb(0x1F1F1F, 0xE5E5E5),
        Ui = new Theme
        {
            Bar = Style.Rgb(0x1F1F1F, 0xDDDDDD), BarHotkey = Style.Rgb(0xC00000, 0xDDDDDD), BarSelected = Style.Rgb(0xFFFFFF, 0x0060C0),
            Menu = Style.Rgb(0x1F1F1F, 0xF3F3F3), MenuHotkey = Style.Rgb(0xC00000, 0xF3F3F3), MenuSelected = Style.Rgb(0xFFFFFF, 0x0060C0),
            MenuShortcut = Style.Rgb(0x6F6F6F, 0xF3F3F3),
            Dialog = Style.Rgb(0x1F1F1F, 0xF3F3F3), DialogTitle = Style.Rgb(0x000000, 0xF3F3F3, Attr.Bold),
            Input = Style.Rgb(0x1F1F1F, 0xFFFFFF), ListSelected = Style.Rgb(0xFFFFFF, 0x0060C0),
            Button = Style.Rgb(0x1F1F1F, 0xDDDDDD), ButtonFocused = Style.Rgb(0xFFFFFF, 0x0060C0, Attr.Bold), Shadow = Style.Rgb(0x999999, 0x777777),
        },
    };

    public static EditorTheme Get(string name, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var t = name switch { "dark" => Dark, "light" => Light, _ => DosBlue };
        if (overrides == null || overrides.Count == 0) return t;
        int C(string key, int fallback) =>
            overrides.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase)).Value is { } v && Settings.ParseColor(v) is int rgb ? rgb : fallback;
        return new EditorTheme
        {
            Name = t.Name, Background = C("background", t.Background), Text = C("text", t.Text), Keyword = C("keyword", t.Keyword),
            String = C("string", t.String), Comment = C("comment", t.Comment), Number = C("number", t.Number), Heading = C("heading", t.Heading),
            Code = C("code", t.Code), Emphasis = C("emphasis", t.Emphasis), Math = C("math", t.Math),
            SelectionText = t.SelectionText, Selection = C("selection", t.Selection), Misspelled = C("misspelled", t.Misspelled),
            Grammar = C("grammar", t.Grammar), Warning = C("warning", t.Warning),
            TabNormal = t.TabNormal, TabActive = t.TabActive, Status = t.Status, HintKey = t.HintKey, HintText = t.HintText, Ui = t.Ui,
        };
    }
}
