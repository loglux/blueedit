using Tui;
using Xunit;

/// <summary>A terminal that replays a script of input events; running out of script fails the test instead of hanging.</summary>
sealed class FakeTerminal(int width = 80, int height = 25) : ITerminal
{
    readonly Queue<InputEvent> _script = new();
    public Screen Screen { get; } = new(width, height);
    public int Frames { get; private set; }

    public FakeTerminal Keys(params InputEvent[] events) { foreach (var e in events) _script.Enqueue(e); return this; }
    public FakeTerminal Type(string text) { foreach (var c in text) _script.Enqueue(new KeyInput(Key.Character, c)); return this; }

    public bool TryNext(out InputEvent? ev, int timeoutMs)
    {
        if (_script.Count == 0 && timeoutMs == 0) { ev = null; return false; }      // "anything waiting?" - no
        if (_script.Count == 0) throw new InvalidOperationException("input script exhausted while the UI was still waiting");
        ev = _script.Dequeue();
        return true;
    }

    public void Present() { Screen.Flush(); Frames++; }
    public void ApplyResize(int w, int h) => Screen.Resize(w, h);

    public string Row(int y) => new string(Enumerable.Range(0, Screen.Width).Select(x => Screen.CharAt(x, y)).ToArray()).TrimEnd();
    public string All() => string.Join("\n", Enumerable.Range(0, Screen.Height).Select(Row));
}

public class WidgetTests
{
    static readonly KeyInput Enter = new(Key.Enter), Esc = new(Key.Escape), Down = new(Key.Down), Up = new(Key.Up),
        Left = new(Key.Left), Right = new(Key.Right), Backspace = new(Key.Backspace), PgDn = new(Key.PageDown), F9 = new(Key.F9);
    static KeyInput Alt(char c) => new(Key.Character, c, Mods.Alt);

    // ---------- prompt ----------
    [Fact]
    public void Prompt_returns_the_typed_text()
    {
        var term = new FakeTerminal().Type("abc").Keys(Backspace).Type("dЖ").Keys(Enter);
        var dlg = new TuiApp(term).RunModal(new PromptDialog("Find", "Search for:"));
        Assert.Equal("abdЖ", dlg.Result);
    }

    [Fact]
    public void Prompt_starts_with_initial_text_and_escape_cancels()
    {
        var term = new FakeTerminal().Type("x").Keys(Esc);
        Assert.Null(new TuiApp(term).RunModal(new PromptDialog("Save as", "File name:", "a.txt")).Result);
    }

    [Fact]
    public void Prompt_draws_a_titled_box_with_label_and_hint()
    {
        var term = new FakeTerminal().Type("li").Keys(Enter);
        new TuiApp(term).RunModal(new PromptDialog("Go to line", "Line number:"));
        var all = term.All();
        Assert.Contains("┏┥Go to line┝", all);
        Assert.Contains("Line number:", all);
        Assert.Contains("Enter = OK    Esc = Cancel", all);
    }

    // ---------- buttons ----------
    static ButtonDialog Unsaved() => new("Unsaved changes", "Save changes to a.txt?", new[] { "Save", "Discard", "Cancel" }, 2);

    [Fact] public void Enter_presses_the_first_button() => Assert.Equal(0, new TuiApp(new FakeTerminal().Keys(Enter)).RunModal(Unsaved()).Result);
    [Fact] public void Escape_cancels() => Assert.Equal(2, new TuiApp(new FakeTerminal().Keys(Esc)).RunModal(Unsaved()).Result);
    [Fact] public void Arrows_move_the_focus() => Assert.Equal(1, new TuiApp(new FakeTerminal().Keys(Right, Enter)).RunModal(Unsaved()).Result);
    [Fact] public void Left_wraps_to_the_last_button() => Assert.Equal(2, new TuiApp(new FakeTerminal().Keys(Left, Enter)).RunModal(Unsaved()).Result);
    [Fact] public void First_letter_picks_a_button() => Assert.Equal(1, new TuiApp(new FakeTerminal().Type("d")).RunModal(Unsaved()).Result);

    [Fact]
    public void Button_dialog_shows_the_question()
    {
        var term = new FakeTerminal().Keys(Esc);
        new TuiApp(term).RunModal(Unsaved());
        Assert.Contains("┏┥Unsaved changes┝", term.All());
        Assert.Contains("Save changes to a.txt?", term.All());
    }

    [Theory]
    [InlineData("Save changes to a.txt?", "Save", "Discard", "Cancel")]
    [InlineData("Replace?", "Replace", "Replace all", "Skip", "Cancel")]
    public void The_buttons_are_centred_inside_the_frame(string question, params string[] buttons)
    {
        var term = new FakeTerminal().Keys(Esc);
        new TuiApp(term).RunModal(new ButtonDialog("Ask", question, buttons, buttons.Length - 1));
        var row = term.All().Split('\n').Single(r => r.Contains(buttons[^1], StringComparison.Ordinal));   // was: past the right border
        var inside = row.Trim()[1..^1];
        Assert.Equal('┃', row.Trim()[0]);
        Assert.Equal('┃', row.Trim()[^1]);
        int left = inside.Length - inside.TrimStart().Length, right = inside.Length - inside.TrimEnd().Length;
        Assert.InRange(left - right, -1, 1);
        Assert.True(left >= 1 && right >= 1, row);
    }

    sealed class StopOnQ(TuiApp app) : IRootView
    {
        public void Draw(Screen screen) { }
        public bool Handle(InputEvent ev) { if (ev is KeyInput { Character: 'q' }) app.RequestStop(); return true; }
        public (int X, int Y)? Cursor => null;
    }

    [Fact]
    public void Tick_runs_from_the_main_loop_and_may_open_a_dialog()
    {
        var term = new FakeTerminal().Type("ab").Keys(Esc).Type("q");
        var app = new TuiApp(term) { TickMs = 0, CoalesceMs = 0 };   // one event per loop, so ticks fall between them
        app.Root = new StopOnQ(app);
        int ticks = 0, dialogs = 0;
        app.Tick = () => { if (++ticks == 2) { app.RunModal(Unsaved()); dialogs++; } };    // the Esc closes it
        app.Run();
        Assert.Equal(1, dialogs);
        Assert.Equal(2, ticks);                                   // after 'a' and 'b'; none after the stopping 'q'
    }

    [Fact]
    public void Waiting_input_is_handled_before_the_next_frame()
    {
        var term = new FakeTerminal().Type(new string('x', 30) + "q");
        var app = new TuiApp(term) { CoalesceMs = 1000 };
        app.Root = new StopOnQ(app);
        app.Run();
        Assert.True(term.Frames <= 2, $"{term.Frames} frames for 31 waiting keys");
    }

    [Fact]
    public void Without_coalescing_every_key_gets_a_frame()
    {
        var term = new FakeTerminal().Type(new string('x', 30) + "q");
        var app = new TuiApp(term) { CoalesceMs = 0 };
        app.Root = new StopOnQ(app);
        app.Run();
        Assert.Equal(31, term.Frames);
    }

    [Fact]
    public void Button_dialog_message_can_have_several_lines()
    {
        var term = new FakeTerminal().Keys(Esc);
        new TuiApp(term).RunModal(new ButtonDialog("Convert", "First line here.\nSecond line here.", new[] { "Yes", "No" }, 1));
        var rows = term.All().Split('\n');
        int first = Array.FindIndex(rows, r => r.Contains("First line here.", StringComparison.Ordinal));
        Assert.True(first >= 0);
        Assert.Contains("Second line here.", rows[first + 1], StringComparison.Ordinal);
        Assert.Contains("Yes", term.All(), StringComparison.Ordinal);
    }

    // an error's text is one long line: wrapped at spaces, its end (what to install) still shown
    [Fact]
    public void Button_dialog_wraps_a_line_wider_than_the_screen()
    {
        var term = new FakeTerminal(60, 20).Keys(Esc);
        var message = "github.com: https needs OpenSSL (libssl), which this system lacks; on Debian or Ubuntu: sudo apt install openssl ca-certificates";
        new TuiApp(term).RunModal(new ButtonDialog("Harper: not installed", message, new[] { "OK" }, 0));
        var inside = term.All().Split('\n').Where(r => r.Contains('┃', StringComparison.Ordinal))
            .Select(r => r.Trim().Trim('┃').Trim()).Where(r => r.Length > 0 && !r.Contains("OK", StringComparison.Ordinal));
        Assert.Equal(message, string.Join(" ", inside));
    }

    // short items, a long title: the box was as wide as the items and cut the title ("...(LibreOffice's, fo")
    [Fact]
    public void A_dialog_is_wide_enough_for_its_title()
    {
        var term = new FakeTerminal(120, 25).Keys(Esc);
        const string title = "Spelling dictionaries (LibreOffice's, for Hunspell)";
        new TuiApp(term).RunModal(new CheckListDialog(title, new[] { "ru-RU   3.5 MB  ru-RU", "en-GB   1.3 MB  en-GB" }, new[] { true, true }, hint: "Enter"));
        Assert.Contains(title, term.All(), StringComparison.Ordinal);
    }

    // ---------- lists ----------
    [Fact]
    public void List_dialog_picks_with_arrows_and_enter()
    {
        var term = new FakeTerminal().Keys(Down, Down, Up, Down, Enter);
        Assert.Equal(2, new TuiApp(term).RunModal(new ListDialog("Outline", new[] { "One", "Two", "Three", "Four" })).Result);
    }

    [Fact]
    public void List_dialog_starts_at_the_given_item_and_escape_gives_null()
    {
        var term = new FakeTerminal().Keys(Esc);
        var dlg = new TuiApp(term).RunModal(new ListDialog("Outline", new[] { "One", "Two" }, selected: 1));
        Assert.Null(dlg.Result);
    }

    [Fact]
    public void Clicking_the_selected_row_again_activates_it()
    {
        var term = new FakeTerminal(80, 25);
        var dlg = new ListDialog("Pick", new[] { "a", "b", "c" });
        // draw the same dialog once to find where row "b" lands, then click it twice
        term.Keys(Esc);
        var app = new TuiApp(term);
        app.RunModal(new ListDialog("Probe", new[] { "a", "b", "c" }));
        int y = Enumerable.Range(0, 25).First(r => term.Row(r).Contains("┃ b", StringComparison.Ordinal));
        int x = term.Row(y).IndexOf("┃ b", StringComparison.Ordinal) + 2;
        term.Keys(new MouseInput(MouseKind.Down, 0, x, y), new MouseInput(MouseKind.Down, 0, x, y));
        Assert.Equal(1, new TuiApp(term).RunModal(dlg).Result);
    }

    [Fact]
    public void Text_dialog_scrolls_and_closes()
    {
        var lines = Enumerable.Range(1, 200).Select(i => $"line {i}").ToArray();
        var term = new FakeTerminal(80, 25).Keys(PgDn, PgDn, PgDn, Esc);
        new TuiApp(term).RunModal(new TextDialog("Help", lines));
        Assert.Contains("┏┥Help┝", term.All());
        Assert.DoesNotContain("┃ line 1 ", term.All());          // scrolled away from the top
        Assert.Contains("Esc closes", term.All());
    }

    [Fact]
    public void File_dialog_navigates_folders_and_picks_a_file()
    {
        var root = Directory.CreateTempSubdirectory("tui-files-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "sub", "inner.txt"), "x");
            File.WriteAllText(Path.Combine(root, "top.txt"), "x");
            // entries: "..", "sub\", "top.txt"  ->  Down, Enter opens sub;  in sub: "..", "inner.txt" -> Down, Enter picks inner.txt
            var term = new FakeTerminal(100, 30).Keys(Down, Enter, Down, Enter);
            var dlg = new TuiApp(term).RunModal(new FileDialog("Open", root));
            Assert.Equal(Path.Combine(root, "sub", "inner.txt"), dlg.Path);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void File_dialog_dot_dot_goes_up_and_shows_the_folder()
    {
        var root = Directory.CreateTempSubdirectory("tui-files-").FullName;
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            var term = new FakeTerminal(100, 30).Keys(Enter, Esc);         // first entry is ".."
            new TuiApp(term).RunModal(new FileDialog("Open", sub));
            Assert.Contains(root, term.All(), StringComparison.Ordinal);
            Assert.Contains("sub" + Path.DirectorySeparatorChar, term.All(), StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------- menu bar ----------
    sealed class Root : IRootView
    {
        public List<InputEvent> Seen { get; } = new();
        public void Draw(Screen screen) => screen.Clear(Style.Default);
        public bool Handle(InputEvent ev) { Seen.Add(ev); return true; }
        public (int X, int Y)? Cursor => (0, 2);
    }

    static (TuiApp App, FakeTerminal Term, List<string> Log, Root Root) MenuApp()
    {
        var log = new List<string>();
        var term = new FakeTerminal(80, 25);
        var app = new TuiApp(term) { Root = new Root() };
        app.Menu = new MenuBar(new[]
        {
            new Menu("_File", new[] { new MenuItem("_New", "Ctrl+N", () => log.Add("new")), new MenuItem("_Open...", "F3", () => log.Add("open")),
                                      new MenuItem("E_xit", "Ctrl+Q", () => { log.Add("exit"); app.RequestStop(); }) }),
            new Menu("_Edit", new[] { new MenuItem("_Undo", "Ctrl+Z", () => log.Add("undo")), new MenuItem("_Redo", "Ctrl+Y", () => log.Add("redo")) }),
            new Menu("Hel_p", new[] { new MenuItem("_Keys and commands", "F1", () => log.Add("help")) }),
        });
        return (app, term, log, (Root)app.Root);
    }

    [Fact]
    public void Bar_layout_matches_the_classic_one()
    {
        var (app, term, _, _) = MenuApp();
        app.Render();
        Assert.Equal(" File  Edit  Help", term.Row(0));
    }

    [Fact]
    public void F9_opens_the_first_menu_and_enter_runs_the_item()
    {
        var (app, term, log, _) = MenuApp();
        term.Keys(F9, Down, Enter, F9, Up, Enter);           // Open..., then Exit (Up wraps to the last item)
        app.Run();
        Assert.Equal(new[] { "open", "exit" }, log);
    }

    [Fact]
    public void Alt_hotkey_opens_its_menu_and_item_hotkey_runs()
    {
        var (app, term, log, _) = MenuApp();
        term.Keys(Alt('e')).Type("r").Keys(Alt('p')).Type("k").Keys(Alt('f')).Type("x");
        app.Run();
        Assert.Equal(new[] { "redo", "help", "exit" }, log);
    }

    [Fact]
    public void Left_and_right_switch_menus_and_escape_closes_without_running()
    {
        var (app, term, log, root) = MenuApp();
        term.Keys(F9, Right, Right, Left, Esc).Type("z").Keys(Alt('f')).Type("x");
        app.Run();
        Assert.Equal(new[] { "exit" }, log);
        Assert.Contains(new KeyInput(Key.Character, 'z'), root.Seen);     // after Esc keys go to the editor again
    }

    [Fact]
    public void Open_menu_swallows_keys_and_draws_its_items()
    {
        var (app, term, log, root) = MenuApp();
        term.Keys(Alt('f'));
        Assert.Throws<InvalidOperationException>(() => app.Run());           // script ends while the menu is still open
        Assert.Empty(root.Seen);
        Assert.Contains("New", term.Row(2));
        Assert.Contains("Ctrl+N", term.Row(2));
        Assert.Contains("Open...", term.Row(3));
    }

    [Fact]
    public void A_setting_in_a_menu_shows_its_state_now()
    {
        // √ before what is on, • before the chosen one of several, the value at the right; asked at each drawing
        bool wrap = true;
        var term = new FakeTerminal(80, 25);
        var app = new TuiApp(term) { Root = new Root() };
        app.Menu = new MenuBar(new[]
        {
            new Menu("_View", new[]
            {
                new MenuItem("Soft _wrap", "Alt+Z", () => wrap = !wrap) { On = () => wrap },
                new MenuItem("_Dark", "", () => { }) { On = () => false, Choice = true },
                new MenuItem("_Light", "", () => { }) { On = () => true, Choice = true },
                new MenuItem("Line _endings", "", () => { }) { Value = () => "CRLF" },
            }),
        });
        term.Keys(Alt('v'));
        Assert.Throws<InvalidOperationException>(() => app.Run());
        Assert.Contains("┃√Soft wrap", term.Row(2), StringComparison.Ordinal);
        Assert.Contains("┃ Dark", term.Row(3), StringComparison.Ordinal);
        Assert.Contains("┃•Light", term.Row(4), StringComparison.Ordinal);
        Assert.Matches(@"┃ Line endings\s+CRLF ┃", term.Row(5));
        wrap = false;
        app.Render();
        Assert.Contains("┃ Soft wrap", term.Row(2), StringComparison.Ordinal);
    }

    [Fact]
    public void Mouse_opens_a_menu_and_picks_an_item()
    {
        var (app, term, log, _) = MenuApp();
        // " File  Edit  Help": "Edit" starts at column 7; its first item is on row 2
        term.Keys(new MouseInput(MouseKind.Down, 0, 8, 0), new MouseInput(MouseKind.Down, 0, 9, 2), Alt('f')).Type("x");
        app.Run();
        Assert.Equal(new[] { "undo", "exit" }, log);
    }

    [Fact]
    public void Keys_reach_the_root_view_when_no_menu_is_open()
    {
        var (app, term, _, root) = MenuApp();
        term.Type("ab").Keys(Alt('f')).Type("x");
        app.Run();
        Assert.Equal(new InputEvent[] { new KeyInput(Key.Character, 'a'), new KeyInput(Key.Character, 'b') }, root.Seen);
    }
}

public class MenuBarValidationTests
{
    [Fact]
    public void Duplicate_item_hotkeys_in_a_menu_are_rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => new MenuBar(new[]
        {
            new Menu("_Tools", new[] { new MenuItem("_Outline", "", () => { }), new MenuItem("Spell check _on/off", "", () => { }) }),
        }));
        Assert.Contains("'o'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Opened_is_called_when_the_bar_opens_not_when_it_moves_to_the_next_menu()
    {
        int opened = 0;
        var bar = new MenuBar(new[]
        {
            new Menu("_File", new[] { new MenuItem("_New", "", () => { }) }),
            new Menu("_Edit", new[] { new MenuItem("_Undo", "", () => { }) }),
        }) { Opened = () => opened++ };
        bar.Open(0); bar.Open(1);
        Assert.Equal(1, opened);
        bar.Close(); bar.Open(1);
        Assert.Equal(2, opened);
    }

    [Fact]
    public void Duplicate_menu_hotkeys_on_the_bar_are_rejected() =>
        Assert.Throws<ArgumentException>(() => new MenuBar(new[]
        {
            new Menu("_File", new[] { new MenuItem("_New", "", () => { }) }),
            new Menu("_Format", new[] { new MenuItem("_Bold", "", () => { }) }),
        }));
}
