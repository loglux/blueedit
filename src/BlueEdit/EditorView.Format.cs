using System.Text.RegularExpressions;

// Markdown-oriented editing commands and document outline.
partial class EditorView
{
    // ---------- inline wrappers: **bold**, *italic*, `code`, ~~strike~~, $math$ ----------
    public void ToggleWrap(string pre, string post)
    {
        if (ReadOnlyView) return;
        Snapshot("format");
        if (ColumnMode) ColumnMode = false;

        // Work out the span to wrap: selection, else the word under the cursor, else an empty spot at the cursor.
        int r1, c1, r2, c2;
        if (HasSelection)
        {
            var a = _anchor!.Value;
            var (s, e) = Order((a.Row, a.Col), (Row, Col));
            (r1, c1, r2, c2) = (s.Item1, s.Item2, e.Item1, e.Item2);
        }
        else if (WordAtCursor() is { } w) { r1 = r2 = Row; c1 = w.Start; c2 = w.Start + w.Length; }
        else { r1 = r2 = Row; c1 = c2 = Col; }

        string before = Lines[r1][..c1], after = Lines[r2][c2..];
        bool outside = before.EndsWith(pre, StringComparison.Ordinal) && after.StartsWith(post, StringComparison.Ordinal) && (c1 != c2 || r1 != r2);

        if (outside)
        {
            Lines[r2] = Lines[r2].Remove(c2, post.Length);
            Lines[r1] = Lines[r1].Remove(c1 - pre.Length, pre.Length);
            c1 -= pre.Length; if (r1 == r2) c2 -= pre.Length;
        }
        else
        {
            Lines[r2] = Lines[r2].Insert(c2, post);
            Lines[r1] = Lines[r1].Insert(c1, pre);
            if (r1 == r2) c2 += pre.Length;
            c1 += pre.Length;
        }

        if (r1 == r2 && c1 == c2) { Row = r1; Col = c1; _anchor = null; }     // empty: caret between the markers
        else { _anchor = (r1, c1); Row = r2; Col = c2; }
        Touch(true);
    }

    public void Bold() => ToggleWrap("**", "**");
    public void Italic() => ToggleWrap("*", "*");
    public void InlineCode() => ToggleWrap("`", "`");
    public void Strike() => ToggleWrap("~~", "~~");
    public void InlineMath() => ToggleWrap("$", "$");

    public void InsertLink()
    {
        if (ReadOnlyView) return;
        Snapshot("format");
        if (HasSelection && !ColumnMode)
        {
            var a = _anchor!.Value;
            var (s, e) = Order((a.Row, a.Col), (Row, Col));
            if (s.Item1 == e.Item1)
            {
                var l = Lines[s.Item1];
                Lines[s.Item1] = l[..s.Item2] + "[" + l[s.Item2..e.Item2] + "]()" + l[e.Item2..];
                Row = s.Item1; Col = e.Item2 + 3; _anchor = null; Touch(true); return;      // caret inside ()
            }
        }
        _anchor = null;
        Lines[Row] = Lines[Row].Insert(Col, "[]()");
        Col += 1; Touch(true);                                                                // caret inside []
    }

    // ---------- line prefixes: headings, lists, quotes, checkboxes ----------
    (int From, int To) SelectedRows()
    {
        if (!HasSelection) return (Row, Row);
        var a = _anchor!.Value;
        return (Math.Min(a.Row, Row), Math.Max(a.Row, Row));
    }

    static readonly Regex ListPrefix = new(@"^(\s*)(?:[-*+]\s\[[ xX]\]\s|[-*+]\s|\d+[.)]\s|>\s?)", RegexOptions.Compiled);

    void ReplaceLine(int row, string text, int caretShift = 0)
    {
        Lines[row] = text;
        if (row == Row) Col = Math.Clamp(Col + caretShift, 0, text.Length);
    }

    public void CycleHeading()
    {
        if (ReadOnlyView) return;
        Snapshot("format");
        var l = Lines[Row];
        int n = 0; while (n < l.Length && l[n] == '#') n++;
        bool isHeading = n > 0 && n <= 6 && n < l.Length && l[n] == ' ';
        string body = isHeading ? l[(n + 1)..] : l;
        int next = isHeading ? (n >= 6 ? 0 : n + 1) : 1;
        string prefix = next == 0 ? "" : new string('#', next) + " ";
        int oldLen = isHeading ? n + 1 : 0;
        ReplaceLine(Row, prefix + body, prefix.Length - oldLen);
        Touch(true);
    }

    /// <summary>Toggles a list-like prefix on the selected rows: removes it if every row has it, otherwise adds it.</summary>
    void TogglePrefix(Func<int, string> prefixFor, Func<string, bool> has)
    {
        if (ReadOnlyView) return;
        Snapshot("format");
        var (from, to) = SelectedRows();
        bool allHave = Enumerable.Range(from, to - from + 1).All(r => has(Lines[r]));
        for (int r = from; r <= to; r++)
        {
            var l = Lines[r];
            var m = ListPrefix.Match(l);
            string indent = m.Success ? m.Groups[1].Value : new string(' ', l.Length - l.TrimStart().Length);
            string body = m.Success ? l[m.Length..] : l.TrimStart();
            string text = allHave ? indent + body : indent + prefixFor(r - from) + body;
            ReplaceLine(r, text, text.Length - l.Length);
        }
        Touch(true);
    }

    public void BulletList() => TogglePrefix(_ => "- ", l => Regex.IsMatch(l, @"^\s*[-*+]\s(?!\[[ xX]\])"));
    public void NumberedList() => TogglePrefix(i => $"{i + 1}. ", l => Regex.IsMatch(l, @"^\s*\d+[.)]\s"));
    public void Quote() => TogglePrefix(_ => "> ", l => Regex.IsMatch(l, @"^\s*>"));

    /// <summary>Adds a task checkbox, or flips an existing one between [ ] and [x].</summary>
    public void ToggleCheckbox()
    {
        if (ReadOnlyView) return;
        var (from, to) = SelectedRows();
        bool anyBox = Enumerable.Range(from, to - from + 1).Any(r => Regex.IsMatch(Lines[r], @"^\s*[-*+]\s\[[ xX]\]"));
        if (!anyBox) { TogglePrefix(_ => "- [ ] ", l => Regex.IsMatch(l, @"^\s*[-*+]\s\[[ xX]\]")); return; }
        Snapshot("format");
        for (int r = from; r <= to; r++)
        {
            Lines[r] = Regex.Replace(Lines[r], @"^(\s*[-*+]\s\[)([ xX])(\])",
                m => m.Groups[1].Value + (m.Groups[2].Value == " " ? "x" : " ") + m.Groups[3].Value, RegexOptions.None);
        }
        Touch(true);
    }

    // ---------- outline ----------
    public readonly record struct Heading(int Line, int Level, string Title);

    static readonly Regex MdHeading = new(@"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$", RegexOptions.Compiled);
    static readonly Regex TexHeading = new(@"\\(part|chapter|section|subsection|subsubsection|paragraph)\*?\{([^}]*)\}", RegexOptions.Compiled);

    public List<Heading> Outline()
    {
        var res = new List<Heading>();
        bool md = _lang?.Name == "Markdown", tex = _lang?.Name == "LaTeX";
        if (!md && !tex) return res;
        bool inFence = false;
        for (int i = 0; i < Lines.Count; i++)
        {
            var l = Lines[i];
            if (md)
            {
                if (l.TrimStart().StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
                if (inFence) continue;
                var m = MdHeading.Match(l);
                if (m.Success) res.Add(new Heading(i, m.Groups[1].Length, m.Groups[2].Value));
            }
            else
            {
                var m = TexHeading.Match(l);
                if (!m.Success) continue;
                int level = m.Groups[1].Value switch { "part" => 1, "chapter" => 1, "section" => 2, "subsection" => 3, "subsubsection" => 4, _ => 5 };
                res.Add(new Heading(i, level, m.Groups[2].Value));
            }
        }
        return res;
    }
}
