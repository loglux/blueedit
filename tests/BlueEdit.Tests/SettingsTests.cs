using Tui;
using Xunit;

public sealed class SettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-settings-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_dir, "settings.json");

    public SettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Missing_file_gives_defaults_without_error()
    {
        var (s, error) = Settings.Load(Path.Combine(_dir, "none.json"));
        Assert.Null(error);
        Assert.Equal("dos-blue", s.Theme);
        Assert.Equal(4, s.TabSize);
        Assert.True(s.AutoIndent);
        Assert.True(s.SpellAuto);                                       // the system's languages
        Assert.Equal(new[] { "en-US" }, s.SpellLanguages);               // fallback when the system names none
        Assert.Equal("live", s.MarkdownView);
    }

    [Fact]
    public void Template_reads_back_to_the_same_values()
    {
        var original = new Settings
        {
            Theme = "dark", TabSize = 2, AutoIndent = false, SpellCheck = false, SpellLanguages = new[] { "en-GB" }, SpellAuto = false,
            WrapProse = false, WrapCode = true, MarkdownView = "source",
            Colors = new Dictionary<string, string> { ["keyword"] = "#ffcc00" },
        };
        var text = Settings.Template(original);
        Assert.Contains("// ", text);                                   // the file explains itself
        var (s, error) = Settings.Parse(text);
        Assert.Null(error);
        Assert.Equal(("dark", 2, false, false, false, true, "source"), (s.Theme, s.TabSize, s.AutoIndent, s.SpellCheck, s.WrapProse, s.WrapCode, s.MarkdownView));
        Assert.Equal(new[] { "en-GB" }, s.SpellLanguages);
        Assert.False(s.SpellAuto);
        Assert.Equal("#ffcc00", s.Colors["keyword"]);
        Assert.True(Settings.Parse(Settings.Template(new Settings())).Settings.SpellAuto);   // "auto" survives too
    }

    [Fact]
    public void Spell_languages_switch_between_auto_and_a_list_in_the_file()
    {
        Settings.SetValue(FilePath, "spellLanguages", "[\"ru-RU\", \"en-GB\"]");
        var (s, error) = Settings.Load(FilePath);
        Assert.Null(error);
        Assert.False(s.SpellAuto);
        Assert.Equal(new[] { "ru-RU", "en-GB" }, s.SpellLanguages);
        Settings.SetValue(FilePath, "spellLanguages", "\"auto\"");                       // a list is replaced whole
        (s, error) = Settings.Load(FilePath);
        Assert.Null(error);
        Assert.True(s.SpellAuto);
        Settings.SetValue(FilePath, "spellLanguages", "[\"en-US\"]");
        Assert.Equal(new[] { "en-US" }, Settings.Load(FilePath).Settings.SpellLanguages);
    }

    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("de-DE", "de")]
    [InlineData("en-GB", "en-GB")]
    [InlineData("en-US", "en-US")]
    [InlineData("ru", "ru")]
    public void Short_language_tags(string tag, string shortTag) => Assert.Equal(shortTag, Spell.ShortTag(tag));

    [Theory]
    [InlineData(null, null, "de_DE.UTF-8", "de-DE")]
    [InlineData(null, null, "C.UTF-8", "")]                             // a server's locale names no language
    [InlineData(null, null, "POSIX", "")]
    [InlineData(null, null, null, "")]
    [InlineData("uk_UA:en_GB:en", null, "en_US.UTF-8", "uk-UA en-GB")]   // LANGUAGE's list, in its order
    [InlineData(null, "pt_BR.utf8", "en_US.UTF-8", "pt-BR")]            // LC_ALL before LANG
    [InlineData(null, null, "sr_RS.UTF-8@latin", "sr-RS")]
    public void Locale_languages(string? language, string? lcAll, string? lang, string tags) =>
        Assert.Equal(tags, string.Join(" ", Spell.LocaleLanguages(name => name switch
        {
            "LANGUAGE" => language, "LC_ALL" => lcAll, "LANG" => lang, _ => null,
        })));

    [WindowsFact]
    public void Windows_languages_are_all_ones_windows_can_check()
    {
        foreach (var tag in Spell.WindowsLanguages()) Assert.True(new Spell(tag).Available, tag);
    }

    [Fact]
    public void Comments_trailing_commas_and_any_key_case_are_accepted()
    {
        var (s, error) = Settings.Parse("// note\n{ /* inline */ \"TabSize\": 8, \"THEME\": \"light\", }");
        Assert.Null(error);
        Assert.Equal((8, "light"), (s.TabSize, s.Theme));
    }

    [Theory]
    [InlineData("{ \"tabSize\": 99 }", "tabSize")]
    [InlineData("{ \"theme\": \"neon\" }", "theme")]
    [InlineData("{ \"autoIndent\": \"yes\" }", "autoIndent")]
    [InlineData("{ \"markdownView\": \"preview\" }", "markdownView")]
    [InlineData("{ \"colors\": { \"keyword\": \"red\" } }", "colors.keyword")]
    [InlineData("{ \"colors\": { \"sparkle\": \"#ffffff\" } }", "colors.sparkle")]
    [InlineData("{ \"lineSpacing\": 12 }", "lineSpacing")]
    [InlineData("{ \"fontSize\": 99 }", "fontSize")]
    [InlineData("{ \"fontSetup\": \"always\" }", "fontSetup")]
    [InlineData("{ \"font\": \"\" }", "font")]
    public void Bad_values_are_reported_and_fall_back_to_defaults(string json, string mentioned)
    {
        var (s, error) = Settings.Parse(json);
        Assert.NotNull(error);
        Assert.Contains(mentioned, error, StringComparison.Ordinal);
        Assert.Equal(4, s.TabSize);
        Assert.Equal("dos-blue", s.Theme);
    }

    [Fact]
    public void One_bad_value_does_not_cancel_the_good_ones()
    {
        var (s, error) = Settings.Parse("{ \"tabSize\": 0, \"theme\": \"dark\" }");
        Assert.NotNull(error);
        Assert.Equal("dark", s.Theme);
    }

    [Fact]
    public void Broken_json_is_an_error_not_a_crash()
    {
        var (s, error) = Settings.Parse("{ \"theme\": ");
        Assert.NotNull(error);
        Assert.Equal("dos-blue", s.Theme);
    }

    [Fact]
    public void SetValue_keeps_comments_and_changes_only_that_value()
    {
        File.WriteAllText(FilePath, "// my own comment\n{\n    // tab\n    \"tabSize\": 3,\n    \"theme\": \"dos-blue\"\n}\n");
        Settings.SetValue(FilePath, "theme", "\"dark\"");
        var text = File.ReadAllText(FilePath);
        Assert.Contains("// my own comment", text);
        Assert.Contains("\"tabSize\": 3", text);
        Assert.Contains("\"theme\": \"dark\"", text);
        Assert.Equal("dark", Settings.Load(FilePath).Settings.Theme);
    }

    [Fact]
    public void SetValue_creates_the_file_from_the_template_when_missing()
    {
        Settings.SetValue(FilePath, "theme", "\"light\"");
        var (s, error) = Settings.Load(FilePath);
        Assert.Null(error);
        Assert.Equal("light", s.Theme);
        Assert.Contains("tabSize", File.ReadAllText(FilePath));
    }

    [Fact]
    public void SetValue_adds_a_key_that_is_not_in_the_file()
    {
        File.WriteAllText(FilePath, "{\n    \"tabSize\": 2\n}\n");
        Settings.SetValue(FilePath, "theme", "\"dark\"");
        var (s, error) = Settings.Load(FilePath);
        Assert.Null(error);
        Assert.Equal(("dark", 2), (s.Theme, s.TabSize));
    }

    [Fact]
    public void SetValue_adds_a_new_key_to_the_outer_object_only_not_into_colors()
    {
        File.WriteAllText(FilePath, "// c\r\n{\r\n    \"tabSize\": 2,\r\n    // \"grammar\": [\"x\"] in a comment\r\n    \"colors\": {\r\n    }\r\n}\r\n");
        Settings.SetValue(FilePath, "grammar", "[\"llm\"]");
        Settings.SetValue(FilePath, "grammar", "[\"languagetool\"]");    // the second time it is changed, not added
        var text = File.ReadAllText(FilePath);
        var (s, error) = Settings.Load(FilePath);
        Assert.Null(error);
        Assert.Equal(new[] { "languagetool" }, s.Grammar);
        Assert.Empty(s.Colors);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "^    \"grammar\"", System.Text.RegularExpressions.RegexOptions.Multiline));
        Assert.Contains("// \"grammar\": [\"x\"] in a comment", text);    // comments are left alone
        Assert.DoesNotContain("\n\n", text.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_set_twice_is_reported()
    {
        var (s, error) = Settings.Parse("{ \"theme\": \"dark\", \"theme\": \"light\" }");
        Assert.Equal("light", s.Theme);
        Assert.Contains("set twice", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Colour_overrides_change_only_the_named_colours()
    {
        var plain = Themes.Get("dark");
        var custom = Themes.Get("dark", new Dictionary<string, string> { ["keyword"] = "#123456" });
        Assert.Equal(0x123456, custom.Keyword);
        Assert.Equal(plain.String, custom.String);
        Assert.Equal(plain.Background, custom.Background);
    }

    [Fact]
    public void Every_theme_name_resolves_to_its_own_theme() =>
        Assert.Equal(Themes.Names, Themes.Names.Select(n => Themes.Get(n).Name));
}

public sealed class EditorOptionsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-opts-" + Guid.NewGuid().ToString("N"));
    public EditorOptionsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string name, string text, Action<EditorView>? configure = null)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView();
        configure?.Invoke(e);
        e.Load(p);
        return e;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public void Tab_inserts_tab_size_spaces(int size)
    {
        var e = Open("a.txt", "x\n", ed => ed.TabSize = size);
        e.HandleKey(new KeyInput(Key.Tab));
        Assert.Equal(new string(' ', size) + "x", e.Lines[0]);
    }

    [Fact]
    public void Auto_indent_can_be_turned_off()
    {
        var on = Open("a.txt", "    indented\n");
        on.Col = on.Lines[0].Length; on.HandleKey(new KeyInput(Key.Enter));
        Assert.Equal("    ", on.Lines[1]);
        var off = Open("b.txt", "    indented\n", ed => ed.AutoIndent = false);
        off.Col = off.Lines[0].Length; off.HandleKey(new KeyInput(Key.Enter));
        Assert.Equal("", off.Lines[1]);
    }

    [Fact]
    public void Wrap_defaults_follow_the_settings()
    {
        Assert.True(Open("a.md", "x").WrapOn);
        Assert.False(Open("a.cs", "x").WrapOn);
        Assert.False(Open("b.md", "x", ed => ed.WrapProse = false).WrapOn);
        Assert.True(Open("b.cs", "x", ed => ed.WrapCode = true).WrapOn);
    }

    [Fact]
    public void Markdown_files_open_in_the_configured_view()
    {
        Assert.Equal(MdView.Live, Open("a.md", "x").View);
        Assert.Equal(MdView.Source, Open("b.md", "x", ed => ed.DefaultMdView = MdView.Source).View);
        Assert.Equal(MdView.Reading, Open("c.md", "x", ed => ed.DefaultMdView = MdView.Reading).View);
    }

    [Fact]
    public void Theme_colours_reach_the_screen()
    {
        var e = Open("a.txt", "hello\n");
        var screen = new Screen(40, 10);
        e.SetArea(new Rect(0, 0, 40, 10));
        e.ApplyTheme(Themes.Get("light"));
        e.Draw(screen);
        Assert.Equal(0xFFFFFF, screen.StyleAt(0, 0).Bg);
        Assert.Equal(0xFFFFFF, screen.StyleAt(30, 5).Bg);              // empty space too
        e.ApplyTheme(Themes.Get("dark"));
        e.Draw(screen);
        Assert.Equal(0x1E1E1E, screen.StyleAt(0, 0).Bg);
    }
}
