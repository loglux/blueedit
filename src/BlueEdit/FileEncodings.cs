using System.Text;

/// <summary>
/// The encodings a file can be read and written in, and the guess made when a file is opened.
/// BOM → that Unicode form; valid UTF-8 → UTF-8; otherwise the Cyrillic code page (Windows 1251 or DOS 866) whose
/// decoding looks more like real text.
/// </summary>
static class FileEncodings
{
    static FileEncodings() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public sealed record Choice(string Name, string Label, Func<Encoding> Create);

    /// <summary>What File > Reopen with encoding and File > Convert encoding offer, in this order.</summary>
    public static readonly IReadOnlyList<Choice> All = new Choice[]
    {
        new("UTF-8", "UTF-8", () => new UTF8Encoding(false)),
        new("UTF-8 BOM", "UTF-8 with BOM", () => new UTF8Encoding(true)),
        new("UTF-16 LE", "UTF-16 LE (Windows \"Unicode\")", () => new UnicodeEncoding(false, true)),
        new("UTF-16 BE", "UTF-16 BE", () => new UnicodeEncoding(true, true)),
        new("cp1251", "Windows-1251  Cyrillic", () => CodePage(1251)),
        new("cp866", "DOS 866       Cyrillic, DOS pseudographics", () => CodePage(866)),
        new("KOI8-R", "KOI8-R        Cyrillic, old Unix mail", () => CodePage(20866)),
        new("cp1252", "Windows-1252  Western European", () => CodePage(1252)),
        new("cp437", "DOS 437       US, DOS pseudographics", () => CodePage(437)),
    };

    public static Encoding CodePage(int cp) => Encoding.GetEncoding(cp);

    /// <summary>Short name for the status line, e.g. "UTF-8 BOM" or "cp866".</summary>
    public static string ShortName(Encoding e) => e switch
    {
        UTF8Encoding u => u.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8",
        UnicodeEncoding => e.CodePage == 1201 ? "UTF-16 BE" : "UTF-16 LE",
        _ => e.CodePage switch { 20866 => "KOI8-R", var cp => "cp" + cp.ToString(System.Globalization.CultureInfo.InvariantCulture) },
    };

    /// <summary>The encoding <see cref="ShortName"/> named, or null for a name it never gives.</summary>
    public static Encoding? FromShortName(string name)
    {
        if (All.FirstOrDefault(c => c.Name == name) is { } c) return c.Create();
        if (name.StartsWith("cp", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int cp))
        {
            try { return CodePage(cp); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        }
        return null;
    }

    /// <summary>Index in <see cref="All"/> of the entry that matches the encoding, or -1.</summary>
    public static int IndexOf(Encoding e) => All.ToList().FindIndex(c => c.Name == ShortName(e));

    /// <summary>Reads a file's bytes the way they are most likely meant.</summary>
    public static (string Text, Encoding Enc) Detect(byte[] b)
    {
        if (StartsWith(b, 0xEF, 0xBB, 0xBF)) return Decode(b, new UTF8Encoding(true));
        if (StartsWith(b, 0xFF, 0xFE)) return Decode(b, new UnicodeEncoding(false, true));
        if (StartsWith(b, 0xFE, 0xFF)) return Decode(b, new UnicodeEncoding(true, true));
        try { return (new UTF8Encoding(false, true).GetString(b), new UTF8Encoding(false)); }
        catch (DecoderFallbackException) { }
        // Not UTF-8: a legacy Cyrillic file. Decode a sample both ways and keep the one that reads like text.
        var sample = b.AsSpan(0, Math.Min(b.Length, 64 * 1024));
        var win = CodePage(1251);
        var dos = CodePage(866);
        var enc = TextScore(dos.GetString(sample)) > TextScore(win.GetString(sample)) ? dos : win;
        return (enc.GetString(b), enc);
    }

    /// <summary>Decodes with the given encoding; a BOM of that encoding at the start is skipped, and a UTF-8 BOM
    /// is kept as part of the encoding so saving writes it back.</summary>
    public static (string Text, Encoding Enc) Decode(byte[] b, Encoding enc)
    {
        if (enc is UTF8Encoding && StartsWith(b, 0xEF, 0xBB, 0xBF))
            return (new UTF8Encoding(true).GetString(b, 3, b.Length - 3), new UTF8Encoding(true));
        if (enc is UTF8Encoding) return (new UTF8Encoding(false).GetString(b), enc);
        var pre = enc.GetPreamble();
        int skip = pre.Length > 0 && b.AsSpan().StartsWith(pre) ? pre.Length : 0;
        return (enc.GetString(b, skip, b.Length - skip), enc);
    }

    /// <summary>
    /// How much a decoding looks like Cyrillic text. Counts pairs of neighbouring Cyrillic letters of the same case
    /// (real words: "ст", "ПР"; the wrong code page mixes cases and punctuation) and box-drawing characters
    /// (DOS pseudographics, only meaningful when the file really is cp866). Runs of one repeated letter do not count:
    /// a cp866 line "════" read as cp1251 is "НННН".
    /// </summary>
    internal static int TextScore(string s)
    {
        int score = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is >= '─' and <= '▟') { score++; continue; }
            if (i == 0 || !IsCyr(c) || !IsCyr(s[i - 1]) || c == s[i - 1]) continue;
            if (char.IsLower(c) == char.IsLower(s[i - 1])) score += 2;
        }
        return score;
    }

    static bool IsCyr(char c) => c is >= 'А' and <= 'я';

    static bool StartsWith(byte[] b, params byte[] prefix) => b.AsSpan().StartsWith(prefix);

    /// <summary>Positions (line, column) of characters the encoding cannot store, at most <paramref name="max"/>.
    /// Saving would turn them into '?'.</summary>
    public static List<(int Row, int Col)> Unencodable(IReadOnlyList<string> lines, Encoding enc, int max = 1000)
    {
        var found = new List<(int, int)>();
        if (enc is UTF8Encoding or UnicodeEncoding) return found;
        var strict = Encoding.GetEncoding(enc.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        for (int r = 0; r < lines.Count && found.Count < max; r++)
        {
            if (Fits(strict, lines[r])) continue;
            var line = lines[r];
            for (int c = 0; c < line.Length && found.Count < max; c++)
            {
                int len = char.IsHighSurrogate(line[c]) && c + 1 < line.Length ? 2 : 1;
                if (!Fits(strict, line.Substring(c, len))) found.Add((r, c));
                c += len - 1;
            }
        }
        return found;
    }

    static bool Fits(Encoding strict, string s)
    {
        try { strict.GetByteCount(s); return true; }
        catch (EncoderFallbackException) { return false; }
    }
}
