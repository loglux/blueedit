using System.Text;
using Tui;
using Xunit;
using KeyRecord = Tui.WindowsKeys.KeyRecord;
using MouseRecord = Tui.WindowsKeys.MouseRecord;

/// <summary>Windows key events turned into VT text (WindowsKeys), then read by the parser as any terminal's input.</summary>
public sealed class WindowsKeysTests
{
    const uint LeftCtrl = 0x8, LeftAlt = 0x2, RightAlt = 0x1, Shift = 0x10;

    static List<InputEvent> Events(params object[] records)
    {
        var keys = new WindowsKeys { Events = true };
        var sb = new StringBuilder();
        foreach (var r in records)
            if (r is KeyRecord k) keys.Key(k, sb);
            else keys.Mouse((MouseRecord)r, sb);
        keys.Flush(sb);
        var parser = new InputParser();
        var output = new List<InputEvent>();
        parser.Feed(sb.ToString(), output);
        parser.FlushPending(output);
        return output;
    }

    static KeyRecord Down(int vk, char ch = '\0', uint state = 0) => new(true, 1, vk, ch, state);

    [Theory]
    [InlineData('\u001f')]           // Latin layout: the control character
    [InlineData('.')]                // Russian layout: the key types "."
    [InlineData('\0')]               // or nothing
    public void Ctrl_slash_is_found_by_the_key_whatever_the_layout_typed(char typed)
    {
        Assert.Equal(new KeyInput(Key.Character, '/', Mods.Ctrl), Assert.Single(Events(Down(0xBF, typed, LeftCtrl))));
    }

    [Fact]
    public void Ctrl_with_a_digit_arrives_at_all()
    {
        Assert.Equal(new KeyInput(Key.Character, '1', Mods.Ctrl), Assert.Single(Events(Down(0x31, '\0', LeftCtrl))));
    }

    [Fact]
    public void Ctrl_letters_are_control_characters_in_any_layout()
    {
        Assert.Equal(new KeyInput(Key.Character, 's', Mods.Ctrl), Assert.Single(Events(Down(0x53, 'ы', LeftCtrl))));
    }

    [Fact]
    public void Alt_letter_keeps_the_typed_letter_for_the_layout_mapping_and_alt_punctuation_takes_the_key()
    {
        Assert.Equal(new KeyInput(Key.Character, 'а', Mods.Alt), Assert.Single(Events(Down(0x46, 'а', LeftAlt))));
        Assert.Equal(new KeyInput(Key.Character, '.', Mods.Alt), Assert.Single(Events(Down(0xBE, 'ю', LeftAlt))));
    }

    [Fact]
    public void AltGr_types_its_character()
    {
        Assert.Equal(new KeyInput(Key.Character, '@'), Assert.Single(Events(Down(0x51, '@', RightAlt | LeftCtrl))));
    }

    [Fact]
    public void Special_keys_with_modifiers()
    {
        Assert.Equal(new KeyInput(Key.Right, Mods: Mods.Ctrl), Assert.Single(Events(Down(0x27, '\0', LeftCtrl))));
        Assert.Equal(new KeyInput(Key.F7, Mods: Mods.Shift), Assert.Single(Events(Down(0x76, '\0', Shift))));
        Assert.Equal(new KeyInput(Key.F1), Assert.Single(Events(Down(0x70))));
        Assert.Equal(new KeyInput(Key.Tab, Mods: Mods.Shift), Assert.Single(Events(Down(0x09, '\t', Shift))));
        Assert.Equal(new KeyInput(Key.Backspace), Assert.Single(Events(Down(0x08, '\b'))));
        Assert.Equal(new KeyInput(Key.Backspace, Mods: Mods.Ctrl), Assert.Single(Events(Down(0x08, '\u007f', LeftCtrl))));
        Assert.Equal(new KeyInput(Key.Enter), Assert.Single(Events(Down(0x0D, '\r', LeftCtrl))));
        Assert.Equal(new KeyInput(Key.Escape), Assert.Single(Events(Down(0x1B, '\u001b'))));
        Assert.Equal(new KeyInput(Key.Character, ' ', Mods.Ctrl), Assert.Single(Events(Down(0x20, ' ', LeftCtrl))));
    }

    [Fact]
    public void Releases_and_lone_modifier_keys_give_nothing_and_repeats_repeat()
    {
        Assert.Empty(Events(new KeyRecord(false, 1, 0x41, 'a', 0), Down(0x11, '\0', LeftCtrl), Down(0x10, '\0', Shift)));
        Assert.Equal(3, Events(new KeyRecord(true, 3, 0x41, 'a', 0)).Count);
    }

    static KeyRecord Up(int vk, char ch) => new(false, 1, vk, ch, 0);

    [Fact]
    public void Windows_terminals_paste_is_one_paste_whatever_keys_the_console_found_for_it()
    {
        // As measured: Latin letters under the Russian layout with no key, space and Enter with theirs, each with its
        // release; all in one read
        var recs = new List<object>();
        foreach (var c in "if x:\r    y")
        {
            int vk = c switch { ' ' => 0x20, '\r' => 0x0D, ':' => 0xBA, _ => 0 };
            recs.Add(Down(vk, c)); recs.Add(Up(vk, c));
        }
        Assert.Equal(new PasteInput("if x:\r    y"), Assert.Single(Events(recs.ToArray())));
        // the same with the Latin layout: every character with its key
        Assert.Equal(new PasteInput("ab\rc"), Assert.Single(Events(Down(0x41, 'a'), Down(0x42, 'b'), Down(0x0D, '\r'), Down(0x43, 'c'))));
    }

    [Fact]
    public void A_short_line_in_one_read_is_typed_and_an_enter_that_ends_it_is_a_typed_enter()
    {
        // a name typed and Enter in a burst (a dialog's field), or a word pasted: typed, so completion and Enter work
        Assert.Equal(new InputEvent[]
        {
            new KeyInput(Key.Character, 'n'), new KeyInput(Key.Character, 'o'), new KeyInput(Key.Enter),
        }, Events(Down(0, 'n'), Down(0, 'o'), Down(0x0D, '\r')));
        // a paste that ends with a line break: the text pasted, the last Enter typed
        Assert.Equal(new InputEvent[] { new PasteInput("a\rb"), new KeyInput(Key.Enter) },
            Events(Down(0, 'a'), Down(0x0D, '\r'), Down(0, 'b'), Down(0x0D, '\r')));
        // a long line is a paste
        Assert.IsType<PasteInput>(Assert.Single(Events("x = compute(alpha, beta)".Select(c => (object)Down(0, c)).ToArray())));
    }

    [Fact]
    public void Typing_a_key_a_read_stays_typing()
    {
        var keys = new WindowsKeys { Events = true };
        var sb = new StringBuilder();
        foreach (var k in new[] { Down(0x41, 'a'), Up(0x41, 'a'), Down(0x0D, '\r'), Down(0x09, '\t') })
        {
            keys.Key(k, sb);
            keys.Flush(sb);                                                   // a read each
        }
        Assert.Equal("a\r\t", sb.ToString());
    }

    [Fact]
    public void Text_without_keys_is_a_paste_and_a_terminal_reply_passes_as_it_is()
    {
        var paste = Events(Down(0, 'a'), Down(0, '('), Down(0, '\r'), Down(0, 'b'));
        Assert.Equal(new PasteInput("a(\rb"), Assert.Single(paste));
        Assert.Equal(new KeyInput(Key.Character, 'ж'), Assert.Single(Events(Down(0, 'ж'))));           // an IME's character
        var reply = Events("\u001b[6;20;10t".Select(c => (object)Down(0, c)).ToArray());
        Assert.IsType<TerminalReply>(Assert.Single(reply));
    }

    [Fact]
    public void Mouse_presses_drags_releases_and_the_wheel()
    {
        var e = Events(
            new MouseRecord(4, 2, 1, 0, 0),                 // left down at column 5, row 3
            new MouseRecord(6, 2, 1, 0, 1),                 // moved with it held
            new MouseRecord(6, 2, 0, 0, 0),                 // released
            new MouseRecord(6, 2, 0, 0, 1),                 // moved, nothing held: nothing
            new MouseRecord(1, 1, 0x00780000, 0, 4),        // wheel away from the user
            new MouseRecord(1, 1, 0xFF880000, LeftCtrl, 4));
        Assert.Equal(new InputEvent[]
        {
            new MouseInput(MouseKind.Down, 0, 4, 2, Mods.None),
            new MouseInput(MouseKind.Drag, 0, 6, 2, Mods.None),
            new MouseInput(MouseKind.Up, 0, 6, 2, Mods.None),
            new MouseInput(MouseKind.WheelUp, 0, 1, 1, Mods.None),
            new MouseInput(MouseKind.WheelDown, 0, 1, 1, Mods.Ctrl),
        }, e);
    }

    [Fact]
    public void Without_key_events_every_record_is_text_as_before()
    {
        var keys = new WindowsKeys();
        var sb = new StringBuilder();
        foreach (var c in "\u001b[1;5Cab") keys.Key(Down(0, c), sb);
        keys.Key(Down(0x40, '\0', LeftCtrl), sb);                                 // Ctrl+Space: the console's Ctrl+@
        keys.Key(Down(0x32, '\0', LeftCtrl | Shift), sb);                         // the same with "@" on Shift+2
        keys.Flush(sb);
        Assert.Equal("\u001b[1;5Cab\0\0", sb.ToString());
    }
}
