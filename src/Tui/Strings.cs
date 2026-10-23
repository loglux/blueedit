namespace Tui;

/// <summary>The words the dialogs write themselves (hints such as "Enter = OK   Esc = Cancel", "File name: ") in the
/// interface's language: the editor sets <see cref="Translate"/> to its table; by default the English stays.</summary>
public static class Strings
{
    public static Func<string, string> Translate { get; set; } = s => s;

    public static string T(string english) => Translate(english);
}
