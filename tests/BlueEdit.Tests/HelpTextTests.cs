using Xunit;

public class HelpTextTests
{
    [Fact]
    public void Help_lines_fit_a_100_column_terminal_with_the_dialog_frame() =>
        Assert.All(HelpText.Lines, l => Assert.True(l.Length <= 86, $"{l.Length} chars: {l}"));

    [Theory]
    [InlineData("F2")]
    [InlineData("Ctrl+F")]
    [InlineData("Alt+Z")]
    [InlineData("F7")]
    [InlineData("Ctrl+E")]
    public void Help_mentions_the_main_keys(string key) => Assert.Contains(HelpText.Lines, l => l.Contains(key, StringComparison.Ordinal));

    // the function keys and Ctrl shortcuts a help names; Alt+letter is left out, as the menus' letters differ by language
    static HashSet<string> Keys(IEnumerable<string> lines) => lines
        .SelectMany(l => System.Text.RegularExpressions.Regex.Matches(l, @"\b(?:(?:Shift|Ctrl|Alt)\+)*F\d{1,2}\b|\bCtrl\+[A-Za-z0-9/]+")
            .Select(m => m.Value)).ToHashSet(StringComparer.Ordinal);

    public static TheoryData<string> Languages() => new(Ui.Available().Where(c => c != "en"));

    [Theory]
    [MemberData(nameof(Languages))]
    public void A_translated_help_fits_and_names_every_key_the_English_one_does(string code)
    {
        var lines = HelpText.For(code);
        Assert.NotEqual(HelpText.Lines[0], lines[0]);
        Assert.All(lines, l => Assert.True(l.Length <= 86, $"{l.Length} chars: {l}"));
        var missing = Keys(HelpText.Lines).Except(Keys(lines)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{code}: the help lacks {string.Join(", ", missing)}");
    }

    [Fact]
    public void English_or_a_language_without_a_help_gets_the_English_one_and_a_users_file_comes_first()
    {
        Assert.Equal(HelpText.Lines, HelpText.For("en"));
        Assert.Equal(HelpText.Lines, HelpText.For("xx"));
        Assert.Equal(HelpText.For("ru"), HelpText.For("ru-RU"));
        var dir = Path.Combine(Path.GetTempPath(), "be-help-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "lang"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "lang", "help.de.txt"), "TASTEN\r\n  F1 Hilfe\r\n");
            Assert.Equal(["TASTEN", "  F1 Hilfe", ""], HelpText.For("de", dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
