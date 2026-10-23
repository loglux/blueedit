using System.Text;
using System.Text.RegularExpressions;

/// <summary>What a Markdown source line looks like once its markup is hidden: display text plus a map back to source columns.</summary>
sealed class Disp
{
    public string Text = "";
    public int[] Map = Array.Empty<int>();          // display index -> source index
    public Tok?[] Over = Array.Empty<Tok?>();       // token override for synthesized characters (bullets, bars...)
    public int SourceLength;
    public int RowWidth;                            // > 0: rows of a laid-out formula, each this wide, one per visual row
    public string?[]? Marks;                        // combining marks drawn over a display character (null: none on the line)
    public Tui.CellPicture? Picture;                // the rows show this typeset picture (Sixel) over their blanks...
    public int PictureRow;                          // ...from this row of it on
    public const int PictureIndent = 2;             // columns before it, as the text pictures are indented

    public int ToSource(int d) => d < Map.Length ? Map[Math.Max(0, d)] : SourceLength;

    /// <summary>First display column showing source column <paramref name="src"/> or later.</summary>
    public int ToDisplay(int src)
    {
        for (int d = 0; d < Map.Length; d++) if (Map[d] >= src) return d;
        return Map.Length;
    }
}

static class Conceal
{
    static readonly Regex Hr = new(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    static readonly Regex Head = new(@"^(\s{0,3})(#{1,6})([ \t]+|$)", RegexOptions.Compiled);
    static readonly Regex Quote = new(@"^(\s*)((?:>[ ]?)+)", RegexOptions.Compiled);
    static readonly Regex Task = new(@"^(\s*)([-*+])([ \t]+)\[([ xX])\]([ \t]+)", RegexOptions.Compiled);
    static readonly Regex Bullet = new(@"^(\s*)([-*+])([ \t]+)", RegexOptions.Compiled);
    static readonly Regex InlineCode = new(@"`[^`\n]+`", RegexOptions.Compiled);
    static readonly Regex Bold = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", RegexOptions.Compiled);
    static readonly Regex Italic = new(@"(?<![\w*\\])([*_])(?=[^\s*_])(.+?)(?<=[^\s*_])\1(?![\w*])", RegexOptions.Compiled);
    static readonly Regex Strike = new(@"~~(?=\S)(.+?)(?<=\S)~~", RegexOptions.Compiled);
    static readonly Regex Image = new(@"!\[([^\]]*)\]\(([^)]*)\)", RegexOptions.Compiled);
    static readonly Regex Link = new(@"\[([^\]]*)\]\(([^)]*)\)", RegexOptions.Compiled);
    static readonly Regex Wiki = new(@"\[\[([^\]|]+\|)?([^\]]+)\]\]", RegexOptions.Compiled);
    static readonly Regex Dollar = new(@"(?<![\\$])\$(?=[^\s$])([^$]+?)(?<=[^\s$])\$(?!\$)", RegexOptions.Compiled);
    static readonly Regex DollarDouble = new(@"(?<!\\)\$\$(.+?)\$\$", RegexOptions.Compiled);
    static readonly Regex Escape =new(@"\\([\\`*_{}\[\]()#+\-.!|~$>])", RegexOptions.Compiled);

    const int HrWidth = 40;
    // shared caches: concurrent, so a background thread (or parallel tests) can never corrupt them
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Disp?> Cache = new();

    /// <summary>Forgets every line laid out so far: after a change in how formulas are written (formulaSymbols).</summary>
    public static void ClearCaches()
    {
        Cache.Clear(); WideCache.Clear(); MathCache.Clear(); PictureCache.Clear(); PictureRowsCache.Clear(); InlineCache.Clear();
    }

    /// <summary>Null when the line shows exactly as written.</summary>
    public static Disp? Get(string line)
    {
        if (line.Length == 0) return null;
        if (Cache.TryGetValue(line, out var hit)) return hit;
        if (Cache.Count > 20000) Cache.Clear();
        var d = Build(line);
        Cache[line] = d;
        return d;
    }

    // ---- character widths ----
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Disp> WideCache = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Disp, Disp> WideOf = new();

    /// <summary>
    /// The same display with a wide character (CJK, "⚫") in two cells, the second a <see cref="Tui.CharWidth.Continuation"/>
    /// mapped to the same source column, combining marks moved into the cell of the character before them
    /// (<see cref="Disp.Marks"/>), and zero-width characters (variation selectors, joiners) left out: one cell
    /// per terminal column, as the caret, the mouse and soft wrap count. <paramref name="d"/> itself (null: the line
    /// as written) when it has none.
    /// </summary>
    public static Disp? FitWidths(Disp? d, string line)
    {
        var text = d?.Text ?? line;
        if (Tui.CharWidth.AllNarrow(text)) return d;
        if (d == null ? WideCache.TryGetValue(line, out var hit) : WideOf.TryGetValue(d, out hit)) return hit;
        var sb = new StringBuilder(text.Length + 8);
        var map = new List<int>(text.Length + 8);
        var over = new List<Tok?>(text.Length + 8);
        var marks = new Dictionary<int, string>();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (Tui.CharWidth.IsZeroWidth(c)) continue;
            if (Tui.CharWidth.IsSkinTone(text, i)) { i++; continue; }
            int src = d != null ? d.Map[i] : i;
            var o = d != null && i < d.Over.Length ? d.Over[i] : null;
            if (Tui.CharWidth.FlagLetter(text, i) is char letter) { sb.Append(letter); map.Add(src); over.Add(o); i++; continue; }
            if (Tui.CharWidth.IsMark(c))
            {
                // on the character before it; one with nothing before it on a dotted circle, as fonts show it
                int at = sb.Length - 1;
                if (at > 0 && sb[at] == Tui.CharWidth.Continuation) at--;
                if (at < 0) { sb.Append('◌'); map.Add(src); over.Add(o); at = 0; }
                marks[at] = marks.GetValueOrDefault(at) + c;
                continue;
            }
            sb.Append(c); map.Add(src); over.Add(o);
            if (Tui.CharWidth.IsWide(c)) { sb.Append(Tui.CharWidth.Continuation); map.Add(src); over.Add(o); }
        }
        var fit = new Disp { Text = sb.ToString(), Map = map.ToArray(), Over = over.ToArray(), SourceLength = d?.SourceLength ?? line.Length };
        if (marks.Count > 0)
        {
            fit.Marks = new string?[fit.Text.Length];
            foreach (var (at, m) in marks) fit.Marks[at] = m;
        }
        if (d == null)
        {
            if (WideCache.Count > 5000) WideCache.Clear();
            WideCache[line] = fit;
        }
        else WideOf.AddOrUpdate(d, fit);
        return fit;
    }

    // ---- display math ($$ ... $$ on separate lines) ----
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Disp> MathCache = new();

    /// <summary>A line of a display-math block rendered as Unicode.</summary>
    public static Disp MathLine(string line)
    {
        if (MathCache.TryGetValue(line, out var hit)) return hit;
        if (MathCache.Count > 5000) MathCache.Clear();
        var text = line.Trim().Length == 0 ? "" : "  " + Latex.ToUnicode(line.Trim());
        var d = new Disp { Text = text, SourceLength = line.Length };
        d.Map = new int[text.Length];
        for (int i = 0; i < text.Length; i++) d.Map[i] = text.Length == 0 ? 0 : Math.Min(line.Length, i * line.Length / Math.Max(1, text.Length));
        d.Over = Enumerable.Repeat<Tok?>(Tok.Math, text.Length).ToArray();
        MathCache[line] = d;
        return d;
    }

    // ---- display math laid out over several rows (the reading view, #49) ----
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> PictureCache = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int, bool), Disp> PictureRowsCache = new();

    /// <summary>A display formula as rows of text, indented as <see cref="MathLine"/> indents (cached: the reading view
    /// asks for it on every frame).</summary>
    public static string[] Picture(string tex)
    {
        if (PictureCache.TryGetValue(tex, out var hit)) return hit;
        if (PictureCache.Count > 2000) { PictureCache.Clear(); PictureRowsCache.Clear(); }
        var box = LatexLayout.Lay(tex);
        var rows = box.Rows.Select(r => "  " + r.TrimEnd()).ToArray();
        PictureCache[tex] = rows;
        return rows;
    }

    /// <summary>The rows of a formula's picture that source line <paramref name="line"/> shows: row
    /// <paramref name="index"/>, or with <paramref name="rest"/> that row and all below it (the block's last line).</summary>
    public static Disp PictureRows(string tex, string line, int index, bool rest)
    {
        if (PictureRowsCache.TryGetValue((tex, index, rest), out var hit) && hit.SourceLength == line.Length) return hit;
        var rows = Picture(tex);
        int width = rows.Max(r => r.Length) + 1;
        var mine = index >= rows.Length ? new[] { "" } : rest ? rows[index..] : new[] { rows[index] };
        var text = string.Concat(mine.Select(r => r.PadRight(width)));
        var d = new Disp { Text = text, SourceLength = line.Length, RowWidth = width };
        d.Map = new int[text.Length];
        for (int i = 0; i < text.Length; i++) d.Map[i] = Math.Min(line.Length, (i % width) * line.Length / width);
        d.Over = Enumerable.Repeat<Tok?>(Tok.Math, text.Length).ToArray();
        PictureRowsCache[(tex, index, rest)] = d;
        return d;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Tui.CellPicture, System.Collections.Concurrent.ConcurrentDictionary<(int, bool, int), Disp>> ImageRowsCache = new();

    /// <summary>As <see cref="PictureRows"/>, for a typeset picture (<see cref="FormulaImages"/>): blank rows as wide
    /// as the picture, which <see cref="Disp.Picture"/> is drawn over.</summary>
    public static Disp ImageRows(Tui.CellPicture pic, string line, int index, bool rest)
    {
        var cache = ImageRowsCache.GetOrCreateValue(pic);
        if (cache.TryGetValue((index, rest, line.Length), out var hit)) return hit;
        int width = Disp.PictureIndent + pic.Cols + 1;
        int rows = index >= pic.Rows ? 1 : rest ? pic.Rows - index : 1;
        var d = new Disp { Text = new string(' ', rows * width), SourceLength = line.Length, RowWidth = width, Picture = index < pic.Rows ? pic : null, PictureRow = index };
        d.Map = new int[d.Text.Length];
        for (int i = 0; i < d.Text.Length; i++) d.Map[i] = Math.Min(line.Length, (i % width) * line.Length / width);
        d.Over = Enumerable.Repeat<Tok?>(Tok.Math, d.Text.Length).ToArray();
        return cache[(index, rest, line.Length)] = d;
    }

    // ---- a line of prose whose inline formulas take more than one row ----
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Disp?> InlineCache = new();

    /// <summary>A line of prose with inline formulas laid out over rows, its words on the formulas' baseline: "∫₁³
    /// (3x^(5/2) + x²)dx" read badly in one row (the user's worked integral, 10 October 2026). Null when every formula
    /// of the line fits in one row anyway (then the one-line form, which wraps).</summary>
    public static Disp? InlinePicture(string line)
    {
        if (!line.Contains('$', StringComparison.Ordinal)) return null;
        if (InlineCache.TryGetValue(line, out var hit)) return hit;
        if (InlineCache.Count > 5000) InlineCache.Clear();
        var d = BuildInline(line);
        InlineCache[line] = d;
        return d;
    }

    static Disp? BuildInline(string s)
    {
        if (Get(s) is not { } shown) return null;
        // the pieces of the shown line: runs of text, and each formula (its Unicode form maps to its opening $)
        var pieces = new List<(LatexLayout.Box Box, int From, int To, int Src)>();
        bool tall = false;
        for (int d = 0; d < shown.Text.Length;)
        {
            int e = d + 1;
            if (shown.Over[d] == Tok.Math && s[shown.Map[d]] == '$')
            {
                int src = shown.Map[d];
                while (e < shown.Text.Length && shown.Map[e] == src && shown.Over[e] == Tok.Math) e++;
                var m = s.AsSpan(src).StartsWith("$$") ? DollarDouble.Match(s, src) : Dollar.Match(s, src);
                if (m.Success && m.Index == src)
                {
                    var box = LatexLayout.Lay(m.Groups[1].Value);
                    tall |= box.Height > 1;
                    pieces.Add((box, d, e, src));
                    d = e;
                    continue;
                }
            }
            while (e < shown.Text.Length && !(shown.Over[e] == Tok.Math && s[shown.Map[e]] == '$')) e++;
            pieces.Add((LatexLayout.Box.Text(shown.Text[d..e]), d, e, -1));
            d = e;
        }
        if (!tall) return null;

        int asc = pieces.Max(p => p.Box.Ascent), desc = pieces.Max(p => p.Box.Descent), height = asc + desc + 1;
        int width = pieces.Sum(p => p.Box.Width) + 1;
        var text = new char[height * width];
        var map = new int[height * width];
        var over = new Tok?[height * width];
        Array.Fill(text, ' ');
        Array.Fill(map, s.Length - 1);                          // the padding at the right: a real column, for its colour
        int x = 0;
        foreach (var (box, from, _, src) in pieces)
        {
            for (int c = 0; c < box.Width; c++)
            {
                // a text column maps to its source character on every row, so a click above a word lands on it
                int at = src >= 0 ? src : shown.Map[from + c];
                for (int r = 0; r < height; r++)
                {
                    int i = r * width + x + c, br = r - (asc - box.Base);
                    map[i] = at;
                    if (br < 0 || br >= box.Height) continue;
                    text[i] = box.Rows[br][c];
                    over[i] = src >= 0 ? Tok.Math : shown.Over[from + c];
                }
            }
            x += box.Width;
        }
        return new Disp { Text = new string(text), Map = map, Over = over, SourceLength = s.Length, RowWidth = width };
    }

    /// <summary>A line that shows nothing (the $$ delimiter lines).</summary>
    public static Disp Empty(string line) => new() { Text = "", SourceLength = line.Length };

    static Disp? Build(string s)
    {
        if (Hr.IsMatch(s))
        {
            var rule = new Disp { Text = new string('─', HrWidth), SourceLength = s.Length };
            rule.Map = Enumerable.Range(0, HrWidth).Select(i => Math.Min(i, s.Length - 1)).ToArray();
            rule.Over = Enumerable.Repeat<Tok?>(Tok.Comment, HrWidth).ToArray();
            return rule;
        }

        var hide = new bool[s.Length];
        var repl = new string?[s.Length];
        var replTok = new Tok?[s.Length];
        var prot = new bool[s.Length];

        void Hide(int from, int len) { for (int i = from; i < from + len && i < s.Length; i++) hide[i] = true; }
        bool Protected(Match m) { for (int i = m.Index; i < m.Index + m.Length; i++) if (prot[i]) return true; return false; }

        // inline code first: its contents must stay untouched
        foreach (Match m in InlineCode.Matches(s))
        {
            hide[m.Index] = true; hide[m.Index + m.Length - 1] = true;
            for (int i = m.Index; i < m.Index + m.Length; i++) prot[i] = true;
        }

        // math: the whole $...$ / $$...$$ span is replaced by its Unicode rendering and shielded from the other rules
        foreach (var rx in new[] { DollarDouble, Dollar })
            foreach (Match m in rx.Matches(s))
            {
                if (Protected(m)) continue;
                for (int i = m.Index; i < m.Index + m.Length; i++) { hide[i] = true; prot[i] = true; }
                hide[m.Index] = false;
                repl[m.Index] = Latex.ToUnicode(m.Groups[1].Value); replTok[m.Index] = Tok.Math;
            }

        // line prefixes
        Match p;
        if ((p = Head.Match(s)).Success) { Hide(p.Groups[2].Index, p.Groups[2].Length); Hide(p.Groups[3].Index, p.Groups[3].Length); }
        else if ((p = Quote.Match(s)).Success)
        {
            for (int i = p.Groups[2].Index; i < p.Groups[2].Index + p.Groups[2].Length; i++)
                if (s[i] == '>') { repl[i] = "│"; replTok[i] = Tok.Comment; }
        }
        else if ((p = Task.Match(s)).Success)
        {
            int mark = p.Groups[2].Index;
            repl[mark] = p.Groups[4].Value == " " ? "☐" : "☑";
            replTok[mark] = Tok.Keyword;
            Hide(p.Groups[3].Index, p.Groups[3].Length);                 // space after '-'
            Hide(p.Groups[4].Index - 1, 3);                              // [ ]
        }
        else if ((p = Bullet.Match(s)).Success)
        {
            repl[p.Groups[2].Index] = "•"; replTok[p.Groups[2].Index] = Tok.Keyword;
        }

        // inline markup (skipping anything inside code spans)
        foreach (Match m in Image.Matches(s))
        {
            if (Protected(m)) continue;
            repl[m.Index] = "▣ "; replTok[m.Index] = Tok.Keyword;
            Hide(m.Index + 1, 1);
            Hide(m.Index + 2 + m.Groups[1].Length, m.Length - 2 - m.Groups[1].Length);
        }
        foreach (Match m in Link.Matches(s))
        {
            if (Protected(m) || (m.Index > 0 && s[m.Index - 1] == '!')) continue;
            Hide(m.Index, 1);
            Hide(m.Index + 1 + m.Groups[1].Length, m.Length - 1 - m.Groups[1].Length);
        }
        foreach (Match m in Wiki.Matches(s))
        {
            if (Protected(m)) continue;
            Hide(m.Index, 2); Hide(m.Groups[1].Index, m.Groups[1].Length); Hide(m.Index + m.Length - 2, 2);
        }
        foreach (Match m in Bold.Matches(s))
        {
            if (Protected(m)) continue;
            int k = m.Groups[1].Length; Hide(m.Index, k); Hide(m.Index + m.Length - k, k);
        }
        foreach (Match m in Strike.Matches(s)) { if (!Protected(m)) { Hide(m.Index, 2); Hide(m.Index + m.Length - 2, 2); } }
        foreach (Match m in Italic.Matches(s)) { if (!Protected(m)) { Hide(m.Index, 1); Hide(m.Index + m.Length - 1, 1); } }
        foreach (Match m in Escape.Matches(s)) { if (!Protected(m) && !hide[m.Index]) hide[m.Index] = true; }

        var text = new StringBuilder(); var map = new List<int>(); var over = new List<Tok?>();
        bool changed = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (hide[i]) { changed = true; continue; }
            if (repl[i] != null)
            {
                changed = true;
                foreach (var c in repl[i]!) { text.Append(c); map.Add(i); over.Add(replTok[i]); }
                continue;
            }
            text.Append(s[i]); map.Add(i); over.Add(null);
        }
        if (!changed) return null;
        return new Disp { Text = text.ToString(), Map = map.ToArray(), Over = over.ToArray(), SourceLength = s.Length };
    }
}
