namespace Tui;

/// <summary>
/// Hotkeys are named by Latin letters (Alt+F, "Y/N"), but with a Russian keyboard layout active the same keys give
/// Cyrillic letters. <see cref="Latin"/> maps a letter of the ЙЦУКЕН layout to the key's QWERTY letter.
/// </summary>
public static class KeyboardLayout
{
    const string Russian = "йцукенгшщзфывапролдячсмитьхъжэбюё";
    const string Qwerty = "qwertyuiopasdfghjklzxcvbnm[];',.`";

    /// <summary>The QWERTY letter of the key that typed <paramref name="c"/> (lower case); other characters lower-cased.</summary>
    public static char Latin(char c)
    {
        c = char.ToLowerInvariant(c);
        int i = Russian.IndexOf(c, StringComparison.Ordinal);
        return i >= 0 ? Qwerty[i] : c;
    }
}
