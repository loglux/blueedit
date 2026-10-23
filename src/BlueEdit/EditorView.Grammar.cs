// Grammar underlines for Markdown and plain text. Paragraphs are found around the lines being drawn (never the
// whole file), sent to the grammar checker once they have not changed for a moment, and its findings are mapped
// back to columns: the paragraph text keeps every source column (markup becomes spaces, lines join with '\n').
partial class EditorView
{
    public IGrammarChecker? Grammar { get; set; }
    public bool GrammarOn { get; set; }
    /// <summary>The spelling languages: a paragraph's language is chosen among them (EditorView.Language.cs).</summary>
    public IReadOnlyList<string> GrammarLanguages { get; set; } = new[] { "en-GB" };
    /// <summary>Findings never shown, by rule (setting "ignoredRules"): LanguageTool rule ids, a model's fixes.</summary>
    public IReadOnlySet<string> IgnoredRules { get; set; } = new HashSet<string>();

    long _lastEditTicks;
    const int GrammarQuietMs = 1500;                  // a paragraph is checked once it has not been edited this long

    bool GrammarApplies => _lang == null || IsMarkdown || IsLatex;

    static readonly System.Text.RegularExpressions.Regex ItemStart = new(@"^\s*(?:[-*+]|\d+[.)])\s", System.Text.RegularExpressions.RegexOptions.Compiled);

    bool IsHeadingLine(int li) => IsMarkdown && Lines[li].TrimStart().StartsWith('#');

    // A line of prose: not blank, not inside a code block / math / comment, not a fence or a table.
    bool ProseLine(int li)
    {
        var l = Lines[li];
        if (l.Trim().Length == 0) return false;
        if (IsLatex) return LatexProseLine(li);
        if (!IsMarkdown) return true;
        EnsureFences();
        if (_states![li] != 0 || _states[li + 1] != 0) return false;     // inside or opening a block
        var t = l.TrimStart();
        if (t.StartsWith("```", StringComparison.Ordinal)) return false;
        // "$$r=7\,\text{cm}$$ and slant height": a display formula with text after it on its line is prose (#70)
        if (t.StartsWith("$$", StringComparison.Ordinal))
            return Prose.MathRx.Match(t) is { Success: true } m && m.Value.EndsWith("$$", StringComparison.Ordinal) && m.Length > 4
                   && t[m.Length..].Any(char.IsLetter);
        return TableDisp(li) == null;
    }

    // A line that begins a paragraph of its own even right after another prose line: a heading or a list item.
    bool StartsParagraph(int li) => IsMarkdown ? IsHeadingLine(li) || ItemStart.IsMatch(Lines[li]) : IsLatex && LatexStartsParagraph(li);

    // ---------- formulas (#48) ----------
    // A formula is not prose, but it is part of a sentence: "где $x$ — длина", "получаем $$E = mc^2$$ где E — ...".
    // Each checker gets the formula in the form it asks for (IGrammarChecker.Formulas; today all: as it is), and any
    // finding that touches a formula is dropped. Measured with tests/BlueEdit.Tests/MathBench.cs on
    // tools/proofbench/cases/math-*.json (docs/tech/proofing-internals.md, "Formulas").

    // A state of a display formula block that runs over lines: $$ or a \[ at a line start (#128).
    bool InMathBlock(int state) => state > 0 && _lang!.Blocks[state - 1].Kind == Tok.Math;

    // A line of a display formula: it opens, continues or closes a $$ block, or is a $$…$$ line of its own.
    bool MathBlockLine(int li)
    {
        if (IsLatex) return LatexMathLine(li);
        if (!IsMarkdown) return false;
        EnsureFences();
        return InMathBlock(_states![li]) || InMathBlock(_states[li + 1]) || StartsWithFormula(li);
    }

    /// <summary>The formulas of a line (start, length): $…$, $$…$$, a $$ that runs on to the next lines, and the part of
    /// a line inside a display block up to its closing $$.</summary>
    internal List<(int Start, int Length)> MathIn(int li)
    {
        var spans = new List<(int, int)>();
        if (IsLatex) return LatexMathIn(li);
        if (!IsMarkdown) return spans;
        EnsureFences();
        var l = Lines[li];
        int from = 0;
        if (InMathBlock(_states![li]))
        {
            var end = _lang!.Blocks[_states[li] - 1].End;
            int close = l.IndexOf(end, StringComparison.Ordinal);
            from = close < 0 ? l.Length : close + end.Length;
            if (from > 0) spans.Add((0, from));
        }
        for (var m = Prose.MathRx.Match(l, from); m.Success; m = m.NextMatch()) spans.Add((m.Index, m.Length));
        return spans;
    }

    /// <summary>The formulas of a paragraph as offsets in <see cref="ParagraphText"/> (and <see cref="ParagraphSource"/>).</summary>
    internal List<(int Offset, int Length)> ParagraphMath(int first, int last)
    {
        var all = new List<(int, int)>();
        int at = 0;
        for (int i = first; i <= last; i++)
        {
            foreach (var (s, n) in MathIn(i)) all.Add((at + s, n));
            at += Lines[i].Length + 1;
        }
        return all;
    }

    /// <summary>The prose paragraph a line belongs to (first and last line), or null. Display formulas inside it
    /// (no blank line around them) belong to it; a paragraph is never only formulas.</summary>
    internal (int First, int Last)? ParagraphAt(int li)
    {
        if (!ProseLine(li)) return null;
        if (IsHeadingLine(li)) return (li, li);
        var (a, b) = Block(li);
        // A display formula with blank lines around it, as maths is usually written, still belongs to the sentence
        // around it (#70): "…so putting $y=0$ gives ⏎⏎ $$…$$ ⏎⏎ where c is a constant." It joins the text above when
        // that has not ended (no . ! ? at its end), and the text below joins too when it goes on in lower case.
        // Measured on the user's coursework: of 1084 display formulas between blank lines, 906 followed text that had
        // not ended, and 92 were followed by text in lower case.
        // forward: formulas (one after another, as a calculation goes on) while the text has not ended, then text that
        // goes on in lower case, then maybe more formulas
        for (int guard = 0; guard < 40 && b - a < 400; guard++)
        {
            if (FormulaAfter(b) is { } f && !EndsSentence(b)) { b = f.Last; continue; }
            if (MathBlockLine(b) && !ProseLine(b) && TextAfter(b) is { } n && GoesOn(n)) { b = Block(n).Last; continue; }
            // "…the radius of the cone ⏎⏎ $$r=7\,\text{cm}$$ and slant height": text that starts with a formula
            if (!EndsSentence(b) && TextAfter(b) is { } m && StartsWithFormula(m)) { b = Block(m).Last; continue; }
            break;
        }
        // backward, the same seen from below: text in lower case after formulas that follow text that has not ended
        for (int guard = 0; guard < 40 && b - a < 400; guard++)
        {
            if (StartsWithFormula(a) && TextBefore(a) is { } p && !EndsSentence(p)) { a = Block(p).First; continue; }
            if (!GoesOn(a) || FormulaBefore(a) is not { } f) break;
            while (FormulaBefore(f.First) is { } g && !EndsSentence(g.Last)) f = g;
            if (TextBefore(f.First) is not { } t || EndsSentence(t)) break;
            a = Block(t).First;
        }
        return (a, b);
    }

    // The lines of prose (and display formulas without blank lines) around li, as Markdown paragraphs go.
    (int First, int Last) Block(int li)
    {
        bool Joins(int i) => ProseLine(i) || MathBlockLine(i);
        int a = li;
        while (a > 0 && li - a < 200 && !StartsParagraph(a) && Joins(a - 1) && !IsHeadingLine(a - 1)) a--;
        int b = li;
        while (b + 1 < Lines.Count && b - li < 200 && Joins(b + 1) && !StartsParagraph(b + 1)) b++;
        return (a, b);
    }

    bool Blank(int i) => Lines[i].Trim().Length == 0;

    // A display formula that stands alone below line `last`, after one or two blank lines: its first and last line.
    (int First, int Last)? FormulaAfter(int last)
    {
        int k = last + 1, blanks = 0;
        while (k < Lines.Count && Blank(k) && blanks < 2) { k++; blanks++; }
        if (blanks == 0 || k >= Lines.Count || !MathBlockLine(k) || ProseLine(k)) return null;
        int e = k;
        while (e + 1 < Lines.Count && MathBlockLine(e + 1) && !ProseLine(e + 1)) e++;
        return (k, e);
    }

    // The same above line `first`.
    (int First, int Last)? FormulaBefore(int first)
    {
        int k = first - 1, blanks = 0;
        while (k >= 0 && Blank(k) && blanks < 2) { k--; blanks++; }
        if (blanks == 0 || k < 0 || !MathBlockLine(k) || ProseLine(k)) return null;
        int s = k;
        while (s > 0 && MathBlockLine(s - 1) && !ProseLine(s - 1)) s--;
        return (s, k);
    }

    // The first line of prose after one or two blank lines below `last` (not a heading, list item or table), or null.
    int? TextAfter(int last)
    {
        int k = last + 1, blanks = 0;
        while (k < Lines.Count && Blank(k) && blanks < 2) { k++; blanks++; }
        return blanks > 0 && k < Lines.Count && ProseLine(k) && !StartsParagraph(k) ? k : null;
    }

    // The last line of prose before one or two blank lines above `first`, or null.
    int? TextBefore(int first)
    {
        int k = first - 1, blanks = 0;
        while (k >= 0 && Blank(k) && blanks < 2) { k--; blanks++; }
        return blanks > 0 && k >= 0 && ProseLine(k) && !IsHeadingLine(k) ? k : null;
    }

    // The line's sentence has ended: . ! ? (after closing quotes or brackets) at its end.
    bool EndsSentence(int li) => Lines[li].TrimEnd().TrimEnd('"', '\'', '»', '”', ')', '*', '_') is { Length: > 0 } t && t[^1] is '.' or '!' or '?';

    // A prose line that begins with a display formula ("$$r=7$$ and slant height").
    bool StartsWithFormula(int li) => Lines[li].TrimStart() is var t && (t.StartsWith("$$", StringComparison.Ordinal) || t.StartsWith(@"\[", StringComparison.Ordinal));

    // The line goes on with the sentence before it: its first letter is lower case ("where", "and", "так что").
    bool GoesOn(int li) => Lines[li].TrimStart() is { Length: > 0 } t && char.IsLower(t[0]);

    /// <summary>The paragraph as a checker sees it: markup blanked, formulas as <paramref name="math"/> says, lines
    /// joined with '\n'; every offset is the same as in the source.</summary>
    internal string ParagraphText(int first, int last, MathView math = MathView.Placeholder)
    {
        if (IsMarkdown) EnsureFences();
        var sb = new System.Text.StringBuilder();
        for (int i = first; i <= last; i++)
        {
            bool inBlock = IsMarkdown && InMathBlock(_states![i]);
            // a line break inside a display formula is a space: its lines would read as blank ones, and LanguageTool
            // ends a sentence (a paragraph) at a blank line - measured: "into\nX \n     \nand" was cut, "into\nX\nand" not
            // a blank line inside a joined paragraph (around a display formula) is a space for the same reason
            if (i > first) sb.Append((inBlock && math != MathView.Source) || Blank(i) || Blank(i - 1) ? ' ' : '\n');
            var chars = Prose.Plain(Lines[i], IsMarkdown, IsLatex).ToCharArray();
            foreach (var (s, n) in MathIn(i))
            {
                if (math == MathView.Source) { Lines[i].CopyTo(s, chars, s, n); continue; }
                Array.Fill(chars, ' ', s, n);
                // one word per formula: not where a display block goes on from the line above
                if (math == MathView.Placeholder && !(inBlock && s == 0)) chars[s] = Prose.Placeholder;
            }
            sb.Append(chars);
        }
        return sb.ToString();
    }

    // Issues of a paragraph, asking the checker only when nobody is typing in it; none that touch a formula.
    IReadOnlyList<GrammarIssue>? IssuesOf(int first, int last)
    {
        if (Grammar is not { Available: true } g) return null;
        var text = ParagraphText(first, last);
        string? withFormulas = null;
        string Form(MathView v) => v == MathView.Placeholder ? text : v == MathView.Source ? withFormulas ??= ParagraphText(first, last, v) : ParagraphText(first, last, v);
        bool typing = Row >= first && Row <= last && Environment.TickCount64 - _lastEditTicks < GrammarQuietMs;
        var issues = g.Issues(Form, LanguageOf(Form(MathView.Spaces)), queue: !typing);
        if (issues is not { Count: > 0 }) return issues;
        var math = ParagraphMath(first, last);
        var source = ParagraphSource(first, last);
        // a finding on markup alone ("**", "- "): the checker saw it as spaces, so it is about the editor's blanking,
        // not the text (the model did that on the user's notes, #70); a pure insertion (a comma) is kept. A formula is
        // blanked too, but whether a change there is kept is ChangesFormula's to say ("$" → "$,", #158)
        bool OnMarkupOnly(GrammarIssue x) =>
            x.Length > 0 && x.Offset + x.Length <= text.Length && string.IsNullOrWhiteSpace(text.Substring(x.Offset, x.Length))
            && !Prose.Touches(math, x.Offset, x.Length)
            && !string.IsNullOrWhiteSpace(source.Substring(x.Offset, Math.Min(x.Length, source.Length - x.Offset)));
        var inFormulas = issues.Where(x => Prose.ChangesFormula(math, source, x)).ToList();
        return issues.Where(x => !IgnoredRules.Contains(x.Rule) && !IgnoredHere.Contains(IgnoreKey(x, source)) && !inFormulas.Contains(x)
            && !MovedIntoFormula(x, math, inFormulas) && !OnMarkupOnly(x)).ToList();
    }

    // "…\right)$." → "…\right).$" (#158, gpt-5.4-mini 3 of 3 times): the diff makes it ")" → ")." inside the formula,
    // dropped, and "." removed after it, which alone read as "possibly delete '.'" without the model's reason. A
    // removal of one or two characters right after a formula goes too, when a finding inside that formula went.
    static bool MovedIntoFormula(GrammarIssue x, List<(int Offset, int Length)> math, List<GrammarIssue> inFormulas) =>
        x.Length is 1 or 2 && x.Replacements.Count > 0 && x.Replacements.All(string.IsNullOrWhiteSpace)
        && math.Any(f => f.Offset + f.Length == x.Offset && inFormulas.Any(d => Prose.Touches([f], d.Offset, d.Length)));

    /// <summary>
    /// F7 → Ignore, until the editor closes, in every open file: a finding's rule at that text, "rule@text" (#129, found
    /// by the Obsidian plugin). Before, the checker was told to ignore the rule, and LanguageTool's rules are general
    /// (COMMA_PARENTHESIS_WHITESPACE): one Ignore hid the rule everywhere. Hiding a rule everywhere is Never show this.
    /// </summary>
    internal static readonly HashSet<string> IgnoredHere = new(StringComparer.Ordinal);

    static string IgnoreKey(GrammarIssue x, string source) =>
        x.Rule + "@" + (x.Offset + x.Length <= source.Length ? source.Substring(x.Offset, x.Length) : "");

    public void IgnoreHere(IEnumerable<GrammarIssue> issues, int first, int last)
    {
        var source = ParagraphSource(first, last);
        foreach (var x in issues) IgnoredHere.Add(IgnoreKey(x, source));
    }

    // Per drawn frame: paragraph found for each line, so a paragraph is assembled once, not once per line.
    readonly Dictionary<int, ((int First, int Last)? Para, IReadOnlyList<GrammarIssue>? Issues)> _grammarFrame = new();

    bool[]? GrammarMaskFor(int li)
    {
        if (!GrammarOn || !GrammarApplies || Grammar is not { Available: true }) return null;
        if (!_grammarFrame.TryGetValue(li, out var entry))
        {
            var p = ParagraphAt(li);
            entry = (p, p is { } pp ? IssuesOf(pp.First, pp.Last) : null);
            if (p is { } q) for (int i = q.First; i <= q.Last; i++) _grammarFrame[i] = entry;
            else _grammarFrame[li] = entry;
        }
        if (entry.Para is not { } para || entry.Issues is not { Count: > 0 } issues) return null;
        int lineStart = 0;
        for (int i = para.First; i < li; i++) lineStart += Lines[i].Length + 1;
        int lineEnd = lineStart + Lines[li].Length;
        bool[]? mask = null;
        foreach (var iss in issues)
        {
            int s = Math.Max(iss.Offset, lineStart), e = Math.Min(iss.Offset + iss.Length, lineEnd);
            if (e <= s) continue;
            mask ??= new bool[Lines[li].Length];
            Array.Fill(mask, true, s - lineStart, e - s);
        }
        return mask;
    }

    /// <summary>The grammar problem at the caret (inside it, or just after it), with where it is in the text.</summary>
    public (GrammarIssue Issue, int First, int Last)? GrammarIssueAtCaret() =>
        GrammarIssuesAtCaret() is { Issues.Count: > 0 } all ? (all.Issues[0], all.First, all.Last) : null;

    /// <summary>Every finding at the caret, from all grammar checkers (they may overlap: one explains, another fixes).</summary>
    public (IReadOnlyList<GrammarIssue> Issues, int First, int Last)? GrammarIssuesAtCaret()
    {
        if (!GrammarOn || !GrammarApplies || ParagraphAt(Row) is not { } p || IssuesOf(p.First, p.Last) is not { } issues) return null;
        int at = Col;
        for (int i = p.First; i < Row; i++) at += Lines[i].Length + 1;
        var hits = issues.Where(x => at >= x.Offset && at <= x.Offset + x.Length).ToList();
        return hits.Count == 0 ? null : (hits, p.First, p.Last);
    }

    /// <summary>The paragraph as it is in the file (markup included), lines joined with '\n': offsets in it are
    /// the same as in <see cref="ParagraphText"/>.</summary>
    internal string ParagraphSource(int first, int last) => string.Join('\n', Lines.GetRange(first, last - first + 1));

    /// <summary>Replaces the lines <paramref name="first"/>..<paramref name="last"/> with <paramref name="text"/>
    /// (one undo step) and puts the caret at <paramref name="caret"/>, an offset into the new text.</summary>
    public void ReplaceParagraph(int first, int last, string text, int caret)
    {
        if (ReadOnlyView) return;
        Snapshot("style"); _lastKind = null;
        var lines = text.Split('\n');
        Lines.RemoveRange(first, last - first + 1);
        Lines.InsertRange(first, lines);
        int r = first;
        caret = Math.Clamp(caret, 0, text.Length);
        while (r < first + lines.Length - 1 && caret > Lines[r].Length) { caret -= Lines[r].Length + 1; r++; }
        Row = r; Col = Math.Min(caret, Lines[r].Length); _anchor = null;
        Touch(true);
    }

    /// <summary>Puts <paramref name="replacement"/> in place of the problem's text (one undo step).</summary>
    public void FixGrammar(GrammarIssue issue, int first, string replacement)
    {
        if (ReadOnlyView) return;
        (int Row, int Col) Pos(int offset)
        {
            int r = first;
            while (r < Lines.Count - 1 && offset > Lines[r].Length) { offset -= Lines[r].Length + 1; r++; }
            return (r, Math.Min(offset, Lines[r].Length));
        }
        var (r1, c1) = Pos(issue.Offset);
        var (r2, c2) = Pos(issue.Offset + issue.Length);
        Snapshot("grammar"); _lastKind = null;
        var tail = Lines[r2][c2..];
        Lines[r1] = Lines[r1][..c1] + replacement + tail;
        if (r2 > r1) Lines.RemoveRange(r1 + 1, r2 - r1);
        Row = r1; Col = c1 + replacement.Length; _anchor = null;
        Touch(true);
    }
}

/// <summary>What the checkers get in place of a formula.</summary>
enum MathView { Placeholder, Spaces, Source }
