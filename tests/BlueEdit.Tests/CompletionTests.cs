using Tui;
using Xunit;

public sealed class CompletionTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-compl-" + Guid.NewGuid().ToString("N"));
    public CompletionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string text, string name = "a.txt")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView(); e.Load(p);
        return e;
    }

    static List<string> Words(Completion.Pool pool, string prefix) => Completion.Filter(pool, prefix).Select(c => c.Word).ToList();

    [Fact]
    public void Nearer_words_come_first_and_the_typed_word_is_not_offered()
    {
        var lines = new[] { "counterFar", "", "", "", "countNear", "cou" };
        var pool = Completion.Collect(lines, 5, 0, [], []);
        Assert.Equal(new[] { "countNear", "counterFar" }, Words(pool, "cou"));
    }

    [Fact]
    public void Same_case_first_then_the_rest_ignoring_case()
    {
        var pool = Completion.Collect(new[] { "Value value VALUES" }, 0, 99, [], []);
        Assert.Equal(new[] { "value", "Value", "VALUES" }, Words(pool, "va"));
    }

    [Fact]
    public void Cyrillic_words_short_words_and_the_exact_prefix()
    {
        var pool = Completion.Collect(new[] { "привет приветствие при пр" }, 0, 99, [], []);
        Assert.Equal(new[] { "привет", "приветствие" }, Words(pool, "при").Order().ToList());
        Assert.Equal(new[] { "приветствие" }, Words(pool, "привет"));      // the word itself is complete already
        Assert.DoesNotContain("пр", Words(pool, "п"));                      // under three letters: not worth offering
    }

    [Fact]
    public void Other_documents_and_keywords_come_after_the_text()
    {
        var pool = Completion.Collect(new[] { "returnValue" }, 0, 99, new[] { new[] { "retryCount" } }, new[] { "return", "returnValue" });
        var c = Completion.Filter(pool, "ret");
        Assert.Equal(new[] { "returnValue", "retryCount", "return" }, c.Select(x => x.Word));
        Assert.Equal(new[] { false, false, true }, c.Select(x => x.Keyword));   // a keyword also in the text counts as text
    }

    [Theory]
    [InlineData("hello wor", 9, "wor")]
    [InlineData("a.b_c", 5, "b_c")]
    [InlineData("x 2abc", 6, "abc")]
    [InlineData("word ", 5, null)]
    [InlineData("123", 3, null)]
    public void Word_before_the_caret(string line, int col, string? prefix)
    {
        var e = Open(line + "\n");
        e.GoToLine(1, col + 1);
        Assert.Equal(prefix, e.WordBeforeCaret()?.Prefix);
    }

    [Fact]
    public void Completing_replaces_the_prefix_and_is_one_undo_step()
    {
        var e = Open("Console.Wri\n");
        e.GoToLine(1, 12);
        e.CompleteWith("WriteLine");
        Assert.Equal(("Console.WriteLine", 17), (e.Lines[0], e.Col));
        e.Undo();
        Assert.Equal("Console.Wri", e.Lines[0]);
    }

    [Fact]
    public void Language_keywords_are_available_for_code()
    {
        Assert.Contains("namespace", Open("x", "a.cs").LanguageKeywords);
        Assert.Empty(Open("x", "a.txt").LanguageKeywords);
    }

    [Fact]
    public void Powershell_commands_complete_as_one_word_with_the_hyphen()
    {
        var e = Open("$files = Get-Ch", "a.ps1");
        e.GoToLine(1, 16);
        var w = e.WordBeforeCaret()!.Value;
        Assert.Equal("Get-Ch", w.Prefix);
        var pool = Completion.Collect(e.Lines, e.Row, w.Start, [], e.LanguageKeywords, e.HyphenWords);
        Assert.Equal("Get-ChildItem", Completion.Filter(pool, w.Prefix)[0].Word);
        e.CompleteWith("Get-ChildItem");
        Assert.Equal("$files = Get-ChildItem", e.Lines[0]);
    }

    [Fact]
    public void After_a_hyphen_every_command_of_that_verb_is_offered()
    {
        var e = Open("Set-", "a.ps1");
        e.GoToLine(1, 5);
        var w = e.WordBeforeCaret()!.Value;
        var pool = Completion.Collect(e.Lines, e.Row, w.Start, [], e.LanguageKeywords, e.HyphenWords);
        var words = Completion.Filter(pool, w.Prefix).Select(c => c.Word).ToList();
        Assert.Contains("Set-Content", words);
        Assert.Contains("Set-Location", words);
        Assert.All(words, x => Assert.StartsWith("Set-", x, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hyphens_stay_word_breaks_outside_powershell()
    {
        var e = Open("x = a-bc", "a.py");
        e.GoToLine(1, 9);
        Assert.Equal("bc", e.WordBeforeCaret()!.Value.Prefix);
        Assert.Contains("print", e.LanguageKeywords);                // Python built-ins are offered too
    }

    // ---------- the popup ----------
    sealed class Term(params InputEvent[] script) : ITerminal
    {
        readonly Queue<InputEvent> _q = new(script);
        public Screen Screen { get; } = new(80, 25);
        public bool TryNext(out InputEvent? ev, int timeoutMs)
        {
            if (_q.Count == 0 && timeoutMs == 0) { ev = null; return false; }
            if (_q.Count == 0) throw new InvalidOperationException("script exhausted");
            ev = _q.Dequeue(); return true;
        }
        public void Present() => Screen.Flush();
        public void ApplyResize(int w, int h) { }
    }

    static KeyInput K(Key k) => new(k);
    static KeyInput C(char c) => new(Key.Character, c);

    (EditorView, CompletionPopup) Popup(string text, int col, params InputEvent[] keys)
    {
        var e = Open(text);
        e.SetArea(new Rect(0, 1, 80, 20));
        e.GoToLine(1, col + 1);
        var w = e.WordBeforeCaret()!.Value;
        var pool = Completion.Collect(e.Lines, e.Row, w.Start, [], []);
        var popup = new TuiApp(new Term(keys)).RunModal(new CompletionPopup(e, pool, () => Themes.Get("dos-blue")));
        return (e, popup);
    }

    [Fact]
    public void Enter_takes_the_chosen_word()
    {
        var (e, p) = Popup("alpha alpine al", 15, K(Key.Down), K(Key.Enter));
        Assert.Equal("alpine", p.Accepted);
        Assert.Equal("alpha alpine alpine", e.Lines[0]);
    }

    [Fact]
    public void Typing_narrows_the_list_and_goes_into_the_text()
    {
        var (e, p) = Popup("alpha alpine al", 15, C('p'), C('i'), K(Key.Tab));
        Assert.Equal("alpha alpine alpine", e.Lines[0]);
    }

    [Fact]
    public void A_key_that_ends_the_word_closes_the_list_and_still_types()
    {
        var (e, p) = Popup("alpha alpine al", 15, C(' '));
        Assert.Null(p.Accepted);
        Assert.Equal("alpha alpine al ", e.Lines[0]);
    }

    [Fact]
    public void Escape_closes_without_changing_the_text()
    {
        var (e, _) = Popup("alpha alpine al", 15, K(Key.Escape));
        Assert.Equal("alpha alpine al", e.Lines[0]);
    }

    // ---------- opening by itself while typing ----------
    (EditorView, CompletionPopup) AutoPopup(string text, int col, params InputEvent[] keys)
    {
        var e = Open(text, "a.cs");
        e.SetArea(new Rect(0, 1, 80, 20));
        e.GoToLine(1, col + 1);
        var w = e.WordBeforeCaret()!.Value;
        var pool = Completion.Collect(e.Lines, e.Row, w.Start, [], []);
        var popup = new TuiApp(new Term(keys)).RunModal(new CompletionPopup(e, pool, () => Themes.Get("dos-blue"), auto: true));
        return (e, popup);
    }

    [Fact]
    public void Opened_by_itself_Enter_starts_a_new_line_unless_a_word_was_chosen()
    {
        var (e, p) = AutoPopup("alpha alpine alp", 16, K(Key.Enter));
        Assert.Null(p.Accepted);
        Assert.Equal(new[] { "alpha alpine alp", "" }, e.Lines.Take(2));
        var (e2, p2) = AutoPopup("alpha alpine alp", 16, K(Key.Down), K(Key.Enter));
        Assert.Equal("alpine", p2.Accepted);
        var (e3, _) = AutoPopup("alpha alpine alp", 16, K(Key.Tab));
        Assert.Equal("alpha alpine alpha", e3.Lines[0]);
    }

    [Fact]
    public void Opens_by_itself_only_where_the_setting_and_the_place_allow()
    {
        var code = Open("var count = 1;\ncou\n", "a.cs");
        code.GoToLine(2, 4);
        Assert.True(code.AutoCompleteHere(3));
        Assert.False(code.AutoCompleteHere(4));                               // not yet four letters
        code.CompleteWhileTyping = "off";
        Assert.False(code.AutoCompleteHere(3));

        var inString = Open("var s = \"cou\";\n", "a.cs");
        inString.GoToLine(1, 13);                                             // after "cou" inside the string
        Assert.False(inString.AutoCompleteHere(3));

        var prose = Open("count\ncou\n", "a.md");
        prose.GoToLine(2, 4);
        Assert.False(prose.AutoCompleteHere(3));
        prose.CompleteWhileTyping = "always";
        Assert.True(prose.AutoCompleteHere(3));

        Assert.Equal("code", new Settings().CompleteWhileTyping);
        Assert.Equal("off", Settings.Parse("""{ "completeWhileTyping": false }""").Settings.CompleteWhileTyping);
    }
}
