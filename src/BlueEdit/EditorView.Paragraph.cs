using System.Text;
using System.Text.RegularExpressions;

/// <summary>How <see cref="EditorView.Align"/> lines up a paragraph.</summary>
enum Alignment { Left, Right, Centre, Justify }

/// <summary>
/// Paragraph formatting, as DOS editors had it (#91): reformat a paragraph to the right margin (join its lines, break
/// them again at words), align it left, right, centred or justified, and wrap while typing at the margin. Plain text
/// and Markdown prose; centring and justifying only in plain text (in Markdown the extra spaces vanish when shown and
/// stay as clutter in the source); never in code. Each command is one undo step.
/// </summary>
sealed partial class EditorView
{
    /// <summary>The right margin: the widest a reformatted, aligned or auto-wrapped line gets (setting "rightMargin").</summary>
    public int RightMargin { get; set; } = 78;

    /// <summary>Wrap while typing: a word typed past the margin goes to a new line (setting "autoWrap").</summary>
    public bool AutoWrap { get; set; }

    // a line's prefix: indentation, Markdown quote marks ("> > "), a list marker ("- ", "1. ") with its spaces
    static readonly Regex PrefixRx = new(@"^(\s*(?:>\s?)*)((?:[-*+]|\d+[.)])\s+)?", RegexOptions.Compiled);

    /// <summary>Why paragraph formatting does not apply here, or null when it does.</summary>
    string? FormatRefusal(bool shapes)
    {
        if (ReadOnlyView) return Ui.T("Read-only");
        if (_lang != null && !IsMarkdown) return Ui.T("Paragraph formatting is for text and Markdown, not code");
        if (shapes && IsMarkdown) return Ui.T("Right, centre and justify are for plain text: Markdown drops the spaces when shown");
        return null;
    }

    // The paragraph at a line: neighbouring prose lines up to a blank line; in Markdown a heading or a list item
    // starts a paragraph of its own, and code blocks, tables and formulas on their own lines are not prose.
    (int First, int Last)? FormatParagraph(int li)
    {
        bool Prose(int i) => Lines[i].Trim().Length > 0 && (!IsMarkdown || (ProseLine(i) && !IsHeadingLine(i)));
        if (!Prose(li)) return null;
        int first = li, last = li;
        while (first > 0 && Prose(first - 1) && !StartsParagraph(first)) first--;
        while (last + 1 < Lines.Count && Prose(last + 1) && !StartsParagraph(last + 1)) last++;
        return (first, last);
    }

    // The paragraphs to work on: each one in the selected lines (a selection ending at a line's start leaves that line
    // out), otherwise the one at the caret; last first, so earlier line numbers stay right while later ones change.
    List<(int First, int Last)> FormatRanges()
    {
        var res = new List<(int First, int Last)>();
        if (HasSelection && _anchor!.Value.Row != Row)
        {
            var (a, b) = _anchor.Value.Row < Row ? (_anchor.Value, (Row, Col)) : ((Row, Col), _anchor.Value);
            int top = a.Row, bottom = b.Col == 0 ? b.Item1 - 1 : b.Item1;
            for (int i = top; i <= bottom; i++)
                if (FormatParagraph(i) is var (f, l)) { res.Add((Math.Max(f, top), Math.Min(l, bottom))); i = l; }
        }
        else if (FormatParagraph(Row) is { } p) res.Add(p);
        res.Reverse();
        return res;
    }

    /// <summary>The words of a text: runs between spaces, but an inline formula ($…$) or code (`…`) is one word,
    /// spaces and all — a line break inside one would break it.</summary>
    internal static List<string> Words(string text)
    {
        var words = new List<string>();
        var w = new StringBuilder();
        char? inside = null;
        foreach (var c in text)
        {
            if (inside == null && char.IsWhiteSpace(c)) { if (w.Length > 0) { words.Add(w.ToString()); w.Clear(); } continue; }
            if (c is '$' or '`') inside = inside == c ? null : inside ?? c;
            w.Append(c);
        }
        if (w.Length > 0) words.Add(w.ToString());
        return words;
    }

    /// <summary>Words filled into lines of at most <paramref name="width"/> characters (a longer word gets a line of
    /// its own); the first line starts with <paramref name="first"/>, the others with <paramref name="rest"/>.</summary>
    internal static List<string> Fill(IReadOnlyList<string> words, int width, string first, string rest)
    {
        var lines = new List<string>();
        var line = new StringBuilder(first);
        bool empty = true;
        foreach (var word in words)
        {
            if (!empty && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear().Append(rest);
                empty = true;
            }
            if (!empty) line.Append(' ');
            line.Append(word);
            empty = false;
        }
        lines.Add(line.ToString());
        return lines;
    }

    /// <summary>A line stretched to <paramref name="width"/>: the spaces between its words widened, the extra ones
    /// given to the gaps from the left. Its prefix (indentation) stays as it is.</summary>
    internal static string Justify(string line, int width, int prefixLength)
    {
        var prefix = line[..prefixLength];
        var words = Words(line[prefixLength..]);
        int gaps = words.Count - 1, letters = words.Sum(w => w.Length);
        int room = width - prefixLength - letters;
        if (gaps <= 0 || room <= gaps) return line;
        var sb = new StringBuilder(prefix);
        for (int i = 0; i < words.Count; i++)
        {
            sb.Append(words[i]);
            if (i < gaps) sb.Append(' ', room / gaps + (i < room % gaps ? 1 : 0));
        }
        return sb.ToString();
    }

    // the prefixes of a paragraph: the first line's own, and the one its next lines take (a list item's text column)
    (string First, string Next) Prefixes(int first, int last)
    {
        var m = PrefixRx.Match(Lines[first]);
        string own = m.Value;
        string rest = m.Groups[2].Success ? m.Groups[1].Value + new string(' ', m.Groups[2].Length) : m.Groups[1].Value;
        if (last > first && !m.Groups[2].Success) rest = PrefixRx.Match(Lines[first + 1]).Groups[1].Value;
        return (own, rest);
    }

    /// <summary>Reformats the paragraph at the caret (or the selected lines) to <see cref="RightMargin"/>; with
    /// <paramref name="justify"/>, also stretches every line but the last to the margin.</summary>
    public string Reformat(bool justify = false) =>
        Each(justify, "reformat", (first, last) =>
        {
            var (own, rest) = Prefixes(first, last);
            var text = string.Join(' ', Enumerable.Range(first, last - first + 1)
                .Select(i => i == first ? Lines[i][own.Length..] : Lines[i][PrefixRx.Match(Lines[i]).Groups[1].Length..]));
            var lines = Fill(Words(text), Math.Max(10, RightMargin), own, rest);
            if (justify)
                for (int i = 0; i < lines.Count - 1; i++) lines[i] = Justify(lines[i], RightMargin, i == 0 ? own.Length : rest.Length);
            return lines;
        }, justify ? Ui.T("Justified") : Ui.T("Reformatted"));

    /// <summary>Aligns the paragraph at the caret (or those in the selected lines): left, right or centred keep the
    /// line breaks; justify reformats first.</summary>
    public string Align(Alignment how) => how == Alignment.Justify ? Reformat(justify: true)
        : Each(how != Alignment.Left, "align", (first, last) => Enumerable.Range(first, last - first + 1).Select(i =>
        {
            var t = Lines[i].Trim();
            int pad = how == Alignment.Right ? RightMargin - t.Length : how == Alignment.Centre ? (RightMargin - t.Length) / 2 : 0;
            return new string(' ', Math.Max(0, pad)) + t;
        }).ToList(), how switch { Alignment.Right => Ui.T("Aligned right"), Alignment.Centre => Ui.T("Centred"), _ => Ui.T("Aligned left") });

    // Puts the new lines of every paragraph to work on in place, as one undo step; the caret ends at the end of the
    // first one. Returns the status line's note.
    string Each(bool shapes, string kind, Func<int, int, List<string>> make, string done)
    {
        if (FormatRefusal(shapes) is { } why) return why;
        var ranges = FormatRanges();
        if (ranges.Count == 0) return Ui.T("Put the caret in a paragraph of text");
        Snapshot(kind); _lastKind = null;
        foreach (var (first, last) in ranges)                 // last paragraph first: earlier rows stay valid
        {
            var lines = make(first, last);
            Lines.RemoveRange(first, last - first + 1);
            Lines.InsertRange(first, lines);
            Row = first + lines.Count - 1;
            Col = Lines[Row].Length;
        }
        _anchor = null;
        Touch(true);
        return ranges.Count == 1 ? done : Ui.N(ranges.Count, "{1}: {0} paragraph", "{1}: {0} paragraphs", done);
    }

    // After a character is typed: past the margin, the word being typed (and what follows it) goes to a new line
    // with the paragraph's continuation prefix. Part of the same undo step as the typing.
    void WrapWhileTyping()
    {
        if (!AutoWrap || ColumnMode || Col <= RightMargin || (_lang != null && !IsMarkdown) || (IsMarkdown && !ProseLine(Row))) return;
        var line = Lines[Row];
        var m = PrefixRx.Match(line);
        int start = m.Length;
        // the last space at or before the margin, after the prefix; none (one long word): no wrap
        int cut = line.LastIndexOf(' ', Math.Min(RightMargin, line.Length - 1));
        if (cut < start) return;
        while (cut > start && line[cut - 1] == ' ') cut--;
        int next = cut;
        while (next < line.Length && line[next] == ' ') next++;
        if (next >= Col) return;                                  // the caret is in the spaces: nothing typed past them yet
        string rest = m.Groups[2].Success ? m.Groups[1].Value + new string(' ', m.Groups[2].Length) : m.Groups[1].Value;
        Lines[Row] = line[..cut];
        Lines.Insert(Row + 1, rest + line[next..]);
        Col = rest.Length + (Col - next);
        Row++;
    }
}
