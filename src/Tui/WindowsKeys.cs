using System.Text;

namespace Tui;

/// <summary>
/// Windows console input read as key events (<c>ReadConsoleInputW</c> without VT input mode), turned into the VT text
/// <see cref="InputParser"/> reads, so the rest of the input path stays the same on every platform. Key events carry
/// the key itself (its virtual key code), which VT text loses: Ctrl+/ with the Russian layout arrives as "." or
/// nothing as text, but as the "/" key here (#184). Measured with a pseudo-console probe; decisions in
/// docs/tech/tui.md, "Key events on Windows".
/// </summary>
sealed class WindowsKeys
{
    /// <summary>One KEY_EVENT_RECORD.</summary>
    public readonly record struct KeyRecord(bool Down, int Repeat, int Vk, char Ch, uint State);

    /// <summary>One MOUSE_EVENT_RECORD (position 0-based).</summary>
    public readonly record struct MouseRecord(int X, int Y, uint Buttons, uint State, uint Flags);

    // dwControlKeyState
    const uint RightAlt = 0x1, LeftAlt = 0x2, RightCtrl = 0x4, LeftCtrl = 0x8, ShiftKey = 0x10;
    // dwEventFlags
    const uint Moved = 0x1, Wheeled = 0x4, HWheeled = 0x8;

    readonly StringBuilder _text = new();          // the text of this read: typed characters and ones with no key
    int _presses;                                  // how many key records made it (a held key's repeats are one)
    uint _buttons;                                 // mouse buttons held, as of the last mouse event

    /// <summary>Key events are on. Off (VT input mode), every record is a character of VT text and passes as it is.</summary>
    public bool Events { get; set; }

    /// <summary>A key event. Text (plain characters, Enter, Tab, and characters with no key code) is kept until
    /// <see cref="Flush"/> or the next other key, so that a paste reaches the parser as one bracketed paste.
    /// Windows Terminal's paste, measured (2026-10-18): the console's key records, a down and an up for each character,
    /// hundreds in one read; characters the active layout has no key for (Latin letters under the Russian layout) with
    /// vk 0, the rest (space, Enter, digits, punctuation) with the key the console found for them. Typing gives one
    /// record a read. So what one read brought is taken together, and <see cref="Flush"/> decides.</summary>
    public void Key(in KeyRecord k, StringBuilder sb)
    {
        if (!Events)
        {
            // VT text: every record is a character. NUL (Ctrl+Space) comes as the console's own Ctrl+@ with no
            // character, which ReadConsoleW read as NUL: vk 0x40 here (measured), but the key that types "@" depends on
            // the layout (the CI runner did not give 0x40), so any Ctrl record without a character is taken as NUL.
            if (k.Down && (k.Ch != '\0' || k.Vk == 0 || (k.State & (LeftCtrl | RightCtrl)) != 0)) sb.Append(k.Ch, Math.Max(1, k.Repeat));
            return;
        }
        if (!k.Down) return;                       // releases mean nothing here, and must not cut a paste in two
        if (k.Vk == 0)
        {
            if (k.Ch != '\0') { _text.Append(k.Ch, Math.Max(1, k.Repeat)); _presses++; }
            return;
        }
        if (IsText(k)) { _text.Append(k.Ch, Math.Max(1, k.Repeat)); _presses++; return; }
        Flush(sb);
        var seq = Translate(k);
        for (int i = 0; i < Math.Max(1, k.Repeat); i++) sb.Append(seq);
    }

    // A key that types its character: no Ctrl or Alt (AltGr types), a printable character, Enter or Tab (not Shift+Tab).
    static bool IsText(in KeyRecord k)
    {
        bool altGr = (k.State & RightAlt) != 0 && (k.State & LeftCtrl) != 0;
        if (!altGr && (k.State & (LeftAlt | RightAlt | LeftCtrl | RightCtrl)) != 0) return false;
        return k.Ch is >= ' ' and not '\u007f' or '\r' || (k.Ch == '\t' && (k.State & ShiftKey) == 0);
    }

    /// <summary>The end of what one read returned: the text kept by <see cref="Key"/> goes out.</summary>
    public void Flush(StringBuilder sb)
    {
        if (_text.Length == 0) return;
        var text = _text.ToString();
        _text.Clear();
        int presses = _presses;
        _presses = 0;
        if (text[0] == '\u001b') { sb.Append(text); return; }                    // a terminal's reply, as it is
        // An Enter that ends it was typed after the text (a name typed and Enter, in a burst); what a paste typed in
        // would harm is its line breaks (auto-indent before each line), so a paste is text with a line break, or a
        // long one; a short line goes in as typed (paired brackets step over the closer the text brings).
        string enter = "";
        if (text.Length > 1 && text[^1] == '\r') { enter = "\r"; text = text[..^1]; presses--; }
        bool paste = (text.Contains('\r') && presses >= 2) || presses >= 16;
        if (paste) sb.Append("\u001b[200~").Append(text).Append("\u001b[201~");
        else sb.Append(text);
        sb.Append(enter);
    }

    static int Modifier(bool shift, bool alt, bool ctrl) => 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);

    static string Translate(in KeyRecord k)
    {
        bool shift = (k.State & ShiftKey) != 0;
        bool altGr = (k.State & RightAlt) != 0 && (k.State & LeftCtrl) != 0;          // AltGr types a character
        bool alt = !altGr && (k.State & (LeftAlt | RightAlt)) != 0;
        bool ctrl = !altGr && (k.State & (LeftCtrl | RightCtrl)) != 0;
        int m = Modifier(shift, alt, ctrl);
        string Csi(char final) => m == 1 ? $"\u001b[{final}" : $"\u001b[1;{m}{final}";
        string Tilde(int n) => m == 1 ? $"\u001b[{n}~" : $"\u001b[{n};{m}~";
        string Ss3(char final) => m == 1 ? $"\u001bO{final}" : $"\u001b[1;{m}{final}";

        switch (k.Vk)
        {
            case 0x26: return Csi('A');
            case 0x28: return Csi('B');
            case 0x27: return Csi('C');
            case 0x25: return Csi('D');
            case 0x24: return Csi('H');
            case 0x23: return Csi('F');
            case 0x2D: return Tilde(2);
            case 0x2E: return Tilde(3);
            case 0x21: return Tilde(5);
            case 0x22: return Tilde(6);
            case >= 0x70 and <= 0x73: return Ss3((char)('P' + k.Vk - 0x70));                 // F1-F4
            case >= 0x74 and <= 0x7B: return Tilde(new[] { 15, 17, 18, 19, 20, 21, 23, 24 }[k.Vk - 0x74]);
            case 0x08: return ctrl ? "\b" : alt ? "\u001b\u007f" : "\u007f";               // Backspace, as Windows Terminal sends it
            // Tab, Enter and Escape as VT input gave them: nothing binds them with Ctrl, and Enter with Ctrl must
            // still be Enter
            case 0x09: return shift && !ctrl && !alt ? "\u001b[Z" : "\t";
            case 0x0D: return alt ? "\u001b\r" : "\r";
            case 0x1B: return alt ? "\u001b\u001b" : "\u001b";
            case 0x20 when ctrl && !alt: return "\0";
        }
        // Ctrl or Alt with a character key: the key on a US layout says which shortcut it is, whatever was typed.
        if ((ctrl || alt) && UsKey(k.Vk) is char us)
        {
            if (ctrl && !alt && us is >= 'a' and <= 'z') return ((char)(us - 'a' + 1)).ToString();     // Ctrl+letter
            if (alt && !ctrl && us is >= 'a' and <= 'z' && k.Ch != '\0') return "\u001b" + k.Ch;      // Alt+letter as typed (Alt+ф: KeyboardLayout)
            if (alt && !ctrl) return "\u001b" + us;
            return $"\u001b[{(int)us};{m}u";                                                         // Ctrl+/ Ctrl+1 ...
        }
        return k.Ch == '\0' ? "" : k.Ch.ToString();                                                    // modifier keys, others
    }

    // The character of a key on a US layout, letters in lower case: a shortcut is named by its key, Shift or not.
    static char? UsKey(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => (char)('a' + vk - 0x41),
        >= 0x30 and <= 0x39 => (char)vk,
        0xBA => ';', 0xBB => '=', 0xBC => ',', 0xBD => '-', 0xBE => '.', 0xBF => '/', 0xC0 => '`',
        0xDB => '[', 0xDC => '\\', 0xDD => ']', 0xDE => '\'',
        _ => null,
    };

    /// <summary>A mouse event, as the SGR mouse report a terminal would send (presses, releases, drags, the wheel).</summary>
    public void Mouse(in MouseRecord e, StringBuilder sb)
    {
        Flush(sb);
        int mods = ((e.State & ShiftKey) != 0 ? 4 : 0) | ((e.State & (LeftAlt | RightAlt)) != 0 ? 8 : 0) |
                   ((e.State & (LeftCtrl | RightCtrl)) != 0 ? 16 : 0);
        int x = e.X + 1, y = e.Y + 1;
        void Report(int b, bool release) => sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"\u001b[<{b | mods};{x};{y}{(release ? 'm' : 'M')}");
        if ((e.Flags & Wheeled) != 0) { Report((int)e.Buttons < 0 ? 65 : 64, false); return; }      // high word: + away from the user
        if ((e.Flags & HWheeled) != 0) { Report((int)e.Buttons < 0 ? 66 : 67, false); return; }
        uint held = e.Buttons & 0x7;
        if ((e.Flags & Moved) != 0)
        {
            if (held != 0) Report(32 + Button(held), false);
            _buttons = held;
            return;
        }
        foreach (uint bit in new uint[] { 1, 4, 2 })                     // left, middle (FROM_LEFT_2ND), right
        {
            bool now = (held & bit) != 0, before = (_buttons & bit) != 0;
            if (now != before) Report(Button(bit), !now);
        }
        _buttons = held;
    }

    // SGR's button numbers: 0 left, 1 middle, 2 right
    static int Button(uint held) => (held & 1) != 0 ? 0 : (held & 4) != 0 ? 1 : 2;
}
