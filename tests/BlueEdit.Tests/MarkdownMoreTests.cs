using Tui;
using Xunit;

/// <summary>Code blocks highlighted by language, hanging indent of wrapped lines, aligned tables.</summary>
public sealed class MarkdownMoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-mdmore-" + Guid.NewGuid().ToString("N"));
    public MarkdownMoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    (EditorView E, Screen S) Show(string text, string name = "a.md", int width = 40, int height = 15, Action<EditorView>? setup = null)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView();
        setup?.Invoke(e);
        e.Load(p);
        var s = new Screen(width, height);
        e.SetArea(new Rect(0, 0, width, height));
        e.ApplyTheme(Themes.Get("dark"));
        e.Draw(s);
        return (e, s);
    }

    static string Row(Screen s, int y) => new string(Enumerable.Range(0, s.Width).Select(x => s.CharAt(x, y)).ToArray()).TrimEnd();
    static readonly EditorTheme Dark = Themes.Get("dark");

    // ---------- code blocks ----------
    [Theory]
    [InlineData("cs", "C#")]
    [InlineData("csharp", "C#")]
    [InlineData("Python", "Python")]
    [InlineData("py", "Python")]
    [InlineData("c++", "C/C++")]
    [InlineData("{.rs}", "Rust")]
    [InlineData("js title=\"x\"", "JavaScript")]
    public void Fence_names_are_understood(string info, string lang) => Assert.Equal(lang, Languages.ForFence(info)?.Name);

    [Fact]
    public void Unknown_fence_names_give_no_language() => Assert.Null(Languages.ForFence("klingon"));

    [Fact]
    public void Code_block_lines_are_coloured_by_their_language()
    {
        // the reading view: no caret line shown raw, everything as it would be read
        var (e, s) = Show("text\n```cs\nint x = 1;\n```\nint after\n", setup: ed => ed.DefaultMdView = MdView.Reading);
        Assert.Equal("C#", e.FenceLanguageAt(2));
        Assert.Null(e.FenceLanguageAt(4));
        int y = Enumerable.Range(0, 10).First(r => Row(s, r).StartsWith("int x", StringComparison.Ordinal));
        Assert.Equal(Dark.Keyword, s.StyleAt(0, y).Fg);              // "int" as a C# keyword
        Assert.Equal(Dark.Number, s.StyleAt(8, y).Fg);               // "1"
        int y2 = Enumerable.Range(0, 10).First(r => Row(s, r).StartsWith("int after", StringComparison.Ordinal));
        Assert.Equal(Dark.Text, s.StyleAt(0, y2).Fg);                // after the block: Markdown text again
    }

    [Fact]
    public void Block_comments_continue_across_lines_inside_a_code_block()
    {
        var (_, s) = Show("```cs\n/* start\nend */ int y;\n```\n", setup: ed => ed.DefaultMdView = MdView.Source);
        Assert.Equal(Dark.Comment, s.StyleAt(0, 2).Fg);               // "end" is still in the comment
        Assert.Equal(Dark.Keyword, s.StyleAt(7, 2).Fg);               // "int" after it is a keyword
    }

    [Fact]
    public void Spelling_in_a_code_block_checks_comments_but_not_identifiers()
    {
        var spell = new Spell("en-US");
        if (!spell.Available) return;                                 // no Windows spell checker on this machine
        var (_, s) = Show("```cs\nvar qwzxv = 1; // qwzxv\n```\n", setup: ed => { ed.DefaultMdView = MdView.Source; ed.Spell = spell; ed.SpellOn = true; });
        Assert.False(s.StyleAt(4, 1).Attr.HasFlag(Attr.Underline));    // the variable name
        Assert.True(s.StyleAt(18, 1).Attr.HasFlag(Attr.Underline));     // the same letters in the comment
    }

    [Fact]
    public void A_block_without_a_known_language_keeps_the_code_colour()
    {
        var (_, s) = Show("```\nint x;\n```\n", setup: ed => ed.DefaultMdView = MdView.Source);
        Assert.Equal(Dark.Code, s.StyleAt(0, 1).Fg);
    }

    // ---------- hanging indent ----------
    [Fact]
    public void Break_leaves_room_for_the_hanging_indent()
    {
        // continuation rows have 10 - 4 = 6 columns: "cccc ", "dddd ", "eeee"
        Assert.Equal(new[] { 0, 10, 15, 20 }, EditorView.Break("aaaa bbbb cccc dddd eeee", 10, 4));
        Assert.Equal(new[] { 0, 10, 20 }, EditorView.Break("aaaa bbbb cccc dddd eeee", 10));
    }

    [Fact]
    public void Wrapped_list_items_continue_under_their_text()
    {
        var (_, s) = Show("- one two three four five six seven eight nine ten\n", width: 20, setup: ed => ed.DefaultMdView = MdView.Reading);
        Assert.Equal("• one two three", Row(s, 0));
        Assert.StartsWith("  ", Row(s, 1), StringComparison.Ordinal);
        Assert.Equal("four", Row(s, 1).Trim().Split(' ')[0]);
    }

    [Fact]
    public void Numbered_items_and_quotes_hang_too_but_plain_paragraphs_do_not()
    {
        var (_, s) = Show("x\n1. alpha beta gamma delta epsilon zeta\n\nplain alpha beta gamma delta epsilon\n", width: 20);
        Assert.StartsWith("   ", Row(s, 2), StringComparison.Ordinal);               // under "alpha", after "1. "
        int plain = Enumerable.Range(0, 10).First(r => Row(s, r).StartsWith("plain", StringComparison.Ordinal));
        Assert.False(Row(s, plain + 1).StartsWith(' '));
    }

    [Fact]
    public void Indented_code_wraps_under_its_indent()
    {
        var (_, s) = Show("    call(first, second, third, fourth)\n", name: "a.cs", width: 20, setup: ed => ed.WrapCode = true);
        Assert.StartsWith("    ", Row(s, 1), StringComparison.Ordinal);
        Assert.NotEqual(' ', Row(s, 1)[4]);
    }

    [Fact]
    public void Caret_click_and_up_down_respect_the_hanging_indent()
    {
        var (e, _) = Show("x\n- one two three four five six seven eight nine ten\n", width: 20);
        // caret on the continuation row, at "four": source column 2 + "one two three ".Length
        e.GoToLine(2, 3 + "one two three ".Length);
        Assert.Equal((2, 2), (e.CursorPos!.Value.X, e.CursorPos.Value.Y));   // drawn after the 2-column indent
        e.HandleKey(new KeyInput(Key.Up));
        e.HandleKey(new KeyInput(Key.Down));
        Assert.Equal((2, 2), (e.CursorPos!.Value.X, e.CursorPos.Value.Y));   // back to the same spot
        e.HandleMouse(new MouseInput(MouseKind.Down, 0, 0, 2));              // a click inside the indent
        e.HandleMouse(new MouseInput(MouseKind.Up, 0, 0, 2));
        Assert.Equal(2 + "one two three ".Length, e.Col);                     // = start of that row's text
    }

    // ---------- tables ----------
    const string Table = "| Name | Qty | Note |\n|:-----|----:|:----:|\n| tea | 2 | **hot** |\n| coffee | 10 | x |\n";

    [Fact]
    public void Cells_are_split_on_pipes_except_escaped_and_in_code()
    {
        var line = @"| a | b\|c | `d|e` |";
        Assert.Equal(new[] { "a", @"b\|c", "`d|e`" }, Tables.Cells(line).Select(c => line[c.Start..c.End].Trim()));
        Assert.Equal(new[] { "a", "b" }, Tables.Cells("a | b").Select(c => "a | b"[c.Start..c.End].Trim()));
    }

    [Fact]
    public void Tables_are_found_with_widths_and_alignment()
    {
        var lines = Table.Split('\n');
        var of = Tables.Find(lines, _ => true);
        var t = of[0]!;
        Assert.Equal((0, 3), (t.First, t.Last));
        Assert.Equal(new[] { 6, 3, 4 }, t.Widths);                           // "coffee", "Qty", "hot" without the stars
        Assert.Equal(new[] { 'l', 'r', 'c' }, t.Align);
        Assert.Null(of[4]);
    }

    [Fact]
    public void A_dash_line_without_pipes_is_not_a_table()
    {
        var of = Tables.Find(new[] { "Title", "-----", "text" }, _ => true);
        Assert.All(of, Assert.Null);
    }

    [Fact]
    public void Tables_render_aligned_with_box_lines()
    {
        var (_, s) = Show("intro\n\n" + Table, width: 40, setup: ed => ed.DefaultMdView = MdView.Reading);
        Assert.Equal("│ Name   │ Qty │ Note │", Row(s, 2));
        Assert.Equal("├────────┼─────┼──────┤", Row(s, 3));
        Assert.Equal("│ tea    │   2 │ hot  │", Row(s, 4));
        Assert.Equal("│ coffee │  10 │  x   │", Row(s, 5));
        Assert.Equal(Dark.Heading, s.StyleAt(2, 2).Fg);                      // header text
    }

    [Fact]
    public void In_live_preview_the_caret_row_of_a_table_shows_the_source()
    {
        var (e, s) = Show(Table + "\nafter\n", width: 40);
        e.GoToLine(3);
        e.Draw(s);
        Assert.Equal("| tea | 2 | **hot** |", Row(s, 2));
        Assert.Equal("│ coffee │  10 │  x   │", Row(s, 3));
    }

    [Fact]
    public void Table_display_maps_back_to_source_columns_without_going_backwards()
    {
        var lines = Table.Split('\n');
        var t = Tables.Find(lines, _ => true)[0]!;
        for (int i = 0; i < 4; i++)
        {
            var d = Tables.Render(lines[i], t, i);
            Assert.Equal(d.Text.Length, d.Map.Length);
            Assert.True(d.Map.Zip(d.Map.Skip(1)).All(p => p.First <= p.Second));
            Assert.All(d.Map, m => Assert.InRange(m, 0, lines[i].Length - 1));
        }
        var tea = Tables.Render(lines[2], t, 2);
        Assert.Equal(lines[2].IndexOf("tea", StringComparison.Ordinal), tea.Map[tea.Text.IndexOf("tea", StringComparison.Ordinal)]);
    }
}
