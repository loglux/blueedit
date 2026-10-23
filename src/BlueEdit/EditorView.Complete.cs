// Word completion support: the word being typed and putting the chosen word in its place.
partial class EditorView
{
    static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The part of a word just before the caret (where Ctrl+Space was pressed), or null when the caret does not
    /// follow a word. Start is its column; a word starts with a letter or underscore.</summary>
    public (int Start, string Prefix)? WordBeforeCaret()
    {
        var l = Lines[Row];
        int s = Math.Min(Col, l.Length);
        while (true)
        {
            while (s > 0 && IsIdentChar(l[s - 1])) s--;
            // PowerShell: "Get-Ch" is one word; carry on past a hyphen that follows a letter
            if (HyphenWords && s > 1 && l[s - 1] == '-' && char.IsLetter(l[s - 2])) { s--; continue; }
            break;
        }
        while (s < Col && char.IsDigit(l[s])) s++;                   // "2abc": the word is "abc"
        return s < Col ? (s, l[s..Col]) : null;
    }

    /// <summary>The file's language writes some words with hyphens (PowerShell's Verb-Noun commands).</summary>
    public bool HyphenWords => _lang?.HyphenWords == true;

    /// <summary>Keywords and standard words (cmdlets, built-ins) of the file's language, for completion.</summary>
    public IEnumerable<string> LanguageKeywords => _lang == null ? Enumerable.Empty<string>() : _lang.Keywords.Concat(_lang.Words);

    /// <summary>Replaces the word part before the caret with <paramref name="word"/>; one undo step.</summary>
    public void CompleteWith(string word)
    {
        if (ReadOnlyView) return;
        var (start, prefix) = WordBeforeCaret() ?? (Col, "");             // right after a dot: nothing typed yet
        Snapshot("complete"); _lastKind = null;
        Lines[Row] = Lines[Row].Remove(start, prefix.Length).Insert(start, word);
        Col = start + word.Length; _anchor = null;
        Touch(true);
    }

    /// <summary>The completion list opening by itself while typing: "code" (in code files), "always", "off".</summary>
    public string CompleteWhileTyping { get; set; } = "code";

    /// <summary>A letter, digit or underscore was typed into a word, or a dot (the list may open by itself), or ( , )
    /// (a function's parameters may be shown).</summary>
    public event Action<char>? WordTyped;

    /// <summary>The list may open by itself here: the setting allows it for this file, the caret ends a word of at
    /// least <paramref name="minChars"/> characters, outside strings and comments.</summary>
    public bool AutoCompleteHere(int minChars)
    {
        if (CompleteWhileTyping == "off" || ReadOnlyView || ColumnMode || HasSelection) return false;
        if (CompleteWhileTyping != "always" && IsProseFile) return false;
        if (WordBeforeCaret() is not { } w || w.Prefix.Length < minChars) return false;
        if (Col < Lines[Row].Length && IsIdentChar(Lines[Row][Col])) return false;      // in the middle of a word
        return !InStringOrComment();
    }

    /// <summary>Right after a dot in code, outside strings and comments: a language server may list members.</summary>
    public bool AutoCompleteAfterDot() =>
        CompleteWhileTyping != "off" && !ReadOnlyView && !ColumnMode && !HasSelection && Col > 0 && Lines[Row][Col - 1] == '.'
        && (CompleteWhileTyping == "always" || !IsProseFile) && !InStringOrComment();

    /// <summary>The lines of a parked (not active) document, for collecting completion words from it.</summary>
    public static IReadOnlyList<string> LinesOf(Doc d) => d.Lines;
}
