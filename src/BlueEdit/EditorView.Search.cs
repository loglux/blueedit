using System.Text.RegularExpressions;

/// <summary>What to look for. Without <see cref="UseRegex"/> the pattern is plain text; <see cref="WholeWord"/> means no
/// letter, digit or underscore right before or after the match (works for Cyrillic too).</summary>
sealed record SearchOptions(string Pattern, bool MatchCase = false, bool WholeWord = false, bool UseRegex = false)
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>The pattern as a regex, or the reason it is not one (a broken regular expression).</summary>
    public (Regex? Regex, string? Error) Build()
    {
        if (Pattern.Length == 0) return (null, "nothing to search for");
        var p = UseRegex ? Pattern : Regex.Escape(Pattern);
        if (WholeWord) p = $@"(?<![\p{{L}}\p{{N}}_])(?:{p})(?![\p{{L}}\p{{N}}_])";
        var o = RegexOptions.CultureInvariant | (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try { return (new Regex(p, o, Timeout), null); }
        catch (ArgumentException ex) { return (null, ex.Message); }
    }

    /// <summary>Short description for the status line: "foo" (case, word, regex).</summary>
    public string Describe()
    {
        var flags = new[] { MatchCase ? Ui.T("case") : null, WholeWord ? Ui.T("word") : null, UseRegex ? Ui.T("regex") : null }.Where(f => f != null);
        var f = string.Join(", ", flags);
        return $"\"{Pattern}\"" + (f.Length > 0 ? $" ({f})" : "");
    }
}

partial class EditorView
{
    public SearchOptions? LastSearch { get; private set; }
    /// <summary>The last find went past the end (or start) of the text and continued from the other side.</summary>
    public bool LastFindWrapped { get; private set; }
    /// <summary>Why the last find or replace could not run (a broken regular expression), or null.</summary>
    public string? SearchError { get; private set; }

    (int Row, int Col) SelStart => _anchor is { } a && (a.Row < Row || a.Row == Row && a.Col < Col) ? a : (Row, Col);

    // Matches of one line, empty matches left out (a pattern like "^" or "x*" finds nothing useful to select).
    static IEnumerable<Match> MatchesIn(Regex rx, string line)
    {
        foreach (Match m in rx.Matches(line))
            if (m.Length > 0) yield return m;
    }

    /// <summary>Selects the next match after the caret (or the previous one before the selection), continuing from the
    /// other end of the text if needed. Returns false when there is none or the pattern is broken (<see cref="SearchError"/>).</summary>
    public bool Find(SearchOptions o, bool backward = false)
    {
        LastSearch = o; LastFindWrapped = false;
        var (rx, error) = o.Build();
        SearchError = error;
        if (rx == null) return false;
        try { return backward ? FindBackward(rx) : FindForward(rx); }
        catch (RegexMatchTimeoutException) { SearchError = "the regular expression takes too long"; return false; }
    }

    bool FindForward(Regex rx)
    {
        int n = Lines.Count;
        for (int i = 0; i <= n; i++)
        {
            int r = (Row + i) % n;
            foreach (var m in MatchesIn(rx, Lines[r]))
            {
                if (i == 0 && m.Index < Col) continue;                 // before the caret on its own line
                if (i == n && m.Index >= Col) break;                  // back on the caret line: only what is before it
                LastFindWrapped = r < Row || i == n;                  // before Select moves the caret
                Select(r, m.Index, m.Length);
                return true;
            }
        }
        return false;
    }

    bool FindBackward(Regex rx)
    {
        int n = Lines.Count;
        var (sr, sc) = SelStart;
        for (int i = 0; i <= n; i++)
        {
            int r = ((sr - i) % n + n) % n;
            Match? last = null;
            foreach (var m in MatchesIn(rx, Lines[r]))
            {
                if (i == 0 && m.Index + m.Length > sc) break;         // must end before the selection
                if (i == n && m.Index < sc) continue;                 // back on the start line: from the selection on
                last = m;
            }
            if (last == null) continue;
            Select(r, last.Index, last.Length);
            LastFindWrapped = r > sr || i == n;
            return true;
        }
        return false;
    }

    void Select(int row, int col, int length)
    {
        Row = row; _anchor = (row, col); Col = col + length;
        ColumnMode = false; _goalX = -1;
        TouchMove();
    }

    /// <summary>Selects <paramref name="length"/> characters at a 1-based line and column (a folder search result).</summary>
    public void SelectAt(int line, int col, int length)
    {
        int r = Math.Clamp(line - 1, 0, Lines.Count - 1);
        int c = Math.Clamp(col - 1, 0, Lines[r].Length);
        Select(r, c, Math.Min(length, Lines[r].Length - c));
    }

    /// <summary>The selected text when it lies within one line: what a Find dialog offers to search for.</summary>
    public string? SelectionOnOneLine =>
        HasSelection && _anchor is { } a && a.Row == Row && !ColumnMode ? Lines[Row][Math.Min(a.Col, Col)..Math.Max(a.Col, Col)] : null;

    /// <summary>Makes F5 continue with these options (after jumping to a folder search result).</summary>
    public void RememberSearch(SearchOptions o) => LastSearch = o;

    /// <summary>Find again with the last options (F5, Shift+F5).</summary>
    public bool FindNext(bool backward = false) => LastSearch != null && Find(LastSearch, backward);

    /// <summary>Plain text, case-insensitive (the old Find).</summary>
    public bool Find(string? text = null) => text == null ? FindNext() : Find(new SearchOptions(text));

    // The text that replaces one match: with a regex, $1, ${name} and $$ are expanded.
    static string Replacement(SearchOptions o, Match m, string replacement) => o.UseRegex ? m.Result(replacement) : replacement;

    /// <summary>Replaces every match; one undo step. Returns how many were replaced (0 also when the pattern is broken).</summary>
    public int ReplaceAll(SearchOptions o, string replacement)
    {
        LastSearch = o;
        var (rx, error) = o.Build();
        SearchError = error;
        if (rx == null || ReadOnlyView) return 0;
        int count = 0;
        var result = new string[Lines.Count];
        try
        {
            for (int r = 0; r < Lines.Count; r++)
                result[r] = rx.Replace(Lines[r], m => { if (m.Length == 0) return m.Value; count++; return Replacement(o, m, replacement); });
        }
        catch (RegexMatchTimeoutException) { SearchError = "the regular expression takes too long"; return 0; }
        if (count == 0) return 0;
        Snapshot("replace");
        for (int r = 0; r < Lines.Count; r++) Lines[r] = result[r];
        _anchor = null;
        Touch(true);
        return count;
    }

    /// <summary>Plain text, case-insensitive (the old ReplaceAll).</summary>
    public int ReplaceAll(string find, string with) => ReplaceAll(new SearchOptions(find), with);

    /// <summary>If the selection is exactly a match, replaces it (own undo step), leaves the caret after the new text
    /// and returns true.</summary>
    public bool ReplaceSelection(SearchOptions o, string replacement)
    {
        if (!HasSelection || ReadOnlyView || _anchor is not { } a || a.Row != Row) return false;
        var (rx, _) = o.Build();
        if (rx == null) return false;
        int start = Math.Min(a.Col, Col), len = Math.Abs(Col - a.Col);
        var m = MatchesIn(rx, Lines[Row]).FirstOrDefault(x => x.Index == start && x.Length == len);
        if (m == null) return false;
        var text = Replacement(o, m, replacement);
        Snapshot("replace1"); _lastKind = null;
        Lines[Row] = Lines[Row].Remove(start, len).Insert(start, text);
        Col = start + text.Length; _anchor = null;
        Touch(true);
        return true;
    }

    /// <summary>
    /// Goes through the matches from the caret on, selecting each and asking <paramref name="ask"/>: 'y' replace,
    /// 'n' skip, 'a' replace this and all the rest, null stop. Stops after one full round through the text.
    /// Returns how many were replaced.
    /// </summary>
    public int ReplaceInteractive(SearchOptions o, string replacement, Func<char?> ask)
    {
        var (rx, error) = o.Build();
        SearchError = error;
        if (rx == null || ReadOnlyView) return 0;
        int replaced = 0;
        int startRow = Row, startCol = Col;        // the round ends when a match would start here again
        bool wrapped = false;
        while (Find(o))
        {
            wrapped |= LastFindWrapped;
            var (r, c) = SelStart;
            if (wrapped && (r > startRow || r == startRow && c >= startCol)) break;
            switch (ask())
            {
                case 'y': if (ReplaceSelection(o, replacement)) replaced++; break;
                case 'n': break;
                case 'a':
                    if (ReplaceSelection(o, replacement)) replaced++;
                    return replaced + ReplaceRest(rx, o, replacement, wrapped, startRow, startCol);
                default: _anchor = null; Touch(); return replaced;
            }
        }
        _anchor = null; Touch();
        return replaced;
    }

    // "All" during an interactive replace: every match from the caret to the end of the round, as one undo step.
    // Replacements only shift text after them, so the round's end (before startCol on the start line) stays put.
    int ReplaceRest(Regex rx, SearchOptions o, string replacement, bool wrapped, int startRow, int startCol)
    {
        var spans = new List<(int Row, int From, int To)>();
        int r0 = Row, c0 = Col, n = Lines.Count;
        if (wrapped)
        {
            if (r0 == startRow) spans.Add((r0, c0, startCol));
            else
            {
                spans.Add((r0, c0, int.MaxValue));
                for (int r = r0 + 1; r < startRow; r++) spans.Add((r, 0, int.MaxValue));
                spans.Add((startRow, 0, startCol));
            }
        }
        else
        {
            spans.Add((r0, c0, int.MaxValue));
            for (int r = r0 + 1; r < n; r++) spans.Add((r, 0, int.MaxValue));
            for (int r = 0; r < startRow; r++) spans.Add((r, 0, int.MaxValue));
            spans.Add((startRow, 0, startCol));
        }
        int count = 0;
        var changed = new Dictionary<int, string>();
        try
        {
            foreach (var (row, from, to) in spans)
            {
                var line = changed.TryGetValue(row, out var l) ? l : Lines[row];
                changed[row] = rx.Replace(line, m =>
                {
                    if (m.Length == 0 || m.Index < from || m.Index >= to) return m.Value;
                    count++;
                    return Replacement(o, m, replacement);
                });
            }
        }
        catch (RegexMatchTimeoutException) { SearchError = "the regular expression takes too long"; return 0; }
        if (count > 0)
        {
            Snapshot("replace"); _lastKind = null;
            foreach (var (row, text) in changed) Lines[row] = text;
        }
        _anchor = null;
        Touch(true);
        return count;
    }
}
