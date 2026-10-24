using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// lang/template/strings.json: what a new interface language starts from, the Russian table (UiTests keeps it whole:
/// every string the code translates, nothing else) with its values emptied and its own header. Kept in step here;
/// BLUEEDIT_WRITE_TEMPLATE=1 (tasks lang-template) writes it anew. The help to translate is lang/help.en.md itself.
/// </summary>
public sealed class LanguageTemplateTests
{
    static string Root([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
    static string TemplateFile => Path.Combine(Root(), "lang", "template", "strings.json");

    const string Header = """
        // A new language for BlueEdit's interface (lang/template/README.md): each line is the English string as the code
        // writes it, then its translation. Write the translation between the empty quotes; an empty one stays English, so
        // a part done is of use already. An underscore marks a menu's letter ("_File"): give each translation one, on a
        // letter that clashes with no other item of its menu. {0}, {1} are values put in: keep each, anywhere in the
        // sentence. A count's forms are split by "|": "{0} file|{0} files" (one, other), or three as Russian has.
        """;

    // one entry of a table: "key": "value", with an optional comment after it
    static readonly Regex Entry = new(@"^(\s*""(?:[^""\\]|\\.)*""\s*:\s*)""(?:[^""\\]|\\.)*""(\s*,?\s*(?://.*)?)$");

    internal static string Strings(string table)
    {
        var lines = table.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int open = Array.IndexOf(lines, "{");
        var body = lines.Skip(open).Select(l => Entry.Match(l) is { Success: true } m ? m.Groups[1].Value + "\"\"" + m.Groups[2].Value : l);
        return Header.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n" + string.Join("\n", body);
    }

    static string Template() => Strings(File.ReadAllText(Path.Combine(Root(), "lang", "ru.json")));

    [Fact]
    public void The_template_has_every_string_of_the_interface()
    {
        if (Environment.GetEnvironmentVariable("BLUEEDIT_WRITE_TEMPLATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TemplateFile)!);
            File.WriteAllText(TemplateFile, Template());
        }
        Assert.True(Template() == File.ReadAllText(TemplateFile).Replace("\r\n", "\n", StringComparison.Ordinal),
            "lang/template/strings.json is behind lang/ru.json: tasks lang-template");
    }

    [Fact]
    public void The_template_reads_as_a_table_of_nothing_translated_with_every_key()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Template()));
        Assert.Empty(Ui.Parse(stream));                       // empty values are skipped: the interface stays English
        var keys = Regex.Matches(Template(), @"^\s*""((?:[^""\\]|\\.)*)""\s*:", RegexOptions.Multiline).Count;
        Assert.Equal(Ui.Load("ru")!.Count, keys);
    }
}
