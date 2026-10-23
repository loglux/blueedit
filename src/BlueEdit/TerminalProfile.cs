using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// The font is the terminal's, not the program's. In Windows Terminal BlueEdit keeps its own profile through a
/// "fragment" (a JSON file Windows Terminal reads from a well-known folder): the user's settings.json and other
/// profiles are never edited, and deleting the fragment removes every trace. Elsewhere it can only give a hint.
/// BLUEEDIT_WT_FRAGMENTS points the fragment folder elsewhere (the tests use it).
/// </summary>
static class TerminalProfile
{
    public const string ProfileName = "BlueEdit";

    static string FragmentsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "Microsoft", "Windows Terminal", "Fragments");

    public static string FragmentDir =>
        Environment.GetEnvironmentVariable("BLUEEDIT_WT_FRAGMENTS") is { Length: > 0 } dir ? dir : Path.Combine(FragmentsRoot, "blueedit");

    public static string FragmentPath => Path.Combine(FragmentDir, "blueedit.json");

    /// <summary>Windows Terminal sets WT_SESSION for everything it runs. A Linux build in WSL inside it sees that
    /// too, but the profile starts be.exe: Windows only.</summary>
    public static bool InWindowsTerminal => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("WT_SESSION") is { Length: > 0 };

    public static bool Installed => File.Exists(FragmentPath);

    static string Hex(int rgb) => "#" + rgb.ToString("X6", CultureInfo.InvariantCulture);

    /// <summary>The fragment: one profile that starts this executable, and a colour scheme matching the editor theme
    /// so the terminal's padding around the editor has the editor's background.</summary>
    public static string Build(string exe, string font, int size, EditorTheme theme)
    {
        var scheme = $"BlueEdit {theme.Name}";
        var doc = new JsonObject
        {
            ["profiles"] = new JsonArray(new JsonObject
            {
                ["name"] = ProfileName,
                ["commandline"] = exe.Contains(' ', StringComparison.Ordinal) ? $"\"{exe}\"" : exe,
                ["startingDirectory"] = "%USERPROFILE%",
                ["icon"] = "✏️",
                ["font"] = new JsonObject { ["face"] = font, ["size"] = size },
                ["colorScheme"] = scheme,
                ["padding"] = "2",
            }),
            ["schemes"] = new JsonArray(new JsonObject
            {
                ["name"] = scheme,
                ["background"] = Hex(theme.Background), ["foreground"] = Hex(theme.Text),
                ["cursorColor"] = Hex(theme.Text), ["selectionBackground"] = Hex(theme.Selection),
                // the 16 console colours (Campbell); the editor itself draws in 24-bit colour
                ["black"] = "#0C0C0C", ["red"] = "#C50F1F", ["green"] = "#13A10E", ["yellow"] = "#C19C00",
                ["blue"] = "#0037DA", ["purple"] = "#881798", ["cyan"] = "#3A96DD", ["white"] = "#CCCCCC",
                ["brightBlack"] = "#767676", ["brightRed"] = "#E74856", ["brightGreen"] = "#16C60C", ["brightYellow"] = "#F9F1A5",
                ["brightBlue"] = "#3B78FF", ["brightPurple"] = "#B4009E", ["brightCyan"] = "#61D6D6", ["brightWhite"] = "#F2F2F2",
            }),
        };
        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>Writes the fragment if it is missing or different. Returns true when the file changed.</summary>
    public static bool Write(string exe, string font, int size, EditorTheme theme)
    {
        var text = Build(exe, font, size, theme);
        if (File.Exists(FragmentPath) && File.ReadAllText(FragmentPath) == text) return false;
        Directory.CreateDirectory(FragmentDir);
        File.WriteAllText(FragmentPath, text);
        return true;
    }

    public static void Remove()
    {
        if (File.Exists(FragmentPath)) File.Delete(FragmentPath);
        if (Directory.Exists(FragmentDir) && !Directory.EnumerateFileSystemEntries(FragmentDir).Any()) Directory.Delete(FragmentDir);
    }

    /// <summary>What to do by hand: shown in "hint" mode and outside Windows Terminal.</summary>
    public static string[] Hint(string font, int size) => new[]
    {
        "",
        "  " + Ui.T("The font belongs to the terminal window, not to the editor, so it is set in the terminal."),
        "",
        "  " + Ui.T("Recommended: {0}, size {1}. Fonts with good coverage of math and box-drawing", font, size),
        "  " + Ui.T("characters: Cascadia Mono, JetBrains Mono, DejaVu Sans Mono, Consolas."),
        "",
        "  Windows Terminal   " + Ui.T("Settings > (your profile) > Appearance > Font face / Font size"),
        "  " + Ui.T("Windows console    Right-click the title bar > Properties > Font"),
        "  Far Manager        " + Ui.T("runs inside one of these, so set it there"),
        "",
        "  " + Ui.T("Every character takes the same size: a terminal cannot make formulas larger than text."),
        "",
        "  " + Ui.T("With \"fontSetup\": \"auto\" in the settings, BlueEdit adds its own profile"),
        "  " + Ui.T("\"BlueEdit\" to Windows Terminal instead, with this font and the theme colours;"),
        "  " + Ui.T("other profiles are not changed."),
        "",
    };
}
