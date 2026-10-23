using Tui;
using Xunit;

// FormulaImages is global (one terminal): these tests run alone, not beside the other layout tests
[CollectionDefinition("formula pictures", DisableParallelization = true)]
public sealed class FormulaPicturesCollection;

[Collection("formula pictures")]
public sealed class FormulaPictureTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-formulapics-" + Guid.NewGuid().ToString("N"));
    int _width = 95, _height = 90;          // what the fake module draws: 10 cells by 5 rows of 10 × 20 pixels

    public FormulaPictureTests()
    {
        Directory.CreateDirectory(_dir);
        FormulaImages.Reset();
        FormulaImages.Renderer = (tex, size) => tex.Contains("bad", StringComparison.Ordinal)
            ? (null, 0, 0, "cannot read it")
            : (Enumerable.Repeat((byte)255, _width * _height).ToArray(), _width, _height, null);
        FormulaImages.SetTerminal(true, (10, 20));
    }

    public void Dispose()
    {
        FormulaImages.SetTerminal(false, null);
        FormulaImages.Renderer = null;
        FormulaImages.Reset();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    EditorView Open(string text, MdView view, int width = 40, int height = 12)
    {
        var p = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(p, text);
        var e = new EditorView { WrapProse = true };
        e.Load(p);
        e.SetView(view);
        e.SetArea(new Rect(0, 0, width, height));
        e.ApplyTheme(Themes.Get("dark"));
        return e;
    }

    // draws once (which asks for the pictures), waits for them, takes them as the editor does, lays out and draws
    Screen DrawWhenDrawn(EditorView e, int width = 40, int height = 12)
    {
        var s = new Screen(width, height);
        e.Draw(s);
        TakePictures(e);
        e.Draw(s);
        return s;
    }

    static void TakePictures(EditorView e)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!FormulaImages.Idle) { Assert.True(DateTime.UtcNow < until, "the pictures were drawn"); Thread.Sleep(10); }
        FormulaImages.Taken();
        e.FormulaPicturesChanged();
    }

    static string Row(Screen s, int y) => new string(Enumerable.Range(0, s.Width).Select(x => s.CharAt(x, y)).ToArray()).TrimEnd();

    [Fact]
    public void A_display_formula_is_text_until_its_picture_is_drawn_then_its_rows_show_the_picture()
    {
        var e = Open("Area:\n$$\n\\frac{a}{b}\n$$\nafter", MdView.Reading);
        var s = new Screen(40, 12);
        e.Draw(s);
        Assert.Equal("  a", Row(s, 2));                             // the text picture meanwhile
        Assert.Null(s.PictureAt(2, 2));

        s = DrawWhenDrawn(e);
        Assert.Equal("Area:", Row(s, 0));
        Assert.Equal("", Row(s, 1));                                // the $$ line
        for (int r = 0; r < 5; r++)
        {
            var at = s.PictureAt(Disp.PictureIndent, 2 + r);
            Assert.NotNull(at);
            Assert.Equal(r, at.Value.Row);
            Assert.Equal(10, at.Value.Picture.Cols);
            Assert.Equal("", Row(s, 2 + r));                        // blanks under it
        }
        Assert.Null(s.PictureAt(Disp.PictureIndent - 1, 2));
        Assert.Equal("", Row(s, 7));                                // the closing $$
        Assert.Equal("after", Row(s, 8));                           // moved down: the picture is 5 rows, the text 3
    }

    [Fact]
    public void A_formula_the_module_cannot_read_or_wider_than_the_window_stays_text()
    {
        _width = 500;                                               // 50 columns: more than the window's 40
        var e = Open("$$\n\\bad{a}\n$$\n\n$$\nx^2 + y^2\n$$", MdView.Reading);
        var s = DrawWhenDrawn(e);
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 40; x++) Assert.Null(s.PictureAt(x, y));
        Assert.Contains("  x² + y²", Enumerable.Range(0, 12).Select(y => Row(s, y)));
    }

    [Fact]
    public void Live_preview_shows_the_formula_the_caret_is_in_as_written()
    {
        var e = Open("text\n$$\nx^2 + y^2\n$$\nafter", MdView.Live);
        var s = DrawWhenDrawn(e);
        Assert.NotNull(s.PictureAt(Disp.PictureIndent, 2));
        e.SelectAt(3, 1, 0);                                        // the caret into the formula
        e.Draw(s);
        Assert.Equal("$$", Row(s, 1));
        Assert.Equal("x^2 + y^2", Row(s, 2));
        Assert.Null(s.PictureAt(Disp.PictureIndent, 2));
    }

    [Fact]
    public void The_line_at_the_top_stays_there_when_pictures_above_it_change_the_height()
    {
        var text = "$$\nx^2 + y^2\n$$\n" + string.Join("\n", Enumerable.Range(1, 30).Select(i => "line " + i));
        var e = Open(text, MdView.Reading, height: 8);
        var s = new Screen(40, 8);
        e.Draw(s);                                                  // asks for the picture; text until it is taken
        e.HandleKey(new KeyInput(Key.PageDown));
        e.Draw(s);
        string top = Row(s, 0);
        Assert.StartsWith("line ", top);
        TakePictures(e);                                            // 5 rows instead of 1 above: the page stays
        e.Draw(s);
        Assert.Equal(top, Row(s, 0));
        e.HandleKey(new KeyInput(Key.Home));
        e.Draw(s);
        Assert.NotNull(s.PictureAt(Disp.PictureIndent, 1));
    }

    [Fact]
    public void A_drawn_formula_is_centred_in_whole_cells_and_its_edges_made_a_little_darker()
    {
        var pic = FormulaImages.Draw((_, _) => (new byte[] { 255, 128, 0, 64 }, 2, 2, null), "x", (4, 6));
        Assert.NotNull(pic);
        Assert.Equal((1, 1), (pic.Cols, pic.Rows));
        // 2 × 2 pixels in a 4 × 6 cell: one column and two rows of space before them
        Assert.Equal(255, pic.At(1, 2));
        Assert.InRange(pic.At(2, 2), 129, 254);                     // half coverage drawn darker than half
        Assert.Equal(0, pic.At(1, 3));
        Assert.Equal(0, pic.At(0, 0));
        Assert.Null(FormulaImages.Draw((_, _) => throw new InvalidOperationException("module failure"), "x", (4, 6)));
    }

    [Fact]
    public void Nothing_is_drawn_or_asked_for_without_a_terminal_that_shows_pictures()
    {
        FormulaImages.SetTerminal(false, (10, 20));
        Assert.False(FormulaImages.Active);
        Assert.Null(FormulaImages.Get("x^2"));
        FormulaImages.SetTerminal(true, null);                      // Sixel, but no cell size
        Assert.False(FormulaImages.Active);
        FormulaImages.SetTerminal(true, (10, 20));
        FormulaImages.Wanted = false;
        try { Assert.False(FormulaImages.Active); }
        finally { FormulaImages.Wanted = true; }
    }
}

public class MathImagesModuleTests
{
    [Theory]
    [InlineData(@"\int_1^3 \left(3x^{5/2} + x^2\right)\,dx = \left[\frac{6}{7}x^{7/2} + \frac{x^3}{3}\right]_1^3")]
    [InlineData(@"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}")]
    [InlineData(@"\begin{align*} y &= 2x + 3 \\ y - 3 &= 2x \end{align*}")]
    [InlineData(@"f(x) = \begin{cases} x^2 & x \ge 0 \\ -x & x < 0 \end{cases}")]
    [InlineData(@"\text{Площадь} = \tfrac12 ab \sin\theta=\boldsymbol{S} \tag{1}")]
    public void The_module_typesets_what_notes_use(string tex)
    {
        var (mask, w, h, error) = BlueEdit.MathImages.Renderer.Render(tex, 15);
        Assert.Null(error);
        Assert.NotNull(mask);
        Assert.Equal(w * h, mask.Length);
        Assert.InRange(h, 10, 120);
        Assert.InRange(w, 20, 1000);
        // cut to what is drawn: something on every edge
        Assert.Contains(Enumerable.Range(0, w), x => mask[x] != 0);
        Assert.Contains(Enumerable.Range(0, w), x => mask[(h - 1) * w + x] != 0);
        Assert.Contains(Enumerable.Range(0, h), y => mask[y * w] != 0);
        Assert.Contains(Enumerable.Range(0, h), y => mask[y * w + w - 1] != 0);
    }

    [Fact]
    public void A_formula_it_cannot_read_comes_back_with_the_reason()
    {
        var (mask, _, _, error) = BlueEdit.MathImages.Renderer.Render(@"\frac{1}{", 15);
        Assert.Null(mask);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData(@"\begin{equation*}a=b\end{equation*}", " a=b ")]
    [InlineData(@"\begin{gathered}a\\b\end{gathered}", @"\begin{gather}a\\b\end{gather}")]
    [InlineData(@"\sin\theta=1", @"\sin\theta =1")]
    [InlineData(@"f^\prime + \dfrac{1}{2}", @"f^' + \frac{1}{2}")]
    [InlineData(@"\bigl( x \bigr)", @" ( x  )")]
    public void Notes_LaTeX_is_rewritten_into_what_CSharpMath_reads(string tex, string expected) =>
        Assert.Equal(expected, BlueEdit.MathImages.Renderer.Normalize(tex));
}
