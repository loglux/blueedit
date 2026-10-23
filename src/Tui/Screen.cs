using System.Text;

namespace Tui;

[Flags]
public enum Attr : byte { None = 0, Bold = 1, Underline = 2, Reverse = 4, Italic = 8, Dim = 16 }

/// <summary>Foreground and background as 0xRRGGBB, or -1 for the terminal's default colour.</summary>
public readonly record struct Style(int Fg, int Bg, Attr Attr = Attr.None)
{
    public static readonly Style Default = new(-1, -1);

    public static Style Rgb(int fg, int bg, Attr attr = Attr.None) => new(fg, bg, attr);}

/// <summary>
/// A grid of cells that is drawn into freely and then flushed: only cells that differ from what the terminal
/// already shows are sent, and styles are only re-sent when they change. One flush is one write.
/// Cells hold one UTF-16 character. A wide character (<see cref="CharWidth"/>) is followed by a
/// <see cref="CharWidth.Continuation"/> cell, which the terminal covers with it; a wide character without one, or a
/// continuation without its wide character (something drawn over half of it), is shown as a blank.
/// </summary>
public sealed class Screen
{
    // Marks: combining characters drawn in the same cell (an accent written apart from its letter).
    // Pic: the cell is part of row PicRow of a picture (column PicCol), sent as Sixel instead of text.
    struct Cell
    {
        public char Ch; public string? Marks; public Style S;
        public CellPicture? Pic; public int PicRow, PicCol;
        public readonly bool Same(Cell o) => Ch == o.Ch && S == o.S && Marks == o.Marks && Pic == o.Pic && PicRow == o.PicRow && PicCol == o.PicCol;
    }

    Cell[] _back = [];
    Cell[] _front = [];            // what the terminal currently shows ('\0' = unknown, forces a repaint)
    Style _sgr = Style.Default;    // style last sent
    bool _sgrKnown;
    int _cursorX = -1, _cursorY = -1;       // where the caller wants the cursor
    int _termX = -1, _termY = -1;           // where the terminal's cursor is now (-1 = unknown)
    bool _cursorVisible = true, _cursorShown = true;

    public int Width { get; private set; }
    public int Height { get; private set; }

    public Screen(int width, int height) => Resize(width, height);

    /// <summary>Changes the size; the next flush clears the terminal and repaints everything.</summary>
    public void Resize(int width, int height)
    {
        Width = Math.Max(1, width); Height = Math.Max(1, height);
        _back = new Cell[Width * Height];
        _front = new Cell[Width * Height];
        Clear(Style.Default);
        _forceClear = true;
        _sgrKnown = false;
    }

    bool _forceClear = true;

    /// <summary>Asks for a full repaint on the next flush (e.g. after the terminal may have lost its contents).</summary>
    public void Invalidate()
    {
        _forceClear = true;
        Array.Clear(_front);
    }

    public void Clear(Style style) => Fill(0, 0, Width, Height, ' ', style);

    public void Fill(int x, int y, int w, int h, char ch, Style style)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(Width, x + w), y1 = Math.Min(Height, y + h);
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
                _back[yy * Width + xx] = new Cell { Ch = ch, S = style };
    }

    public void Set(int x, int y, char ch, Style style)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        _back[y * Width + x] = new Cell { Ch = ch < ' ' ? ' ' : ch, S = style };
    }

    /// <summary>A character with the combining marks drawn over it (null: none).</summary>
    public void Set(int x, int y, char ch, string? marks, Style style)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        _back[y * Width + x] = new Cell { Ch = ch < ' ' ? ' ' : ch, Marks = marks, S = style };
    }

    /// <summary>Writes text starting at (x, y); clipped to the screen. Returns the column after the last character.
    /// A wide character takes two cells, a zero-width one none; a combining mark goes on the character before it.</summary>
    public int Text(int x, int y, string text, Style style)
    {
        int start = x;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (CharWidth.IsSkinTone(text, i)) { i++; continue; }
            if (CharWidth.FlagLetter(text, i) is char letter) { Set(x++, y, letter, style); i++; continue; }
            if (CharWidth.IsMark(c))
            {
                int at = x - 1;
                if (at > start && CharAt(at, y) == CharWidth.Continuation) at--;
                if (at >= start && (uint)at < (uint)Width && (uint)y < (uint)Height) _back[y * Width + at].Marks += c;
                continue;
            }
            if (CharWidth.IsZeroWidth(c)) continue;
            Set(x++, y, c, style);
            if (CharWidth.IsWide(c)) Set(x++, y, CharWidth.Continuation, style);
        }
        return x;
    }

    /// <summary>
    /// Row <paramref name="row"/> of a picture at (x, y): its cells show the picture instead of text, sent as Sixel by
    /// <see cref="Flush"/>; anything drawn over a cell afterwards covers that cell of it. A row that does not fit the
    /// screen whole is not shown (its cells stay blank), nor one on the last row: after a picture the terminal's
    /// cursor goes to the row below it, which would scroll the screen there.
    /// </summary>
    public void Picture(int x, int y, CellPicture pic, int row, Style style)
    {
        if (x < 0 || x + pic.Cols > Width || (uint)y >= (uint)(Height - 1) || (uint)row >= (uint)pic.Rows) return;
        for (int c = 0; c < pic.Cols; c++) _back[y * Width + x + c] = new Cell { Ch = ' ', S = style, Pic = pic, PicRow = row, PicCol = c };
    }

    /// <summary>The picture row a cell shows (<see cref="Picture"/>), or null.</summary>
    public (CellPicture Picture, int Row)? PictureAt(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height && _back[y * Width + x].Pic is { } p ? (p, _back[y * Width + x].PicRow) : null;

    public string? MarksAt(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? _back[y * Width + x].Marks : null;
    public char CharAt(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? _back[y * Width + x].Ch : ' ';
    public Style StyleAt(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? _back[y * Width + x].S : Style.Default;

    public void SetCursor(int x, int y, bool visible)
    {
        _cursorX = x; _cursorY = y; _cursorVisible = visible;
    }

    /// <summary>Builds the terminal output that turns what the terminal shows into the current back buffer. Empty when nothing changed.</summary>
    public string Flush()
    {
        var sb = new StringBuilder(256);
        SendTitle(sb);
        if (_forceClear)
        {
            sb.Append("\x1b[0m\x1b[2J");
            _sgr = Style.Default; _sgrKnown = true;
            Array.Clear(_front);
            _forceClear = false;
            _termX = _termY = -1;
        }

        else if (UseScrollRegions && !AnyPicture()) TryScroll(sb);
        SendPictures(sb);

        int curX = _termX, curY = _termY;                          // where the terminal's cursor is after what we have written
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            int x = 0;
            while (x < Width)
            {
                if (_back[row + x].Same(_front[row + x]) && _front[row + x].Ch != '\0') { x++; continue; }
                // a change in the right half of a wide character or of a surrogate pair: the whole of it is rewritten
                if (x > 0 && (CharWidth.IsWide(_front[row + x - 1].Ch) || char.IsLowSurrogate(_back[row + x].Ch) || char.IsLowSurrogate(_front[row + x].Ch))) x--;
                if (curX != x || curY != y) { sb.Append("\x1b[").Append(y + 1).Append(';').Append(x + 1).Append('H'); }
                bool first = true;
                while (x < Width)
                {
                    var cell = _back[row + x];
                    if (!first && cell.Same(_front[row + x]) && _front[row + x].Ch != '\0') break;
                    first = false;
                    AppendStyle(sb, cell.S);
                    if (CharWidth.IsWide(cell.Ch) && x + 1 < Width && _back[row + x + 1].Ch == CharWidth.Continuation)
                    {
                        sb.Append(cell.Ch).Append(cell.Marks);              // the terminal moves two columns
                        _front[row + x] = cell;
                        _front[row + x + 1] = _back[row + x + 1];
                        x += 2;
                        continue;
                    }
                    if (cell.Ch == CharWidth.Continuation || CharWidth.IsWide(cell.Ch)) sb.Append(' ');   // half a wide character
                    else sb.Append(cell.Ch).Append(cell.Marks);
                    _front[row + x] = cell;
                    x++;
                }
                curX = x; curY = y;
                if (curX >= Width) curX = -1;                      // writing the last column leaves the cursor in an undefined spot
            }
        }

        _termX = curX; _termY = curY;

        // cursor position and visibility
        if (_cursorVisible && (uint)_cursorX < (uint)Width && (uint)_cursorY < (uint)Height)
        {
            if (_termX != _cursorX || _termY != _cursorY)
            {
                sb.Append("\x1b[").Append(_cursorY + 1).Append(';').Append(_cursorX + 1).Append('H');
                _termX = _cursorX; _termY = _cursorY;
            }
            if (!_cursorShown) { sb.Append("\x1b[?25h"); _cursorShown = true; }
        }
        else if (_cursorShown) { sb.Append("\x1b[?25l"); _cursorShown = false; }

        return sb.ToString();
    }

    bool AnyPicture()
    {
        for (int i = 0; i < _back.Length; i++) if (_back[i].Pic != null || _front[i].Pic != null) return true;
        return false;
    }

    // Is (x, y) the first cell of a whole row `row` of `pic`?
    bool PictureRowAt(int x, int y, CellPicture pic, int row)
    {
        if (y >= Height || x + pic.Cols > Width) return false;
        for (int c = 0; c < pic.Cols; c++)
        {
            var cell = _back[y * Width + x + c];
            if (cell.Pic != pic || cell.PicRow != row || cell.PicCol != c) return false;
        }
        return true;
    }

    bool RowChanged(int x, int y, int cols)
    {
        for (int c = 0; c < cols; c++)
        {
            var f = _front[y * Width + x + c];
            if (f.Ch == '\0' || !_back[y * Width + x + c].Same(f)) return true;
        }
        return false;
    }

    /// <summary>
    /// Pictures go before the text: each run of rows of one picture that changed (moved, scrolled in, uncovered) is
    /// sent as one Sixel image at its place, and the text pass then skips its cells. A cell written over a picture
    /// later erases it there (Windows Terminal keeps images per cell), which is how a moved picture leaves its old
    /// place and how a menu covers one.
    /// </summary>
    void SendPictures(StringBuilder sb)
    {
        bool sent = false;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var cell = _back[y * Width + x];
                if (cell.Pic is not { } pic || cell.PicCol != 0 || !PictureRowAt(x, y, pic, cell.PicRow) || !RowChanged(x, y, pic.Cols)) continue;
                // the first row of a run (the one above, if it continues it, sent it already)
                if (y > 0 && PictureRowAt(x, y - 1, pic, cell.PicRow - 1) && RowChanged(x, y - 1, pic.Cols)) continue;
                int n = 1;
                while (PictureRowAt(x, y + n, pic, cell.PicRow + n) && RowChanged(x, y + n, pic.Cols)) n++;
                // the text under it erased first (ECH): the picture hides it, but a glyph that reaches past its cell,
                // like the stroke of an integral, would still show a pixel or two outside the picture
                AppendStyle(sb, cell.S);
                for (int r = 0; r < n; r++)
                    sb.Append("\u001b[").Append(y + r + 1).Append(';').Append(x + 1).Append('H')
                      .Append("\u001b[").Append(pic.Cols).Append('X');
                sb.Append("\u001b[").Append(y + 1).Append(';').Append(x + 1).Append('H');
                sb.Append(pic.Sixel(cell.PicRow, n, cell.S.Fg, cell.S.Bg));
                for (int r = 0; r < n; r++) Array.Copy(_back, (y + r) * Width + x, _front, (y + r) * Width + x, pic.Cols);
                sent = true;
            }
        if (sent) _termX = _termY = -1;                            // the cursor is somewhere below the last picture
    }

    /// <summary>
    /// Let the terminal move scrolled rows itself (scroll region + IND/RI) instead of resending them. Much less
    /// output, but off by default: Windows Terminal was seen to show it wrongly (lines out of place while scrolling
    /// with the arrow keys), although the same output is right through ConPTY with a plain VT emulator.
    /// </summary>
    public bool UseScrollRegions { get; set; }

    /// <summary>The window's (or tab's) title, sent with the next flush when it changed (OSC 0); null leaves it alone.
    /// Control characters are dropped: a file name could carry one.</summary>
    public string? Title { get; set; }
    string? _sentTitle;

    void SendTitle(StringBuilder sb)
    {
        if (Title == null || Title == _sentTitle) return;
        _sentTitle = Title;
        sb.Append("\u001b]0;");
        foreach (var c in Title) if (!char.IsControl(c)) sb.Append(c);
        sb.Append('\u0007');
    }

    /// <summary>How far a block of rows may have moved for <see cref="TryScroll"/> to notice (a page is drawn in full anyway).</summary>
    const int MaxScroll = 3;

    bool RowMatches(int backRow, int frontRow)
    {
        int b = backRow * Width, f = frontRow * Width;
        for (int x = 0; x < Width; x++)
        {
            var fc = _front[f + x];
            if (fc.Ch == '\0' || !_back[b + x].Same(fc)) return false;
        }
        return true;
    }

    /// <summary>
    /// When a block of rows moved up or down by a few lines (the editor scrolled), the terminal is told to move
    /// them itself (a scroll region plus SU/SD) instead of receiving every row again; only the rows that scrolled in
    /// are drawn afterwards. Used when it saves at least three rows of output.
    /// </summary>
    void TryScroll(StringBuilder sb)
    {
        int bestK = 0, bestFrom = 0, bestTo = 0, bestGain = 2;
        for (int k = -MaxScroll; k <= MaxScroll; k++)
        {
            if (k == 0) continue;
            int y = Math.Max(0, -k), end = Math.Min(Height, Height - k);     // rows whose source row y+k exists
            while (y < end)
            {
                if (!RowMatches(y, y + k)) { y++; continue; }
                int from = y, gain = 0;
                for (; y < end && RowMatches(y, y + k); y++)
                    if (!RowMatches(y, y)) gain++;                           // rows that would otherwise be repainted
                if (gain > bestGain) (bestK, bestFrom, bestTo, bestGain) = (k, from, y - 1, gain);
            }
        }
        if (bestK == 0) return;

        // k > 0: content moved up (back row y shows old row y+k); k < 0: it moved down.
        // VT100 only, which every terminal knows: a scroll region, then IND (ESC D) at its bottom line moves it up a
        // line, RI (ESC M) at its top line moves it down. (SU/SD, CSI S/T, are newer and not understood everywhere.)
        int top = Math.Min(bestFrom, bestFrom + bestK), bottom = Math.Max(bestTo, bestTo + bestK), n = Math.Abs(bestK);
        sb.Append("\x1b[").Append(top + 1).Append(';').Append(bottom + 1).Append('r');
        sb.Append("\x1b[").Append((bestK > 0 ? bottom : top) + 1).Append(";1H");
        for (int i = 0; i < n; i++) sb.Append(bestK > 0 ? "\u001bD" : "\u001bM");   // not "\x1bD": \x takes the D as a hex digit
        sb.Append("\x1b[r");                                                // full-screen margins again (homes the cursor)
        if (bestK > 0)
        {
            Array.Copy(_front, (top + n) * Width, _front, top * Width, (bottom - top + 1 - n) * Width);
            Array.Clear(_front, (bottom - n + 1) * Width, n * Width);     // scrolled in: unknown until drawn
        }
        else
        {
            Array.Copy(_front, top * Width, _front, (top + n) * Width, (bottom - top + 1 - n) * Width);
            Array.Clear(_front, top * Width, n * Width);
        }
        _termX = _termY = -1;
    }

    void AppendStyle(StringBuilder sb, Style s)
    {
        if (_sgrKnown && s == _sgr) return;
        sb.Append("\x1b[0");
        if ((s.Attr & Attr.Bold) != 0) sb.Append(";1");
        if ((s.Attr & Attr.Dim) != 0) sb.Append(";2");
        if ((s.Attr & Attr.Italic) != 0) sb.Append(";3");
        if ((s.Attr & Attr.Underline) != 0) sb.Append(";4");
        if ((s.Attr & Attr.Reverse) != 0) sb.Append(";7");
        if (s.Fg >= 0) sb.Append(";38;2;").Append((s.Fg >> 16) & 255).Append(';').Append((s.Fg >> 8) & 255).Append(';').Append(s.Fg & 255);
        if (s.Bg >= 0) sb.Append(";48;2;").Append((s.Bg >> 16) & 255).Append(';').Append((s.Bg >> 8) & 255).Append(';').Append(s.Bg & 255);
        sb.Append('m');
        _sgr = s; _sgrKnown = true;
    }
}
