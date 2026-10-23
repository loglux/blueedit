using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Tui;
using Xunit;

/// <summary>The interface in other languages (Ui.cs, #199): the tables, the fallback to English, and for every table
/// the menus it gives — each key of a table a string the code has, the menu letters keys that do not clash.</summary>
[CollectionDefinition("ui language", DisableParallelization = true)]
public sealed class UiLanguageCollection;

// the language is global and these switch it: they run alone, not beside the tests that read English messages
[Collection("ui language")]
public sealed class UiTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-ui-" + Guid.NewGuid().ToString("N"));
    public UiTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static string Root([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
    static string Editor() => Path.Combine(Root(), "src", "BlueEdit");

    public static TheoryData<string> Tables() => new(Ui.Available().Where(c => c != "en"));

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("ru-RU", "ru-ru")]
    [InlineData("ru_RU.UTF-8", "ru-ru")]
    [InlineData("pt_BR", "pt-br")]
    [InlineData("C", "en")]
    public void A_setting_or_LANG_becomes_a_tables_name(string setting, string code) => Assert.Equal(code, Ui.Code(setting));

    [Fact]
    public void A_regional_code_takes_its_languages_table_and_one_without_a_table_is_none()
    {
        Assert.Equal("_Файл", Ui.Load("ru-ru")!["_File"]);
        Assert.Null(Ui.Load("xx"));
        Assert.Equal("en", Ui.Available()[0]);
        Assert.Contains("ru", Ui.Available());
    }

    [Fact]
    public void A_users_file_adds_a_language_and_changes_strings()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "lang"));
        File.WriteAllText(Path.Combine(_dir, "lang", "de.json"), "// German, a start\n{ \"_File\": \"_Datei\", \"_Edit\": \"\" }");
        File.WriteAllText(Path.Combine(_dir, "lang", "ru.json"), "{ \"_File\": \"Ф_айл\" }");
        var de = Ui.Load("de", _dir)!;
        Assert.Equal("_Datei", de["_File"]);
        Assert.False(de.ContainsKey("_Edit"));                  // an empty value: not translated, stays English
        var ru = Ui.Load("ru", _dir)!;
        Assert.Equal("Ф_айл", ru["_File"]);
        Assert.Equal("Пр_авка", ru["_Edit"]);
        Assert.Contains("de", Ui.Available(_dir));
    }

    [Fact]
    public void A_string_a_table_lacks_stays_English()
    {
        try
        {
            Assert.True(Ui.Use("ru"));
            Assert.Equal("_Файл", Ui.T("_File"));
            Assert.Equal("No such string anywhere", Ui.T("No such string anywhere"));
            Assert.True(Ui.Use("xx"));
            Assert.Equal("en", Ui.Language);
            Assert.Equal("_File", Ui.T("_File"));
        }
        finally { Ui.Use("en"); }
    }

    // every string literal of the editor's code
    static readonly Lazy<HashSet<string>> Literals = new(() =>
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in Sources())
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"(?<!@)""((?:[^""\\\n]|\\.)*)"""))
                set.Add(Unescape(m.Groups[1].Value));
        return set;
    });

    static IEnumerable<string> Sources() => new[] { Editor(), Path.Combine(Editor(), "Lsp"), Path.Combine(Root(), "src", "Tui") }
        .SelectMany(d => Directory.EnumerateFiles(d, "*.cs"));

    // the strings the code hands to T() as they are written: T("…"), Tf("…", …), Ui.T("…"), Strings.T("…")
    // and a count's words, N(n, "{0} file", "{0} files"): the key is the second; Tc("status", "Ln"): "status|Ln"
    const string Lit = @"""((?:[^""\\\n]|\\.)*)""";
    static readonly Lazy<HashSet<string>> Translated = new(() => Sources().SelectMany(f =>
    {
        var src = File.ReadAllText(f);
        return Regex.Matches(src, @"\bTf?\(" + Lit).Select(m => Unescape(m.Groups[1].Value))
            .Concat(Regex.Matches(src, @"\b(?:Tn|N)\([^,""]+, " + Lit + ", " + Lit).Select(m => Unescape(m.Groups[2].Value)))
            .Concat(Regex.Matches(src, @"\bTc\(" + Lit + ", " + Lit).Select(m => Unescape(m.Groups[1].Value) + "|" + Unescape(m.Groups[2].Value)));
    }).ToHashSet(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(Tables))]
    public void A_built_in_table_has_every_string_the_code_translates(string code)
    {
        Assert.True(Translated.Value.Count >= 150, "T(\"…\") calls not found");
        var table = Ui.Load(code)!;
        var missing = Translated.Value.Where(s => !table.ContainsKey(s)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{code}: not translated: {string.Join(" | ", missing)}");
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void A_translation_keeps_the_places_of_the_values(string code)
    {
        static string Places(string s) => string.Join(",", Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Order(StringComparer.Ordinal));
        // a count's forms ("{0} файл|{0} файла|{0} файлов") each keep the places
        var wrong = Ui.Load(code)!.Where(p => p.Value.Split('|').Any(v => Places(p.Key) != Places(v))).Select(p => p.Value).ToList();
        Assert.True(wrong.Count == 0, $"{code}: {{0}}, {{1}} not as in the English: {string.Join(" | ", wrong)}");
    }

    [Fact]
    public void A_string_with_values_takes_them_in_the_translations_order()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "lang"));
        File.WriteAllText(Path.Combine(_dir, "lang", "xx.json"), "{ \"{0} of {1}\": \"{1}: {0}\", \"Line {0}\": \"Line {1}\", \"Drives\": \"Laufwerke\" }");
        try
        {
            Assert.True(Ui.Use("xx", _dir));
            Assert.Equal("b: a", Ui.T("{0} of {1}", "a", "b"));
            Assert.Equal("Line 5", Ui.T("Line {0}", 5));                // a wrong place in a user's file: the English
            Assert.Equal("Saved 1 file", Ui.N(1, "Saved {0} file", "Saved {0} files"));     // not in the table: English
            Assert.Equal("Saved 3 files", Ui.N(3, "Saved {0} file", "Saved {0} files"));
            Assert.Equal("Laufwerke", Strings.T("Drives"));                // the dialogs' own words too
        }
        finally { Ui.Use("en"); }
    }

    // a C# string literal's text: \" \\ \n (enough for the strings a table translates)
    static string Unescape(string s) => Regex.Replace(s, @"\\(.)", m => m.Groups[1].Value switch { "n" => "\n", "t" => "\t", var c => c });

    [Theory]
    [MemberData(nameof(Tables))]
    public void Every_key_of_a_table_is_a_string_the_code_has(string code)
    {
        // "hint|Save": a word in one place (Ui.Tc), the word itself a string of the code
        // (or a T()/Tc() call's: the literal scan cannot read a string inside an interpolated one, as the status line's)
        var stale = Ui.Load(code)!.Keys.Where(k => !Literals.Value.Contains(k[(k.IndexOf('|', StringComparison.Ordinal) + 1)..])
            && !Translated.Value.Contains(k)).ToList();
        Assert.True(stale.Count == 0, $"{code}: not in the code (renamed or removed?): {string.Join(" | ", stale)}");
    }

    // the menus as App.Menus.cs builds them: each menu's label and its items' labels
    static List<(string Label, List<string> Items)> Menus()
    {
        var src = File.ReadAllText(Path.Combine(Editor(), "App.Menus.cs"));
        var menus = new List<(string, List<string>)>();
        foreach (Match m in Regex.Matches(src, @"new (Menu|MenuItem)\(T\(""((?:[^""\\]|\\.)*)""\)"))
        {
            var label = Unescape(m.Groups[2].Value);
            if (m.Groups[1].Value == "Menu") menus.Add((label, new List<string>()));
            else menus[^1].Item2.Add(label);
        }
        return menus;
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void A_tables_menus_are_whole_and_their_letters_do_not_clash(string code)
    {
        var table = Ui.Load(code)!;
        var menus = Menus();
        Assert.True(menus.Count >= 9 && menus.Sum(m => m.Items.Count) >= 80, "menus not found in App.Menus.cs");
        string Tr(string s) => table.TryGetValue(s, out var t) ? t : s;
        var missing = menus.SelectMany(m => m.Items.Prepend(m.Label)).Where(s => !table.ContainsKey(s)).ToList();
        Assert.True(missing.Count == 0, $"{code}: menu labels not translated: {string.Join(" | ", missing)}");
        foreach (var s in menus.SelectMany(m => m.Items.Prepend(m.Label)))
            Assert.True(s.Count(c => c == '_') == Tr(s).Count(c => c == '_'), $"{code}: \"{Tr(s)}\" should mark a letter as \"{s}\" does");
        // MenuBar refuses two menus, or two items of a menu, on one key (in either layout)
        _ = new MenuBar(menus.Select(m => new Menu(Tr(m.Label), m.Items.Select(i => new MenuItem(Tr(i), "", () => { })).ToList())));
        // the menu takes Alt before the editor: no menu on a key the editor uses with Alt
        var alt = Directory.EnumerateFiles(Editor(), "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"Alt\('([a-z])'\)").Select(x => x.Groups[1].Value[0])).ToHashSet();
        var taken = menus.Where(m => alt.Contains(MenuBar.HotkeyOf(Tr(m.Label)))).Select(m => Tr(m.Label)).ToList();
        Assert.True(taken.Count == 0, $"{code}: menus on a key the editor uses with Alt: {string.Join(", ", taken)}");
    }

    [Fact]
    public void The_English_help_names_each_menus_Alt_letter()
    {
        // "Alt+F/E/S/..." in the help's first lines: View's V was once left out
        var line = HelpText.Lines.First(l => l.StartsWith("Menu:", StringComparison.Ordinal));
        var listed = Regex.Match(line, @"Alt\+([A-Z](?:/[A-Z])*)").Groups[1].Value;
        var letters = string.Join("/", Menus().Select(m => char.ToUpperInvariant(MenuBar.HotkeyOf(m.Label))));
        Assert.Equal(letters, listed);
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void A_table_has_each_key_once(string code)
    {
        // JSON takes the last of two equal keys without a word: one of the translations would be lost unseen
        var text = File.ReadAllText(Path.Combine(Root(), "lang", code + ".json"));
        var twice = Regex.Matches(text, @"(?m)^\s*""((?:[^""\\]|\\.)*)""\s*:").GroupBy(m => m.Groups[1].Value).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(twice.Count == 0, $"{code}: keys twice: {string.Join(" | ", twice)}");
    }

    [Theory]
    [InlineData(1, "1 файл"), InlineData(2, "2 файла"), InlineData(5, "5 файлов"), InlineData(11, "11 файлов")]
    [InlineData(12, "12 файлов"), InlineData(21, "21 файл"), InlineData(22, "22 файла"), InlineData(111, "111 файлов")]
    [InlineData(0, "0 файлов")]
    public void A_count_takes_the_russian_form_for_its_number(int n, string expected)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "lang"));
        File.WriteAllText(Path.Combine(_dir, "lang", "xx.json"), "{ \"{0} files\": \"{0} файл|{0} файла|{0} файлов\" }");
        try
        {
            Assert.True(Ui.Use("xx", _dir));
            Assert.Equal(expected, Ui.N(n, "{0} file", "{0} files"));
        }
        finally { Ui.Use("en"); }
    }

    [Fact]
    public void A_hotkey_is_a_key_in_either_layout()
    {
        Assert.Equal('a', MenuBar.HotkeyOf("_Файл"));
        Assert.Equal(',', MenuBar.HotkeyOf("_Блок"));
        Assert.Equal('f', MenuBar.HotkeyOf("_File"));
    }
}
