// Block indent: Tab on a selection of several lines shifts them right, Shift+Tab shifts the selected lines (or the
// caret line) left. The selection stays on the same text so the shift can be repeated.
partial class EditorView
{
    // Rows a block command works on. A selection that ends at the very start of a line does not include that line
    // (selecting lines 3-5 with Shift+Down leaves the caret at the start of line 6).
    (int From, int To) BlockRows()
    {
        var (from, to) = SelectedRows();
        if (HasSelection && to > from)
        {
            var a = _anchor!.Value;
            var end = (a.Row > Row || a.Row == Row && a.Col > Col) ? a : (Row, Col);
            if (end.Col == 0) to--;
        }
        return (from, to);
    }

    bool SelectionSpansLines => HasSelection && _anchor!.Value.Row != Row;

    public void IndentBlock()
    {
        if (ReadOnlyView) return;
        var (from, to) = BlockRows();
        Snapshot("indent"); _lastKind = null;
        var pad = new string(' ', TabSize);
        for (int r = from; r <= to; r++)
        {
            if (Lines[r].Length == 0) continue;                      // empty lines stay empty
            Lines[r] = pad + Lines[r];
            ShiftCols(r, 0, TabSize);
        }
        Touch(true);
    }

    public void UnindentBlock()
    {
        if (ReadOnlyView) return;
        var (from, to) = BlockRows();
        int Lead(string l) => l.Length > 0 && l[0] == '\t' ? 1 : l.TakeWhile(c => c == ' ').Take(TabSize).Count();
        if (Enumerable.Range(from, to - from + 1).All(r => Lead(Lines[r]) == 0)) return;   // nothing to move: no undo step
        Snapshot("indent"); _lastKind = null;
        for (int r = from; r <= to; r++)
        {
            int n = Lead(Lines[r]);
            if (n == 0) continue;
            Lines[r] = Lines[r][n..];
            ShiftCols(r, n, -n);
        }
        Touch(true);
    }

    // Keeps the caret and the selection anchor on the same text after `delta` characters were inserted (or removed)
    // at the start of row `r`; positions inside removed text go to column 0.
    void ShiftCols(int r, int removed, int delta)
    {
        if (Row == r) Col = Math.Max(0, Col < removed ? 0 : Col + delta);
        if (_anchor is { } a && a.Row == r) _anchor = (r, Math.Max(0, a.Col < removed ? 0 : a.Col + delta));
    }
}
