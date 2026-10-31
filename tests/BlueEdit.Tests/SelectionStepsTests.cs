using Xunit;

public class SelectionStepsTests
{
    // the pieces Alt+= goes through from a caret, as text
    static List<string> Steps(string text, int line, int col, bool prose = true, (int, int)[]? headings = null)
    {
        var lines = text.Split('\n');
        var result = new List<string>();
        (int, int) start = (line, col), end = (line, col);
        while (SelectionSteps.Next(lines, start, end, prose, headings) is { } next)
        {
            start = next.Start; end = next.End;
            result.Add(Text(lines, start, end));
            Assert.True(result.Count < 30, "does not end");
        }
        return result;
    }

    static string Text(string[] lines, (int Line, int Col) a, (int Line, int Col) b) =>
        a.Line == b.Line ? lines[a.Line][a.Col..b.Col]
        : string.Join("\n", new[] { lines[a.Line][a.Col..] }.Concat(lines[(a.Line + 1)..b.Line]).Append(lines[b.Line][..b.Col]));

    [Fact]
    public void In_text_from_a_word_through_the_brackets_and_sentence_to_the_paragraph()
    {
        const string t = "Food sets a ceiling (the limit of growth). It slows down.\n\nNext one.";
        Assert.Equal(new[]
        {
            "limit",
            "the limit of growth",
            "(the limit of growth)",
            "Food sets a ceiling (the limit of growth).",
            "Food sets a ceiling (the limit of growth). It slows down.",
            t,
        }, Steps(t, 0, 27));
    }

    [Fact]
    public void Russian_words_and_quotes_count()
    {
        var s = Steps("Он сказал «добрый день» и ушёл.", 0, 12);
        Assert.Equal("добрый", s[0]);
        Assert.Equal("добрый день", s[1]);
        Assert.Equal("«добрый день»", s[2]);
    }

    [Fact]
    public void An_apostrophe_is_not_a_quote_in_text_but_is_in_code()
    {
        Assert.DoesNotContain("t say it, don", Steps("I don't say it, don't.", 0, 8));
        Assert.Contains("ab cd", Steps("x = 'ab cd'", 0, 6, prose: false));
    }

    [Fact]
    public void A_formula_is_a_piece_and_its_stops_end_no_sentence()
    {
        var s = Steps("It is $x = 3. y$ here. Next.", 0, 7);
        Assert.Equal("x", s[0]);
        Assert.Contains("x = 3. y", s);
        Assert.Contains("It is $x = 3. y$ here.", s);
    }

    [Fact]
    public void A_decimal_or_a_file_name_ends_no_sentence()
    {
        Assert.Contains("Pi is 3.14 or a.txt here.", Steps("Pi is 3.14 or a.txt here. Next.", 0, 1));
    }

    [Fact]
    public void Brackets_go_outward_over_lines_and_nested_ones_are_passed()
    {
        const string t = "f(a, g(b), [c,\n  d])";
        var s = Steps(t, 1, 2, prose: false);
        Assert.Equal("d", s[0]);
        Assert.Contains("c,\n  d", s);
        Assert.Contains("[c,\n  d]", s);
        Assert.Contains("a, g(b), [c,\n  d]", s);
        Assert.Contains("(a, g(b), [c,\n  d])", s);
    }

    [Fact]
    public void A_block_by_its_indent_in_code()
    {
        const string t = "def f():\n    a = 1\n\n    b = 2\nz = 3";
        Assert.Contains("    a = 1\n\n    b = 2", Steps(t, 1, 5, prose: false));
    }

    [Fact]
    public void Sections_nest_up_to_the_whole_file()
    {
        const string t = "# One\n\nintro\n\n## Two\n\nword here\n\n## Three\n\nmore";
        var s = Steps(t, 6, 1, headings: new[] { (0, 1), (4, 2), (8, 2) });
        Assert.Equal(new[] { "word", "word here", "## Two\n\nword here", t }, s);
    }

    [Fact]
    public void From_an_empty_line_it_goes_to_the_section_or_the_file()
    {
        Assert.Equal(new[] { "a\n\nb" }, Steps("a\n\nb", 1, 0));
    }

    [Fact]
    public void An_empty_file_offers_nothing()
    {
        Assert.Empty(Steps("", 0, 0));
    }

    [Fact]
    public void A_bracket_without_a_pair_is_passed_over()
    {
        Assert.Equal(new[] { "word", "(word", }, Steps("(word", 0, 2).Take(2));
    }

    [Fact]
    public void Each_step_holds_the_one_before()
    {
        const string t = "Say \"a (b c) d\" now. More \"text\".\n\nx";
        var s = Steps(t, 0, 8);
        for (int i = 1; i < s.Count; i++) Assert.Contains(s[i - 1], s[i], StringComparison.Ordinal);
    }
}
