// Auto-closing brackets and quotes: typing an opener adds its closer, typing the closer steps over it, Backspace
// between an empty pair removes both, and an opener typed over a selection on one line wraps it.
partial class EditorView
{
    /// <summary>"code" (default): in code files; "always": also in prose (brackets and double quotes only, not the
    /// apostrophe); "off".</summary>
    public string AutoClose { get; set; } = "code";

    static readonly Dictionary<char, char> PairOf = new() { ['('] = ')', ['['] = ']', ['{'] = '}', ['"'] = '"', ['\''] = '\'', ['`'] = '`' };

    internal bool IsProseFile => _lang == null || _lang.Name is "Markdown" or "LaTeX";

    bool PairsOn => !ReadOnlyView && !ColumnMode && AutoClose switch { "off" => false, "always" => true, _ => !IsProseFile };

    // Inside a string or a comment: the character before the caret is one, and the caret is not just past its end.
    bool InStringOrComment()
    {
        if (Col == 0) return false;
        var kinds = TokensFor(Row);
        if (Col - 1 >= kinds.Length) return false;
        var k = kinds[Col - 1];
        if (k is not (Tok.String or Tok.Comment)) return false;
        return Col >= kinds.Length ? k == Tok.Comment : kinds[Col] == k;
    }

    /// <summary>Typing <paramref name="ch"/> with pairing in mind; false when it is an ordinary character here.</summary>
    bool TypePaired(char ch)
    {
        if (!PairsOn) return false;
        var line = Lines[Row];
        char next = Col < line.Length ? line[Col] : '\0';
        char prev = Col > 0 ? line[Col - 1] : '\0';
        bool quote = ch is '"' or '\'' or '`';
        if (IsProseFile && ch is '\'' or '`') return false;     // apostrophes, and Markdown's own backticks

        // the closer is already there (typed in pairs before): step over it
        if (!HasSelection && next == ch && (ch is ')' or ']' or '}' || quote))
        {
            _anchor = null; Col++; Touch(); return true;
        }
        if (!PairOf.TryGetValue(ch, out var close)) return false;

        if (HasSelection)
        {
            var (s, e) = Order(_anchor!.Value, (Row, Col));
            if (s.Item1 != e.Item1) return false;                // several lines: typing replaces, as usual
            Snapshot("wrap"); _lastKind = null;
            var l = Lines[s.Item1];
            Lines[s.Item1] = l[..s.Item2] + ch + l[s.Item2..e.Item2] + close + l[e.Item2..];
            _anchor = (s.Item1, s.Item2 + 1); Row = s.Item1; Col = e.Item2 + 1;   // the wrapped text stays selected
            Touch(true);
            return true;
        }

        if (quote && (char.IsLetterOrDigit(prev) || prev == ch || InStringOrComment())) return false;   // don't, "a", inside a string
        if (!quote && InStringOrComment()) return false;
        // only before nothing, a space or a closing character: before a word the opener is likely meant for it
        if (next != '\0' && !char.IsWhiteSpace(next) && ")]}>,;:.".IndexOf(next) < 0) return false;

        Snapshot("type");
        Lines[Row] = line.Insert(Col, $"{ch}{close}"); Col++;
        Touch(true);
        return true;
    }

    /// <summary>Backspace between an empty pair: both go. False when it is an ordinary Backspace.</summary>
    bool BackspacePair()
    {
        if (!PairsOn || HasSelection || Col == 0) return false;
        var line = Lines[Row];
        if (Col >= line.Length || !PairOf.TryGetValue(line[Col - 1], out var close) || line[Col] != close) return false;
        if (IsProseFile && line[Col - 1] is '\'' or '`') return false;
        Snapshot("back");
        Lines[Row] = line.Remove(Col - 1, 2); Col--;
        Touch(true);
        return true;
    }
}
