using System.Text.RegularExpressions;

// Grammar and style in LaTeX (#78), the way Markdown has them: the same paragraphs, formulas and checkers, with
// LaTeX's own idea of what is text. A line is prose unless it is in the preamble (before \begin{document}, when there
// is one), inside an environment that holds no prose (equation, align, figure, table, verbatim…) or a display formula
// \[ … \] / $$ … $$ running over lines, or a comment. Commands are blanked by Prose.LatexPlain; formulas and the
// arguments of \cite, \ref, \label… are the line's formulas: a finding that touches one is dropped, as in Markdown.
partial class EditorView
{
    public bool IsLatex => _lang?.Name == "LaTeX";

    // environments with no prose inside (starred forms too)
    static readonly HashSet<string> NonProseEnvironments = new(StringComparer.Ordinal)
    {
        "equation", "align", "alignat", "gather", "multline", "flalign", "eqnarray", "displaymath", "math", "split",
        "verbatim", "lstlisting", "minted", "comment", "figure", "table", "tabular", "tabularx", "longtable", "tikzpicture",
        "algorithm", "algorithmic", "thebibliography", "filecontents",
    };

    // of those, the display formulas: their lines belong to the sentence around them, as $$ blocks do in Markdown (#70)
    static readonly HashSet<string> MathEnvironments = new(StringComparer.Ordinal)
    {
        "equation", "align", "alignat", "gather", "multline", "flalign", "eqnarray", "displaymath", "math", "split",
    };

    static readonly Regex EnvRx = new(@"\\(begin|end)\{([A-Za-z]+)\*?\}", RegexOptions.Compiled);

    bool[]? _texNonProse, _texMath;
    int _texVersion = -1;

    // which lines are not prose, worked out for the whole file once per edit (a LaTeX file of a few thousand lines
    // takes a millisecond or so; it is only asked while drawing and checking)
    bool[] TexNonProse()
    {
        if (_texNonProse != null && _texVersion == EditVersion && _texNonProse.Length == Lines.Count) return _texNonProse;
        var skip = new bool[Lines.Count];
        var math = new bool[Lines.Count];
        int docStart = Lines.FindIndex(l => l.Contains(@"\begin{document}", StringComparison.Ordinal));
        int depth = 0, mathDepth = 0;                    // inside environments with no prose; of them, display formulas
        bool display = false;                            // inside \[ … \] or $$ … $$ over lines
        for (int i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            int comment = Prose.LatexComment(line);
            var code = comment >= 0 ? line[..comment] : line;
            bool inside = depth > 0 || display || i <= docStart, isMath = (mathDepth > 0 && mathDepth == depth) || display;
            foreach (Match m in EnvRx.Matches(code))
            {
                var env = m.Groups[2].Value;
                if (!NonProseEnvironments.Contains(env)) continue;
                bool opens = m.Groups[1].Value == "begin";
                if (opens) { depth++; if (MathEnvironments.Contains(env)) mathDepth++; }
                else { depth = Math.Max(0, depth - 1); if (MathEnvironments.Contains(env)) mathDepth = Math.Max(0, mathDepth - 1); }
                inside = true;                           // a line that opens or closes such an environment is not prose
                if (MathEnvironments.Contains(env) && (opens ? depth == mathDepth : depth + 1 == mathDepth + 1)) isMath = true;
            }
            // a display formula over lines: \[ without its \] on the line, or a $$ that does not close on it
            int opensD = Regex.Count(code, @"\\\["), closesD = Regex.Count(code, @"\\\]");
            int dollars = Regex.Count(code, @"(?<!\\)\$\$");
            if (display) inside = isMath = true;
            if (opensD > closesD || (!display && dollars % 2 == 1)) { display = true; inside = isMath = true; }
            else if (display && (closesD > opensD || dollars % 2 == 1)) display = false;
            skip[i] = inside || code.Trim().Length == 0;
            math[i] = isMath && i > docStart;
        }
        _texNonProse = skip;
        _texMath = math;
        _texVersion = EditVersion;
        return skip;
    }

    /// <summary>A line of a display formula over lines (\begin{equation} … \end{equation}, \[ … \]): it belongs to the
    /// sentence around it, the whole line a formula.</summary>
    bool LatexMathLine(int li) { TexNonProse(); return _texMath![li]; }

    bool LatexProseLine(int li) => !TexNonProse()[li] && Prose.LatexPlain(Lines[li]).Any(char.IsLetter);

    // \section{…}, \item, a \begin or \end of a list start a paragraph of their own
    static readonly Regex LatexParagraphStart = new(@"^\s*\\(?:part|chapter|section|subsection|subsubsection|paragraph|subparagraph|item|begin|end|caption)\b", RegexOptions.Compiled);

    // a heading is a paragraph of its own: the line after it starts the next one (an \item's next lines stay in it)
    static readonly Regex LatexHeading = new(@"^\s*\\(?:part|chapter|section|subsection|subsubsection|paragraph|subparagraph|caption)\*?\{", RegexOptions.Compiled);

    bool LatexStartsParagraph(int li) =>
        !LatexMathLine(li) && (LatexParagraphStart.IsMatch(Lines[li]) || (li > 0 && LatexHeading.IsMatch(Lines[li - 1])));

    /// <summary>The formulas of a LaTeX line: $…$, \(…\), \[…\] on the line, and \cite{…}, \ref{…}, \label{…}… with their
    /// arguments; nothing in a comment.</summary>
    List<(int Start, int Length)> LatexMathIn(int li)
    {
        var line = Lines[li];
        if (LatexMathLine(li)) return line.Length == 0 ? new() : new() { (0, line.Length) };     // the whole line
        int comment = Prose.LatexComment(line);
        var code = comment >= 0 ? line[..comment] : line;
        var spans = new List<(int, int)>();
        for (var m = Prose.MathRx.Match(code); m.Success; m = m.NextMatch()) spans.Add((m.Index, m.Length));
        foreach (Match m in Prose.LatexCommandRx.Matches(code))
            if (Prose.ReferenceCommands.Contains(m.Groups[1].Value)) spans.Add((m.Index, m.Length));
        spans.Sort();
        return spans;
    }
}
