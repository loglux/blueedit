using System.Text.RegularExpressions;

/// <summary>One completion candidate. <see cref="Keyword"/>: from the language's keyword list, not from the text.
/// From a language server: <see cref="Tag"/> says what it is (m method, f field, cls class...), <see cref="Insert"/>
/// is the text to put in when it differs from the word shown.</summary>
sealed record Candidate(string Word, bool Keyword, string? Tag = null, string? Insert = null);

/// <summary>
/// Word completion (Ctrl+Space): every word of the open documents plus the language's keywords. The words are collected
/// once when the list opens; typing more letters only filters that set, so a large file costs nothing per key.
/// Order: words that start with the prefix in the same case first, then by how close to the caret they occur
/// (the current document before the others), keywords after the text's own words, then alphabetical.
/// </summary>
static partial class Completion
{
    [GeneratedRegex(@"[\p{L}_][\p{L}\p{N}_]*")]
    private static partial Regex WordRx();

    // PowerShell: Get-ChildItem is one word
    [GeneratedRegex(@"[\p{L}_][\p{L}\p{N}_]*(?:-\p{L}[\p{L}\p{N}_]*)*")]
    private static partial Regex HyphenWordRx();

    const int MinLength = 3;

    /// <summary>Word -> rank (smaller is better): distance in lines from the caret in the current document,
    /// or a large base for other documents and keywords.</summary>
    public sealed class Pool
    {
        internal readonly Dictionary<string, int> Rank = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Keywords = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, (string Tag, string Insert)> Server = new(StringComparer.Ordinal);
        /// <summary>The candidates came from a language server (in its order); then an empty prefix lists them all
        /// (right after a dot).</summary>
        public bool FromServer => Server.Count > 0;
    }

    /// <summary>A language server's pool with the text's words and keywords after its own candidates: a server that
    /// knows little (a lone file, a project still loading) never hides what plain completion would offer.</summary>
    public static Pool Merge(Pool server, Pool words)
    {
        int after = server.Rank.Count == 0 ? 0 : server.Rank.Values.Max() + 1;
        foreach (var (w, rank) in words.Rank)
            if (server.Rank.TryAdd(w, after + rank) && words.Keywords.Contains(w)) server.Keywords.Add(w);
        return server;
    }

    /// <summary>A pool of a language server's completions, in the order it gave them.</summary>
    public static Pool FromServer(IEnumerable<(string Label, string Tag, string Insert)> items)
    {
        var pool = new Pool();
        int rank = 0;
        foreach (var (label, tag, insert) in items)
            if (label.Length > 0 && pool.Rank.TryAdd(label, rank++)) pool.Server[label] = (tag, insert);
        return pool;
    }

    const int OtherDocuments = 1_000_000, KeywordRank = 2_000_000;

    /// <summary>Collects the words. The word the caret is in (at caretRow, starting at wordStart) is left out once,
    /// so a word typed for the first time does not offer itself.</summary>
    public static Pool Collect(IReadOnlyList<string> lines, int caretRow, int wordStart, IEnumerable<IReadOnlyList<string>> otherDocuments,
        IEnumerable<string> keywords, bool hyphenWords = false)
    {
        var pool = new Pool();
        var rx = hyphenWords ? HyphenWordRx() : WordRx();
        void Add(string w, int rank)
        {
            if (w.Length < MinLength) return;
            if (!pool.Rank.TryGetValue(w, out int old) || rank < old) pool.Rank[w] = rank;
        }
        for (int r = 0; r < lines.Count; r++)
            foreach (Match m in rx.Matches(lines[r]))
                if (!(r == caretRow && m.Index == wordStart)) Add(m.Value, Math.Abs(r - caretRow));
        foreach (var doc in otherDocuments)
            foreach (var line in doc)
                foreach (Match m in rx.Matches(line)) Add(m.Value, OtherDocuments);
        foreach (var k in keywords)
        {
            if (!pool.Rank.ContainsKey(k)) { Add(k, KeywordRank); pool.Keywords.Add(k); }
        }
        return pool;
    }

    /// <summary>Candidates for a prefix, best first. The prefix itself (already complete) is not offered.</summary>
    public static List<Candidate> Filter(Pool pool, string prefix, int max = 200)
    {
        if (prefix.Length == 0 && !pool.FromServer) return new();
        return pool.Rank
            .Where(p => p.Key.Length > prefix.Length && p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(p => pool.Server.TryGetValue(p.Key, out var s)
                ? new Candidate(p.Key, false, s.Tag, s.Insert == p.Key ? null : s.Insert)
                : new Candidate(p.Key, pool.Keywords.Contains(p.Key)))
            .ToList();
    }
}
