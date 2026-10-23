using System.Text.RegularExpressions;
using Tui;
using Xunit;

public class PictureTests
{
    static readonly Style Math = Style.Rgb(0xFFFFFF, 0x000080);

    // a picture 2 cells wide and 3 tall of 4 × 5 pixel cells: a diagonal of full coverage, a half-covered pixel
    static CellPicture Diagonal()
    {
        int w = 8, h = 15;
        var mask = new byte[w * h];
        for (int y = 0; y < h; y++) mask[y * w + y * w / h] = 255;
        mask[2] = 128;
        return new CellPicture(mask, 2, 3, 4, 5);
    }

    // Decodes Sixel output into palette indexes per pixel (-1: not painted) and the colours of the palette.
    static (int[,] Pixels, Dictionary<int, (int R, int G, int B)> Palette) Decode(string sixel)
    {
        var m = Regex.Match(sixel, "^\u001bP0;1;0q\"1;1;(\\d+);(\\d+)(.*)\u001b\\\\$", RegexOptions.Singleline);
        Assert.True(m.Success, "a DCS q … ST with square pixels and the size");
        int w = int.Parse(m.Groups[1].Value), h = int.Parse(m.Groups[2].Value);
        var px = new int[h, w];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y, x] = -1;
        var palette = new Dictionary<int, (int, int, int)>();
        string body = m.Groups[3].Value;
        int colour = 0, cx = 0, band = 0;
        for (int i = 0; i < body.Length;)
        {
            char c = body[i];
            if (c == '#')
            {
                var d = Regex.Match(body[(i + 1)..], @"^(\d+)(?:;2;(\d+);(\d+);(\d+))?");
                colour = int.Parse(d.Groups[1].Value);
                if (d.Groups[2].Success) palette[colour] = (int.Parse(d.Groups[2].Value), int.Parse(d.Groups[3].Value), int.Parse(d.Groups[4].Value));
                i += 1 + d.Length;
                continue;
            }
            if (c == '$') { cx = 0; i++; continue; }
            if (c == '-') { cx = 0; band++; i++; continue; }
            int n = 1;
            if (c == '!')
            {
                var d = Regex.Match(body[(i + 1)..], @"^\d+");
                n = int.Parse(d.Value);
                i += 1 + d.Length;
                c = body[i];
            }
            Assert.InRange(c, '?', '~');
            for (int k = 0; k < n; k++, cx++)
                for (int b = 0; b < 6; b++)
                    if (((c - 63) >> b & 1) != 0)
                    {
                        Assert.True(band * 6 + b < h && cx < w, "nothing painted outside the picture");
                        px[band * 6 + b, cx] = colour;
                    }
            i++;
        }
        return (px, palette);
    }

    [Fact]
    public void Sixel_paints_every_pixel_in_shades_between_the_background_and_the_foreground()
    {
        var pic = Diagonal();
        var (px, palette) = Decode(pic.Sixel(0, 3, 0xFFFFFF, 0x000000));
        Assert.Equal(15, px.GetLength(0));
        Assert.Equal(8, px.GetLength(1));
        for (int y = 0; y < 15; y++)
            for (int x = 0; x < 8; x++)
            {
                int expected = (pic.At(x, y) * (Sixel.Levels - 1) + 127) / 255;
                Assert.Equal(expected, px[y, x]);
            }
        Assert.Equal((0, 0, 0), palette[0]);                       // background
        Assert.Equal((100, 100, 100), palette[Sixel.Levels - 1]);  // foreground (percent)
        Assert.Equal((53, 53, 53), palette[8]);                    // the half-covered pixel: halfway
    }

    [Fact]
    public void A_run_of_rows_is_cut_out_of_the_picture()
    {
        var pic = Diagonal();
        var (px, _) = Decode(pic.Sixel(1, 2, 0xFFFFFF, 0x000000));
        Assert.Equal(10, px.GetLength(0));
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal((pic.At(x, y + 5) * (Sixel.Levels - 1) + 127) / 255, px[y, x]);
    }

    static int Count(string s, string what) => Regex.Matches(s, Regex.Escape(what)).Count;

    [Fact]
    public void A_picture_is_sent_once_at_its_place_before_the_text_and_not_again_while_it_stays()
    {
        var s = new Screen(10, 6);
        var pic = Diagonal();
        s.Text(0, 0, "abc", Style.Default);
        for (int r = 0; r < 3; r++) s.Picture(3, 1 + r, pic, r, Math);
        var first = s.Flush();
        Assert.Equal(1, Count(first, "\u001bP"));
        Assert.Contains("\u001b[2;4H\u001bP", first);              // rows 0-2 together at row 2, column 4
        for (int r = 0; r < 3; r++)                                 // the text under it erased before it is drawn
            Assert.True(first.IndexOf($"\u001b[{r + 2};4H\u001b[2X", StringComparison.Ordinal) is >= 0 and var at
                        && at < first.IndexOf("\u001bP", StringComparison.Ordinal));
        Assert.True(first.IndexOf("\u001bP", StringComparison.Ordinal) < first.IndexOf("abc", StringComparison.Ordinal));
        for (int r = 0; r < 3; r++) s.Picture(3, 1 + r, pic, r, Math);
        Assert.Equal("", s.Flush());
    }

    [Fact]
    public void A_scrolled_picture_is_sent_at_its_new_place_and_its_old_cells_are_written_over()
    {
        var s = new Screen(10, 6);
        var pic = Diagonal();
        for (int r = 0; r < 3; r++) s.Picture(3, 1 + r, pic, r, Math);
        s.Flush();
        // scrolled up a row: its first row is out of the area, the others one row higher
        s.Fill(0, 0, 10, 6, ' ', Style.Default);
        for (int r = 1; r < 3; r++) s.Picture(3, r - 1, pic, r, Math);
        var frame = s.Flush();
        Assert.Contains("\u001b[1;4H\u001bP", frame);
        Assert.Equal(pic.Sixel(1, 2, 0xFFFFFF, 0x000080), frame[(frame.IndexOf("\u001bP", StringComparison.Ordinal))..][..pic.Sixel(1, 2, 0xFFFFFF, 0x000080).Length]);
        Assert.Contains("\u001b[3;4H", frame);                     // the row it left: blanks over the old picture
    }

    [Fact]
    public void Text_drawn_over_a_picture_covers_it_and_the_picture_comes_back_when_it_goes()
    {
        var s = new Screen(10, 6);
        var pic = Diagonal();
        void Draw(bool menu)
        {
            s.Fill(0, 0, 10, 6, ' ', Style.Default);
            for (int r = 0; r < 3; r++) s.Picture(3, 1 + r, pic, r, Math);
            if (menu) s.Text(2, 2, "menu", Style.Default);
        }
        Draw(false); s.Flush();
        Draw(true);
        var covered = s.Flush();
        Assert.DoesNotContain("\u001bP", covered);                // the rows not under it still show it
        Assert.Contains("menu", covered);
        Draw(false);
        var back = s.Flush();
        Assert.Contains("\u001b[3;4H\u001bP", back);               // the row that was covered, sent again
        Assert.Equal(1, Count(back, "\u001bP"));
    }

    [Fact]
    public void A_picture_row_on_the_last_row_or_past_the_right_edge_is_not_shown()
    {
        var s = new Screen(10, 4);
        var pic = Diagonal();
        s.Picture(3, 3, pic, 0, Math);                              // the last row: the terminal would scroll
        s.Picture(9, 1, pic, 0, Math);                              // one column of two would fit
        Assert.DoesNotContain("\u001bP", s.Flush());
    }

    [Fact]
    public void Answers_to_the_graphics_queries_are_parsed()
    {
        var p = new InputParser();
        var events = new List<InputEvent>();
        p.Feed("\u001b[?61;4;6;7;14;21;22;23;24;28;32;42;52c\u001b[6;20;10t", events);
        Assert.Collection(events,
            e => { var r = Assert.IsType<TerminalReply>(e); Assert.Equal('c', r.Final); Assert.Contains(4, r.Values); },
            e => { var r = Assert.IsType<TerminalReply>(e); Assert.Equal('t', r.Final); Assert.Equal(new[] { 6, 20, 10 }, r.Values); });
    }
}
