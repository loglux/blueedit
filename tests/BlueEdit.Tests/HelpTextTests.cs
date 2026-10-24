using Tui;
using Xunit;

public class HelpTextTests
{
    static readonly List<string> English = HelpText.Lines(HelpText.English).ToList();

    [Theory]
    [InlineData("F2")]
    [InlineData("Ctrl+F")]
    [InlineData("Alt+Z")]
    [InlineData("F7")]
    [InlineData("Ctrl+E")]
    public void Help_mentions_the_main_keys(string key) => Assert.Contains(English, l => l.Contains(key, StringComparison.Ordinal));

    [Fact]
    public void The_help_is_in_sections_of_rows_and_paragraphs()
    {
        Assert.True(HelpText.English.Count >= 10, $"{HelpText.English.Count} sections");
        Assert.All(HelpText.English, s => Assert.NotEmpty(s.Rows));
        var files = HelpText.English.First(s => s.Title == "Files");
        Assert.Contains(new HelpRow("F2, Ctrl+S", "Save"), files.Rows);
        // a paragraph is one row however its source lines ran; no blank row first or last
        Assert.Contains(files.Rows, r => r.Keys == "" && r.Text.StartsWith("In Open and Save as:", StringComparison.Ordinal));
        Assert.All(HelpText.English, s => Assert.NotEqual(new HelpRow("", ""), s.Rows[^1]));
    }

    [Fact]
    public void Lines_of_a_paragraph_join_and_a_blank_line_ends_it()
    {
        var help = HelpText.Parse("before the first section\n# A\n\nF1 | Help\none\ntwo\n\nthree\n\n\n# B\nx | y\n");
        Assert.Equal(["A", "B"], help.Select(s => s.Title));
        Assert.Equal([new HelpRow("F1", "Help"), new HelpRow("", "one two"), new HelpRow("", ""), new HelpRow("", "three")], help[0].Rows);
    }

    // the function keys and Ctrl shortcuts a help names; Alt+letter is left out, as the menus' letters differ by language
    static HashSet<string> Keys(IEnumerable<string> lines) => lines
        .SelectMany(l => System.Text.RegularExpressions.Regex.Matches(l, @"\b(?:(?:Shift|Ctrl|Alt)\+)*F\d{1,2}\b|\bCtrl\+[A-Za-z0-9/]+")
            .Select(m => m.Value)).ToHashSet(StringComparer.Ordinal);

    public static TheoryData<string> Languages() => new(Ui.Available().Where(c => c != "en"));

    [Theory]
    [MemberData(nameof(Languages))]
    public void A_translated_help_has_the_sections_in_their_places_and_names_every_key(string code)
    {
        var help = HelpText.For(code);
        Assert.NotEqual(HelpText.English[0].Title, help[0].Title);
        // F1 opens a section by its place in the English help
        Assert.Equal(HelpText.English.Count, help.Count);
        var missing = Keys(English).Except(Keys(HelpText.Lines(help))).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{code}: the help lacks {string.Join(", ", missing)}");
        for (int i = 0; i < help.Count; i++)
        {
            var extra = Keys(HelpText.Lines([help[i]])).Except(Keys(HelpText.Lines([HelpText.English[i]]))).ToList();
            Assert.True(extra.Count == 0, $"{code}: section {i} ({help[i].Title}) has keys of another: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void F1_opens_on_the_section_of_the_files_kind()
    {
        Assert.Equal("Markdown", HelpText.English[HelpText.SectionFor(markdown: true, code: false)].Title);
        Assert.Equal("Code", HelpText.English[HelpText.SectionFor(markdown: false, code: true)].Title);
        Assert.Equal(0, HelpText.SectionFor(markdown: false, code: false));
    }

    [Fact]
    public void English_or_a_language_without_a_help_gets_the_English_one_and_a_users_file_comes_first()
    {
        Assert.Equal(HelpText.English, HelpText.For("en"));
        Assert.Equal(HelpText.English, HelpText.For("xx"));
        Assert.Equal(HelpText.Lines(HelpText.For("ru")), HelpText.Lines(HelpText.For("ru-RU")));
        var dir = Path.Combine(Path.GetTempPath(), "be-help-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "lang"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "lang", "help.de.md"), "# Tasten\r\nF1 | Hilfe\r\n");
            var help = HelpText.For("de", dir);
            Assert.Equal("Tasten", Assert.Single(help).Title);
            Assert.Equal(new HelpRow("F1", "Hilfe"), Assert.Single(help[0].Rows));
        }
        finally { Directory.Delete(dir, true); }
    }
}
