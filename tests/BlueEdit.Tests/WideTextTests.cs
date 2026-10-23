using System.Text;
using Tui;
using Xunit;

/// <summary>Wide characters (CJK, "⚫") take two columns on screen, variation selectors none: the editor's caret,
/// mouse and soft wrap count columns as the terminal does.</summary>
public sealed class WideTextTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "blueedit-tests-" + Guid.NewGuid().ToString("N"));

    public WideTextTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string name, string text, int w = 40, int h = 6)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text, new UTF8Encoding(false));
        var e = new EditorView();
        e.Load(p);
        e.SetArea(new Rect(0, 0, w, h));
        return e;
    }

    static Screen Drawn(EditorView e)
    {
        var s = new Screen(e.Area.W, e.Area.H);
        e.Draw(s);
        return s;
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.md")]
    public void A_wide_character_takes_two_cells_and_the_caret_steps_over_it(string name)
    {
        var e = Open(name, "a⚫b\n");
        var s = Drawn(e);
        Assert.Equal('⚫', s.CharAt(1, 0));
        Assert.Equal(CharWidth.Continuation, s.CharAt(2, 0));
        Assert.Equal('b', s.CharAt(3, 0));
        e.Col = 2; e.HandleKey(new KeyInput(Key.End, '\0', Mods.None)); e.HandleKey(new KeyInput(Key.Left, '\0', Mods.None));
        Assert.Equal(2, e.Col);
        Assert.Equal((3, 0), e.CursorPos);                           // before "b", right of both cells of the circle
    }

    [Fact]
    public void A_concealed_heading_with_a_wide_character_keeps_its_two_cells()
    {
        var e = Open("a.md", "x\n### ⚫ Unknown\n");
        var s = Drawn(e);
        int at = Enumerable.Range(0, 40).First(x => s.CharAt(x, 1) == '⚫');
        Assert.Equal(CharWidth.Continuation, s.CharAt(at + 1, 1));
        Assert.Equal(' ', s.CharAt(at + 2, 1));
        Assert.Equal('U', s.CharAt(at + 3, 1));
    }

    [Fact]
    public void A_click_on_either_half_of_a_wide_character_lands_on_it()
    {
        var e = Open("a.txt", "中文x\n");
        e.HandleMouse(new MouseInput(MouseKind.Down, 0, 3, 0));
        e.HandleMouse(new MouseInput(MouseKind.Up, 0, 3, 0));
        Assert.Equal(1, e.Col);                                     // the right half of 文
        e.HandleMouse(new MouseInput(MouseKind.Down, 0, 4, 1));     // another place first: no double click
        e.HandleMouse(new MouseInput(MouseKind.Up, 0, 4, 1));
        e.HandleMouse(new MouseInput(MouseKind.Down, 0, 4, 0));
        Assert.Equal(2, e.Col);                                     // x
    }

    [Fact]
    public void A_variation_selector_takes_no_cell()
    {
        var e = Open("a.txt", "⚠️x\n");
        var s = Drawn(e);
        Assert.Equal('⚠', s.CharAt(0, 0));
        Assert.Equal('x', s.CharAt(1, 0));
        e.Col = 2;
        e.HandleKey(new KeyInput(Key.Right, '\0', Mods.None)); e.HandleKey(new KeyInput(Key.Left, '\0', Mods.None));
        Assert.Equal((1, 0), e.CursorPos);
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.md")]
    public void An_accent_written_apart_is_drawn_over_its_letter(string name)
    {
        var e = Open(name, "x\ncafé ok\n");
        var s = Drawn(e);
        Assert.Equal('e', s.CharAt(3, 1));
        Assert.Equal("́", s.MarksAt(3, 1));
        Assert.Equal(' ', s.CharAt(4, 1));
        Assert.Equal('o', s.CharAt(5, 1));
    }

    [Fact]
    public void Emoji_terminals_join_differently_are_drawn_so_none_joins_them()
    {
        var e = Open("a.txt", "x\n👍🏽 🇬🇧 👨‍👩‍👧!\n");
        var s = Drawn(e);
        var row = new StringBuilder();
        for (int x = 0; x < 40; x++) if (s.CharAt(x, 1) != CharWidth.Continuation) row.Append(s.CharAt(x, 1));
        Assert.Equal("👍 GB 👨👩👧!", row.ToString().TrimEnd());
    }

    static readonly KeyInput Left =new(Key.Left, '\0', Mods.None), Right = new(Key.Right, '\0', Mods.None);

    [Theory]
    [InlineData("café", 5, 3)]                 // e + accent
    [InlineData("a🔴", 3, 1)]                         // a surrogate pair
    [InlineData("a👍🏽", 5, 1)]                       // an emoji with a skin tone
    [InlineData("a👨‍👩‍👧", 9, 1)]                      // emoji joined by ZWJ
    [InlineData("a🇬🇧", 5, 1)]                        // a flag: two regional indicators
    [InlineData("a⚠️", 3, 1)]                   // a selector
    public void The_caret_steps_over_a_whole_character_and_delete_removes_it(string text, int end, int before)
    {
        var e = Open("a.txt", text + "\n");
        Assert.Equal(end, text.Length);
        e.Col = end;
        e.HandleKey(Left);
        Assert.Equal(before, e.Col);
        e.HandleKey(Right);
        Assert.Equal(end, e.Col);
        e.HandleKey(new KeyInput(Key.Backspace, '\0', Mods.None));
        Assert.Equal(text[..before], e.Lines[0]);
        e.Lines[0] = text; e.Col = before;
        e.HandleKey(new KeyInput(Key.Delete, '\0', Mods.None));
        Assert.Equal(text[..before], e.Lines[0]);
    }

    [Fact]
    public void Soft_wrap_does_not_split_a_wide_character()
    {
        var e = Open("a.md", new string('中', 30) + "\n", w: 20);
        var s = Drawn(e);
        for (int y = 0; y < 4; y++)
        {
            Assert.NotEqual(CharWidth.Continuation, s.CharAt(0, y));
            for (int x = 0; x < 20; x++)
                if (s.CharAt(x, y) == '中') Assert.Equal(CharWidth.Continuation, s.CharAt(x + 1, y));
        }
    }
}
