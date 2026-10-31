namespace Tui;

/// <summary>A menu entry. The first underscore in the label marks its hotkey letter ("_Open..." = O); any later one is
/// shown as it is (a file named my_notes.md). The hotkey is a key, in either layout: "_Файл" is the key of Ф, A on a
/// QWERTY keyboard (<see cref="KeyboardLayout.Latin"/>).</summary>
public sealed record MenuItem(string Label, string Shortcut, Action Run)
{
    public char Hotkey => MenuBar.HotkeyOf(Label);
    public string Text => MenuBar.TextOf(Label);

    /// <summary>Whether what the item turns on is on now: √ at its left, as in Far's menus. Asked whenever the menu is
    /// drawn, so it is never out of date.</summary>
    public Func<bool>? On { get; init; }

    /// <summary>One of several choices (the Markdown views, the themes): • marks the chosen one instead of √.</summary>
    public bool Choice { get; init; }

    /// <summary>The setting's value now, at the right where a shortcut would stand ("CRLF"). Asked with every frame while
    /// the menu is open, so it reads what is in memory, not the disk. A long one is cut, so the menu keeps its width.</summary>
    public Func<string>? Value { get; init; }

    const int MaxValue = 24;

    internal string Right
    {
        get
        {
            if (Shortcut.Length > 0) return Shortcut;
            var value = Value?.Invoke() ?? "";
            return value.Length > MaxValue ? value[..(MaxValue - 1)] + "…" : value;
        }
    }
}

public sealed record Menu(string Label, IReadOnlyList<MenuItem> Items)
{
    public char Hotkey => MenuBar.HotkeyOf(Label);
    public string Text => MenuBar.TextOf(Label);

    /// <summary>Items known only when the menu opens (the open files), under the fixed ones.</summary>
    public Func<IEnumerable<MenuItem>>? More { get; init; }

    internal IReadOnlyList<MenuItem> Shown => More == null ? Items : Items.Concat(More()).ToList();
}

/// <summary>
/// The top row of the screen. F9 or Alt+hotkey opens a menu; arrows move, Enter or the item's hotkey runs it,
/// Esc closes. The mouse opens menus and picks items.
/// </summary>
public sealed class MenuBar
{
    readonly List<Menu> _menus;
    int _open = -1, _item;
    readonly List<(int X, int W)> _slots = new();

    public MenuBar(IEnumerable<Menu> menus)
    {
        _menus = menus.ToList();
        var bar = _menus.Where(m => m.Hotkey != '\0').GroupBy(m => m.Hotkey).FirstOrDefault(g => g.Count() > 1);
        if (bar != null) throw new ArgumentException($"Menus share the hotkey '{bar.Key}': {string.Join(", ", bar.Select(m => m.Text))}");
        foreach (var m in _menus)
        {
            var dup = m.Items.Where(i => i.Hotkey != '\0').GroupBy(i => i.Hotkey).FirstOrDefault(g => g.Count() > 1);
            if (dup != null) throw new ArgumentException($"Menu {m.Text}: items share the hotkey '{dup.Key}': {string.Join(", ", dup.Select(i => i.Text))}");
        }
    }

    public bool IsOpen => _open >= 0;
    public IReadOnlyList<Menu> Menus => _menus;

    /// <summary>Called when the bar opens from closed (not when it moves to the next menu): a chance to start, in the
    /// background, what an item may need.</summary>
    public Action? Opened { get; init; }

    /// <summary>Text at the right end of the bar (a clock), asked for with every frame; left out when the menus reach it.</summary>
    public Func<string>? Right { get; init; }

    public static char HotkeyOf(string label)
    {
        int i = label.IndexOf('_', StringComparison.Ordinal);
        return i >= 0 && i + 1 < label.Length ? KeyboardLayout.Latin(label[i + 1]) : '\0';
    }

    /// <summary>The label without its hotkey mark: the first underscore only.</summary>
    public static string TextOf(string label) => label.IndexOf('_', StringComparison.Ordinal) is >= 0 and var i ? label.Remove(i, 1) : label;

    public void Open(int index)
    {
        bool was = IsOpen;
        _open = Math.Clamp(index, 0, _menus.Count - 1); _item = 0;
        if (!was) Opened?.Invoke();
    }
    public void Close() => _open = -1;

    /// <summary>Gives the bar a chance at an event. Returns true when the event was used.</summary>
    public bool TryHandle(InputEvent ev, TuiApp app)
    {
        if (!IsOpen)
        {
            switch (ev)
            {
                case KeyInput { Key: Key.F9, Mods: Mods.None }: Open(0); return true;
                case KeyInput { Key: Key.Character, Mods: Mods.Alt } k:
                    int m = _menus.FindIndex(x => x.Hotkey == KeyboardLayout.Latin(k.Character));
                    if (m < 0) return false;
                    Open(m); return true;
                case MouseInput { Kind: MouseKind.Down, Button: 0, Y: 0 } mouse:
                    int hit = SlotAt(mouse.X);
                    if (hit < 0) return false;
                    Open(hit); return true;
            }
            return false;
        }

        var items = _menus[_open].Shown;
        switch (ev)
        {
            case KeyInput { Key: Key.Escape } or KeyInput { Key: Key.F9 }: Close(); return true;
            case KeyInput { Key: Key.Left }: Open((_open + _menus.Count - 1) % _menus.Count); return true;
            case KeyInput { Key: Key.Right }: Open((_open + 1) % _menus.Count); return true;
            case KeyInput { Key: Key.Up }: _item = (_item + items.Count - 1) % items.Count; return true;
            case KeyInput { Key: Key.Down }: _item = (_item + 1) % items.Count; return true;
            case KeyInput { Key: Key.Home }: _item = 0; return true;
            case KeyInput { Key: Key.End }: _item = items.Count - 1; return true;
            case KeyInput { Key: Key.Enter }: RunItem(items[_item]); return true;
            case KeyInput { Key: Key.Character } k when (k.Mods & ~(Mods.Shift | Mods.Alt)) == 0:
                {
                    char c = KeyboardLayout.Latin(k.Character);                 // "а" on a Russian layout is the F key
                    int i = items.ToList().FindIndex(x => x.Hotkey == c);
                    if (i >= 0) { RunItem(items[i]); return true; }
                    int m = _menus.FindIndex(x => x.Hotkey == c);
                    if (k.Alt && m >= 0) Open(m);
                    return true;
                }
            case MouseInput { Kind: MouseKind.Down } mouse:
                {
                    if (mouse.Y == 0) { int hit = SlotAt(mouse.X); if (hit >= 0 && hit != _open) Open(hit); else Close(); return true; }
                    var box = DropDownRect();
                    int row = mouse.Y - box.Y - 1;
                    if (box.Contains(mouse.X, mouse.Y) && row >= 0 && row < items.Count) { _item = row; RunItem(items[row]); }
                    else Close();
                    return true;
                }
            case MouseInput { Kind: MouseKind.Move or MouseKind.Drag } mouse:
                {
                    var box = DropDownRect();
                    int row = mouse.Y - box.Y - 1;
                    if (box.Contains(mouse.X, mouse.Y) && row >= 0 && row < items.Count) _item = row;
                    return true;
                }
            case MouseInput: return true;
            case KeyInput: return true;                // everything else is swallowed while a menu is open
        }
        return false;

        void RunItem(MenuItem item)
        {
            Close();
            item.Run();
        }
    }

    int SlotAt(int x)
    {
        for (int i = 0; i < _slots.Count; i++)
            if (x >= _slots[i].X - 1 && x < _slots[i].X + _slots[i].W + 1) return i;
        return -1;
    }

    // " File  Edit  Search ..." - one leading space, labels two spaces apart
    public void DrawBar(Screen s, Theme t)
    {
        s.Fill(0, 0, s.Width, 1, ' ', t.Bar);
        _slots.Clear();
        int x = 1;
        for (int i = 0; i < _menus.Count; i++)
        {
            var m = _menus[i];
            bool sel = i == _open;
            if (sel) s.Fill(x - 1, 0, m.Text.Length + 2, 1, ' ', t.BarSelected);
            _slots.Add((x, m.Text.Length));
            DrawLabel(s, x, 0, m.Label, sel ? t.BarSelected : t.Bar, sel ? t.BarSelected : t.BarHotkey);
            x += m.Text.Length + 2;
        }
        if (Right?.Invoke() is { Length: > 0 } right && s.Width - right.Length - 1 >= x)
            s.Text(s.Width - right.Length - 1, 0, right, t.Bar);
    }

    Rect DropDownRect()
    {
        var items = _menus[_open].Shown;
        int labelW = items.Max(i => i.Text.Length), keyW = items.Max(i => i.Right.Length);
        int w = 1 + labelW + (keyW > 0 ? 2 + keyW : 0) + 1 + 2;
        int x = _slots.Count > _open ? Math.Max(0, _slots[_open].X - 1) : 0;
        return new Rect(x, 1, w, items.Count + 2);
    }

    public void DrawDropDown(Screen s, Theme t)
    {
        if (!IsOpen) return;
        var items = _menus[_open].Shown;
        var r = DropDownRect();
        if (r.X + r.W > s.Width) r = r with { X = Math.Max(0, s.Width - r.W) };
        Draw.Box(s, r, "", t.Menu, t.Menu);
        int keyW = items.Max(i => i.Right.Length);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            bool sel = i == _item;
            var style = sel ? t.MenuSelected : t.Menu;
            s.Fill(r.X + 1, r.Y + 1 + i, r.W - 2, 1, ' ', style);
            // the mark in the column before the label, which is otherwise blank
            if (it.On?.Invoke() == true) s.Set(r.X + 1, r.Y + 1 + i, it.Choice ? '•' : '√', style);
            DrawLabel(s, r.X + 2, r.Y + 1 + i, it.Label, style, sel ? t.MenuSelected : t.MenuHotkey);
            if (it.Right is { Length: > 0 } right)
                s.Text(r.X + r.W - 2 - keyW, r.Y + 1 + i, right.PadLeft(keyW), sel ? t.MenuSelected : t.MenuShortcut);
        }
        Draw.Shadow(s, r, t.Shadow);
    }

    static void DrawLabel(Screen s, int x, int y, string label, Style style, Style hot)
    {
        int mark = label.IndexOf('_', StringComparison.Ordinal);
        for (int i = 0; i < label.Length; i++)
            if (i != mark) s.Set(x++, y, label[i], i == mark + 1 && mark >= 0 ? hot : style);
    }
}
