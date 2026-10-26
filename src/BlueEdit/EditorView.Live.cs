// Markdown view modes: Source (as written), Live preview (markup hidden except on the caret line), Reading (all hidden, read-only).
enum MdView { Source, Live, Reading }

partial class EditorView
{
    public MdView View { get; private set; }
    MdView _editView = MdView.Live;       // what Ctrl+E returns to after Reading
    int _liveRow = -1;

    public bool IsMarkdown => _lang?.Name == "Markdown";
    bool Concealing => IsMarkdown && View != MdView.Source;
    public bool ReadingView => Concealing && View == MdView.Reading;
    /// <summary>Opened with -r: nothing in the buffer may be changed.</summary>
    public bool ReadOnlyFile { get; set; }
    public bool ReadOnlyView => ReadOnlyFile || ReadingView;
    public string ViewName => !IsMarkdown ? "" : View switch { MdView.Source => "SRC", MdView.Live => "LIVE", _ => "READ" };

    // Live preview shows the caret's line as written, and the whole display formula the caret is in (its picture would
    // otherwise change under the caret), as Obsidian does
    bool RawLine(int li) => !Concealing || (View == MdView.Live && (li == Row || InCaretFormula(li)));

    (int Row, int Version, (int First, int Last)? Block) _caretFormula = (-1, -1, null);

    bool InCaretFormula(int li)
    {
        if (_caretFormula.Row != Row || _caretFormula.Version != EditVersion) _caretFormula = (Row, EditVersion, FormulaLines(Row));
        return _caretFormula.Block is var (first, last) && li >= first && li <= last;
    }

    /// <summary>The lines of the display formula (a $$ or \[ block) line li belongs to, its delimiter lines included, or
    /// null when it is not in one.</summary>
    internal (int First, int Last)? FormulaLines(int li)
    {
        if ((uint)li >= (uint)Lines.Count || _lang == null) return null;
        var st = _states ??= Highlighter.States(Lines, _lang);
        bool In(int i) => i >= 0 && i < Lines.Count && st[i] > 0 && _lang.Blocks[st[i] - 1].Kind == Tok.Math;
        int at = In(li) ? li : In(li + 1) ? li + 1 : -1;       // inside, or the line that opens it
        if (at < 0) return null;
        int first = at, last = at;
        while (In(first - 1) && st[first - 1] == st[at]) first--;
        while (In(last + 1) && st[last + 1] == st[at]) last++;
        return (Math.Max(0, first - 1), last);
    }

    /// <summary>How a line is shown, or null when exactly as written: concealed, and with wide and zero-width
    /// characters given their columns (<see cref="Conceal.FitWidths"/>).</summary>
    Disp? DispFor(int li)
    {
        var d = ConcealFor(li);
        return d is { RowWidth: > 0 } ? d : Conceal.FitWidths(d, Lines[li]);
    }

    /// <summary>Concealed form of a line, or null when it is shown as written.</summary>
    Disp? ConcealFor(int li)
    {
        if (!Concealing || RawLine(li)) return null;
        _states ??= Highlighter.States(Lines, _lang);
        int st = _states[li];
        var line = Lines[li];
        // display math: the $$ and \[ \] delimiter lines vanish, the lines between them become a picture or Unicode
        var block = st > 0 ? _lang!.Blocks[st - 1] : null;
        bool inMath = block is { Kind: Tok.Math };
        var trimmed = line.Trim();
        if ((trimmed == "$$" && (st == 0 || inMath)) || (trimmed == @"\[" && st == 0) || (inMath && trimmed == block!.End && block.End == @"\]"))
            return Conceal.Empty(line);
        if (inMath) return PictureFor(li) ?? Conceal.MathLine(line);
        if (st != 0) return null;                                // inside a code fence / comment: shown as written
        if (ImageFor(li) is { } image) return image;
        if (PictureFor(li) is { } single) return single;
        if (TableDisp(li) is { } table) return table;
        if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) return null;    // fence marker lines stay visible
        // prose with an inline formula that needs more than one row: laid out over rows with soft wrap, when it fits
        if (WrapOn && Conceal.InlinePicture(line) is { } inline && inline.RowWidth <= WrapCap) return inline;
        return Conceal.Get(line);
    }

    // A display formula laid out over several rows (#49): in the reading view with soft wrap, the rows given to the
    // block's lines in turn, the last line taking the rest; null where it is not one or does not fit the window (then
    // the one-line Unicode form).
    Disp? PictureFor(int li)
    {
        if (!Concealing || !WrapOn) return null;
        var line = Lines[li];
        int first = li, last = li;
        string tex;
        var t = line.Trim();
        var states = _states!;
        if (states[li] == 0)
        {
            // "$$ formula $$" or "\[ formula \]" on a line of its own
            if ((OneLine("$$", "$$") ?? OneLine(@"\[", @"\]")) is not { } one) return null;
            tex = one;
        }
        else
        {
            // the lines of a $$ or \[ block (#128: Mathpix and pandoc write \[ and \] on lines of their own)
            if (_lang!.Blocks[states[li] - 1] is not { Kind: Tok.Math } block) return null;
            var (open, close) = (block.Start, block.End);
            bool Content(int i) => states[i] > 0 && _lang.Blocks[states[i] - 1] == block && Lines[i].Trim() != close;
            if (!Content(li)) return null;
            while (first > 0 && Content(first - 1)) first--;
            while (last + 1 < Lines.Count && Content(last + 1)) last++;
            if (first == 0 || Lines[first - 1].Trim() != open) return null;          // "$$ a" opening on a line with text
            var src = string.Join("\n", Lines.Skip(first).Take(last - first + 1));
            tex = src.TrimEnd().EndsWith(close, StringComparison.Ordinal) ? src.TrimEnd()[..^close.Length] : src;
        }
        if (string.IsNullOrWhiteSpace(tex)) return null;
        // typeset, where the terminal shows pictures and the module drew this one (until then: text)
        if (FormulaImages.Get(tex) is { } pic && Disp.PictureIndent + pic.Cols + 1 <= WrapCap)
            return Conceal.ImageRows(pic, line, li - first, li == last);
        var rows = Conceal.Picture(tex);
        if (rows.Max(r => r.Length) + 1 > WrapCap) return null;
        return Conceal.PictureRows(tex, line, li - first, li == last);

        string? OneLine(string open, string close) =>
            t.Length > open.Length + close.Length && t.StartsWith(open, StringComparison.Ordinal) && t.EndsWith(close, StringComparison.Ordinal)
                ? t[open.Length..^close.Length] : null;
    }

    // A line that is only an image, as a picture (MarkdownImages, FormulaImages), as display formulas are: with soft
    // wrap, as wide as the window lets it be and at most half its height (a photo larger than the window would leave
    // nothing of the text around it), Obsidian's "|300" narrower still. Until it is read, or when it cannot be: text.
    Disp? ImageFor(int li)
    {
        if (!Concealing || !WrapOn || !FormulaImages.ImagesActive) return null;
        if (MarkdownImages.Parse(Lines[li]) is not { } image || MarkdownImages.Resolve(Path, image.Target, image.Wiki) is not { } file) return null;
        int maxCols = WrapCap - Disp.PictureIndent - 1;
        if (image.Width is int px && FormulaImages.Cell.W > 0) maxCols = Math.Min(maxCols, Math.Max(1, (px + FormulaImages.Cell.W - 1) / FormulaImages.Cell.W));
        int maxRows = Math.Max(3, TextArea.H / 2);
        return FormulaImages.GetImage(file.Path, maxCols, maxRows, file.Stamp) is { } pic ? Conceal.ImageRows(pic, Lines[li], 0, true) : null;
    }

    /// <summary>Formula pictures arrived (or went: another cell size, the setting): the lines are laid out again, the
    /// row at the top of the window kept there, so the page does not jump when pictures above it change height.</summary>
    public void FormulaPicturesChanged()
    {
        if (!Concealing || !WrapOn || _rowOf == null) return;
        EnsureLayout();
        var top = SegmentAt(_top);
        _rowOf = null;
        EnsureLayout();
        _top = Math.Clamp(_rowOf![top.Line] + Math.Min(top.Index, _segs![top.Line].Length - 1), 0, Math.Max(0, TotalRows - 1));
        if (!ReadingView) EnsureVisible();
    }

    string TextForLayout(int li) => DispFor(li)?.Text ?? Lines[li];
    int DisplayCol(int li, int srcCol) => DispFor(li)?.ToDisplay(srcCol) ?? srcCol;

    public void SetView(MdView v)
    {
        if (!IsMarkdown) return;
        if (v != MdView.Reading) _editView = v;
        View = v; _anchor = null; _rowOf = null; _liveRow = -1;
        Touch();
    }

    public void ToggleReading() => SetView(View == MdView.Reading ? _editView : MdView.Reading);
}
