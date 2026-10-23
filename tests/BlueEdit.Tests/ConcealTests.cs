using Xunit;

public class ConcealTests
{
    [Theory]
    [InlineData("# Heading one", "Heading one")]
    [InlineData("### Deep *heading*", "Deep heading")]
    [InlineData("Some **bold** and *italic* and ~~gone~~ end", "Some bold and italic and gone end")]
    [InlineData("A [link text](http://example.com/x) here", "A link text here")]
    [InlineData("see ![pic](a.png) now", "see ▣ pic now")]
    [InlineData("[[Note|alias]] and [[Plain]]", "alias and Plain")]
    [InlineData("- bullet item", "• bullet item")]
    [InlineData("  * nested **b**", "  • nested b")]
    [InlineData("- [ ] todo", "☐ todo")]
    [InlineData("- [x] done", "☑ done")]
    [InlineData("> quoted *text*", "│ quoted text")]
    [InlineData("1. numbered **x**", "1. numbered x")]
    [InlineData(@"escaped \*star\* and \# hash", "escaped *star* and # hash")]
    public void Hides_markup(string source, string shown)
    {
        var d = Conceal.Get(source);
        Assert.NotNull(d);
        Assert.Equal(shown, d!.Text);
    }

    [Theory]
    [InlineData("plain line no markup")]
    [InlineData("snake_case_name and 2*3*4")]
    [InlineData("")]
    public void Leaves_ordinary_text_alone(string source) => Assert.Null(Conceal.Get(source));

    [Fact]
    public void Code_spans_are_not_processed()
    {
        var d = Conceal.Get("a `code **kept**` b");
        Assert.Equal("a code **kept** b", d!.Text);
    }

    [Fact]
    public void Horizontal_rule_becomes_a_line()
    {
        var d = Conceal.Get("---");
        Assert.NotNull(d);
        Assert.All(d!.Text, c => Assert.Equal('─', c));
    }

    [Theory]
    [InlineData("inline $a^2$ math", "inline a² math")]
    [InlineData("cost $5 and $10 total", "cost $5 and $10 total")]
    [InlineData("one line $$x \\in \\mathbb{R}$$ end", "one line x ∈ ℝ end")]
    public void Math_is_rendered_only_when_it_is_math(string source, string shown)
    {
        var d = Conceal.Get(source);
        Assert.Equal(shown, d?.Text ?? source);
    }

    [Fact]
    public void Map_points_back_to_the_source_columns()
    {
        var d = Conceal.Get("a **bold** z")!;
        Assert.Equal("a bold z", d.Text);
        Assert.Equal(new[] { 0, 1, 4, 5, 6, 7, 10, 11 }, d.Map);
        Assert.Equal(4, d.ToSource(2));            // 'b' of bold
        Assert.Equal(4, d.ToDisplay(6));           // first display column at or after source column 6
        Assert.Equal(12, d.ToSource(99));          // past the end -> end of the source line
    }

    [Fact]
    public void Display_math_lines_are_converted_and_delimiters_vanish()
    {
        Assert.Equal("", Conceal.Empty("$$").Text);
        Assert.Equal("  ∫₀¹ x² dx = ⅓", Conceal.MathLine(@"\int_0^1 x^2\,dx = \frac{1}{3}").Text);
    }
}
