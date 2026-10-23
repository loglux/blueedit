using Tui;
using Xunit;

/// <summary>Display formulas laid out over several rows in the reading view (#49).</summary>
public sealed class LatexLayoutTests : IDisposable
{
    static string[] Lay(string tex) => LatexLayout.Lay(tex).Rows.Select(r => r.TrimEnd()).ToArray();

    [Fact]
    public void A_fraction_stands_over_a_bar_on_the_baseline()
    {
        Assert.Equal(new[] { "       x² + 1", "f(x) = ──────", "       2x - 3" }, Lay(@"f(x) = \frac{x^2 + 1}{2x - 3}"));
        Assert.Equal(new[] { "  1", "─────", "    1", "1 + ─", "    x" }, Lay(@"\frac{1}{1 + \frac{1}{x}}"));
        // a factor right after it keeps off the bar
        Assert.Equal(new[] { "6  7/2", "─ x", "7" }, Lay(@"\frac{6}{7}x^{7/2}"));
        Assert.Equal(new[] { "6", "─ (46.7)", "7" }, Lay(@"\frac{6}{7}(46.7)"));
    }

    [Fact]
    public void Operators_are_spaced_as_in_print_whatever_the_source_has()
    {
        // a converted book writes no spaces around operators and one between a number and a letter
        Assert.Equal(new[] { "     3y + 1", "y² + ────── < 4", "     2 - y²" }, Lay(@"y^{2}+\frac{3 y+1}{2-y^{2}}<4"));
        Assert.Equal(new[] { "  1", "- ─ ≤ x", "  2" }, Lay(@"-\frac{1}{2}\le x"));      // a sign, not an operator
    }

    [Fact]
    public void A_root_of_one_row_is_written_in_one_row_and_a_taller_one_has_a_bar_over_it()
    {
        // a bar on a row of its own floated above the sign (the user, 10 October 2026)
        Assert.Equal(new[] { "    -b ± √(b² - 4ac)", "x = ────────────────", "           2a" },
            Lay(@"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}"));
        Assert.Equal(new[] { " 7/2", "3    = 3³ · √3" }, Lay(@"3^{7/2} = 3^3 \cdot \sqrt{3}"));
        Assert.Equal(new[] { " ─", "│1", "│─", "√2" }, Lay(@"\sqrt{\frac{1}{2}}"));
    }

    [Fact]
    public void Limits_go_above_and_below_sum_and_lim_and_beside_an_integral()
    {
        Assert.Equal(new[] { " n     n(n + 1)", " ∑ i = ────────", "i=1       2" }, Lay(@"\sum_{i=1}^{n} i = \frac{n(n+1)}{2}"));
        Assert.Equal(new[] { "     sin x", " lim ───── = 1", "x → 0  x" }, Lay(@"\lim_{x \to 0} \frac{\sin x}{x} = 1"));
        Assert.Equal(new[] { "⌠b", "⎮    f(x) dx", "⌡a+1" }, Lay(@"\int_{a+1}^{b} f(x)\,dx"));
        Assert.Equal(new[] { "⌠1         1", "⎮  x² dx = ─", "⌡0         3" }, Lay(@"\int_0^1 x^2 \, dx = \frac{1}{3}"));
    }

    [Fact]
    public void An_integral_sign_is_as_tall_as_its_integrand_and_three_rows_at_least()
    {
        Assert.Equal(new[] { "⌠", "⎮ x dx", "⌡" }, Lay(@"\int x\,dx"));
        // up to the relation: the fraction after "=" does not make it taller
        Assert.Equal(new[] { "⌠1   1        1   1", "⎮  ───── dx = ─ + ─", "⌡0 x + 1      2   x" },
            Lay(@"\int_0^1 \frac{1}{x+1}\,dx = \frac{1}{2} + \frac{1}{x}"));
        Assert.Equal(new[] { "⌠⌠", "⎮⎮  f dA", "⌡⌡D" }, Lay(@"\iint_D f\,dA"));
    }

    [Fact]
    public void Scripts_are_script_characters_when_unicode_has_them_otherwise_a_row_up()
    {
        Assert.Equal(new[] { "cos²α + sin²α = 1" }, Lay(@"\cos^2 \alpha + \sin^2 \alpha = 1"));
        Assert.Equal(new[] { " -x²/2", "e" }, Lay(@"e^{-x^2/2}"));
        Assert.Null(LatexLayout.Rows(@"a^2 + b^2 = c^2"));                // one row: the one-line form does
    }

    [Fact]
    public void Brackets_grow_with_what_they_hold()
    {
        Assert.Equal(new[] { "⎛ a ⎞2   a²", "⎜ ─ ⎟  = ──", "⎝ b ⎠    b²" }, Lay(@"\left( \frac{a}{b} \right)^2 = \frac{a^2}{b^2}"));
        Assert.Equal(new[] { "⎧ 1  1 ⎫", "⎨ ─, ─ ⎬", "⎩ 2  3 ⎭" }, Lay(@"\left\{ \frac{1}{2}, \frac{1}{3} \right\}"));
        Assert.Equal(new[] { "E[(X - μ)²]" }, Lay(@"E\left[(X - \mu)^2\right]"));
    }

    [Fact]
    public void Aligned_rows_cases_and_matrices_are_grids()
    {
        Assert.Equal(new[] { "2x + 3 = 7", "    2x = 4", "     x = 2" }, Lay(@"\begin{aligned} 2x + 3 &= 7 \\ 2x &= 4 \\ x &= 2 \end{aligned}"));
        Assert.Equal(new[] { "|x| = ⎧ x,    x ⩾ 0", "      ⎩ -x,   x < 0" }, Lay(@"|x| = \begin{cases} x, & x \geqslant 0 \\ -x, & x < 0 \end{cases}"));
        Assert.Equal(new[] { "⎛1  2⎞", "⎝3  4⎠" }, Lay(@"\begin{pmatrix} 1 & 2 \\ 3 & 4 \end{pmatrix}"));
        Assert.Equal(new[] { "a    bb", "───────", "ccc   d" }, Lay(@"\begin{array}{l|r} a & bb \\ \hline ccc & d \end{array}"));
    }

    [Fact]
    public void Marks_over_and_under_a_tag_and_unknown_commands()
    {
        Assert.Equal(new[] { "─→   ─→", "AB + BC" }, Lay(@"\vec{AB} + \overrightarrow{BC}"));
        Assert.Equal(new[] { "1 + ⋯ + n", "╰───┬───╯", " n terms" }, Lay(@"\underbrace{1 + \cdots + n}_{n \text{ terms}}"));
        Assert.Equal(new[] { "a² + b² = c²   (1)" }, Lay(@"a^2 + b^2 = c^2 \tag{1}"));
        Assert.Equal(new[] { @"\weird{x} + 1" }, Lay(@"\weird{x} + 1"));               // as written, nothing lost
        Assert.NotNull(LatexLayout.Lay(@"\frac{1}{2 + {x \left( \begin{aligned} a & b"));   // broken input does not throw
    }

    // ---------- in the editor ----------
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-mathlayout-" + Guid.NewGuid().ToString("N"));
    public LatexLayoutTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    Screen Show(string text, MdView view, bool wrap = true, int width = 40, int caretLine = 0)
    {
        var p = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(p, text);
        var e = new EditorView { WrapProse = wrap };
        e.Load(p);
        e.SetView(view);
        if (caretLine > 0) e.SelectAt(caretLine + 1, 1, 0);
        var s = new Screen(width, 12);
        e.SetArea(new Rect(0, 0, width, 12));
        e.ApplyTheme(Themes.Get("dark"));
        e.Draw(s);
        return s;
    }

    static string Row(Screen s, int y) => new string(Enumerable.Range(0, s.Width).Select(x => s.CharAt(x, y)).ToArray()).TrimEnd();
    static string[] Rows(Screen s, int n) => Enumerable.Range(0, n).Select(y => Row(s, y)).ToArray();

    const string Note = "Area:\n$$\n\\frac{a}{b}\n$$\nafter";

    [Fact]
    public void The_reading_view_lays_a_display_formula_out_over_rows_and_the_text_below_moves_down()
    {
        var s = Show(Note, MdView.Reading);
        Assert.Equal(new[] { "Area:", "", "  a", "  ─", "  b", "", "after" }, Rows(s, 7));
        // one line of its own: "$$ ... $$"
        s = Show("$$\\frac{1}{2}$$\nnext", MdView.Reading);
        Assert.Equal(new[] { "  1", "  ─", "  2", "next" }, Rows(s, 4));
        // a block over several source lines: one formula, rows given to the lines in turn
        s = Show("$$\n\\begin{aligned}\na &= 1 \\\\\nbb &= 2\n\\end{aligned}\n$$\nend", MdView.Reading);
        Assert.Equal(new[] { "", "   a = 1", "  bb = 2", "", "", "", "end" }, Rows(s, 7));
    }

    [Fact]
    public void Live_preview_lays_it_out_too_and_shows_the_block_as_written_while_the_caret_is_in_it()
    {
        string[] picture = { "Area:", "", "  a", "  ─", "  b", "", "after" };
        string[] source = { "Area:", "$$", @"\frac{a}{b}", "$$", "after" };
        Assert.Equal(picture, Rows(Show(Note, MdView.Live), 7));
        Assert.Equal(source, Rows(Show(Note, MdView.Live, caretLine: 2), 5));
        Assert.Equal(source, Rows(Show(Note, MdView.Live, caretLine: 3), 5));
        // \[ and \] on lines of their own (#128), as Mathpix and pandoc write them
        const string bracket = "Area:\n\\[\n\\frac{a}{b}\n\\]\nafter";
        Assert.Equal(picture, Rows(Show(bracket, MdView.Reading), 7));
        Assert.Equal(picture, Rows(Show(bracket, MdView.Live), 7));
    }

    [Fact]
    public void A_line_of_text_with_a_tall_inline_formula_is_laid_out_over_rows_on_its_baseline()
    {
        const string text = "Area: $\\int_1^3 (3x^{5/2} + x^2)\\,dx$ **here**\nF: $F(x) = \\frac{6}{7}x^{7/2}$.\nplain $x^2$ only\nnext";
        string[] want =
        {
            "      ⌠3    5/2",
            "Area: ⎮  (3x    + x²) dx here",
            "      ⌡1",
            "          6  7/2",
            "F: F(x) = ─ x   .",
            "          7",
            "plain x² only",
            "next",
        };
        Assert.Equal(want, Rows(Show(text, MdView.Reading), want.Length));
        Assert.Equal(want, Rows(Show(text, MdView.Live, caretLine: 3), want.Length));
        // the caret's line in Live preview as written; without soft wrap one line
        Assert.Equal("F: $F(x) = \\frac{6}{7}x^{7/2}$.", Rows(Show(text, MdView.Live, caretLine: 1), 6)[3]);
        Assert.StartsWith("Area: ∫₁³", Row(Show(text, MdView.Reading, wrap: false), 0), StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_in_the_reading_view_scroll_the_page_at_once_and_the_wheel_and_keys_agree()
    {
        var p = Path.Combine(_dir, "long.md");
        File.WriteAllText(p, string.Join("\n", Enumerable.Range(1, 60).Select(i => "line " + i)));
        var e = new EditorView();
        e.Load(p);
        e.SetView(MdView.Reading);
        var s = new Screen(40, 12);
        e.SetArea(new Rect(0, 0, 40, 12));
        e.ApplyTheme(Themes.Get("dark"));
        string Top() { e.Draw(s); return Row(s, 0); }
        Assert.Equal("line 1", Top());
        e.HandleKey(new KeyInput(Key.Down));
        Assert.Equal("line 2", Top());                                       // one press, one row
        e.HandleKey(new KeyInput(Key.PageDown));
        Assert.Equal("line 13", Top());
        e.HandleKey(new KeyInput(Key.Character, ' '));
        Assert.Equal("line 24", Top());
        e.HandleKey(new KeyInput(Key.Character, ' ', Mods.Shift));
        Assert.Equal("line 13", Top());
        e.HandleMouse(new MouseInput(MouseKind.WheelDown, 0, 5, 5));
        e.HandleKey(new KeyInput(Key.Down));
        Assert.Equal("line 17", Top());                                      // on from the wheel, not back to the caret
        e.HandleKey(new KeyInput(Key.End));
        Assert.Equal("line 49", Top());                                      // the last page full
        Assert.Equal(48, e.Row);                                             // the caret on the first row shown
        e.HandleKey(new KeyInput(Key.Home));
        Assert.Equal("line 1", Top());
    }

    [Fact]
    public void Without_wrap_or_in_a_narrow_window_it_stays_one_line()
    {
        Assert.Equal("  a/b", Row(Show(Note, MdView.Reading, wrap: false), 2));
        // 23 columns of picture in a 20-column window: the one-line form, wrapped
        Assert.StartsWith("  ½ + 1/(a", Row(Show("x\n$$\n\\frac{1}{2} + \\frac{1}{a + b + c + d + e}\n$$\n", MdView.Reading, width: 20), 2), StringComparison.Ordinal);
    }
}
