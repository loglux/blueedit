// The language of each paragraph for the grammar checkers and the style model (ParagraphLanguage.cs): among the
// languages the document states in its front matter, else the spelling languages. When two of those share a script
// (English and German), a short line takes the document's main language, found from its longer paragraphs; that
// pass is made only then, and not while typing: Russian and English alone are told apart by their letters.
partial class EditorView
{
    (List<string> Lines, int Version, string Listed, List<string> Found)? _documentLanguages;
    readonly Dictionary<string, string> _languageGuesses = new();
    const int MostParagraphs = 400;                  // enough to tell a document's languages, as the plugin found

    /// <summary>The language to check a paragraph as: <paramref name="text"/> is its text with formulas and markup
    /// blanked out.</summary>
    internal string LanguageOf(string text)
    {
        var stated = StatedLanguages();
        IReadOnlyList<string> listed = stated.Count > 0 ? stated : GrammarLanguages;
        IReadOnlyList<string> document = stated.Count > 0 ? stated
            : ParagraphLanguage.NeedsDocument(listed) ? DocumentLanguages(listed) : Array.Empty<string>();
        return ParagraphLanguage.Choose(text, listed, document);
    }

    /// <summary>The languages the document states ("language: de" in a Markdown front matter), spelled as the
    /// spelling languages spell them when one of those is the same language ("de" → "de-DE").</summary>
    internal List<string> StatedLanguages()
    {
        if (!IsMarkdown) return new();
        return ParagraphLanguage.Stated(Lines).Select(s =>
            GrammarLanguages.FirstOrDefault(g => g.Equals(s, StringComparison.OrdinalIgnoreCase)
                || (!s.Contains('-', StringComparison.Ordinal) && g.StartsWith(s + "-", StringComparison.OrdinalIgnoreCase))) ?? s).Distinct().ToList();
    }

    /// <summary>The document's languages from its longer paragraphs, most used first (ParagraphLanguage.DocumentLanguages).
    /// Found again after an edit once typing has paused; only the paragraphs that changed are guessed again.</summary>
    internal List<string> DocumentLanguages(IReadOnlyList<string> listed)
    {
        var key = string.Join(',', listed);
        if (_documentLanguages is { } d && d.Listed == key && ReferenceEquals(d.Lines, Lines)
            && (d.Version == EditVersion || Environment.TickCount64 - _lastEditTicks < GrammarQuietMs)) return d.Found;
        var previous = _documentLanguages is { } p && p.Listed == key && ReferenceEquals(p.Lines, Lines) ? p.Found : null;
        if (_languageGuesses.Count > 5000) _languageGuesses.Clear();
        bool ready = ParagraphLanguage.Ready;
        var chosen = new List<(string, string)>();
        foreach (var text in ProseBlocks().Take(MostParagraphs))
        {
            var guessKey = key + "\n" + text;
            if (!_languageGuesses.TryGetValue(guessKey, out var language))
            {
                language = ParagraphLanguage.Choose(text, listed);
                if (ready) _languageGuesses[guessKey] = language;
            }
            chosen.Add((text, language));
        }
        var found = ParagraphLanguage.DocumentLanguages(chosen, previous);
        // before the model is loaded nothing is found from the text: found again once it is
        if (ready) _documentLanguages = (Lines, EditVersion, key, found);
        return found;
    }

    // The document's runs of prose lines, markup and formulas blanked out
    IEnumerable<string> ProseBlocks()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i <= Lines.Count; i++)
        {
            if (i < Lines.Count && ProseLine(i) && !IsHeadingLine(i))
            {
                var chars = Prose.Plain(Lines[i], IsMarkdown, IsLatex).ToCharArray();
                foreach (var (s, n) in MathIn(i)) Array.Fill(chars, ' ', s, n);
                sb.Append(chars).Append(' ');
                continue;
            }
            if (sb.Length > 0) yield return sb.ToString();
            sb.Clear();
        }
    }
}
