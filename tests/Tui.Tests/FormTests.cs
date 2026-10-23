using Tui;
using Xunit;

public class FormTests
{
    static readonly KeyInput Enter = new(Key.Enter), Esc = new(Key.Escape), Tab = new(Key.Tab), Right = new(Key.Right);
    static KeyInput Alt(char c) => new(Key.Character, c, Mods.Alt);

    static FormDialog Find(string initial = "") => new("Find", new[] { new FormField("Find:", initial), new FormField("Replace with:") },
        new[] { new FormToggle("Match _case"), new FormToggle("_Whole word", true) });

    [Fact]
    public void Fields_tab_and_toggles_come_back_on_enter()
    {
        var term = new FakeTerminal().Type("foo").Keys(Tab).Type("bar").Keys(Alt('c'), Alt('w'), Enter);
        var d = new TuiApp(term).RunModal(Find());
        Assert.Equal(new[] { "foo", "bar" }, d.Values);
        Assert.Equal(new[] { true, false }, d.Toggles);
    }

    [Fact]
    public void Escape_cancels()
    {
        var d = new TuiApp(new FakeTerminal().Type("x").Keys(Esc)).RunModal(Find());
        Assert.Null(d.Values);
    }

    [Fact]
    public void Typing_replaces_the_offered_text_but_moving_first_keeps_it()
    {
        Assert.Equal("new", new TuiApp(new FakeTerminal().Type("new").Keys(Enter)).RunModal(Find("old")).Values![0]);
        Assert.Equal("old!", new TuiApp(new FakeTerminal().Keys(Right).Type("!").Keys(Enter)).RunModal(Find("old")).Values![0]);
    }

    [Fact]
    public void Toggles_work_with_a_russian_keyboard_layout()
    {
        var d = new TuiApp(new FakeTerminal().Keys(Alt('с'), Enter)).RunModal(Find());     // Cyrillic es: the C key
        Assert.True(d.Toggles[0]);
    }

    [Fact]
    public void Dialog_shows_labels_and_toggle_states()
    {
        var term = new FakeTerminal().Keys(Esc);
        new TuiApp(term).RunModal(Find("abc"));
        var all = term.All();
        Assert.Contains("Find:", all, StringComparison.Ordinal);
        Assert.Contains("[ ] Match case", all, StringComparison.Ordinal);
        Assert.Contains("[x] Whole word", all, StringComparison.Ordinal);
    }

    static readonly KeyInput Space = new(Key.Character, ' '), Down = new(Key.Down);

    [Fact]
    public void Check_list_toggles_with_space_and_auto_excludes_the_rest()
    {
        var items = new[] { "Auto", "ru-RU", "en-GB", "en-US" };
        var d = new TuiApp(new FakeTerminal().Keys(Down, Space, Down, Space, Enter))
            .RunModal(new CheckListDialog("Langs", items, new[] { true, false, false, false }, firstIsExclusive: true));
        Assert.Equal(new[] { false, true, true, false }, d.Checked);           // picking a language turns Auto off
        var back = new TuiApp(new FakeTerminal().Keys(Space, Enter))
            .RunModal(new CheckListDialog("Langs", items, new[] { false, true, true, false }, firstIsExclusive: true));
        Assert.Equal(new[] { true, false, false, false }, back.Checked);       // Auto clears the others
    }

    [Fact]
    public void Check_list_escape_cancels()
    {
        var d = new TuiApp(new FakeTerminal().Keys(Space, Esc)).RunModal(new CheckListDialog("L", new[] { "a" }, new[] { false }));
        Assert.Null(d.Checked);
    }

    [Theory]
    [InlineData('y', 'y')]
    [InlineData('Y', 'y')]
    [InlineData('н', 'y')]          // Russian layout, Y key
    [InlineData('ф', 'a')]          // Russian layout, A key
    public void Key_prompt_takes_the_key_in_either_layout(char typed, char expected)
    {
        var p = new TuiApp(new FakeTerminal().Keys(new KeyInput(Key.Character, typed))).RunModal(new KeyPrompt("Replace?", "yna"));
        Assert.Equal(expected, p.Result);
    }

    [Fact]
    public void Key_prompt_ignores_other_keys_and_esc_stops()
    {
        var p = new TuiApp(new FakeTerminal().Type("xz").Keys(Esc)).RunModal(new KeyPrompt("Replace?", "yna"));
        Assert.Null(p.Result);
    }

    [Theory]
    [InlineData('а', 'f')]
    [InlineData('я', 'z')]
    [InlineData('f', 'f')]
    [InlineData('1', '1')]
    public void Alt_with_a_russian_letter_becomes_the_latin_key(char typed, char latin)
    {
        var ev = (KeyInput)TuiApp.Normalize(new KeyInput(Key.Character, typed, Mods.Alt));
        Assert.Equal(latin, ev.Character);
        var plain = (KeyInput)TuiApp.Normalize(new KeyInput(Key.Character, typed));
        Assert.Equal(typed, plain.Character);                     // ordinary typing is left alone
    }
}
