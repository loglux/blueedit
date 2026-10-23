using System.Globalization;
using System.Text.Json;

/// <summary>
/// The interface in the chosen language (#199): a table per language, "lang/&lt;code&gt;.json", whose keys are the
/// English strings as the code writes them and whose values are their translations. English is the source and the
/// fallback: a string a table lacks stays English, so a gap cannot break the interface. The tables are built into the
/// editor; a file of the same name in the settings folder's "lang" adds a language or overrides strings, no build needed.
/// </summary>
static class Ui
{
    static Dictionary<string, string> _table = new();

    /// <summary>The language in use: "en" or the code of a table.</summary>
    public static string Language { get; private set; } = "en";

    /// <summary>The string in the interface's language: <paramref name="english"/> when the table has none.</summary>
    public static string T(string english) => _table.TryGetValue(english, out var t) ? t : english;

    /// <summary>A word that reads differently in one place: the key "context|English" first ("hint|Save": the short
    /// word under F2), then the word alone ("Save": a button).</summary>
    public static string Tc(string context, string english) =>
        _table.TryGetValue(context + "|" + english, out var t) ? t : T(english);

    /// <summary>A string with values put in: the key keeps {0}, {1} where they go ("Save changes to {0}?"), so a
    /// translation can move them. A translation whose places are wrong (a user's file) gives the English.</summary>
    public static string T(string english, params object?[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, T(english), args); }
        catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, english, args); }
    }

    /// <summary>A count in words: <paramref name="one"/> for 1, <paramref name="other"/> else, {0} the count and {1}…
    /// the <paramref name="more"/> values. The table's key is <paramref name="other"/>; its value gives the language's
    /// forms split by "|": two as English has, or three as Russian, Ukrainian and Belarusian have (one, few,
    /// many: 1 файл, 2 файла, 5 файлов; 21 файл, 11 файлов).</summary>
    public static string N(long count, string one, string other, params object?[] more)
    {
        var args = more.Prepend(count).ToArray();
        string english = count == 1 ? one : other;
        if (!_table.TryGetValue(other, out var t)) return string.Format(CultureInfo.CurrentCulture, english, args);
        var forms = t.Split('|');
        long n = Math.Abs(count), d = n % 10, h = n % 100;
        var form = forms.Length switch
        {
            1 => forms[0],
            2 => forms[n == 1 ? 0 : 1],
            _ => forms[d == 1 && h != 11 ? 0 : d is >= 2 and <= 4 && h is < 12 or > 14 ? 1 : 2],
        };
        try { return string.Format(CultureInfo.CurrentCulture, form, args); }
        catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, english, args); }
    }

    static Ui() => Tui.Strings.Translate = T;

    /// <summary>Switch to the language of the setting "uiLanguage" ("auto": the system's, BLUEEDIT_UI_LANGUAGE over
    /// both); true when it changed.</summary>
    public static bool Use(string setting, string? userFolder = null)
    {
        var code = Code(Environment.GetEnvironmentVariable("BLUEEDIT_UI_LANGUAGE") is { Length: > 0 } env ? env : setting);
        var table = code == "en" ? null : Load(code, userFolder);
        var language = table == null ? "en" : code;
        if (language == Language) return false;
        Language = language;
        _table = table ?? new Dictionary<string, string>();
        return true;
    }

    /// <summary>"auto" or a code ("ru", "ru-RU", "pt_BR") as a table's name: "ru", "pt-br".</summary>
    internal static string Code(string setting)
    {
        var s = setting.Trim();
        if (s.Length == 0 || s.Equals("auto", StringComparison.OrdinalIgnoreCase)) s = CultureInfo.CurrentUICulture.Name;
        s = s.Replace('_', '-').ToLowerInvariant();
        int dot = s.IndexOf('.', StringComparison.Ordinal);            // LANG=ru_RU.UTF-8
        if (dot >= 0) s = s[..dot];
        return s.Length == 0 || s is "c" or "posix" ? "en" : s;
    }

    /// <summary>The languages there are tables for, English first.</summary>
    public static IReadOnlyList<string> Available(string? userFolder = null)
    {
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in typeof(Ui).Assembly.GetManifestResourceNames())
            if (name.StartsWith("lang.", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)) codes.Add(name[5..^5]);
        if (userFolder != null && Directory.Exists(Path.Combine(userFolder, "lang")))
            foreach (var f in Directory.EnumerateFiles(Path.Combine(userFolder, "lang"), "*.json")) codes.Add(Path.GetFileNameWithoutExtension(f).ToLowerInvariant());
        codes.Remove("en");
        return codes.Prepend("en").ToList();
    }

    /// <summary>The table for a code ("pt-br"; failing that its language, "pt"): the built-in one with the user's file
    /// over it. Null when there is neither.</summary>
    internal static Dictionary<string, string>? Load(string code, string? userFolder = null)
    {
        foreach (var c in code.Contains('-', StringComparison.Ordinal) ? new[] { code, code[..code.IndexOf('-', StringComparison.Ordinal)] } : new[] { code })
        {
            Dictionary<string, string>? table = null;
            using (var stream = typeof(Ui).Assembly.GetManifestResourceStream($"lang.{c}.json"))
                if (stream != null) table = Parse(stream);
            var file = userFolder == null ? null : Path.Combine(userFolder, "lang", c + ".json");
            if (file != null && File.Exists(file))
            {
                try
                {
                    using var f = File.OpenRead(file);
                    var mine = Parse(f);
                    table ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (k, v) in mine) table[k] = v;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            }
            if (table != null) return table;
        }
        return null;
    }

    /// <summary>A table: one JSON object, English string to translation; comments allowed, an empty value skipped.</summary>
    internal static Dictionary<string, string> Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } v) table[p.Name] = v;
        return table;
    }
}
