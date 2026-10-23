using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit;

public class HarperTests
{
    [Theory]
    [InlineData("en-GB", "British")]
    [InlineData("en-gb", "British")]
    [InlineData("en-US", "American")]
    [InlineData("en", "American")]
    [InlineData("en-AU", "Australian")]
    [InlineData("en-CA", "Canadian")]
    [InlineData("ru-RU", null)]
    [InlineData("eng", null)]
    public void The_dialect_comes_from_the_language(string language, string? dialect) =>
        Assert.Equal(dialect, HarperChecker.DialectOf(language));

    [Fact]
    public void Positions_are_lines_and_utf16_characters()
    {
        var text = "Первая строка\nsecond line\nthird";
        Assert.Equal(0, HarperChecker.OffsetOf(text, Pos(0, 0)));
        Assert.Equal(text.IndexOf("line", StringComparison.Ordinal), HarperChecker.OffsetOf(text, Pos(1, 7)));
        Assert.Equal(text.IndexOf("third", StringComparison.Ordinal), HarperChecker.OffsetOf(text, Pos(2, 0)));
        Assert.Equal(text.Length, HarperChecker.OffsetOf(text, Pos(9, 0)));          // past the end: the end
    }

    static JsonObject Pos(int line, int character) => new() { ["line"] = line, ["character"] = character };

    static IReadOnlyList<GrammarIssue> Ask(HarperChecker h, string text, string language)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (h.Issues(text, language) is { } issues) return issues;
            Thread.Sleep(20);
        }
        throw new TimeoutException("Harper did not answer: " + h.Status);
    }

    // the real harper-ls, when it is installed (%LOCALAPPDATA%\BlueEdit\harper or PATH)
    [Fact]
    public void The_real_server_finds_errors_with_fixes_and_keeps_british_english()
    {
        if (HarperChecker.ServerIn(HarperChecker.DefaultHome) == null) return;
        using var h = new HarperChecker(HarperChecker.DefaultHome);
        var text = "The colour of the solution changed and it was better then expected.";
        var found = Ask(h, text, "en-GB");
        var then = Assert.Single(found);
        Assert.Equal("then", text.Substring(then.Offset, then.Length));
        Assert.Contains("than", then.Replacements);
        Assert.Equal("Harper", then.Source);
        // British forms pass in en-GB; Russian is not Harper's
        Assert.Empty(Ask(h, "The committee have agreed; whilst the organisation analysed its behaviour, it learnt little.", "en-GB"));
        Assert.Empty(Ask(h, "Он сказал что придёт.", "ru-RU"));
        // spelling is the spelling provider's: off here even for an American spelling in en-GB
        Assert.Empty(Ask(h, "The color changed.", "en-GB"));
    }
}
