namespace Tui;

/// <summary>A row of a help section: a key and what it does, or (no key) a paragraph; both empty: a blank line.</summary>
public sealed record HelpRow(string Keys, string Text);

/// <summary>A section of the help: its title in the contents and its rows.</summary>
public sealed record HelpSection(string Title, IReadOnlyList<HelpRow> Rows);

/// <summary>
/// The F1 help: the sections' titles at the left, the chosen one at the right. A key's row is laid out here, the key
/// in a column of its own and in its own colour, the text wrapped beside it, a paragraph wrapped to the width: the
/// text it comes from is not aligned by hand, so a translation is not either. Up and Down choose the section (a click
/// too), PgUp/PgDn, Space, Home/End and the wheel scroll it, Esc, Enter or F1 close.
/// </summary>
public sealed class HelpDialog(string title, IReadOnlyList<HelpSection> sections, int section = 0) : Modal
{
    const int KeysMax = 26;                                             // a longer key has its text on the next line

    public int Section { get; private set; } = Math.Clamp(section, 0, Math.Max(0, sections.Count - 1));
    public int Scroll { get; private set; }

    Rect _contents;
    int _visible = 1, _lines;

    /// <summary>The chosen section as lines to draw: (column, text, kind) runs, kind 0 text, 1 key, 2 heading.</summary>
    internal static List<List<(int X, string Text, int Kind)>> Layout(HelpSection sec, int width)
    {
        var lines = new List<List<(int, string, int)>> { new() { (0, sec.Title, 2) }, new() };
        int keysW = Math.Min(KeysMax, Math.Max(0, sec.Rows.Where(r => r.Keys.Length <= KeysMax).Select(r => r.Keys.Length).DefaultIfEmpty(0).Max()));
        int textX = keysW + 2;
        foreach (var row in sec.Rows)
        {
            if (row.Keys.Length == 0)
            {
                if (row.Text.Length == 0) { lines.Add(new()); continue; }
                foreach (var l in Tui.Draw.Wrap(row.Text, Math.Max(10, width))) lines.Add(new() { (0, l, 0) });
                continue;
            }
            var text = Tui.Draw.Wrap(row.Text, Math.Max(10, width - textX));
            if (row.Keys.Length > keysW) lines.Add(new() { (0, row.Keys, 1) });
            for (int i = 0; i < text.Count; i++)
            {
                var line = new List<(int, string, int)>();
                if (i == 0 && row.Keys.Length <= keysW) line.Add((0, row.Keys, 1));
                line.Add((textX, text[i], 0));
                lines.Add(line);
            }
        }
        return lines;
    }

    protected internal override void Draw(Screen s, Theme t)
    {
        var r = Tui.Draw.Centered(s, (int)(s.Width * 0.92), (int)(s.Height * 0.9), title);
        Tui.Draw.Box(s, r, title, t.Dialog, t.DialogTitle);
        int leftW = Math.Min(24, sections.Select(x => x.Title.Length).DefaultIfEmpty(8).Max() + 2);
        int divider = r.X + 2 + leftW;
        for (int y = r.Y + 1; y < r.Y + r.H - 1; y++) s.Set(divider, y, '│', t.Dialog);
        s.Set(divider, r.Y, '┯', t.Dialog);
        s.Set(divider, r.Y + r.H - 1, '┷', t.Dialog);
        _contents = new Rect(r.X + 1, r.Y + 1, leftW + 1, r.H - 2);
        for (int i = 0; i < sections.Count && i < _contents.H; i++)
            Tui.Draw.Field(s, _contents.X, _contents.Y + i, _contents.W, " " + sections[i].Title, i == Section ? t.ListSelected : t.Dialog);

        int x0 = divider + 2, width = r.X + r.W - 2 - x0;
        var hint = Strings.T("↑↓ section   PgUp/PgDn scroll   Esc close");
        _visible = Math.Max(1, r.H - 4);                                // the frame, a blank row and the keys under the text
        var lines = sections.Count > 0 ? Layout(sections[Section], width) : new();
        _lines = lines.Count;
        Scroll = Math.Clamp(Scroll, 0, Math.Max(0, _lines - _visible));
        for (int i = 0; i < _visible && Scroll + i < lines.Count; i++)
            foreach (var (x, text, kind) in lines[Scroll + i])
            {
                var shown = text.Length > width - x ? text[..Math.Max(0, width - x)] : text;
                s.Text(x0 + x, r.Y + 1 + i, shown, kind switch { 1 => t.MenuHotkey, 2 => t.DialogTitle, _ => t.Dialog });
            }
        // more below or above: said by the keys' row
        string more = Scroll + _visible < _lines ? "  ▼" : Scroll > 0 ? "  ▲" : "";
        var keys = hint + more;
        s.Text(x0, r.Y + r.H - 2, keys.Length > width ? keys[..width] : keys, t.MenuShortcut);
        Tui.Draw.Shadow(s, r, t.Shadow);
    }

    void Choose(int i) { if (sections.Count == 0) return; Section = Math.Clamp(i, 0, sections.Count - 1); Scroll = 0; }

    void ScrollBy(int n) => Scroll = Math.Clamp(Scroll + n, 0, Math.Max(0, _lines - _visible));

    protected internal override void Handle(InputEvent ev)
    {
        int page = Math.Max(1, _visible - 1);
        switch (ev)
        {
            case KeyInput { Key: Key.Escape or Key.Enter or Key.F1 }: Closed = true; return;
            case KeyInput { Key: Key.Up }: Choose(Section - 1); return;
            case KeyInput { Key: Key.Down }: Choose(Section + 1); return;
            case KeyInput { Key: Key.PageDown }: ScrollBy(page); return;
            case KeyInput { Key: Key.PageUp }: ScrollBy(-page); return;
            case KeyInput { Key: Key.Character, Character: ' ' } k: ScrollBy(k.Mods == Mods.Shift ? -page : page); return;
            case KeyInput { Key: Key.Home }: Scroll = 0; return;
            case KeyInput { Key: Key.End }: ScrollBy(_lines); return;
            case MouseInput { Kind: MouseKind.WheelDown }: ScrollBy(3); return;
            case MouseInput { Kind: MouseKind.WheelUp }: ScrollBy(-3); return;
            case MouseInput { Kind: MouseKind.Down, Button: 0 } m when _contents.Contains(m.X, m.Y) && m.Y - _contents.Y < sections.Count:
                Choose(m.Y - _contents.Y);
                return;
        }
    }
}
