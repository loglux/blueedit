using Tui;
using Xunit;

/// <summary>Enter after a line that opens a block indents one step more (Python's ":", the brace languages' brackets).</summary>
public sealed class SmartIndentTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-indent-" + Guid.NewGuid().ToString("N"));
    public SmartIndentTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string text, string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView();
        e.Load(p);
        e.SetArea(new Rect(0, 0, 80, 10));
        e.Draw(new Screen(80, 10));
        return e;
    }

    static void Type(EditorView e, string s)
    {
        foreach (var ch in s) e.HandleKey(ch == '\n' ? new KeyInput(Key.Enter) : new KeyInput(Key.Character, ch));
    }

    [Fact]
    public void After_a_python_colon_the_next_line_is_indented_one_step_deeper()
    {
        var e = Open("\n", "a.py");
        Type(e, "def func():\nreturn 1");
        Assert.Equal("def func():", e.Lines[0]);
        Assert.Equal("    return 1", e.Lines[1]);
        Type(e, "\nif x:\npass");                                   // the indent is kept, then deepened again
        Assert.Equal("    if x:", e.Lines[2]);
        Assert.Equal("        pass", e.Lines[3]);
    }

    [Fact]
    public void Between_braces_the_caret_gets_its_own_line_and_the_closer_goes_below()
    {
        var e = Open("\n", "a.cs");
        Type(e, "void F() {");                                     // the closer is added (autoClose "code")
        Assert.Equal("void F() {}", e.Lines[0]);
        Type(e, "\nx();");
        Assert.Equal(new[] { "void F() {", "    x();", "}" }, e.Lines.Take(3).ToArray());
    }

    [Fact]
    public void Plain_text_and_markdown_only_keep_the_indent()
    {
        var e = Open("\n", "a.md");
        Type(e, "Note:\nnext");
        Assert.Equal("next", e.Lines[1]);
        var t = Open("\n", "a.txt");
        Type(t, "  Items {\nnext");
        Assert.Equal("  next", t.Lines[1]);
    }
}
