using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Markdown pipe tables, shown aligned in the Live and Reading views:
///     | Name | Qty |          │ Name   │ Qty │
///     |------|----:|    ->    ├────────┼─────┤
///     | tea  |   2 |          │ tea    │   2 │
/// A table is a header row, a separator row (dashes, optional colons for alignment) and the rows after it. Every
/// displayed character maps back to a source column, so the caret, selection and mouse work as on the raw text.
/// </summary>
static class Tables
{
    static readonly Regex SepRx = new(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$", RegexOptions.Compiled);

    public static bool IsRow(string line) => line.Contains('|', StringComparison.Ordinal) && line.Trim().Length > 0;
    public static bool IsSeparator(string line) => line.Contains('|', StringComparison.Ordinal) && SepRx.IsMatch(line);

    /// <summary>Source spans [Start, End) of the cells, between the pipes (a leading and a trailing pipe are optional;
    /// \| and pipes inside `code` do not split).</summary>
    public static List<(int Start, int End)> Cells(string line)
    {
        var cells = new List<(int, int)>();
        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t') i++;
        if (i < line.Length && line[i] == '|') i++;
        int start = i;
        bool code = false;
        for (; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '`') code = !code;
            else if (c == '\\') i++;
            else if (c == '|' && !code) { cells.Add((start, i)); start = i + 1; }
        }
        if (line.AsSpan(start).Trim().Length > 0 || cells.Count == 0) cells.Add((start, line.Length));   // no trailing pipe
        return cells;
    }

    public sealed class Table
    {
        public required int First, Last;          // header line .. last row
        public required int[] Widths;             // display width of every column's content
        public required char[] Align;             // 'l', 'r' or 'c'
    }

    /// <summary>Finds the tables: line index -> its table (or null). <paramref name="plain"/> says which lines may be
    /// part of one (not inside a code block or math).</summary>
    public static Table?[] Find(IReadOnlyList<string> lines, Func<int, bool> plain)
    {
        var of = new Table?[lines.Count];
        for (int i = 0; i + 1 < lines.Count; i++)
        {
            if (!plain(i) || !plain(i + 1) || !IsRow(lines[i]) || IsSeparator(lines[i]) || !IsSeparator(lines[i + 1])) continue;
            int last = i + 1;
            while (last + 1 < lines.Count && plain(last + 1) && IsRow(lines[last + 1])) last++;
            var sep = Cells(lines[i + 1]);
            var align = sep.Select(c =>
            {
                var t = lines[i + 1][c.Start..c.End].Trim();
                return t.StartsWith(':') && t.EndsWith(':') && t.Length > 1 ? 'c' : t.EndsWith(':') ? 'r' : 'l';
            }).ToArray();
            int cols = align.Length;
            var widths = new int[cols];
            for (int r = i; r <= last; r++)
            {
                if (r == i + 1) continue;
                var cells = Cells(lines[r]);
                for (int c = 0; c < Math.Min(cols, cells.Count); c++)
                    widths[c] = Math.Max(widths[c], Shown(lines[r], cells[c]).Text.Length);
            }
            for (int c = 0; c < cols; c++) widths[c] = Math.Max(widths[c], 3);
            var table = new Table { First = i, Last = last, Widths = widths, Align = align };
            for (int r = i; r <= last; r++) of[r] = table;
            i = last;
        }
        return of;
    }

    // A cell's content as shown: trimmed, inline markup hidden; Map is relative to the line.
    static (string Text, int[] Map, Tok?[] Over) Shown(string line, (int Start, int End) cell)
    {
        int s = cell.Start, e = cell.End;
        while (s < e && line[s] is ' ' or '\t') s++;
        while (e > s && line[e - 1] is ' ' or '\t') e--;
        var raw = line[s..e];
        var d = Conceal.Get(raw);
        if (d == null) return (raw, Enumerable.Range(s, raw.Length).ToArray(), new Tok?[raw.Length]);
        return (d.Text, d.Map.Select(m => s + m).ToArray(), d.Over);
    }

    /// <summary>The display form of one line of a table.</summary>
    public static Disp Render(string line, Table t, int lineIndex)
    {
        var text = new StringBuilder();
        var map = new List<int>();
        var over = new List<Tok?>();
        int last = Math.Max(0, line.Length - 1);
        void Put(char ch, int src, Tok? tok) { text.Append(ch); map.Add(Math.Clamp(src, 0, last)); over.Add(tok); }

        int cols = t.Widths.Length;
        if (lineIndex == t.First + 1)                                       // the separator row
        {
            var cells = Cells(line);
            Put('├', 0, Tok.Comment);
            for (int c = 0; c < cols; c++)
            {
                int src = c < cells.Count ? cells[c].Start : last;
                for (int k = 0; k < t.Widths[c] + 2; k++) Put('─', src, Tok.Comment);
                Put(c == cols - 1 ? '┤' : '┼', c < cells.Count ? cells[c].End : last, Tok.Comment);
            }
            return Finish(text, map, over, line);
        }

        var row = Cells(line);
        bool header = lineIndex == t.First;
        int at = row.Count > 0 ? Math.Max(0, row[0].Start - 1) : 0;
        Put('│', at, Tok.Comment);
        for (int c = 0; c < cols; c++)
        {
            (int Start, int End) cell = c < row.Count ? row[c] : (line.Length, line.Length);
            var (content, cmap, cover) = c < row.Count ? Shown(line, cell) : ("", Array.Empty<int>(), Array.Empty<Tok?>());
            int pad = t.Widths[c] - content.Length;
            int left = t.Align[c] == 'r' ? pad : t.Align[c] == 'c' ? pad / 2 : 0;
            int first = cmap.Length > 0 ? cmap[0] : cell.Start, end = cmap.Length > 0 ? cmap[^1] + 1 : cell.Start;
            Put(' ', cell.Start, Tok.Normal);
            for (int k = 0; k < left; k++) Put(' ', first, Tok.Normal);
            for (int k = 0; k < content.Length; k++) Put(content[k], cmap[k], cover[k] ?? (header ? Tok.Heading : null));
            for (int k = 0; k < pad - left; k++) Put(' ', end, Tok.Normal);
            Put(' ', end, Tok.Normal);
            Put('│', cell.End, Tok.Comment);
        }
        return Finish(text, map, over, line);
    }

    static Disp Finish(StringBuilder text, List<int> map, List<Tok?> over, string line)
    {
        // the map must not go backwards (positions are looked up by scanning it)
        for (int i = 1; i < map.Count; i++) if (map[i] < map[i - 1]) map[i] = map[i - 1];
        return new Disp { Text = text.ToString(), Map = map.ToArray(), Over = over.ToArray(), SourceLength = line.Length };
    }
}
