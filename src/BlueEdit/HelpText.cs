/// <summary>Text of the F1 help screen: lang/help.en.txt, and in other languages lang/help.&lt;code&gt;.txt, built in.
/// Keep it in step with the keys in App.Menus.cs; lines stay within 86 columns. A translation is a whole text, not a
/// table of lines: its lines break where that language needs, and HelpTextTests checks that it names the same keys.
/// </summary>
static class HelpText
{
    /// <summary>The help in an interface language ("ru", "pt-br": failing that "pt"): the settings folder's
    /// lang/help.&lt;code&gt;.txt, else the built-in one, else the English.</summary>
    public static List<string> For(string language, string? userFolder = null)
    {
        foreach (var c in language.Contains('-', StringComparison.Ordinal) ? new[] { language, language[..language.IndexOf('-', StringComparison.Ordinal)] } : new[] { language })
        {
            if (c == "en") break;
            var file = userFolder == null ? null : Path.Combine(userFolder, "lang", $"help.{c}.txt");
            try { if (file != null && File.Exists(file)) return Split(File.ReadAllText(file)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (BuiltIn(c) is { } builtIn) return builtIn;
        }
        return Lines.ToList();
    }

    static List<string>? BuiltIn(string code)
    {
        using var stream = typeof(HelpText).Assembly.GetManifestResourceStream($"lang.help.{code}.txt");
        return stream == null ? null : Split(new StreamReader(stream).ReadToEnd());
    }

    static List<string> Split(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n').Append("").ToList();

    /// <summary>The English help, lang/help.en.txt.</summary>
    public static readonly string[] Lines = BuiltIn("en")!.ToArray();
}
