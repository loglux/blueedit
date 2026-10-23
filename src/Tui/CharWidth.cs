namespace Tui;

/// <summary>
/// How many terminal columns a UTF-16 character takes. Most take one; East Asian wide and full-width characters and
/// the emoji shown as pictures by default take two (the terminal moves its cursor two columns after them); variation
/// selectors and zero-width joiners take none. Characters outside the BMP come as surrogate pairs, two cells already,
/// which matches the emoji among them.
/// </summary>
public static class CharWidth
{
    /// <summary>The cell right of a wide character: covered by it, nothing is written there.</summary>
    public const char Continuation = '￿';

    // East Asian Width W and F in the BMP, and the BMP characters with Emoji_Presentation=Yes (Unicode 15.1)
    static readonly (char From, char To)[] Wide =
    [
        ('ᄀ', 'ᅟ'), ('⌚', '⌛'), ('〈', '〉'), ('⏩', '⏬'), ('⏰', '⏰'),
        ('⏳', '⏳'), ('◽', '◾'), ('☔', '☕'), ('♈', '♓'), ('♿', '♿'),
        ('⚓', '⚓'), ('⚡', '⚡'), ('⚪', '⚫'), ('⚽', '⚾'), ('⛄', '⛅'),
        ('⛎', '⛎'), ('⛔', '⛔'), ('⛪', '⛪'), ('⛲', '⛳'), ('⛵', '⛵'),
        ('⛺', '⛺'), ('⛽', '⛽'), ('✅', '✅'), ('✊', '✋'), ('✨', '✨'),
        ('❌', '❌'), ('❎', '❎'), ('❓', '❕'), ('❗', '❗'), ('➕', '➗'),
        ('➰', '➰'), ('➿', '➿'), ('⬛', '⬜'), ('⭐', '⭐'), ('⭕', '⭕'),
        ('⺀', '〾'), ('ぁ', '㏿'), ('㐀', '䶿'), ('一', '鿿'), ('ꀀ', '꓏'),
        ('ꥠ', '꥿'), ('가', '힣'), ('豈', '﫿'), ('︐', '︙'), ('︰', '﹯'),
        ('＀', '｠'), ('￠', '￦'),
    ];

    /// <summary>Takes two columns.</summary>
    public static bool IsWide(char c)
    {
        if (c < 'ᄀ') return false;
        int lo = 0, hi = Wide.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (c < Wide[mid].From) hi = mid - 1;
            else if (c > Wide[mid].To) lo = mid + 1;
            else return true;
        }
        return false;
    }

    /// <summary>
    /// Takes no column: variation selectors, zero-width space, joiners and marks. They are left out of what is drawn,
    /// so "⚠️" shows as "⚠", one column in every terminal (with the selector some draw it in two and some in one).
    /// </summary>
    public static bool IsZeroWidth(char c) => c is >= '︀' and <= '️' or >= '​' and <= '‏' or >= '⁠' and <= '⁤' or '﻿';

    /// <summary>
    /// A combining mark (Unicode Mn, Me: an accent, a vowel sign written apart from its letter). Every terminal draws
    /// it over the character before it, in the same cell. Variation selectors are Mn too, but are left out instead
    /// (<see cref="IsZeroWidth"/>).
    /// </summary>
    public static bool IsMark(char c) =>
        c >= '̀' && !IsZeroWidth(c) && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) is
            System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.EnclosingMark;

    // Emoji that terminals join or not as they please, drawn so that none joins them: a skin tone (U+1F3FB..1F3FF)
    // after an emoji is left out (joined, "👍🏽" takes two columns; apart, four), and a flag's two regional indicators
    // (U+1F1E6..1F1FF) are drawn as their letters, "🇬🇧" as "GB" (joined, a flag in two columns; apart, two boxed
    // letters in two or four). Emoji joined by ZWJ fall apart by themselves: the joiner is not drawn.

    /// <summary>A skin-tone modifier starts at <paramref name="i"/> (a surrogate pair).</summary>
    public static bool IsSkinTone(string s, int i) => i + 1 < s.Length && s[i] == '\uD83C' && s[i + 1] is >= '\uDFFB' and <= '\uDFFF';

    /// <summary>The letter of the regional indicator (a surrogate pair) at <paramref name="i"/>, or null.</summary>
    public static char? FlagLetter(string s, int i) =>
        i + 1 < s.Length && s[i] == '\uD83C' && s[i + 1] is >= '\uDDE6' and <= '\uDDFF' ? (char)('A' + (s[i + 1] - '\uDDE6')) : null;

    /// <summary>Whether a string has anything that does not take exactly one column per UTF-16 character.</summary>
    public static bool AllNarrow(string s)
    {
        foreach (char c in s)
            if (c >= '̀' && (c == '\uD83C' || IsMark(c) || c >= 'ᄀ' && (IsWide(c) || IsZeroWidth(c)))) return false;
        return true;
    }
}
