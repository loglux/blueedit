using Tui;
using Xunit;

public class InputParserTests
{
    static List<InputEvent> Parse(string input, bool flushTimeout = false)
    {
        var p = new InputParser();
        var events = new List<InputEvent>();
        p.Feed(input, events);
        if (flushTimeout) p.FlushPending(events);
        return events;
    }

    static KeyInput One(string input, bool flush = false) => Assert.IsType<KeyInput>(Assert.Single(Parse(input, flush)));

    [Theory]
    [InlineData("a", 'a', Mods.None)]
    [InlineData("Z", 'Z', Mods.None)]
    [InlineData("2", '2', Mods.None)]
    [InlineData("-", '-', Mods.None)]
    [InlineData("ж", 'ж', Mods.None)]
    [InlineData("Ж", 'Ж', Mods.None)]
    [InlineData("\u0002", 'b', Mods.Ctrl)]        // Ctrl+B
    [InlineData("\u0011", 'q', Mods.Ctrl)]        // Ctrl+Q
    [InlineData("\u001a", 'z', Mods.Ctrl)]        // Ctrl+Z
    [InlineData("\0", ' ', Mods.Ctrl)]            // Ctrl+Space
    public void Printable_and_control_characters(string input, char ch, Mods mods)
    {
        var k = One(input);
        Assert.Equal(Key.Character, k.Key);
        Assert.Equal(ch, k.Character);
        Assert.Equal(mods, k.Mods);
    }

    [Theory]
    [InlineData("\r", Key.Enter)]
    [InlineData("\t", Key.Tab)]
    [InlineData("\u007f", Key.Backspace)]
    public void Simple_keys(string input, Key key) => Assert.Equal(key, One(input).Key);

    [Fact]
    public void Ctrl_backspace_arrives_as_BS()
    {
        var k = One("\b");
        Assert.Equal(Key.Backspace, k.Key);
        Assert.True(k.Ctrl);
    }

    [Fact]
    public void Lone_escape_waits_for_the_timeout_and_then_is_the_Escape_key()
    {
        var p = new InputParser();
        var events = new List<InputEvent>();
        p.Feed("\x1b", events);
        Assert.Empty(events);
        Assert.True(p.HasPending);
        p.FlushPending(events);
        Assert.Equal(Key.Escape, Assert.IsType<KeyInput>(Assert.Single(events)).Key);
        Assert.False(p.HasPending);
    }

    [Theory]
    [InlineData("\x1b[A", Key.Up)]
    [InlineData("\x1b[B", Key.Down)]
    [InlineData("\x1b[C", Key.Right)]
    [InlineData("\x1b[D", Key.Left)]
    [InlineData("\x1b[H", Key.Home)]
    [InlineData("\x1b[F", Key.End)]
    [InlineData("\x1b[5~", Key.PageUp)]
    [InlineData("\x1b[6~", Key.PageDown)]
    [InlineData("\x1b[2~", Key.Insert)]
    [InlineData("\x1b[3~", Key.Delete)]
    [InlineData("\x1bOP", Key.F1)]
    [InlineData("\x1bOQ", Key.F2)]
    [InlineData("\x1bOR", Key.F3)]
    [InlineData("\x1bOS", Key.F4)]
    [InlineData("\x1b[15~", Key.F5)]
    [InlineData("\x1b[17~", Key.F6)]
    [InlineData("\x1b[18~", Key.F7)]
    [InlineData("\x1b[19~", Key.F8)]
    [InlineData("\x1b[20~", Key.F9)]
    [InlineData("\x1b[21~", Key.F10)]
    [InlineData("\x1b[23~", Key.F11)]
    [InlineData("\x1b[24~", Key.F12)]
    public void Navigation_and_function_keys(string input, Key key)
    {
        var k = One(input);
        Assert.Equal(key, k.Key);
        Assert.Equal(Mods.None, k.Mods);
    }

    [Theory]
    [InlineData("\x1b[1;5C", Key.Right, Mods.Ctrl)]
    [InlineData("\x1b[1;5D", Key.Left, Mods.Ctrl)]
    [InlineData("\x1b[1;2B", Key.Down, Mods.Shift)]
    [InlineData("\x1b[1;6C", Key.Right, Mods.Shift | Mods.Ctrl)]
    [InlineData("\x1b[1;3A", Key.Up, Mods.Alt)]
    [InlineData("\x1b[6;5~", Key.PageDown, Mods.Ctrl)]
    [InlineData("\x1b[2;5~", Key.Insert, Mods.Ctrl)]
    [InlineData("\x1b[2;2~", Key.Insert, Mods.Shift)]
    [InlineData("\x1b[3;2~", Key.Delete, Mods.Shift)]
    [InlineData("\x1b[17;2~", Key.F6, Mods.Shift)]
    [InlineData("\x1b[1;2Q", Key.F2, Mods.Shift)]
    public void Modifiers_on_keys(string input, Key key, Mods mods)
    {
        var k = One(input);
        Assert.Equal(key, k.Key);
        Assert.Equal(mods, k.Mods);
    }

    [Fact]
    public void Shift_tab() => Assert.Equal(new KeyInput(Key.Tab, Mods: Mods.Shift), One("\x1b[Z"));

    [Theory]
    [InlineData("\u001bf", 'f')]
    [InlineData("\u001bh", 'h')]
    [InlineData("\u001b1", '1')]
    [InlineData("\u001bz", 'z')]
    public void Alt_letter_and_digit_is_ESC_prefix(string input, char ch)
    {
        var k = One(input);
        Assert.Equal(ch, k.Character);
        Assert.Equal(Mods.Alt, k.Mods);
    }

    [Fact]
    public void Modify_other_keys_and_csi_u_forms()
    {
        Assert.Equal(new KeyInput(Key.Character, 'k', Mods.Ctrl), One("\x1b[27;5;107~"));
        Assert.Equal(new KeyInput(Key.Character, 'k', Mods.Ctrl | Mods.Shift), One("\x1b[107;6u"));
        Assert.Equal(new KeyInput(Key.Enter, Mods: Mods.Shift), One("\x1b[13;2u"));
    }

    [Fact]
    public void Sequences_split_across_reads_are_reassembled()
    {
        var p = new InputParser();
        var events = new List<InputEvent>();
        p.Feed("\x1b", events); p.Feed("[", events); p.Feed("1;5", events);
        Assert.Empty(events);
        p.Feed("C", events);
        Assert.Equal(new KeyInput(Key.Right, Mods: Mods.Ctrl), Assert.Single(events));
    }

    [Fact]
    public void Several_events_in_one_read()
    {
        var events = Parse("ab\x1b[A\r");
        Assert.Equal(4, events.Count);
        Assert.Equal(new KeyInput(Key.Up), events[2]);
        Assert.Equal(new KeyInput(Key.Enter), events[3]);
    }

    [Theory]
    [InlineData("\x1b[<0;10;5M", MouseKind.Down, 0, 9, 4)]
    [InlineData("\x1b[<0;10;5m", MouseKind.Up, 0, 9, 4)]
    [InlineData("\x1b[<2;1;1M", MouseKind.Down, 2, 0, 0)]
    [InlineData("\x1b[<32;20;7M", MouseKind.Drag, 0, 19, 6)]
    [InlineData("\x1b[<35;3;4M", MouseKind.Move, 0, 2, 3)]
    [InlineData("\x1b[<64;8;8M", MouseKind.WheelUp, 0, 7, 7)]
    [InlineData("\x1b[<65;8;8M", MouseKind.WheelDown, 0, 7, 7)]
    public void Sgr_mouse(string input, MouseKind kind, int button, int x, int y)
    {
        var m = Assert.IsType<MouseInput>(Assert.Single(Parse(input)));
        Assert.Equal((kind, button, x, y), (m.Kind, m.Button, m.X, m.Y));
    }

    [Fact]
    public void Mouse_modifiers() =>
        Assert.Equal(Mods.Ctrl | Mods.Shift, Assert.IsType<MouseInput>(Assert.Single(Parse("\x1b[<20;1;1M"))).Mods);

    [Fact]
    public void Bracketed_paste_is_one_event_with_the_exact_text()
    {
        var events = Parse("\x1b[200~line one\nline \x1b[ two\x1b[201~");
        Assert.Equal("line one\nline \x1b[ two", Assert.IsType<PasteInput>(Assert.Single(events)).Text);
    }

    [Fact]
    public void Paste_split_across_reads()
    {
        var p = new InputParser();
        var events = new List<InputEvent>();
        p.Feed("\x1b[200~hel", events); p.Feed("lo wor", events); p.Feed("ld\x1b[20", events);
        Assert.Empty(events);
        p.Feed("1~x", events);
        Assert.Equal("hello world", Assert.IsType<PasteInput>(events[0]).Text);
        Assert.Equal(new KeyInput(Key.Character, 'x'), events[1]);
    }

    [Fact]
    public void Unknown_sequences_are_swallowed_without_blocking_later_input()
    {
        var events = Parse("\x1b[99;99Zq");
        Assert.Contains(new KeyInput(Key.Character, 'q'), events);
    }

    [Fact]
    public void Kitty_keys_as_measured_in_windows_terminal_over_ssh()
    {
        // #184: flags 1 + 4 pushed; with Ctrl or Alt the base layout's key counts, not what the layout types
        Assert.Equal(new KeyInput(Key.Character, '/', Mods.Ctrl), One("\u001b[1105::47;5u"));      // Ctrl+/ with Russian
        Assert.Equal(new KeyInput(Key.Character, '/', Mods.Ctrl), One("\u001b[47;5u"));            // and with English
        Assert.Equal(new KeyInput(Key.Character, '1', Mods.Ctrl), One("\u001b[8470::49;5u"));      // Ctrl+1 with Russian
        Assert.Equal(new KeyInput(Key.Character, 'x', Mods.Alt), One("\u001b[120;3u"));
        Assert.Equal(new KeyInput(Key.Escape), One("\u001b[27;129u"));                           // 128: Num Lock on
        Assert.Equal(new KeyInput(Key.Character, 's', Mods.Ctrl), One("\u001b[115;5u"));
        Assert.Equal(new KeyInput(Key.Character, 'a', Mods.Ctrl | Mods.Shift), One("\u001b[97:65;6u"));
        Assert.Equal(new KeyInput(Key.Character, 'f', Mods.Alt), One("\u001b[1092::102;3u"));      // Alt+а with Russian
    }

    [Fact]
    public void The_kitty_flags_answer_is_a_reply_not_a_key()
    {
        var events = Parse("\u001b[?0uq");
        var reply = Assert.IsType<TerminalReply>(events[0]);
        Assert.Equal('u', reply.Final);
        Assert.Equal([0], reply.Values);
        Assert.Equal(new KeyInput(Key.Character, 'q'), events[1]);
    }
}
