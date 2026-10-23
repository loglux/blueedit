using System.Text;
using Tui;
using Xunit;

/// <summary>The line-number column: what it shows and that the text, caret and mouse move right of it.</summary>
public sealed class LineNumberTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "blueedit-tests-" + Guid.NewGuid().ToString("N"));

    public LineNumberTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string name, string text, int w = 40, int h = 10, bool numbers = true)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text, new UTF8Encoding(false));
        var e = new EditorView();
        e.Load(p);
        e.SetArea(new Rect(0, 0, w, h));
        e.LineNumbersOn = numbers;
        return e;
    }

    static string Row(Screen s, int y) => new(Enumerable.Range(0, s.Width).Select(x => s.CharAt(x, y)).ToArray());

    static Screen Drawn(EditorView e)
    {
        var s = new Screen(e.Area.W, e.Area.H);
        e.Draw(s);
        return s;
    }

    [Fact]
    public void Off_by_default_and_once_on_kept_for_every_file_opened()
    {
        Assert.False(new EditorView().LineNumbersOn);
        Assert.False(new Settings().LineNumbers);
        var e = Open("a.cs", "int x;\n");
        e.Load(Path.Combine(_dir, "b.md"));
        Assert.True(e.LineNumbersOn);
    }

    [Fact]
    public void The_setting_is_read_and_the_two_old_ones_pass_without_a_warning()
    {
        var p = Path.Combine(_dir, "settings.json");
        File.WriteAllText(p, """{ "lineNumbers": true, "lineNumbersCode": true, "lineNumbersProse": false }""");
        var (s, error) = Settings.Load(p);
        Assert.True(s.LineNumbers);
        Assert.Null(error);
    }

    [Fact]
    public void Numbers_are_right_aligned_as_wide_as_the_last_one_and_the_text_follows()
    {
        var e = Open("a.cs", string.Join("\n", Enumerable.Range(1, 12).Select(i => "line" + i)));
        var s = Drawn(e);
        Assert.StartsWith(" 1 line1", Row(s, 0), StringComparison.Ordinal);
        Assert.StartsWith(" 9 line9", Row(s, 8), StringComparison.Ordinal);
        Assert.Equal((3, 0), e.CursorPos);
        e.LineNumbersOn = false;
        Assert.StartsWith("line1", Row(Drawn(e), 0), StringComparison.Ordinal);
        Assert.Equal((0, 0), e.CursorPos);
    }

    [Fact]
    public void A_wrapped_line_is_numbered_on_its_first_row_only()
    {
        var e = Open("a.md", "first\n" + string.Join(" ", Enumerable.Repeat("word", 20)) + "\nlast\n", w: 30);
        var s = Drawn(e);
        Assert.StartsWith("1 first", Row(s, 0), StringComparison.Ordinal);
        Assert.StartsWith("2 word", Row(s, 1), StringComparison.Ordinal);
        Assert.StartsWith("  word", Row(s, 2), StringComparison.Ordinal);
        Assert.Contains(Enumerable.Range(3, 5).Select(y => Row(s, y)), r => r.StartsWith("3 last", StringComparison.Ordinal));
        // the rows are as wide as the text area: no row runs under the numbers or past the edge
        Assert.All(Enumerable.Range(0, 10).Select(y => Row(s, y)), r => Assert.True(r.TrimEnd().Length <= 30));
    }

    [Fact]
    public void A_click_lands_in_the_text_and_one_on_a_number_at_the_start_of_its_line()
    {
        var e = Open("a.cs", "alpha\nbravo\ncharlie\n");
        e.HandleMouse(new MouseInput(MouseKind.Down, 0, 2 + 3, 1));
        e.HandleMouse(new MouseInput(MouseKind.Up, 0, 2 + 3, 1));
        Assert.Equal((1, 3), (e.Row, e.Col));
        e.HandleMouse(new MouseInput(MouseKind.Down, 0, 0, 2));
        Assert.Equal((2, 0), (e.Row, e.Col));
    }

    [Fact]
    public void A_narrow_window_shows_no_numbers()
    {
        var e = Open("a.cs", "alpha\n", w: 15);
        Assert.StartsWith("alpha", Row(Drawn(e), 0), StringComparison.Ordinal);
    }
}
