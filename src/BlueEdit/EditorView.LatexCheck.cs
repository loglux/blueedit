// Formulas that would not typeset (LatexCheck.cs, #192): shown as errors the way a language server's are, so they are
// underlined, walked through with Shift+F7 and explained by F7. The rules of writing (LatexCheck.Style) are warnings
// with a fix F7 puts in. Found again after each edit, for the whole file: a pass over its formulas without any server
// or model.
partial class EditorView
{
    /// <summary>Check the formulas of Markdown and LaTeX files (setting "latexCheck").</summary>
    public bool LatexCheckOn { get; set; } = true;

    /// <summary>Also the rules of writing (LatexCheck.Style) as warnings (setting "latexStyle"); a rule in
    /// <see cref="IgnoredRules"/> is not shown.</summary>
    public bool LatexStyleOn { get; set; } = true;

    (List<string> Lines, int Version, bool Style, IReadOnlySet<string> Ignored, IReadOnlyList<LspDiagnostic> Found)? _latexFound;

    /// <summary>The errors (and warnings) of the file's formulas, as diagnostics with the source "LaTeX".</summary>
    internal IReadOnlyList<LspDiagnostic> LatexDiagnostics()
    {
        if (!LatexCheckOn || !(IsMarkdown || IsLatex)) return Array.Empty<LspDiagnostic>();
        if (_latexFound is { } f && ReferenceEquals(f.Lines, Lines) && f.Version == EditVersion && f.Style == LatexStyleOn
            && ReferenceEquals(f.Ignored, IgnoredRules)) return f.Found;
        var found = new List<LspDiagnostic>();
        foreach (var (text, at) in Formulas())
        {
            var style = LatexStyleOn ? LatexCheck.Style(text).Where(p => !IgnoredRules.Contains(p.Rule)) : [];
            foreach (var p in LatexCheck.Check(text).Concat(style))
            {
                var (l1, c1) = at[Math.Min(p.Offset, at.Count - 1)];
                var (l2, c2) = at[Math.Min(p.Offset + Math.Max(p.Length, 1) - 1, at.Count - 1)];
                // a fix is for a stretch on one line, and only where the formula's characters are the line's
                bool fixable = p.Fix != null && l1 == l2 && c2 - c1 == Math.Max(p.Length, 1) - 1;
                found.Add(new LspDiagnostic(l1, c1, l2, c2 + 1, p.Rule.Length > 0 ? 2 : 1, p.Message, "LaTeX",
                    Rule: p.Rule, Fix: fixable ? p.Fix : null));
            }
        }
        found.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Col.CompareTo(b.Col));
        _latexFound = (Lines, EditVersion, LatexStyleOn, IgnoredRules, found);
        return found;
    }

    /// <summary>Puts a formula rule's fix in place of its stretch (F7 on a LaTeX warning).</summary>
    public void FixLatex(LspDiagnostic d)
    {
        if (ReadOnlyView || d.Fix is not { } fix || d.Line != d.EndLine || d.Line >= Lines.Count || d.EndCol > Lines[d.Line].Length) return;
        Snapshot("latex");
        Lines[d.Line] = Lines[d.Line].Remove(d.Col, d.EndCol - d.Col).Insert(d.Col, fix);
        Row = d.Line; Col = d.Col + fix.Length; _anchor = null;
        Touch(true);
    }

    // Each formula of the file with the line and column of each of its characters: a display formula over several
    // lines is one, its lines joined with '\n'. Not in code blocks or `inline code`. In a LaTeX file only formulas
    // within a line ($…$, \(…\), \[…\]): the environments of its display lines are not checked yet.
    IEnumerable<(string Text, List<(int Line, int Col)> At)> Formulas()
    {
        var text = new System.Text.StringBuilder();
        var at = new List<(int, int)>();
        bool open = false;                                       // a display formula goes on to the next line
        for (int li = 0; li < Lines.Count; li++)
        {
            var line = Lines[li];
            if (!line.Contains('$', StringComparison.Ordinal) && !line.Contains('\\', StringComparison.Ordinal) && !open) continue;
            List<(int Start, int Length)> spans;
            if (IsLatex)
            {
                if (LatexMathLine(li)) continue;
                spans = LatexMathIn(li).Where(s => line[s.Start] == '$' || line.AsSpan(s.Start).StartsWith(@"\(") || line.AsSpan(s.Start).StartsWith(@"\[")).ToList();
            }
            else
            {
                spans = MathIn(li);
                if (_states![li] != 0 && !InMathBlock(_states[li])) continue;               // a code block
            }
            if (open && spans.Count == 0 && IsMarkdown && InMathBlock(_states![li]))   // a blank line inside $$ … $$
            {
                text.Append('\n'); at.Add((li, 0));
                continue;
            }
            foreach (var (s, n) in spans)
            {
                if (!open && InInlineCode(line, s)) continue;
                if (open && s != 0) { open = false; text.Clear(); at.Clear(); }
                for (int i = s; i < s + n; i++) { text.Append(line[i]); at.Add((li, i)); }
                bool goesOn = IsMarkdown && s + n == line.Length && li + 1 < Lines.Count && InMathBlock(_states![li + 1]);
                if (goesOn) { text.Append('\n'); at.Add((li, line.Length)); open = true; continue; }
                open = false;
                yield return (text.ToString(), new List<(int, int)>(at));
                text.Clear(); at.Clear();
            }
            if (open && spans.Count == 0) { open = false; text.Clear(); at.Clear(); }
        }
    }

    // whether column `col` is inside `…` on the line: an odd number of backticks before it
    static bool InInlineCode(string line, int col)
    {
        int ticks = 0;
        for (int i = 0; i < col; i++) if (line[i] == '`') ticks++;
        return ticks % 2 == 1;
    }
}
