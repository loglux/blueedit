using System.Text.Json;
using Xunit;

public sealed class TerminalProfileTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-wt-" + Guid.NewGuid().ToString("N"));
    public TerminalProfileTests() => Environment.SetEnvironmentVariable("BLUEEDIT_WT_FRAGMENTS", _dir);
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BLUEEDIT_WT_FRAGMENTS", null);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Fragment_has_one_profile_with_the_font_and_a_scheme_in_the_theme_colours()
    {
        var json = TerminalProfile.Build(@"C:\Program Files\bp\be.exe", "JetBrains Mono", 14, Themes.Get("dark"));
        using var doc = JsonDocument.Parse(json);
        var profile = doc.RootElement.GetProperty("profiles")[0];
        Assert.Equal("BlueEdit", profile.GetProperty("name").GetString());
        Assert.Equal("\"C:\\Program Files\\bp\\be.exe\"", profile.GetProperty("commandline").GetString());   // quoted: has a space
        Assert.Equal("JetBrains Mono", profile.GetProperty("font").GetProperty("face").GetString());
        Assert.Equal(14, profile.GetProperty("font").GetProperty("size").GetInt32());
        var scheme = doc.RootElement.GetProperty("schemes")[0];
        Assert.Equal(profile.GetProperty("colorScheme").GetString(), scheme.GetProperty("name").GetString());
        Assert.Equal("#1E1E1E", scheme.GetProperty("background").GetString());
        Assert.Equal(16 + 5, scheme.EnumerateObject().Count());                      // name, bg, fg, cursor, selection + 16
    }

    [Fact]
    public void Write_only_touches_the_file_when_something_changed_and_remove_cleans_up()
    {
        var theme = Themes.Get("dos-blue");
        Assert.False(TerminalProfile.Installed);
        Assert.True(TerminalProfile.Write("bp.exe", "Cascadia Mono", 12, theme));
        Assert.True(TerminalProfile.Installed);
        Assert.False(TerminalProfile.Write("bp.exe", "Cascadia Mono", 12, theme));
        Assert.True(TerminalProfile.Write("bp.exe", "Cascadia Mono", 12, Themes.Get("light")));
        TerminalProfile.Remove();
        Assert.False(TerminalProfile.Installed);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Font_settings_round_trip_through_the_template()
    {
        var (s, error) = Settings.Parse(Settings.Template(new Settings { FontSetup = "hint", Font = "Name \"quoted\"", FontSize = 16 }));
        Assert.Null(error);
        Assert.Equal(("hint", "Name \"quoted\"", 16), (s.FontSetup, s.Font, s.FontSize));
    }

    [Fact]
    public void Hint_names_the_font_and_fits_the_help_width()
    {
        var hint = TerminalProfile.Hint("Iosevka", 13);
        Assert.Contains(hint, l => l.Contains("Iosevka, size 13", StringComparison.Ordinal));
        Assert.All(hint, l => Assert.True(l.Length <= 96, l));
    }
}
