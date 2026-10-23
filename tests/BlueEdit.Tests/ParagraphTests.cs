using Tui;
using Xunit;

/// <summary>Paragraph formatting (#91): reformat to the margin, align, justify, wrap while typing.</summary>
public sealed class ParagraphTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-para-" + Guid.NewGuid().ToString("N"));
    public ParagraphTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string text, string name = "a.txt", int margin = 20)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView { RightMargin = margin };
        e.Load(p);
        e.SetArea(new Rect(0, 0, 80, 20));
        e.Draw(new Screen(80, 20));
        return e;
    }

    static void Type(EditorView e, string s)
    {
        foreach (var ch in s) e.HandleKey(new KeyInput(Key.Character, ch));
    }

    [Fact]
    public void Words_keep_inline_formulas_and_code_whole()
    {
        Assert.Equal(new[] { "where", "$x + 1$", "is", "`a b`", "here" }, EditorView.Words("where $x + 1$ is  `a b` here"));
    }

    [Fact]
    public void Fill_breaks_at_words_within_the_width_and_a_long_word_gets_its_own_line()
    {
        var lines = EditorView.Fill(EditorView.Words("one two three four five sixsixsixsixsixsix seven"), 10, "", "");
        Assert.Equal(new[] { "one two", "three four", "five", "sixsixsixsixsixsix", "seven" }, lines);
        Assert.All(lines.Where(l => !l.Contains("six", StringComparison.Ordinal)), l => Assert.True(l.Length <= 10));
    }

    [Fact]
    public void Justify_widens_the_gaps_from_the_left()
    {
        Assert.Equal("one   two  three", EditorView.Justify("one two three", 16, 0));
        Assert.Equal("  a    b", EditorView.Justify("  a b", 8, 2));                // the indentation stays
        Assert.Equal("single", EditorView.Justify("single", 16, 0));                 // one word: as it is
    }

    [Fact]
    public void Reformat_joins_and_breaks_the_paragraph_and_leaves_the_next_one()
    {
        var e = Open("aaa bbb\nccc ddd eee fff ggg hhh iii\n\nnext para\n");
        e.Reformat();
        Assert.Equal(new[] { "aaa bbb ccc ddd eee", "fff ggg hhh iii", "", "next para", "" }, e.Lines);
        e.Undo();                                                                    // one undo step
        Assert.Equal("aaa bbb", e.Lines[0]);
        Assert.Equal("ccc ddd eee fff ggg hhh iii", e.Lines[1]);
    }

    [Fact]
    public void A_selection_reformats_each_paragraph_in_it_on_its_own()
    {
        var e = Open("aaa bbb ccc ddd eee fff\n\nggg hhh iii jjj kkk lll\n");
        e.SelectAll();
        Assert.Contains("2 paragraphs", e.Reformat(), StringComparison.Ordinal);
        Assert.Equal(new[] { "aaa bbb ccc ddd eee", "fff", "", "ggg hhh iii jjj kkk", "lll", "" }, e.Lines);
    }

    [Fact]
    public void List_items_and_quotes_keep_their_marks_and_hang()
    {
        var e = Open("- one two three four five six\n", "a.md");
        e.Reformat();
        Assert.Equal(new[] { "- one two three four", "  five six", "" }, e.Lines);
        var q = Open("> one two three four five six\n", "q.md");
        q.Reformat();
        Assert.Equal(new[] { "> one two three four", "> five six", "" }, q.Lines);
    }

    [Fact]
    public void Align_right_and_centre_keep_the_line_breaks()
    {
        var e = Open("ab\ncdef\n", margin: 10);
        e.Align(Alignment.Right);
        Assert.Equal(new[] { "        ab", "      cdef", "" }, e.Lines);
        e.Align(Alignment.Centre);
        Assert.Equal(new[] { "    ab", "   cdef", "" }, e.Lines);
        e.Align(Alignment.Left);
        Assert.Equal(new[] { "ab", "cdef", "" }, e.Lines);
    }

    [Fact]
    public void Justify_reformats_and_stretches_all_but_the_last_line()
    {
        var e = Open("aaa bb c dddd ee f ggg\n", margin: 12);
        e.Align(Alignment.Justify);
        Assert.Equal(new[] { "aaa   bb   c", "dddd   ee  f", "ggg", "" }, e.Lines);
    }

    [Fact]
    public void Markdown_gets_reformat_but_not_centre_or_justify_and_code_gets_none()
    {
        var md = Open("one two three four five six\n", "a.md");
        Assert.Contains("plain text", md.Align(Alignment.Centre), StringComparison.Ordinal);
        Assert.Contains("plain text", md.Align(Alignment.Justify), StringComparison.Ordinal);
        Assert.Equal("one two three four five six", md.Lines[0]);
        var cs = Open("int a = 1; int b = 2; int c = 3; int d = 4;\n", "a.cs");
        Assert.Contains("not code", cs.Reformat(), StringComparison.Ordinal);
        Assert.Equal("int a = 1; int b = 2; int c = 3; int d = 4;", cs.Lines[0]);
    }

    [Fact]
    public void Markdown_headings_code_blocks_and_formula_lines_are_left_alone()
    {
        var e = Open("# A heading that is long enough\n\n```\ncode code code code code code\n```\n", "a.md");
        Assert.Contains("caret in a paragraph", e.Reformat(), StringComparison.Ordinal);
        e.GoToLine(4);
        Assert.Contains("caret in a paragraph", e.Reformat(), StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_wrap_moves_the_word_typed_past_the_margin_to_a_new_line()
    {
        var e = Open("\n");
        e.AutoWrap = true;
        Type(e, "one two three four five");
        Assert.Equal("one two three four", e.Lines[0]);
        Assert.Equal("five", e.Lines[1]);
        Assert.Equal((1, 4), (e.Row, e.Col));
        e.Undo();                                                                    // with the typing, as one step
        Assert.Equal("", e.Lines[0]);
    }

    [Fact]
    public void Auto_wrap_is_off_by_default_and_never_in_code()
    {
        var e = Open("\n");
        Type(e, "one two three four five");
        Assert.Equal("one two three four five", e.Lines[0]);
        var cs = Open("\n", "a.cs");
        cs.AutoWrap = true;
        Type(cs, "int a = 1; int b = 2; int c;");
        Assert.Single(cs.Lines, l => l.Length > 0);
    }
}
