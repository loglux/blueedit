using Tui;

/// <summary>
/// Everything under the menu bar: the tab strip (row 1), the text area, the status line and the key hints (last two rows).
/// </summary>
sealed class MainView(EditorView editor) : IRootView
{
    /// <summary>Colours of the tab strip, status line and hints; replaced when the theme changes.</summary>
    public EditorTheme Theme { get; set; } = Themes.Get("dos-blue");

    readonly List<(int X, int W)> _tabSlots = new();

    public Func<IReadOnlyList<(string Name, bool Modified, bool Active)>> Tabs { get; init; } = () => Array.Empty<(string, bool, bool)>();
    public Func<string> Status { get; init; } = () => "";
    public IReadOnlyList<(string Key, string Text)> Hints { get; init; } = Array.Empty<(string, string)>();
    public Action<int>? SwitchTo { get; init; }

    static Rect EditorArea(int width, int height) => new(0, 2, width, Math.Max(1, height - 4));

    public void Draw(Screen s)
    {
        editor.SetArea(EditorArea(s.Width, s.Height));
        DrawTabs(s);
        editor.Draw(s);
        Tui.Draw.Field(s, 0, s.Height - 2, s.Width, Status(), Theme.Status);
        DrawHints(s);
    }

    void DrawTabs(Screen s)
    {
        s.Fill(0, 1, s.Width, 1, ' ', Theme.TabNormal);
        _tabSlots.Clear();
        int x = 0, n = 0;
        foreach (var (name, modified, active) in Tabs())
        {
            n++;
            var shown = name.Length > 22 ? name[..21] + "…" : name;
            var label = $" {n}:{shown}{(modified ? "*" : "")} ";
            if (x + label.Length > s.Width) break;
            s.Text(x, 1, label, active ? Theme.TabActive : Theme.TabNormal);
            _tabSlots.Add((x, label.Length));
            x += label.Length;
        }
    }

    // DOS-style key hints: the key in white on black, what it does in black on cyan
    void DrawHints(Screen s)
    {
        int y = s.Height - 1, x = 0;
        s.Fill(0, y, s.Width, 1, ' ', Theme.HintText);
        foreach (var (key, text) in Hints)
        {
            string t = " " + Ui.Tc("hint", text) + " ";    // in the interface's language ("hint|Save": "Сохр")
            if (x + key.Length + t.Length > s.Width) break;
            x = s.Text(x, y, key, Theme.HintKey);
            x = s.Text(x, y, t, Theme.HintText);
        }
    }

    public bool Handle(InputEvent ev)
    {
        switch (ev)
        {
            case KeyInput k: return editor.HandleKey(k);
            case PasteInput p: editor.InsertText(p.Text); return true;
            case ResizeInput r: editor.SetArea(EditorArea(r.Width, r.Height)); return true;
            case MouseInput { Kind: MouseKind.Down, Button: 0, Y: 1 } m:
                for (int i = 0; i < _tabSlots.Count; i++)
                    if (m.X >= _tabSlots[i].X && m.X < _tabSlots[i].X + _tabSlots[i].W) { SwitchTo?.Invoke(i); return true; }
                return false;
            case MouseInput m: return editor.HandleMouse(m);
        }
        return false;
    }

    public (int X, int Y)? Cursor => editor.CursorPos;
}
