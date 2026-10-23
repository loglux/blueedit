using System.Text;

namespace Tui;

/// <summary>
/// Turns the character stream a terminal sends (VT input) into <see cref="InputEvent"/>s.
/// The same parser serves every platform: Windows in VT-input mode, and Linux/macOS terminals.
/// A lone ESC is ambiguous (the Escape key or the start of a sequence), so the host calls
/// <see cref="FlushPending"/> after a short pause with no further input.
/// </summary>
public sealed class InputParser
{
    const char Esc = '\x1b';
    string _pending = "";
    StringBuilder? _paste;

    /// <summary>True when input is buffered that only a timeout can resolve (a lone ESC).</summary>
    public bool HasPending => _pending.Length > 0 && _paste == null;

    public void Feed(string chars, List<InputEvent> output)
    {
        _pending += chars;
        while (_pending.Length > 0)
        {
            if (_paste != null)
            {
                if (!ContinuePaste(output)) return;
                continue;
            }
            int used = TryParseOne(_pending, output);
            if (_paste != null) continue;               // a paste just started; its text is in _pending
            if (used == 0) return;                      // incomplete sequence: wait for more input
            _pending = _pending[used..];
        }
    }

    /// <summary>Called after a pause: a pending lone ESC is the Escape key, an unfinished sequence is dropped as typed text.</summary>
    public void FlushPending(List<InputEvent> output)
    {
        if (_paste != null || _pending.Length == 0) return;
        if (_pending == "\x1b") output.Add(new KeyInput(Key.Escape));
        else
        {
            // an unfinished escape sequence: treat ESC as Alt on whatever followed, if it is a plain character
            var rest = _pending;
            _pending = "";
            if (rest.Length == 2 && rest[0] == Esc) output.Add(AltChar(rest[1]));
        }
        _pending = "";
    }

    // ---- one event from the front of the buffer; returns characters consumed, 0 = need more ----
    int TryParseOne(string s, List<InputEvent> output)
    {
        char c = s[0];
        if (c == Esc) return ParseEscape(s, output);
        if (char.IsHighSurrogate(c))
        {
            if (s.Length < 2) return 0;
            output.Add(new KeyInput(Key.Character, '?'));      // characters outside the BMP are not displayable by the cell grid
            return 2;
        }
        output.Add(FromChar(c));
        return 1;
    }

    static KeyInput FromChar(char c) => c switch
    {
        '\r' or '\n' => new KeyInput(Key.Enter),
        '\t' => new KeyInput(Key.Tab),
        '\x7f' => new KeyInput(Key.Backspace),
        '\b' => new KeyInput(Key.Backspace, Mods: Mods.Ctrl),      // Windows Terminal sends BS for Ctrl+Backspace
        '\0' => new KeyInput(Key.Character, ' ', Mods.Ctrl),
        >= '\x01' and <= '\x1a' => new KeyInput(Key.Character, (char)('a' + c - 1), Mods.Ctrl),
        >= '\x1c' and <= '\x1f' => new KeyInput(Key.Character, (char)('\\' + c - 0x1c), Mods.Ctrl),   // Ctrl+\ ] ^ _
        _ => new KeyInput(Key.Character, c),
    };

    static KeyInput AltChar(char c)
    {
        var k = FromChar(c);
        return k with { Mods = k.Mods | Mods.Alt };
    }

    int ParseEscape(string s, List<InputEvent> output)
    {
        if (s.Length == 1) return 0;                                    // ESC alone: wait (timeout decides)
        char c = s[1];
        if (c == '[') return ParseCsi(s, output);
        if (c == 'O') return ParseSs3(s, output);
        if (c == Esc)                                                   // ESC ESC: Alt+Escape
        {
            output.Add(new KeyInput(Key.Escape, Mods: Mods.Alt));
            return 2;
        }
        output.Add(AltChar(c));                                         // ESC x = Alt+x
        return 2;
    }

    static int ParseSs3(string s, List<InputEvent> output)
    {
        if (s.Length < 3) return 0;
        Key key = s[2] switch
        {
            'P' => Key.F1, 'Q' => Key.F2, 'R' => Key.F3, 'S' => Key.F4,
            'A' => Key.Up, 'B' => Key.Down, 'C' => Key.Right, 'D' => Key.Left,
            'H' => Key.Home, 'F' => Key.End,
            _ => Key.None,
        };
        if (key != Key.None) output.Add(new KeyInput(key));
        return 3;
    }

    // xterm modifier parameter: 1 + (shift 1, alt 2, ctrl 4)
    static Mods DecodeMods(int param)
    {
        if (param <= 1) return Mods.None;
        int m = param - 1;
        return ((m & 1) != 0 ? Mods.Shift : 0) | ((m & 2) != 0 ? Mods.Alt : 0) | ((m & 4) != 0 ? Mods.Ctrl : 0);
    }

    int ParseCsi(string s, List<InputEvent> output)
    {
        // ESC [ <params> <final byte 0x40-0x7E>
        int i = 2;
        while (i < s.Length && s[i] is >= '\x20' and <= '\x3f') i++;
        if (i >= s.Length) return 0;
        char final = s[i];
        string body = s[2..i];
        int used = i + 1;

        if (body.StartsWith('<') && (final is 'M' or 'm'))                // SGR mouse: ESC [ < b ; x ; y M|m
        {
            var p = body[1..].Split(';');
            if (p.Length == 3 && int.TryParse(p[0], out int b) && int.TryParse(p[1], out int x) && int.TryParse(p[2], out int y))
                output.Add(DecodeMouse(b, x - 1, y - 1, final == 'm'));
            return used;
        }

        if (body.StartsWith('?') && final == 'c')                         // device attributes: ESC [ ? 61 ; 4 ; … c
        {
            output.Add(new TerminalReply('c', body[1..].Split(';').Select(t => int.TryParse(t, out int v) ? v : 0).ToArray()));
            return used;
        }

        if (body.StartsWith('?') && final == 'u')                         // kitty keyboard flags: ESC [ ? flags u
        {
            output.Add(new TerminalReply('u', [int.TryParse(body[1..], out int flags) ? flags : 0]));
            return used;
        }

        if (final == 'u' && body.Contains(':'))                           // kitty: ESC [ code : shifted : base ; mods : event u
        {
            var fields = body.Split(';');
            var keys = fields[0].Split(':');
            var m = DecodeMods(fields.Length > 1 && int.TryParse(fields[1].Split(':')[0], out int mv) ? mv : 1);
            int code = int.TryParse(keys[0], out int cv) ? cv : 0;
            // with Ctrl or Alt the key counts, not what the layout types on it: the base layout's key (Ctrl+/ with Russian)
            if ((m & (Mods.Ctrl | Mods.Alt)) != 0 && keys.Length > 2 && int.TryParse(keys[2], out int bv) && bv > 0) code = bv;
            output.Add(FromCode(code, m));
            return used;
        }

        var nums = body.Split(';').Select(t => int.TryParse(t, out int v) ? v : 0).ToArray();
        int n0 = nums.Length > 0 ? nums[0] : 0;
        int mods = nums.Length > 1 ? nums[1] : 1;
        if (final == 't' && nums.Length == 3)                              // a size report: ESC [ 6 ; h ; w t
        {
            output.Add(new TerminalReply('t', nums));
            return used;
        }

        switch (final)
        {
            case 'A' or 'B' or 'C' or 'D' or 'H' or 'F':
                {
                    Key k = final switch { 'A' => Key.Up, 'B' => Key.Down, 'C' => Key.Right, 'D' => Key.Left, 'H' => Key.Home, _ => Key.End };
                    output.Add(new KeyInput(k, Mods: DecodeMods(mods)));
                    return used;
                }
            case 'P' or 'Q' or 'R' or 'S':
                output.Add(new KeyInput(final switch { 'P' => Key.F1, 'Q' => Key.F2, 'R' => Key.F3, _ => Key.F4 }, Mods: DecodeMods(mods)));
                return used;
            case 'Z':
                output.Add(new KeyInput(Key.Tab, Mods: Mods.Shift));
                return used;
            case '~':
                if (n0 == 200) { return StartPaste(s, used, output); }
                if (n0 == 27 && nums.Length == 3)                         // modifyOtherKeys: ESC [ 27 ; mod ; code ~
                {
                    output.Add(FromCode(nums[2], DecodeMods(nums[1])));
                    return used;
                }
                {
                    Key k = n0 switch
                    {
                        1 or 7 => Key.Home, 2 => Key.Insert, 3 => Key.Delete, 4 or 8 => Key.End, 5 => Key.PageUp, 6 => Key.PageDown,
                        11 => Key.F1, 12 => Key.F2, 13 => Key.F3, 14 => Key.F4, 15 => Key.F5,
                        17 => Key.F6, 18 => Key.F7, 19 => Key.F8, 20 => Key.F9, 21 => Key.F10, 23 => Key.F11, 24 => Key.F12,
                        _ => Key.None,
                    };
                    if (k != Key.None) output.Add(new KeyInput(k, Mods: DecodeMods(mods)));
                }
                return used;
            case 'u':                                                    // CSI u (kitty / fixterms): ESC [ code ; mod u
                output.Add(FromCode(n0, DecodeMods(mods)));
                return used;
            default:
                return used;                                              // unknown sequence: swallowed
        }
    }

    static KeyInput FromCode(int code, Mods mods) => code switch
    {
        13 => new KeyInput(Key.Enter, Mods: mods),
        9 => new KeyInput(Key.Tab, Mods: mods),
        27 => new KeyInput(Key.Escape, Mods: mods),
        127 or 8 => new KeyInput(Key.Backspace, Mods: mods),
        _ when code is >= 32 and < 0xD800 => new KeyInput(Key.Character, char.ToLowerInvariant((char)code), mods),
        _ => new KeyInput(Key.None),
    };

    static MouseInput DecodeMouse(int b, int x, int y, bool release)
    {
        var mods = ((b & 4) != 0 ? Mods.Shift : 0) | ((b & 8) != 0 ? Mods.Alt : 0) | ((b & 16) != 0 ? Mods.Ctrl : 0);
        bool motion = (b & 32) != 0;
        int low = b & 3;
        if ((b & 64) != 0)
        {
            var wheel = (b & 3) switch { 0 => MouseKind.WheelUp, 1 => MouseKind.WheelDown, 2 => MouseKind.WheelLeft, _ => MouseKind.WheelRight };
            return new MouseInput(wheel, 0, x, y, mods);
        }
        if (motion) return new MouseInput(low == 3 ? MouseKind.Move : MouseKind.Drag, low == 3 ? 0 : low, x, y, mods);
        return new MouseInput(release ? MouseKind.Up : MouseKind.Down, low, x, y, mods);
    }

    // ---- bracketed paste: ESC [ 200 ~ ... ESC [ 201 ~ ----
    int StartPaste(string s, int used, List<InputEvent> output)
    {
        _paste = new StringBuilder();
        _pending = s[used..];
        return 0;                                                        // handled by ContinuePaste on the next loop turn
    }

    bool ContinuePaste(List<InputEvent> output)
    {
        const string end = "\x1b[201~";
        int at = _pending.IndexOf(end, StringComparison.Ordinal);
        if (at < 0)
        {
            // keep the tail that could be the start of the end marker
            int keep = Math.Min(end.Length - 1, _pending.Length);
            int cut = _pending.Length - keep;
            _paste!.Append(_pending, 0, cut);
            _pending = _pending[cut..];
            return false;
        }
        _paste!.Append(_pending, 0, at);
        output.Add(new PasteInput(_paste.ToString()));
        _paste = null;
        _pending = _pending[(at + end.Length)..];
        return true;
    }
}
