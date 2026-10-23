using Xunit;

/// <summary>Formatting programs (Formatters.cs): which one is used, the change as one edit, ruff and black for real.</summary>
public sealed class FormattersTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-fmt-" + Guid.NewGuid().ToString("N"));
    public FormattersTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void The_change_is_one_edit_between_what_both_texts_share()
    {
        Assert.Empty(Formatters.Diff("a\nb\n", "a\nb\n"));
        var e = Assert.Single(Formatters.Diff("x=1\ny = 2\nz=3\n", "x = 1\ny = 2\nz = 3\n"));
        Assert.Equal(new LspEdit(0, 1, 2, 2, " = 1\ny = 2\nz = "), e);                // between "x" and "3\n"
        var tail = Assert.Single(Formatters.Diff("a\n", "a\n\nb\n"));
        Assert.Equal(new LspEdit(1, 0, 1, 0, "\nb\n"), tail);
    }

    [Fact]
    public void A_formatter_in_the_settings_comes_first_and_off_turns_formatting_off()
    {
        var none = new Dictionary<string, ServerCommand?>();
        Assert.Null(Formatters.For("Markdown", none));
        var mine = new Dictionary<string, ServerCommand?> { ["Python"] = new ServerCommand("cmd", ["/c", "type"]) };
        if (OperatingSystem.IsWindows())
        {
            var f = Formatters.For("Python", mine);
            Assert.Equal(("cmd", true), (f!.Value.Command.Command, f.Value.Configured));
        }
        Assert.Null(Formatters.For("Python", new Dictionary<string, ServerCommand?> { ["Python"] = null }));
        Assert.Null(Formatters.For("Python", new Dictionary<string, ServerCommand?> { ["Python"] = new ServerCommand("no-such-formatter", []) }));
        var (s, error) = Settings.Parse("""{ "formatters": { "Python": { "command": "black", "args": ["-q", "-"] }, "C#": "off" } }""");
        Assert.Null(error);
        Assert.Equal("black", s.Formatters["python"]!.Command);
        Assert.Null(s.Formatters["C#"]);
    }

    [Theory]
    [InlineData("ruff")]
    [InlineData("black")]
    public async Task Ruff_and_black_format_the_text_and_say_what_is_wrong(string program)
    {
        var command = Formatters.Defaults["Python"].Single(c => c.Command == program);
        if (LspClient.Resolve(program) == null) return;                       // not installed here
        var path = Path.Combine(_dir, "m.py");
        var (text, error) = await Formatters.Run(command, path, "x=1\ndef f( a ):\n  return a\n", TimeSpan.FromSeconds(60));
        Assert.Null(error);
        Assert.Equal("x = 1\n\n\ndef f(a):\n    return a\n", text);
        (text, error) = await Formatters.Run(command, path, "def f(:\n", TimeSpan.FromSeconds(60));
        Assert.Null(text);
        Assert.StartsWith(program + ": ", error, StringComparison.Ordinal);    // its complaint about the syntax error
        File.WriteAllText(Path.Combine(_dir, "pyproject.toml"),                // the project's own settings are used
            program == "ruff" ? "[tool.ruff.format]\nquote-style = \"single\"\n" : "[tool.black]\nskip-string-normalization = true\n");
        (text, _) = await Formatters.Run(command, path, "s = 'a'\n", TimeSpan.FromSeconds(60));
        Assert.Equal("s = 'a'\n", text);
        (text, _) = await Formatters.Run(command, path, "s = \"a\"\n", TimeSpan.FromSeconds(60));
        Assert.Equal(program == "ruff" ? "s = 'a'\n" : "s = \"a\"\n", text);
    }
}
