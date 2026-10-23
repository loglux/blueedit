using Tui;
using Xunit;

public class ScreenTests
{
    static readonly Style Plain = Style.Default;
    static readonly Style Blue = Style.Rgb(0xAAAAAA, 0x0000AA);

    [Fact]
    public void First_flush_clears_and_paints_then_second_flush_is_empty()
    {
        var s = new Screen(10, 3);
        s.Text(0, 0, "hello", Plain);
        var first = s.Flush();
        Assert.StartsWith("\x1b[0m\x1b[2J", first);
        Assert.Contains("hello", first);
        Assert.Equal("", s.Flush());
    }

    [Fact]
    public void The_title_is_sent_once_when_it_changes_without_control_characters()
    {
        var s = new Screen(10, 3);
        s.Flush();
        Assert.Equal("", s.Flush());
        s.Title = "a\u001b]b.md - BlueEdit";
        Assert.Equal("\u001b]0;a]b.md - BlueEdit\u0007", s.Flush());
        Assert.Equal("", s.Flush());
        s.Title = null;                                       // null: left as it is
        Assert.Equal("", s.Flush());
    }

    [Fact]
    public void Only_changed_cells_are_sent()
    {
        var s = new Screen(20, 3);
        s.Text(0, 0, "hello world", Plain);
        s.Flush();
        s.Set(6, 0, 'W', Plain);
        var frame = s.Flush();
        Assert.Equal("\x1b[1;7HW", frame);
    }

    [Fact]
    public void Adjacent_changes_share_one_cursor_move()
    {
        var s = new Screen(20, 3);
        s.Flush();
        s.Text(2, 1, "abc", Plain);
        var frame = s.Flush();
        Assert.Equal("\x1b[2;3Habc", frame);
    }

    [Fact]
    public void Style_is_only_resent_when_it_changes()
    {
        var s = new Screen(20, 2);
        s.Flush();
        s.Text(0, 0, "aa", Blue);
        s.Text(2, 0, "bb", Plain);
        var frame = s.Flush();
        Assert.Equal(1, CountOf(frame, ";48;2;0;0;170m"));
        Assert.Contains("aa", frame);
        Assert.Contains("bb", frame);
    }

    [Fact]
    public void Truecolor_and_attributes_are_encoded()
    {
        var s = new Screen(5, 1);
        s.Flush();
        s.Set(0, 0, 'x', Style.Rgb(255, 0x112233, Attr.Bold | Attr.Underline));
        Assert.Equal("\x1b[1;1H\x1b[0;1;4;38;2;0;0;255;48;2;17;34;51mx", s.Flush());
    }

    [Fact]
    public void Drawing_outside_the_screen_is_ignored()
    {
        var s = new Screen(5, 2);
        s.Flush();
        s.Set(-1, 0, 'x', Plain); s.Set(5, 0, 'x', Plain); s.Set(0, 2, 'x', Plain);
        s.Text(3, 1, "abcdef", Plain);          // clipped at the right edge
        var frame = s.Flush();
        Assert.Contains("ab", frame);
        Assert.DoesNotContain("c", frame);
        Assert.Equal('\0', '\0');
    }

    [Fact]
    public void Control_characters_become_blanks()
    {
        var s = new Screen(5, 1);
        s.Set(0, 0, '\t', Plain);
        Assert.Equal(' ', s.CharAt(0, 0));
    }

    [Fact]
    public void Resize_repaints_everything()
    {
        var s = new Screen(10, 2);
        s.Text(0, 0, "keep", Plain);
        s.Flush();
        s.Resize(12, 3);
        s.Text(0, 0, "keep", Plain);
        var frame = s.Flush();
        Assert.StartsWith("\x1b[0m\x1b[2J", frame);
        Assert.Contains("keep", frame);
    }

    [Fact]
    public void Invalidate_forces_a_full_repaint_of_unchanged_content()
    {
        var s = new Screen(6, 1);
        s.Text(0, 0, "same", Plain);
        s.Flush();
        s.Invalidate();
        Assert.Contains("same", s.Flush());
    }

    [Fact]
    public void Cursor_position_and_visibility_are_tracked()
    {
        var s = new Screen(10, 3);
        s.SetCursor(4, 1, true);
        var first = s.Flush();
        Assert.EndsWith("\x1b[2;5H", first);
        Assert.Equal("", s.Flush());                         // nothing moved: nothing sent
        s.SetCursor(6, 2, true);
        Assert.Equal("\x1b[3;7H", s.Flush());
        s.SetCursor(0, 0, false);
        Assert.Equal("\x1b[?25l", s.Flush());
        s.SetCursor(1, 0, true);
        Assert.Equal("\x1b[1;2H\x1b[?25h", s.Flush());
    }

    // ---------- scrolling ----------
    static void Lines(Screen s, int first, int top = 0, int bottom = -1)
    {
        if (bottom < 0) bottom = s.Height - 1;
        for (int y = top; y <= bottom; y++) { s.Fill(0, y, s.Width, 1, ' ', Blue); s.Text(0, y, $"line {first + y - top} of the text", Blue); }
    }

    [Fact]
    public void Scrolling_by_a_line_moves_rows_in_the_terminal_instead_of_resending_them()
    {
        var s = new Screen(60, 30) { UseScrollRegions = true };
        var term = new MiniTerminal(60, 30);
        Lines(s, 0, 1, 27); s.Text(0, 0, "menu", Plain); s.Text(0, 29, "status", Plain);
        term.Apply(s.Flush());
        Lines(s, 1, 1, 27);                                    // the text area scrolled down by one line
        var frame = s.Flush();
        term.Apply(frame);
        Assert.Contains("\u001b[2;28r\u001b[28;1H\u001bD\u001b[r", frame, StringComparison.Ordinal);
        Assert.DoesNotContain('ƽ', frame);                // what "\x1bD" in C# really is
        Assert.True(frame.Length < 200, $"{frame.Length} chars: only the new bottom line should be drawn");
        Assert.Equal(Rows(s), term.Rows());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(-2)]
    [InlineData(7)]                                           // too far: plain repaint, still correct
    public void Terminal_shows_exactly_the_buffer_after_any_scroll(int by)
    {
        var s = new Screen(40, 20) { UseScrollRegions = true };
        var term = new MiniTerminal(40, 20);
        s.Text(0, 0, "top bar", Plain);
        Lines(s, 100, 2, 17);
        term.Apply(s.Flush());
        for (int step = 0; step < 6; step++)
        {
            Lines(s, 100 + by * (step + 1), 2, 17);
            if (step == 3) s.Text(5, 9, "EDIT", Plain);         // a change inside the scrolled block as well
            term.Apply(s.Flush());
            Assert.Equal(Rows(s), term.Rows());
        }
    }

    [Fact]
    public void By_default_scrolled_rows_are_resent_not_moved_by_the_terminal()
    {
        var s = new Screen(40, 10);
        var term = new MiniTerminal(40, 10);
        Lines(s, 0);
        term.Apply(s.Flush());
        Lines(s, 1);
        var frame = s.Flush();
        term.Apply(frame);
        Assert.DoesNotContain("r\u001b[", frame, StringComparison.Ordinal);       // no scroll region
        Assert.Equal(Rows(s), term.Rows());
    }

    [Fact]
    public void Typing_does_not_trigger_a_scroll()
    {
        var s = new Screen(40, 10) { UseScrollRegions = true };
        Lines(s, 0);
        s.Flush();
        s.Set(3, 4, 'X', Blue);
        Assert.DoesNotContain("r\x1b[", s.Flush());
    }

    static string[] Rows(Screen s) =>
        Enumerable.Range(0, s.Height).Select(y => new string(Enumerable.Range(0, s.Width).Select(x => s.CharAt(x, y)).ToArray())).ToArray();

    /// <summary>Just enough of a VT terminal to replay what Screen sends: text, CUP, ED 2, DECSTBM, IND, RI; styles ignored.
    /// IND/RI only scroll when the cursor is on the region's bottom/top line, as in a real terminal.</summary>
    sealed class MiniTerminal(int w, int h)
    {
        readonly char[,] _c = Blank(w, h);
        int _x, _y, _top, _bottom = h - 1;

        static char[,] Blank(int w, int h) { var c = new char[h, w]; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) c[y, x] = ' '; return c; }

        public string[] Rows() => Enumerable.Range(0, h).Select(y => new string(Enumerable.Range(0, w).Select(x => _c[y, x]).ToArray())).ToArray();

        void Scroll(int n)            // n > 0: up
        {
            for (int i = 0; i < Math.Abs(n); i++)
            {
                if (n > 0) for (int y = _top; y < _bottom; y++) for (int x = 0; x < w; x++) _c[y, x] = _c[y + 1, x];
                else for (int y = _bottom; y > _top; y--) for (int x = 0; x < w; x++) _c[y, x] = _c[y - 1, x];
                for (int x = 0; x < w; x++) _c[n > 0 ? _bottom : _top, x] = ' ';
            }
        }

        public void Apply(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\x1b') { if (_x < w) _c[_y, _x] = s[i]; _x = Math.Min(_x + 1, w); continue; }
                if (s[i + 1] == 'D') { if (_y == _bottom) Scroll(1); else _y = Math.Min(_y + 1, h - 1); i++; continue; }
                if (s[i + 1] == 'M') { if (_y == _top) Scroll(-1); else _y = Math.Max(_y - 1, 0); i++; continue; }
                int j = i + 2;                                   // after ESC [
                while (j < s.Length && !char.IsLetter(s[j])) j++;
                var args = s[(i + 2)..j];
                int[] p = args.Split(';').Select(a => int.TryParse(a, out var v) ? v : 0).ToArray();
                switch (s[j])
                {
                    case 'H': _y = p[0] - 1; _x = p[1] - 1; break;
                    case 'J': for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) _c[y, x] = ' '; break;
                    case 'r':
                        (_top, _bottom) = args.Length == 0 ? (0, h - 1) : (p[0] - 1, p[1] - 1);
                        _x = _y = 0;
                        break;
                    case 'S': Scroll(Math.Max(1, p[0])); break;
                    case 'T': Scroll(-Math.Max(1, p[0])); break;
                }
                i = j;
            }
        }
    }

    static int CountOf(string haystack, string needle)
    {
        int count = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }
}
