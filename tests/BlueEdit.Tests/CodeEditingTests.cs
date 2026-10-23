using Tui;
using Xunit;

/// <summary>Line comments, moving lines, a closer going back to its opener's indent, the bracket pair marked.</summary>
public sealed class CodeEditingTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-code-" + Guid.NewGuid().ToString("N"));
    public CodeEditingTests() => Directory.CreateDirectory(_dir);
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

    static void Key(EditorView e, Key k, Mods m = Mods.None) => e.HandleKey(new KeyInput(k, '\0', m));
    static void Type(EditorView e, string s)
    {
        foreach (var ch in s) e.HandleKey(ch == '\n' ? new KeyInput(Tui.Key.Enter) : new KeyInput(Tui.Key.Character, ch));
    }

    [Fact]
    public void Ctrl_slash_comments_the_selected_lines_at_their_indent_and_back()
    {
        var e = Open("def f():\n    a = 1\n\n    b = 2\n", "a.py");
        e.HandleKey(new KeyInput(Tui.Key.Down));
        e.HandleKey(new KeyInput(Tui.Key.Down, '\0', Mods.Shift));
        e.HandleKey(new KeyInput(Tui.Key.Down, '\0', Mods.Shift));
        e.HandleKey(new KeyInput(Tui.Key.Down, '\0', Mods.Shift));           // rows 1-3 selected, ending at row 4's start
        e.HandleKey(new KeyInput(Tui.Key.Character, '_', Mods.Ctrl));        // Ctrl+/ as terminals send it
        Assert.Equal(new[] { "def f():", "    # a = 1", "", "    # b = 2" }, e.Lines.Take(4).ToArray());
        e.HandleKey(new KeyInput(Tui.Key.Character, '/', Mods.Ctrl));
        Assert.Equal(new[] { "def f():", "    a = 1", "", "    b = 2" }, e.Lines.Take(4).ToArray());
        var cs = Open("int x;\n", "a.cs");
        cs.HandleKey(new KeyInput(Tui.Key.Character, '/', Mods.Ctrl));
        Assert.Equal("// int x;", cs.Lines[0]);
    }

    [Fact]
    public void Alt_up_and_down_move_the_line_and_the_caret_with_it()
    {
        var e = Open("one\ntwo\nthree\n", "a.txt");
        e.HandleKey(new KeyInput(Tui.Key.Down));
        Key(e, Tui.Key.Down, Mods.Alt);
        Assert.Equal(new[] { "one", "three", "two" }, e.Lines.Take(3).ToArray());
        Assert.Equal(2, e.Row);
        Key(e, Tui.Key.Up, Mods.Alt); Key(e, Tui.Key.Up, Mods.Alt);
        Assert.Equal(new[] { "two", "one", "three" }, e.Lines.Take(3).ToArray());
        Key(e, Tui.Key.Up, Mods.Alt);                                          // at the top: nothing
        Assert.Equal("two", e.Lines[0]);
    }

    [Fact]
    public void A_closer_typed_on_an_empty_line_goes_to_its_openers_indent()
    {
        var e = Open("\n", "a.cs");
        e.AutoClose = "off";                                                   // no closer typed ahead
        Type(e, "if (x) {\nf();\n}");
        Assert.Equal(new[] { "if (x) {", "    f();", "}" }, e.Lines.Take(3).ToArray());
        var py = Open("\n", "a.py");                                           // not in Python
        Type(py, "x = [\n]");
        Assert.Equal("]", py.Lines[1].Trim());
    }

    [Fact]
    public void A_servers_edits_are_one_undo_step_and_the_caret_stays_on_its_text()
    {
        var e = Open("class C{\nint x=1;\n}\n", "a.cs");
        e.HandleKey(new KeyInput(Tui.Key.Down));
        e.HandleKey(new KeyInput(Tui.Key.End));                                // after "int x=1;"
        // as a formatter sends them: against the original text, any order, "\r\n" made "\n" by EditsFrom
        var edits = LanguageServers.EditsFrom(System.Text.Json.Nodes.JsonNode.Parse("""
            [ { "range": { "start": { "line": 1, "character": 5 }, "end": { "line": 1, "character": 6 } }, "newText": " = " },
              { "range": { "start": { "line": 0, "character": 7 }, "end": { "line": 1, "character": 0 } }, "newText": "\r\n{\r\n    " } ]
            """));
        Assert.True(e.ApplyEdits(edits));
        Assert.Equal(new[] { "class C", "{", "    int x = 1;", "}" }, e.Lines.Take(4).ToArray());
        Assert.Equal((2, 14), (e.Row, e.Col));                                 // still after "1;"
        e.Undo();
        Assert.Equal(new[] { "class C{", "int x=1;", "}" }, e.Lines.Take(3).ToArray());
        var overlapping = new[] { new LspEdit(0, 0, 0, 5, "a"), new LspEdit(0, 3, 0, 6, "b") };
        Assert.False(e.ApplyEdits(overlapping));
        Assert.Equal("class C{", e.Lines[0]);
    }

    [Fact]
    public void The_bracket_at_the_caret_and_its_pair_are_marked()
    {
        var e = Open("f(a, (b))\n", "a.cs");
        e.HandleKey(new KeyInput(Tui.Key.Right));                              // caret on "(" at 1
        var s = new Screen(80, 10);
        e.Draw(s);
        int x0 = e.CursorPos!.Value.X - 1, y = e.CursorPos.Value.Y;          // the text's first column
        Assert.True((s.StyleAt(x0 + 1, y).Attr & Attr.Underline) != 0);
        Assert.True((s.StyleAt(x0 + 8, y).Attr & Attr.Underline) != 0);      // its ")" at 8, not the inner one at 7
        Assert.True((s.StyleAt(x0 + 7, y).Attr & Attr.Underline) == 0);
    }
}
