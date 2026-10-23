using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>"file:12" and "file:12:5" as compilers, grep and other tools print them.</summary>
static partial class Location
{
    [GeneratedRegex(@"^(?<path>.+?):(?<line>\d+)(?::(?<col>\d+))?:?$")]
    private static partial Regex Rx();

    /// <summary>Splits off a line and column. A name that exists as written is taken literally, so a file really
    /// called "notes:2" still opens. Line and column are 0 when absent.</summary>
    public static (string Path, int Line, int Col) Parse(string arg)
    {
        if (File.Exists(arg) || Directory.Exists(arg)) return (arg, 0, 0);
        var m = Rx().Match(arg);
        if (!m.Success || m.Groups["path"].Value is "" or [_]) return (arg, 0, 0);    // "C:5" is not a file:line
        return (m.Groups["path"].Value, Number(m.Groups["line"]), m.Groups["col"].Success ? Number(m.Groups["col"]) : 0);
    }

    // too many digits: as far as it goes (the editor clamps to the last line)
    static int Number(Group g) => int.TryParse(g.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue;
}
