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

    [Fact]
    public void A_finding_shows_its_fixes_as_buttons_then_its_explanation_whole_then_the_actions_and_who_found_it()
    {
        const string message = "The plural subject \"examples\" takes the plural verb \"check\", not the singular \"checks\", "
            + "which agrees with a singular subject only.";
        var term = new FakeTerminal().Keys(Esc);
        var d = new TuiApp(term).RunModal(new FixDialog("Grammar: \"checks\"", new[] { "check" }, message, new[] { "Ignore", "Never show this" }, "claude-haiku-5-5"));
        Assert.Null(d.Result);
        var rows = term.All().Split('\n');
        int fix = Array.FindIndex(rows, r => r.Contains("[► check ◄]", StringComparison.Ordinal));
        int first = Array.FindIndex(rows, r => r.Contains("The plural subject", StringComparison.Ordinal));
        int actions = Array.FindIndex(rows, r => r.Contains("[  Ignore  ] [  Never show this  ]", StringComparison.Ordinal));
        Assert.True(fix > 0 && first > fix && actions > first, term.All());
        // the checker at the right of the actions, as information: not a button
        Assert.Matches(@"Never show this  \]\s+claude-haiku-5-5 ┃", rows[actions]);
        // every word of the message on the screen, none cut at the frame
        var shown = string.Join(" ", rows.Skip(first).Take(actions - first).Select(r => r.Trim('┃', ' ')));
        foreach (var word in message.Split(' ')) Assert.Contains(word, shown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_finding_takes_the_focused_button_down_goes_to_the_actions_and_esc_takes_nothing()
    {
        FixDialog Make() => new("Spelling: \"recieve\"", new[] { "receive", "relieve" }, "", new[] { "Add to dictionary", "Ignore" }, "Windows spelling");
        Assert.Equal(0, new TuiApp(new FakeTerminal().Keys(Enter)).RunModal(Make()).Result);
        Assert.Equal(1, new TuiApp(new FakeTerminal().Keys(Right, Enter)).RunModal(Make()).Result);
        Assert.Equal(2, new TuiApp(new FakeTerminal().Keys(Down, Enter)).RunModal(Make()).Result);    // to "Add to dictionary"
        Assert.Equal(3, new TuiApp(new FakeTerminal().Keys(Tab, Tab, Tab, Enter)).RunModal(Make()).Result);
        Assert.Null(new TuiApp(new FakeTerminal().Keys(Esc)).RunModal(Make()).Result);
    }

    [Fact]
    public void A_finding_without_a_fix_says_so()
    {
        var term = new FakeTerminal().Keys(Enter);
        var d = new TuiApp(term).RunModal(new FixDialog("Grammar: \"go\"", Array.Empty<string>(), "Possible agreement error", new[] { "Ignore" }, "LanguageTool"));
        Assert.Equal(0, d.Result);                                       // Enter: the first button, Ignore
        Assert.Contains("No fix offered: the checker only names the problem.", term.All(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_above_buttons_keeps_its_lines_and_has_no_line_about_fixes()
    {
        var term = new FakeTerminal().Keys(Right, Enter);
        var table = "pyright    Python: errors, completion    found\ncsharp-ls  C#: errors, completion        not installed";
        var d = new TuiApp(term).RunModal(new FixDialog("Code tools", Array.Empty<string>(), table, new[] { "Install the missing", "Remove", "Close" },
            none: "", table: true));
        Assert.Equal(1, d.Result);                                       // Right: the second button
        var rows = term.All().Split('\n');
        Assert.Contains(rows, r => r.Contains("pyright    Python: errors, completion    found", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Contains("csharp-ls  C#: errors, completion        not installed", StringComparison.Ordinal));
        Assert.DoesNotContain("No fix offered", term.All(), StringComparison.Ordinal);
    }

    [Fact]
    public void Buttons_under_a_list_are_reached_with_tab_and_come_after_the_entries()
    {
        ListDialog Make() => new("Set up", new[] { "a", "b", "c" }, buttons: new[] { "Remove...", "Other" });
        Assert.Equal(1, new TuiApp(new FakeTerminal().Keys(Down, Enter)).RunModal(Make()).Result);
        Assert.Equal(3, new TuiApp(new FakeTerminal().Keys(Tab, Enter)).RunModal(Make()).Result);
        Assert.Equal(4, new TuiApp(new FakeTerminal().Keys(Tab, Right, Enter)).RunModal(Make()).Result);
        Assert.Equal(0, new TuiApp(new FakeTerminal().Keys(Tab, Tab, Tab, Enter)).RunModal(Make()).Result);   // round to the list
        var term = new FakeTerminal().Keys(Tab, Esc);
        Assert.Null(new TuiApp(term).RunModal(Make()).Result);
        Assert.Contains("[► Remove... ◄]", term.All(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_is_as_wide_as_its_line_of_keys()
    {
        var term = new FakeTerminal().Keys(Esc);
        new TuiApp(term).RunModal(new ListDialog("L", new[] { "a" }, buttons: new[] { "B" }));
        var keys = term.All().Split('\n').First(r => r.Contains("Esc = cancel", StringComparison.Ordinal));
        Assert.Matches(@"Esc = cancel\s*┃", keys);                        // inside the frame
    }

    [Fact]
    public void A_choice_of_one_in_a_check_list_has_round_marks()
    {
        var term = new FakeTerminal().Keys(Esc);
        new TuiApp(term).RunModal(new CheckListDialog("Grammar", new[] { "One", "Two", "Several" }, new[] { true, false, false },
            radio: (on, i) => i < 2 && !on[2]));
        Assert.Contains("(•) One", term.All(), StringComparison.Ordinal);
        Assert.Contains("( ) Two", term.All(), StringComparison.Ordinal);
        Assert.Contains("[ ] Several", term.All(), StringComparison.Ordinal);
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
