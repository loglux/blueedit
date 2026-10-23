using System.Text.RegularExpressions;

/// <summary>Counts for File > Info (#100). <see cref="ProseWords"/>: in Markdown, the words of the text itself — no
/// markup, formulas or code, as a word processor would count an essay; elsewhere the same as <see cref="Words"/>.</summary>
sealed record TextStats(int Lines, int Paragraphs, int Words, int Chars, int CharsNoSpaces, int ProseWords, int Formulas, int CodeLines);

sealed partial class EditorView
{
    // a word: a run without spaces that has a letter or a digit in it ("—", "*", "-" alone are not words)
    static readonly Regex WordTokenRx = new(@"[^\s]*[\p{L}\p{N}][^\s]*", RegexOptions.Compiled);

    internal static int CountWords(string text) => WordTokenRx.Count(text);

    /// <summary>The counts of the file, or (with <paramref name="selection"/>) of the selected lines.</summary>
    public TextStats Stats(bool selection = false)
    {
        int first = 0, last = Lines.Count - 1;
        IReadOnlyList<string> lines = Lines;
        if (selection && HasSelection)
        {
            lines = SelectedText();
            first = Math.Min(_anchor!.Value.Row, Row);
            last = first + lines.Count - 1;
        }
        // a final empty line (the file ends with a line break) is not a line of text
        int count = lines.Count > 0 && lines[^1].Length == 0 && lines.Count > 1 ? lines.Count - 1 : lines.Count;
        int paragraphs = 0, words = 0, chars = 0, noSpaces = 0;
        bool inPara = false;
        foreach (var l in lines)
        {
            bool blank = l.Trim().Length == 0;
            if (!blank && !inPara) paragraphs++;
            inPara = !blank;
            words += CountWords(l);
            chars += l.Length;
            noSpaces += l.Count(c => !char.IsWhiteSpace(c));
        }
        chars += Math.Max(0, count - 1);                                  // the line breaks between the lines
        if (!IsMarkdown) return new TextStats(count, paragraphs, words, chars, noSpaces, words, 0, 0);

        // Markdown: the words of prose lines with markup and formulas blanked; formulas and code lines counted apart
        EnsureFences();
        int prose = 0, formulas = 0, code = 0;
        last = Math.Min(last, Lines.Count - 1);
        for (int i = first; i <= last; i++)
        {
            if (ProseLine(i)) prose += CountWords(ParagraphText(i, i, MathView.Spaces));
            else if (_states![i] != 0 && !InMathBlock(_states[i]) && Lines[i].Trim().Length > 0
                     && !Lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)) code++;
            formulas += MathIn(i).Count(m => !(InMathBlock(_states![i]) && m.Start == 0));   // a display block once
        }
        return new TextStats(count, paragraphs, words, chars, noSpaces, prose, formulas, code);
    }
}
