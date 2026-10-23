// Shift+F7: from one spelling or grammar problem to the next, for proofreading a long text. Each problem found is
// selected, so F7 can fix it at once.
partial class EditorView
{
    readonly record struct Problem(int Col, int Len, string What);

    // Typos (the spelling underline) and grammar findings that start on a line, left to right.
    List<Problem> ProblemsOn(int li)
    {
        var res = new List<Problem>();
        var line = Lines[li];
        if (SpellOn && Spell is { Available: true })
        {
            var toks = TokensFor(li);
            if (BadFor(li, line, _states![li], toks) is { } bad)
                for (int i = 0; i < bad.Length;)
                {
                    if (!bad[i]) { i++; continue; }
                    int s = i;
                    while (i < bad.Length && bad[i]) i++;
                    var w = line[s..i];
                    res.Add(new Problem(s, i - s, Lookalike.IsMixed(w) ? Ui.T("Latin and Cyrillic letters mixed: {0}", w) : Ui.T("Spelling ({0}): {1}", Spell.Name, w)));
                }
        }
        if (GrammarOn && GrammarApplies && Grammar is { Available: true } && ParagraphAt(li) is { } p && IssuesOf(p.First, p.Last) is { } issues)
        {
            int lineStart = 0;
            for (int i = p.First; i < li; i++) lineStart += Lines[i].Length + 1;
            foreach (var iss in issues)
                if (iss.Offset >= lineStart && iss.Offset < lineStart + Math.Max(1, line.Length))
                    res.Add(new Problem(iss.Offset - lineStart, Math.Min(iss.Length, line.Length - (iss.Offset - lineStart)), (iss.Source.Length > 0 ? Ui.T("Grammar ({0}): {1}", iss.Source, iss.Message) : Ui.T("Grammar: {0}", iss.Message))));
        }
        foreach (var d in DiagnosticsStartingOn(li))
        {
            int s = Math.Min(d.Col, Math.Max(0, line.Length - 1));
            int e = d.EndLine == li ? Math.Min(d.EndCol, line.Length) : line.Length;
            res.Add(new Problem(s, Math.Max(1, e - s), $"{SeverityName(d.Severity)} ({d.Source}): {FirstLine(d.Message)}"));
        }
        res.Sort((a, b) => a.Col.CompareTo(b.Col));
        return res;
    }

    /// <summary>Selects the next (or previous) problem after (before) the caret, going round the text. Returns what it
    /// is, or null when the text has none that are known (grammar of paragraphs not checked yet is asked for).</summary>
    public string? GoToNextProblem(bool backward = false)
    {
        int n = Lines.Count;
        var (sr, sc) = SelStart;
        for (int k = 0; k <= n; k++)
        {
            int li = backward ? ((sr - k) % n + n) % n : (sr + k) % n;
            var probs = ProblemsOn(li);
            if (probs.Count == 0) continue;
            Problem? pick = backward
                ? (k == 0 ? probs.LastOrDefault(p => p.Col < sc) : k == n ? probs.LastOrDefault(p => p.Col >= sc) : probs[^1])
                : (k == 0 ? probs.FirstOrDefault(p => p.Col > sc) : k == n ? probs.FirstOrDefault(p => p.Col <= sc) : probs[0]);
            if (pick is not { Len: > 0 } found) continue;
            Select(li, found.Col, found.Len);
            return found.What;
        }
        return null;
    }
}
