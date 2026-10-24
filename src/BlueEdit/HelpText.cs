using Tui;

/// <summary>The F1 help: lang/help.en.md, and in other languages lang/help.&lt;code&gt;.md, built in. A section is
/// "# Title"; a row "keys | what they do"; any other line a paragraph (lines one after another join, a blank line
/// ends it). Nothing is aligned by hand: <see cref="HelpDialog"/> lays the rows out, so a translation only writes its
/// words. Keep it in step with the keys in App.Menus.cs; a translation has the English sections in the same order
/// (F1 opens on one by its place) and names the same keys (HelpTextTests).</summary>
static class HelpText
{
    /// <summary>The help in an interface language ("ru", "pt-br": failing that "pt"): the settings folder's
    /// lang/help.&lt;code&gt;.md, else the built-in one, else the English.</summary>
    public static List<HelpSection> For(string language, string? userFolder = null)
    {
        foreach (var c in language.Contains('-', StringComparison.Ordinal) ? new[] { language, language[..language.IndexOf('-', StringComparison.Ordinal)] } : new[] { language })
        {
            if (c == "en") break;
            var file = userFolder == null ? null : Path.Combine(userFolder, "lang", $"help.{c}.md");
            try { if (file != null && File.Exists(file)) return Parse(File.ReadAllText(file)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (BuiltIn(c) is { } builtIn) return Parse(builtIn);
        }
        return English.ToList();
    }

    static string? BuiltIn(string code)
    {
        using var stream = typeof(HelpText).Assembly.GetManifestResourceStream($"lang.help.{code}.md");
        return stream == null ? null : new StreamReader(stream).ReadToEnd();
    }

    public static List<HelpSection> Parse(string text)
    {
        var sections = new List<HelpSection>();
        List<HelpRow>? rows = null;
        bool joins = false;                                             // the line before was a paragraph's
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                Close();
                rows = new();
                sections.Add(new HelpSection(line[2..].Trim(), rows));
                joins = false;
                continue;
            }
            if (rows == null) continue;                                 // nothing before the first section
            if (line.Length == 0)
            {
                if (rows.Count > 0 && rows[^1] is not { Keys: "", Text: "" }) rows.Add(new HelpRow("", ""));
                joins = false;
                continue;
            }
            int bar = line.IndexOf(" | ", StringComparison.Ordinal);
            if (bar > 0) { rows.Add(new HelpRow(line[..bar].Trim(), line[(bar + 3)..].Trim())); joins = false; continue; }
            if (joins) rows[^1] = rows[^1] with { Text = rows[^1].Text + " " + line };
            else rows.Add(new HelpRow("", line));
            joins = true;
        }
        Close();
        return sections;

        void Close() { while (rows is { Count: > 0 } && rows[^1] is { Keys: "", Text: "" }) rows.RemoveAt(rows.Count - 1); }
    }

    /// <summary>The English help, lang/help.en.md.</summary>
    public static readonly IReadOnlyList<HelpSection> English = Parse(BuiltIn("en")!);

    /// <summary>A help's words, a row a line ("keys  text"): what the tests read.</summary>
    public static IEnumerable<string> Lines(IEnumerable<HelpSection> help) =>
        help.SelectMany(s => new[] { s.Title }.Concat(s.Rows.Select(r => r.Keys.Length > 0 ? r.Keys + "  " + r.Text : r.Text)));

    /// <summary>Where F1 opens: the section of the file's kind (Markdown, Code), else the first. By the English
    /// titles' places, which a translation keeps.</summary>
    public static int SectionFor(bool markdown, bool code)
    {
        string? wanted = markdown ? "Markdown" : code ? "Code" : null;
        int i = wanted == null ? -1 : English.ToList().FindIndex(s => s.Title == wanted);
        return Math.Max(0, i);
    }
}
