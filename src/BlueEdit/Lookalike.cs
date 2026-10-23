/// <summary>
/// Words typed partly in the wrong keyboard layout: "рrивет", "Cобака" — a Latin letter that looks just like a
/// Cyrillic one (or the other way round). They read correctly, so they must be pointed out, and can be repaired by
/// turning the odd letters into their lookalikes in the word's main alphabet.
/// </summary>
static class Lookalike
{
    // pairs that look the same, position for position (lower-case в, н, м, т have no Latin twin)
    const string Cyrillic = "аАВсСеЕНкКМоОрРТхХуі";
    const string Latin    = "aABcCeEHkKMoOpPTxXyi";

    static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';
    static bool IsLatin(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    /// <summary>Both Cyrillic and Latin letters in one word.</summary>
    public static bool IsMixed(string word)
    {
        bool cyr = false, lat = false;
        foreach (char c in word) { if (IsCyrillic(c)) cyr = true; else if (IsLatin(c)) lat = true; }
        return cyr && lat;
    }

    /// <summary>The word in one alphabet (the one most of its letters are in), or null when a letter of the other
    /// alphabet has no lookalike (then it is not a layout slip but a real mix).</summary>
    public static string? Fix(string word)
    {
        int cyr = word.Count(IsCyrillic), lat = word.Count(IsLatin);
        bool toCyrillic = cyr >= lat;
        var chars = word.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (toCyrillic && IsLatin(c))
            {
                int k = Latin.IndexOf(c, StringComparison.Ordinal);
                if (k < 0) return null;
                chars[i] = Cyrillic[k];
            }
            else if (!toCyrillic && IsCyrillic(c))
            {
                int k = Cyrillic.IndexOf(c, StringComparison.Ordinal);
                if (k < 0) return null;
                chars[i] = Latin[k];
            }
        }
        return new string(chars);
    }
}
