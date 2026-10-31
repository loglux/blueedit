// Everyday code editing (the user, 11 October 2026: "what else does a code editor need"): line comments on and off,
// moving lines up and down, a closing bracket typed on an empty line going back to its opener's indent, and the
// bracket pair at the caret shown.
partial class EditorView
{
    const string Openers = "([{", Closers = ")]}";
    /// <summary>How far a bracket's pair is looked for, in lines either way: the search runs on every frame.</summary>
    const int BracketScanLines = 1000;

    // the line comment of the file's language: "#", "//", "--"…; Batch's "REM " gives way to "::"
    string? LineComment => _lang?.LineComments is { Length: > 0 } c ? (c.Contains("::") ? "::" : c[0].TrimEnd()) : null;

    // the rows a line command works on: the selection's (its last row left out when the selection ends at its start)
    (int First, int Last) CodeRows()
    {
        if (!HasSelection) return (Row, Row);
        var (s, e) = Order(_anchor!.Value, (Row, Col));
        int last = e.Item1 > s.Item1 && e.Item2 == 0 ? e.Item1 - 1 : e.Item1;
        return (s.Item1, last);
    }

    /// <summary>Ctrl+/: comments the line or the selected lines out with the language's line comment, or back in when
    /// all of them are comments already. The comment goes at the lines' smallest indent, as VS Code puts it.</summary>
    public void ToggleComment()
    {
        if (ReadOnlyView || LineComment is not { } mark) return;
        var (first, last) = CodeRows();
        var rows = Enumerable.Range(first, last - first + 1).Where(r => Lines[r].Trim().Length > 0).ToList();
        if (rows.Count == 0) return;
        bool allCommented = rows.All(r => Lines[r].TrimStart(' ').StartsWith(mark, StringComparison.Ordinal));
        int indent = rows.Min(r => Lines[r].Length - Lines[r].TrimStart(' ').Length);
        Snapshot("comment"); _lastKind = null;
        int delta = 0;
        foreach (int r in rows)
        {
            var l = Lines[r];
            if (allCommented)
            {
                int at = l.Length - l.TrimStart(' ').Length;
                int n = mark.Length + (at + mark.Length < l.Length && l[at + mark.Length] == ' ' ? 1 : 0);
                Lines[r] = l.Remove(at, n);
                if (r == Row) delta = -Math.Min(n, Math.Max(0, Col - at));
            }
            else
            {
                Lines[r] = l.Insert(indent, mark + " ");
                if (r == Row && Col >= indent) delta = mark.Length + 1;
            }
        }
        Col = Math.Clamp(Col + delta, 0, Lines[Row].Length);
        if (_anchor is { } a) _anchor = (a.Item1, Math.Min(a.Item2, Lines[a.Item1].Length));
        Touch(true);
    }

    /// <summary>Alt+Up / Alt+Down: the line, or the selected lines, swap places with the line above or below; the caret
    /// and the selection go with them.</summary>
    public void MoveLines(int dir)
    {
        if (ReadOnlyView) return;
        var (first, last) = CodeRows();
        if (dir < 0 ? first == 0 : last >= Lines.Count - 1) return;
        Snapshot("movelines"); _lastKind = null;
        if (dir < 0)
        {
            var above = Lines[first - 1];
            Lines.RemoveAt(first - 1);
            Lines.Insert(last, above);
        }
        else
        {
            var below = Lines[last + 1];
            Lines.RemoveAt(last + 1);
            Lines.Insert(first, below);
        }
        Row += dir;
        if (_anchor is { } a) _anchor = (a.Item1 + dir, a.Item2);
        Touch(true);
    }

    /// <summary>The selection's start and end, in order; null without one.</summary>
    public ((int Line, int Col) Start, (int Line, int Col) End)? SelectionSpan =>
        HasSelection ? Order(_anchor!.Value, (Row, Col)) : null;

    /// <summary>A language server's edits (formatting), all made against the current text: one undo step, the caret
    /// kept on its text. Returns false when they do not fit the text (it changed since they were asked for).</summary>
    public bool ApplyEdits(IReadOnlyList<LspEdit> edits)
    {
        if (ReadOnlyView || edits.Count == 0) return edits.Count == 0;
        var starts = new int[Lines.Count];
        for (int i = 1; i < Lines.Count; i++) starts[i] = starts[i - 1] + Lines[i - 1].Length + 1;
        int Offset(int line, int col) => line >= Lines.Count ? starts[^1] + Lines[^1].Length
            : starts[line] + Math.Min(col, Lines[line].Length);
        foreach (var e in edits)
            if (e.Line < 0 || e.Line > Lines.Count || e.EndLine > Lines.Count || (e.EndLine, e.EndCol).CompareTo((e.Line, e.Col)) < 0) return false;
        // from the end, so that earlier offsets stay right; the caret moves by what changed before it
        var sorted = edits.Select(e => (S: Offset(e.Line, e.Col), E: Offset(e.EndLine, e.EndCol), e.Text)).OrderBy(x => x.S).ToList();
        for (int i = 1; i < sorted.Count; i++) if (sorted[i].S < sorted[i - 1].E) return false;      // overlapping
        var text = new System.Text.StringBuilder(string.Join('\n', Lines));
        int caret = Offset(Row, Col), moved = caret;
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var (s, e, t) = sorted[i];
            text.Remove(s, e - s).Insert(s, t);
            if (e <= caret) moved += t.Length - (e - s);
            else if (s < caret) moved = moved - (caret - s) + Math.Min(caret - s, t.Length);
        }
        var result = text.ToString();
        if (result == string.Join('\n', Lines)) return true;
        Snapshot("format"); _lastKind = null;
        Lines = result.Split('\n').ToList();
        moved = Math.Clamp(moved, 0, result.Length);
        int r = 0;
        while (r < Lines.Count - 1 && moved > Lines[r].Length) { moved -= Lines[r].Length + 1; r++; }
        Row = r; Col = Math.Min(moved, Lines[r].Length); _anchor = null;
        Touch(true);
        return true;
    }

    bool IsBraceLanguage => _lang?.Name is "C#" or "C/C++" or "Java" or "JavaScript" or "Go" or "Rust" or "PowerShell" or "JSON" or "Shell";

    /// <summary>A closer typed where only spaces stand before the caret moves the line to its opener's indent: "}" lines
    /// up with the line that opened the block. False when it is an ordinary character here.</summary>
    bool TypeCloserDedented(char ch)
    {
        int k = Closers.IndexOf(ch);
        if (!AutoIndent || !IsBraceLanguage || k < 0) return false;
        var line = Lines[Row];
        if (Col == 0 || line[..Col].Trim(' ').Length > 0) return false;
        if (Col < line.Length && line[Col] == ch) return false;            // steps over the closer already there
        if (FindPair(Row, Col - 1, Closers[k], Openers[k], -1, depth: 1) is not { } opener) return false;
        int indent = Lines[opener.Line].Length - Lines[opener.Line].TrimStart(' ').Length;
        Snapshot("type");
        Lines[Row] = new string(' ', indent) + ch + line[Col..];
        Col = indent + 1;
        Touch(true);
        return true;
    }

    /// <summary>Whether the caret's word is code (not in a string or comment).</summary>
    public bool CaretInCode => WordAtCursor() is { } w && IsCode(Row, w.Start);

    // outside strings and comments
    bool IsCode(int li, int c)
    {
        var t = TokensFor(li);
        return c < t.Length && t[c] is not (Tok.String or Tok.Comment);
    }

    // the pair of a bracket: scanning from (li, c) in direction dir for `pair`, counting `self` in between;
    // depth 0: (li, c) is the bracket itself, 1: the scan starts after it
    (int Line, int Col)? FindPair(int li, int c, char self, char pair, int dir, int depth = 0)
    {
        for (int n = 0; n <= BracketScanLines && li >= 0 && li < Lines.Count; n++, li += dir)
        {
            var line = Lines[li];
            for (int i = n == 0 ? c : dir > 0 ? 0 : line.Length - 1; i >= 0 && i < line.Length; i += dir)
            {
                char ch = line[i];
                if (ch != self && ch != pair || !IsCode(li, i)) continue;
                if (ch == self) depth++;
                else if (--depth == 0) return (li, i);
            }
        }
        return null;
    }

    /// <summary>The bracket at the caret (or just before it) and its pair, for the frame to mark; null in prose, in
    /// reading mode, in a string or comment, or when the pair is not found nearby.</summary>
    ((int Line, int Col) A, (int Line, int Col) B)? MatchingBrackets()
    {
        if (IsProseFile || ReadingView || HasSelection) return null;
        return BracketPairAtCaret();
    }

    /// <summary>Ctrl+]: to the pair of the bracket at the caret (or just before it), in code and in the formulas and
    /// brackets of text. Says why not when it cannot.</summary>
    public string? GoToMatchingBracket()
    {
        if (ReadingView) return null;
        if (BracketPairAtCaret() is not { } pair) return BracketAtCaret() ? Ui.T("No pair for this bracket") : Ui.T("No bracket at the caret");
        GoToLine(pair.B.Line + 1, pair.B.Col + 1);
        return null;
    }

    bool BracketAtCaret()
    {
        var line = Lines[Row];
        return new[] { Col, Col - 1 }.Any(c => c >= 0 && c < line.Length && (Openers.Contains(line[c]) || Closers.Contains(line[c])) && IsCode(Row, c));
    }

    ((int Line, int Col) A, (int Line, int Col) B)? BracketPairAtCaret()
    {
        var line = Lines[Row];
        foreach (int c in new[] { Col, Col - 1 })
        {
            if (c < 0 || c >= line.Length) continue;
            int o = Openers.IndexOf(line[c]), k = Closers.IndexOf(line[c]);
            if (o < 0 && k < 0 || !IsCode(Row, c)) continue;
            var other = o >= 0 ? FindPair(Row, c, Openers[o], Closers[o], 1) : FindPair(Row, c, Closers[k], Openers[k], -1);
            return other is { } p ? ((Row, c), p) : null;
        }
        return null;
    }
}
