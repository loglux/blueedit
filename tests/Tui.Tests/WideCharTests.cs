using System.Text;
using System.Text.RegularExpressions;
using Tui;
using Xunit;

/// <summary>Wide characters: the screen's output read back by a terminal that gives them two columns.</summary>
public class WideCharTests
{
    static readonly Style Plain = Style.Default;

    /// <summary>The little a terminal does that matters here: cursor moves, clearing, and printing where a wide
    /// character takes two columns and printing over either half of one blanks the other half (as xterm does).</summary>
    sealed class Vt(int w, int h)
    {
        readonly string[,] _cells = Blank(w, h);
        int _x, _y;

        static string[,] Blank(int w, int h)
        {
            var c = new string[h, w];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) c[y, x] = " ";
            return c;
        }

        public void Feed(string output)
        {
            for (int i = 0; i < output.Length; i++)
            {
                if (output[i] == '\u001b')
                {
                    var m = Regex.Match(output[(i + 1)..], @"^\[[0-9;?]*[A-Za-z]");
                    var seq = m.Value;
                    if (seq.EndsWith('H'))
                    {
                        var p = seq[1..^1].Split(';');
                        _y = int.Parse(p[0]) - 1; _x = int.Parse(p[1]) - 1;
                    }
                    else if (seq == "[2J") for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) _cells[y, x] = " ";
                    i += seq.Length;
                    continue;
                }
                string ch = char.IsHighSurrogate(output[i]) ? output.Substring(i++, 2) : output[i].ToString();
                int width = ch.Length == 2 || CharWidth.IsWide(ch[0]) ? 2 : 1;
                Put(_x, ch);
                if (width == 2) Put(_x + 1, "");                 // the right half
                _x += width;
            }
        }

        void Put(int x, string ch)
        {
            if (x >= w) return;
            if (_cells[_y, x] == "" && x > 0) _cells[_y, x - 1] = " ";                     // over a right half
            if (x + 1 < w && _cells[_y, x + 1] == "" && ch != "") _cells[_y, x + 1] = " ";  // over a left half
            _cells[_y, x] = ch;
        }

        public string Row(int y)
        {
            var sb = new StringBuilder();
            for (int x = 0; x < w; x++) sb.Append(_cells[y, x]);
            return sb.ToString();
        }
    }

    static string Shown(Screen s, int y)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < s.Width; x++) if (s.CharAt(x, y) != CharWidth.Continuation) sb.Append(s.CharAt(x, y));
        return sb.ToString();
    }

    [Fact]
    public void Black_circle_and_cjk_are_wide_selectors_take_no_column()
    {
        Assert.True(CharWidth.IsWide('⚫'));
        Assert.True(CharWidth.IsWide('中'));
        Assert.True(CharWidth.IsWide('가'));
        Assert.False(CharWidth.IsWide('a'));
        Assert.False(CharWidth.IsWide('ж'));
        Assert.False(CharWidth.IsWide('⚠'));             // text presentation by default
        Assert.False(CharWidth.IsWide('─'));
        Assert.True(CharWidth.IsZeroWidth('️'));
        Assert.True(CharWidth.AllNarrow("plain text, ж, ─, 🔴"));      // a surrogate pair is two cells already
        Assert.False(CharWidth.AllNarrow("### ⚫ Unknown"));
    }

    [Fact]
    public void Text_gives_a_wide_character_two_cells()
    {
        var s = new Screen(20, 1);
        Assert.Equal(6, s.Text(0, 0, "a⚫b⚠️c", Plain));
        Assert.Equal(CharWidth.Continuation, s.CharAt(2, 0));
        Assert.Equal('b', s.CharAt(3, 0));
        Assert.Equal('c', s.CharAt(5, 0));
    }

    [Fact]
    public void Rows_scrolled_past_a_wide_character_leave_nothing_behind()
    {
        // what left a column of "d"s on screen: "### ⚫ Unknown — Not Yet Investigated" drawn on each row in turn
        var s = new Screen(40, 6);
        var vt = new Vt(40, 6);
        string[] lines = ["### ⚫ Unknown — Not Yet Investigated", "[What's still pending]", "", "## Network Map", "[Paste]", "", "## Recommendations", "1. [Immediate]"];
        for (int top = 0; top < 3; top++)
        {
            s.Clear(Plain);
            for (int y = 0; y < 6; y++) s.Text(0, y, lines[top + y], Plain);
            vt.Feed(s.Flush());
            for (int y = 0; y < 6; y++) Assert.Equal(Shown(s, y).TrimEnd(), vt.Row(y).TrimEnd());
        }
        for (int top = 2; top >= 0; top--)
        {
            s.Clear(Plain);
            for (int y = 0; y < 6; y++) s.Text(0, y, lines[top + y], Plain);
            vt.Feed(s.Flush());
            for (int y = 0; y < 6; y++) Assert.Equal(Shown(s, y).TrimEnd(), vt.Row(y).TrimEnd());
        }
    }

    [Fact]
    public void Drawing_over_half_of_a_wide_character_blanks_the_other_half()
    {
        var s = new Screen(10, 1);
        var vt = new Vt(10, 1);
        s.Text(0, 0, "a中b", Plain);
        vt.Feed(s.Flush());
        Assert.Equal("a中b", vt.Row(0).TrimEnd());
        s.Set(2, 0, 'x', Plain);                                   // over the right half: a popup's edge
        vt.Feed(s.Flush());
        Assert.Equal("a x" + "b", vt.Row(0).TrimEnd());
        s.Text(1, 0, "中", Plain);
        vt.Feed(s.Flush());
        Assert.Equal("a中b", vt.Row(0).TrimEnd());
        s.Set(1, 0, 'y', Plain);                                   // over the left half
        vt.Feed(s.Flush());
        Assert.Equal("ay b", vt.Row(0).TrimEnd());
    }

    [Fact]
    public void A_combining_accent_goes_into_the_cell_of_its_letter()
    {
        var s = new Screen(10, 1);
        Assert.Equal(5, s.Text(0, 0, "café!", Plain));             // "café!" with the accent written apart
        Assert.Equal('e', s.CharAt(3, 0));
        Assert.Equal("́", s.MarksAt(3, 0));
        Assert.Equal('!', s.CharAt(4, 0));
        Assert.Contains("café!", s.Flush());
        Assert.True(CharWidth.IsMark('́'));
        Assert.True(CharWidth.IsMark('ָ'));                         // a Hebrew vowel point
        Assert.False(CharWidth.IsMark('️'));                        // a selector is left out, not drawn over
        Assert.False(CharWidth.AllNarrow("café"));
    }

    [Fact]
    public void Changing_only_the_accent_redraws_the_cell()
    {
        var s = new Screen(10, 1);
        s.Text(0, 0, "é", Plain);
        s.Flush();
        s.Text(0, 0, "è", Plain);
        Assert.Contains("è", s.Flush());
    }

    [Fact]
    public void A_wide_character_in_the_last_column_is_a_blank()
    {
        var s = new Screen(3, 1);
        var vt = new Vt(3, 1);
        s.Text(0, 0, "ab中", Plain);
        vt.Feed(s.Flush());
        Assert.Equal("ab ", vt.Row(0));
    }
}
