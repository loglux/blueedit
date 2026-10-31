partial class EditorView
{
    readonly record struct SelectState((int Row, int Col)? Anchor, int Row, int Col);

    // the states Alt+= came from, for Alt+-; they hold only while the selection is the one Alt+= left
    readonly Stack<SelectState> _selectSteps = new();
    SelectState? _selectLeft;

    SelectState SelectNow => new(_anchor, Row, Col);

    /// <summary>Alt+=: the next larger piece around the selection (SelectionSteps).</summary>
    public void ExpandSelection()
    {
        if (ReadingView) return;
        if (_selectLeft != SelectNow) _selectSteps.Clear();
        (int Row, int Col) here = (Row, Col), start = SelStart;
        var end = _anchor is { } a && HasSelection && start == here ? a : here;
        var headings = IsProseFile ? Outline().Select(h => (h.Line, h.Level)).ToList() : null;
        if (SelectionSteps.Next(Lines, start, end, IsProseFile, headings, IsProseFile ? null : IsCode) is not { } next) return;
        _selectSteps.Push(SelectNow);
        ColumnMode = false;
        _anchor = next.Start; (Row, Col) = next.End;
        _selectLeft = SelectNow;
        Touch();
    }

    /// <summary>Alt+-: back one step of Alt+=.</summary>
    public void ShrinkSelection()
    {
        if (_selectLeft != SelectNow) _selectSteps.Clear();
        if (_selectSteps.Count == 0) return;
        var was = _selectSteps.Pop();
        _anchor = was.Anchor; Row = was.Row; Col = was.Col;
        _selectLeft = SelectNow;
        Touch();
    }
}
