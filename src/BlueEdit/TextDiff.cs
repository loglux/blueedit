using System.Text.RegularExpressions;

/// <summary>One change from an original text to a corrected one: replace Length characters at Offset with Replacement.</summary>
sealed record TextChange(int Offset, int Length, string Replacement)
{
    public string OriginalIn(string text) => text.Substring(Offset, Length);
}

/// <summary>
/// Finds what a proofreader changed by comparing its corrected text with the original, word by word (words,
/// spaces and punctuation are separate tokens). A pure insertion ("сказал что" → "сказал, что") is widened to the
/// word before it, so it can be underlined and replaced like any other change.
/// </summary>
static partial class TextDiff
{
    [GeneratedRegex(@"\w+|\s+|[^\w\s]", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRx();

    static List<(int Start, string Text)> Tokens(string s) => TokenRx().Matches(s).Select(m => (m.Index, m.Value)).ToList();

    // Any two runs of whitespace pair up, and the original's stays: a model that puts a line break where a space was
    // (Gemma 3 12B between sentences) is not correcting anything, and an unpaired blank pulled the next word's fix out
    // of line (". Красивая" → ".\nКрасивый" instead of "Красивая" → "Красивый").
    static bool Same(string a, string b) => a == b || (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b));

    public static List<TextChange> Changes(string original, string corrected)
    {
        var a = Tokens(original);
        var b = Tokens(corrected);
        int n = a.Count, m = b.Count;
        // longest common subsequence of tokens (paragraphs are short: a full table is fine)
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = Same(a[i].Text, b[j].Text) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var changes = new List<TextChange>();
        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && Same(a[x].Text, b[y].Text)) { x++; y++; continue; }
            int x0 = x, y0 = y;
            while ((x < n || y < m) && !(x < n && y < m && Same(a[x].Text, b[y].Text)))
            {
                if (y < m && (x == n || lcs[x, y + 1] >= lcs[x + 1, y])) y++; else x++;
            }
            int start = x0 < n ? a[x0].Start : original.Length;
            int end = x > x0 ? a[x - 1].Start + a[x - 1].Text.Length : start;
            var repl = string.Concat(b.Skip(y0).Take(y - y0).Select(t => t.Text));
            if (end == start)                                       // pure insertion: take the word before (or after) along
            {
                int prev = x0 - 1;
                while (prev >= 0 && string.IsNullOrWhiteSpace(a[prev].Text)) prev--;
                if (prev >= 0)
                {
                    var wordBefore = original[a[prev].Start..start];
                    changes.Add(new TextChange(a[prev].Start, start - a[prev].Start, wordBefore + repl));
                    continue;
                }
                if (x0 < n) { changes.Add(new TextChange(start, a[x0].Text.Length, repl + a[x0].Text)); continue; }
            }
            changes.Add(new TextChange(start, end - start, repl));
        }
        return JoinNeighbours(original, changes);
    }

    /// <summary>
    /// Changes with only whitespace between them are one change (#141): a few neighbouring words rewritten at once
    /// come out of the token pairing as pieces that are each wrong alone ("что бы подготовится" → "чтобы
    /// подготовиться" as "что" → "чтобы", "бы" → "подготовиться", "подготовится " → ""), and taking one of them
    /// gave "чтобы бы подготовится". Whitespace both ends of a joined change share is trimmed off again. Changes with
    /// a word between them stay apart.
    /// </summary>
    static List<TextChange> JoinNeighbours(string original, List<TextChange> changes)
    {
        var res = new List<TextChange>();
        foreach (var c in changes)
        {
            if (res.Count > 0 && res[^1] is var p && c.Offset >= p.Offset + p.Length
                && string.IsNullOrWhiteSpace(original[(p.Offset + p.Length)..c.Offset]))
            {
                var gap = original[(p.Offset + p.Length)..c.Offset];
                res[^1] = new TextChange(p.Offset, c.Offset + c.Length - p.Offset, p.Replacement + gap + c.Replacement);
                continue;
            }
            res.Add(c);
        }
        for (int i = 0; i < res.Count; i++)
        {
            var (off, len, repl) = (res[i].Offset, res[i].Length, res[i].Replacement);
            while (len > 0 && repl.Length > 0 && char.IsWhiteSpace(original[off]) && original[off] == repl[0]) { off++; len--; repl = repl[1..]; }
            while (len > 0 && repl.Length > 0 && char.IsWhiteSpace(original[off + len - 1]) && original[off + len - 1] == repl[^1]) { len--; repl = repl[..^1]; }
            res[i] = new TextChange(off, len, repl);
        }
        return res;
    }

    /// <summary>Share of the original's characters touched by the changes (0..1): a proofreader that rewrote the
    /// paragraph instead of correcting it gives a high number.</summary>
    public static double ChangedShare(string original, IReadOnlyList<TextChange> changes) =>
        original.Length == 0 ? 0 : (double)changes.Sum(c => Math.Max(c.Length, 1)) / original.Length;
}
