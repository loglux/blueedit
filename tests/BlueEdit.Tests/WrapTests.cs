using Xunit;

public class WrapTests
{
    static readonly string Words = string.Join(" ", Enumerable.Range(0, 60).Select(i => $"w{i:000}"));

    [Fact]
    public void Short_line_is_one_row() => Assert.Equal(new[] { 0 }, EditorView.Break("short", 20));

    [Fact]
    public void Breaks_at_the_last_space_that_fits()
    {
        var starts = EditorView.Break(Words, 109);
        Assert.Equal(new[] { 0, 105, 210 }, starts);       // 21 words of 5 columns per row
    }

    [Fact]
    public void Very_long_word_is_cut_at_the_width() =>
        Assert.Equal(new[] { 0, 10, 20 }, EditorView.Break(new string('x', 25), 10));

    [Theory]
    [InlineData(10)]
    [InlineData(37)]
    [InlineData(109)]
    public void Rows_never_exceed_the_width_and_cover_the_whole_line(int cap)
    {
        var starts = EditorView.Break(Words, cap);
        Assert.Equal(0, starts[0]);
        for (int i = 0; i < starts.Length; i++)
        {
            int end = i + 1 < starts.Length ? starts[i + 1] : Words.Length;
            Assert.InRange(end - starts[i], 1, cap);
        }
    }
}
