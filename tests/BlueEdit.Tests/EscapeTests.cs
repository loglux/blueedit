using Tui;
using Xunit;

public sealed class EscapeTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-esc-" + Guid.NewGuid().ToString("N"));
    public EscapeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static readonly KeyInput Esc = new(Key.Escape);

    EditorView Open(string text, string name = "a.txt", Action? onEscape = null, MdView view = MdView.Live)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView { EscapeAction = onEscape, DefaultMdView = view };
        e.Load(p);
        return e;
    }

    [Fact]
    public void Without_escape_quits_esc_only_cancels_as_before()
    {
        var e = Open("hello world\n");
        e.SelectAt(1, 1, 5);
        e.HandleKey(Esc);
        Assert.False(e.HasSelection);
        e.HandleKey(Esc);                                         // nothing else happens
        Assert.Equal("hello world", e.Lines[0]);
    }

    [Fact]
    public void Esc_cancels_the_selection_first_then_closes()
    {
        int closed = 0;
        var e = Open("hello world\n", onEscape: () => closed++);
        e.SelectAt(1, 1, 5);
        e.HandleKey(Esc);
        Assert.Equal((false, 0), (e.HasSelection, closed));
        e.HandleKey(Esc);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Esc_leaves_reading_mode_before_closing()
    {
        int closed = 0;
        var e = Open("# Title\ntext\n", "a.md", () => closed++, MdView.Reading);
        Assert.True(e.ReadingView);
        e.HandleKey(Esc);
        Assert.Equal((false, 0), (e.ReadingView, closed));
        e.HandleKey(Esc);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void A_read_only_file_closes_at_once()
    {
        int closed = 0;
        var p = Path.Combine(_dir, "r.txt"); File.WriteAllText(p, "x");
        var e = new EditorView { EscapeAction = () => closed++, ReadOnlyFile = true };
        e.Load(p);
        e.HandleKey(Esc);
        Assert.Equal(1, closed);
    }

    [Theory]
    [InlineData(new[] { "far" }, true)]
    [InlineData(new[] { "cmd", "far" }, true)]                       // Far ran it through cmd /c
    [InlineData(new[] { "cmd", "conhost", "far" }, true)]
    [InlineData(new[] { "pwsh", "windowsterminal" }, false)]         // typed in PowerShell in Windows Terminal
    [InlineData(new[] { "explorer" }, false)]
    [InlineData(new[] { "git", "far" }, false)]                      // git started it (even if git ran inside Far)
    [InlineData(new string[0], false)]
    public void Started_by_far(string[] ancestors, bool far) => Assert.Equal(far, ParentProcess.StartedByFar(ancestors));

    [Fact]
    public void Ancestors_can_be_read() => Assert.NotEmpty(ParentProcess.Ancestors());

    [Theory]
    [InlineData("{ \"escapeQuits\": true }", "on")]
    [InlineData("{ \"escapeQuits\": false }", "off")]
    [InlineData("{ \"escapeQuits\": \"auto\" }", "auto")]
    [InlineData("{ }", "auto")]
    public void Escape_quits_setting(string json, string value) => Assert.Equal(value, Settings.Parse(json).Settings.EscapeQuits);

    [Fact]
    public void A_bad_escape_quits_value_is_reported() => Assert.NotNull(Settings.Parse("{ \"escapeQuits\": \"sometimes\" }").Error);
}
