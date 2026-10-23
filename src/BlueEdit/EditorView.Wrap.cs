// Soft wrap: a logical line may occupy several visual rows. All scrolling is done in visual rows.
// With wrap off, visual row == logical line and horizontal scrolling (_left) is used instead.
partial class EditorView
{
    public bool WrapOn { get; private set; }

    int[]? _rowOf;          // visual row of each line's first segment; _rowOf[Lines.Count] = total rows
    int[][]? _segs;         // per line: start column of every segment
    int[] _hangs = Array.Empty<int>();   // per line: columns the continuation rows are indented by (hanging indent)
    int _layoutCap;         // characters per row the layout was built for
    int _goalX = -1;        // remembered x for vertical movement (-1 = take it from the cursor)

    public void ToggleSoftWrap()
    {
        WrapOn = !WrapOn;
        _rowOf = null; _left = 0; _top = 0;
        Touch();
    }

    // One column stays free so the caret can sit after the last character of a row.
    int WrapCap => Math.Max(1, TextArea.W - 1);

    void EnsureLayout()
    {
        if (!WrapOn) return;
        int cap = WrapCap;
        if (_rowOf != null && _layoutCap == cap && _rowOf.Length == Lines.Count + 1) return;

        int n = Lines.Count;
        _states ??= Highlighter.States(Lines, _lang);
        _rowOf = new int[n + 1]; _segs = new int[n][]; _hangs = new int[n];
        int row = 0;
        for (int i = 0; i < n; i++)
        {
            _rowOf[i] = row;
            LayoutLine(i, cap);
            row += _segs[i].Length;
        }
        _rowOf[n] = row; _layoutCap = cap;
    }

    void LayoutLine(int li, int cap)
    {
        if (DispFor(li) is { RowWidth: > 0 } picture)
        {
            // a laid-out formula: one row of its picture per visual row
            _hangs[li] = 0;
            _segs![li] = Enumerable.Range(0, Math.Max(1, picture.Text.Length / picture.RowWidth)).Select(k => k * picture.RowWidth).ToArray();
            return;
        }
        var text = TextForLayout(li);
        int hang = HangFor(text, cap);
        _hangs[li] = hang;
        _segs![li] = Break(text, cap, hang);
    }

    // Continuation rows of a list item or quote start under its text, those of an indented line under the indent:
    //     - a long item that goes on
    //       on the next row
    static readonly System.Text.RegularExpressions.Regex HangRx = new(
        @"^[ \t]*(?:(?:[-*+•☐☑]|\d+[.)])[ \t]+(?:\[[ xX]\][ \t]+)?|(?:[>│][ \t]?)+)?",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    int HangFor(string text, int cap)
    {
        int hang = IsMarkdown ? HangRx.Match(text).Length : text.Length - text.TrimStart(' ', '\t').Length;
        return hang >= text.Length || hang > cap / 2 ? 0 : hang;        // nothing after it, or too deep: no hang
    }

    int HangOf(Seg s) => s.Index > 0 ? _hangs[s.Line] : 0;

    /// <summary>
    /// Re-breaks single lines of a valid layout, e.g. the two lines whose markup is shown or hidden when the caret
    /// moves in Live preview, instead of the whole file. Rows below shift when a line's row count changed.
    /// </summary>
    void RelayoutLines(int a, int b)
    {
        if (!WrapOn || _rowOf == null || _segs == null || _layoutCap != WrapCap || _rowOf.Length != Lines.Count + 1) return;
        foreach (int li in a == b ? new[] { a } : new[] { a, b })
        {
            if ((uint)li >= (uint)Lines.Count) continue;
            int before = _segs[li].Length;
            LayoutLine(li, _layoutCap);
            int d = _segs[li].Length - before;
            if (d != 0) for (int i = li + 1; i <= Lines.Count; i++) _rowOf[i] += d;
        }
    }

    /// <summary>The current layout as text, for tests comparing incremental and full layouts.</summary>
    internal string LayoutSignature()
    {
        EnsureLayout();
        return string.Join(",", _rowOf!) + "|" + string.Join(";", _segs!.Select(s => string.Join(",", s))) + "|" + string.Join(",", _hangs);
    }

    internal void DropLayout() => _rowOf = null;

    // Break at the last space that fits (the space stays at the end of its row); hard-break very long words.
    // Rows after the first have `hang` fewer columns (they are drawn indented).
    internal static int[] Break(string line, int cap, int hang = 0)
    {
        if (line.Length <= cap) return new[] { 0 };
        var starts = new List<int> { 0 };
        int start = 0, room = cap;
        while (line.Length - start > room)
        {
            int limit = start + room;
            int sp = line.LastIndexOf(' ', limit - 1, limit - start);
            int next = sp >= start ? sp + 1 : limit;
            // a cut through a word must not split a wide character from its second cell, or a surrogate pair
            if (sp < start && next - 1 > start && (line[next] == Tui.CharWidth.Continuation || char.IsLowSurrogate(line[next]))) next--;
            starts.Add(next); start = next;
            room = Math.Max(1, cap - hang);
        }
        return starts.ToArray();
    }

    int TotalRows { get { if (!WrapOn) return Lines.Count; EnsureLayout(); return _rowOf![Lines.Count]; } }

    readonly record struct Seg(int Line, int Index, int Start, int Len);

    Seg SegmentAt(int vrow)
    {
        EnsureLayout();
        vrow = Math.Clamp(vrow, 0, Math.Max(0, _rowOf![Lines.Count] - 1));
        int lo = 0, hi = Lines.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_rowOf[mid] <= vrow) lo = mid; else hi = mid - 1;
        }
        int k = vrow - _rowOf[lo];
        var s = _segs![lo];
        int start = s[k], end = k + 1 < s.Length ? s[k + 1] : TextForLayout(lo).Length;
        return new Seg(lo, k, start, end - start);
    }

    // Last column the caret may take on a row: not the first column of the next row.
    int MaxCol(Seg s) => s.Index + 1 < _segs![s.Line].Length ? s.Start + s.Len - 1 : TextForLayout(s.Line).Length;

    (int Row, int X) CursorVisual()
    {
        int dc = DisplayCol(Row, Col);
        if (!WrapOn) return (Row, dc - _left);
        EnsureLayout();
        var s = _segs![Row];
        int k = s.Length - 1;
        while (k > 0 && s[k] > dc) k--;
        return (_rowOf![Row] + k, dc - s[k] + (k > 0 ? _hangs[Row] : 0));
    }

    void MoveVertical(int delta)
    {
        int from = Row;
        if (!WrapOn)
        {
            if (_goalX < 0) _goalX = DisplayCol(Row, Col);
            int target = Math.Clamp(Row + delta, 0, Lines.Count - 1);
            var shown = DispFor(target);                       // looked up while the caret is still on the old line
            int dc = Math.Min(_goalX, TextForLayout(target).Length);
            Row = target;
            Col = target == from ? dc : (shown?.ToSource(dc) ?? dc);
            return;
        }
        var (vr, x) = CursorVisual();
        if (_goalX < 0) _goalX = x;
        var seg = SegmentAt(Math.Clamp(vr + delta, 0, TotalRows - 1));
        var segShown = DispFor(seg.Line);
        int dcol = Math.Min(seg.Start + Math.Max(0, _goalX - HangOf(seg)), MaxCol(seg));
        Row = seg.Line;
        Col = seg.Line == from ? dcol : (segShown?.ToSource(dcol) ?? dcol);
    }

    void LineHome() { if (WrapOn) { var (vr, _) = CursorVisual(); Col = SegToSource(SegmentAt(vr).Start); } else Col = 0; }
    void LineEnd() { if (WrapOn) { var (vr, _) = CursorVisual(); Col = SegToSource(MaxCol(SegmentAt(vr))); } else Col = Lines[Row].Length; }

    int SegToSource(int displayCol) => DispFor(Row)?.ToSource(displayCol) ?? displayCol;
}
