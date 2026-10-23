using Tui;
using Xunit;

public sealed class PairsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-pairs-" + Guid.NewGuid().ToString("N"));
    public PairsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string text, string name = "a.cs", string autoClose = "code")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView { AutoClose = autoClose };
        e.Load(p);
        e.SetArea(new Rect(0, 0, 80, 10));
        e.Draw(new Screen(80, 10));
        return e;
    }

    static void Type(EditorView e, string s)
    {
        foreach (var ch in s) e.HandleKey(new KeyInput(Key.Character, ch));
    }

    static void End(EditorView e) => e.HandleKey(new KeyInput(Key.End));

    [Fact]
    public void An_opener_adds_its_closer_and_typing_the_closer_steps_over_it()
    {
        var e = Open("\n");
        Type(e, "f(");
        Assert.Equal("f()", e.Lines[0]);
        Assert.Equal(2, e.Col);
        Type(e, "a)");
        Assert.Equal("f(a)", e.Lines[0]);                  // not f(a))
        Assert.Equal(4, e.Col);
        Type(e, "; x = [{");
        Assert.Equal("f(a); x = [{}]", e.Lines[0]);
        Type(e, "}]");
        Assert.Equal("f(a); x = [{}]", e.Lines[0]);
    }

    [Fact]
    public void Quotes_pair_but_not_inside_a_word_or_a_string()
    {
        var e = Open("\n");
        Type(e, "s = \"");
        Assert.Equal("s = \"\"", e.Lines[0]);
        Type(e, "don't\"");                                 // the apostrophe after a letter is single; " steps over
        Assert.Equal("s = \"don't\"", e.Lines[0]);
        Type(e, "; c = '");
        Assert.Equal("s = \"don't\"; c = ''", e.Lines[0]);
    }

    [Fact]
    public void Before_a_word_the_opener_is_typed_alone()
    {
        var e = Open("value\n");
        Type(e, "(");
        Assert.Equal("(value", e.Lines[0]);
    }

    [Fact]
    public void Backspace_in_an_empty_pair_removes_both_and_undo_works()
    {
        var e = Open("\n");
        Type(e, "x(");
        e.HandleKey(new KeyInput(Key.Backspace));
        Assert.Equal("x", e.Lines[0]);
        Type(e, "(1");
        e.HandleKey(new KeyInput(Key.Backspace));
        e.HandleKey(new KeyInput(Key.Backspace));           // now between ( and ): both go
        Assert.Equal("x", e.Lines[0]);
    }

    [Fact]
    public void An_opener_over_a_selection_wraps_it()
    {
        var e = Open("call name now\n");
        e.HandleKey(new KeyInput(Key.Right, Mods: Mods.Ctrl));
        for (int i = 0; i < 4; i++) e.HandleKey(new KeyInput(Key.Right, Mods: Mods.Shift));   // "name"
        Type(e, "(");
        Assert.Equal("call (name) now", e.Lines[0]);
        Type(e, "\"");
        Assert.Equal("call (\"name\") now", e.Lines[0]);   // still selected: wraps again
    }

    [Fact]
    public void Prose_gets_no_pairs_unless_always_and_never_apostrophes()
    {
        var md = Open("\n", "n.md");
        Type(md, "a (b");
        Assert.Equal("a (b", md.Lines[0]);

        var always = Open("\n", "n.md", "always");
        Type(always, "a (b) it's \"q");
        Assert.Equal("a (b) it's \"q\"", always.Lines[0]);

        var off = Open("\n", "a.cs", "off");
        Type(off, "f(");
        Assert.Equal("f(", off.Lines[0]);
    }

    [Fact]
    public void No_pairs_in_a_comment()
    {
        var e = Open("// note\n");
        End(e);
        Type(e, " (see");
        Assert.Equal("// note (see", e.Lines[0]);
    }

    [Fact]
    public void Auto_close_is_a_setting()
    {
        Assert.Equal("code", new Settings().AutoClose);
        Assert.Equal("always", Settings.Parse("""{ "autoClose": "always" }""").Settings.AutoClose);
        Assert.Equal("off", Settings.Parse("""{ "autoClose": false }""").Settings.AutoClose);
        Assert.NotNull(Settings.Parse("""{ "autoClose": "sometimes" }""").Error);
    }
}
