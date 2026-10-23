using Tui;

/// <summary>
/// The Ctrl+Space list under the word being typed. Typing goes on into the text and narrows the list; Up/Down choose,
/// Enter or Tab take the word, Esc closes. Any other key closes the list and goes to the editor as usual.
/// Opened by itself while typing (<see cref="Auto"/>), Enter takes the word only after a choice with the arrows —
/// otherwise it closes the list and starts a new line, as the user was typing it for.
/// </summary>
sealed class CompletionPopup : Modal
{
    const int MaxRows = 8;

    readonly EditorView _editor;
    readonly Completion.Pool _pool;
    readonly Func<EditorTheme> _theme;
    readonly ListBox _list = new();
    List<Candidate> _items = new();
    Rect _rect;

    bool _chosen;                                  // moved through the list with the arrows or the wheel

    public CompletionPopup(EditorView editor, Completion.Pool pool, Func<EditorTheme> theme, bool auto = false)
    {
        _editor = editor; _pool = pool; _theme = theme; Auto = auto;
        Refilter();
    }

    /// <summary>Opened by itself while typing, not by Ctrl+Space.</summary>
    public bool Auto { get; }

    /// <summary>The candidates for the current prefix (what the list shows).</summary>
    public IReadOnlyList<Candidate> Items => _items;
    public string? Accepted { get; private set; }

    void Refilter()
    {
        var prefix = _editor.WordBeforeCaret()?.Prefix ?? "";
        _items = Completion.Filter(_pool, prefix);
        _list.Items = _items.Select(c => c.Word).ToList();
        _list.Selected = 0;
        if (_items.Count == 0) Closed = true;
        // opened by itself: nothing to offer once the word typed is the only candidate
        if (Auto && _items.Count == 1 && _items[0].Word == prefix) Closed = true;
    }

    void Accept()
    {
        if (_items.Count == 0) { Closed = true; return; }
        Accepted = _items[_list.Selected].Word;
        _editor.CompleteWith(_items[_list.Selected].Insert ?? Accepted);
        Closed = true;
    }

    protected override void Draw(Screen s, Theme t)
    {
        if (_editor.CursorPos is not (int cx, int cy)) return;
        var prefixLength = _editor.WordBeforeCaret()?.Prefix.Length ?? 0;
        int rows = Math.Min(MaxRows, _items.Count);
        int width = Math.Clamp(_items.Max(c => c.Word.Length + (c.Keyword ? 4 : c.Tag is { Length: > 0 } tag ? tag.Length + 2 : 0)) + 2, 12, Math.Max(12, s.Width / 2));
        int x = Math.Clamp(cx - prefixLength - 1, 0, Math.Max(0, s.Width - width));
        int y = cy + 1 + rows <= s.Height - 2 ? cy + 1 : Math.Max(1, cy - rows);   // below the line, or above when no room
        _rect = new Rect(x, y, width, rows);
        var th = _theme();
        var normal = th.Ui.Menu; var selected = th.Ui.MenuSelected;
        _list.Draw(s, new Rect(x + 1, y, width - 1, rows), normal, selected);
        for (int r = 0; r < rows; r++)
        {
            int idx = _list.Top + r;
            s.Set(x, y + r, ' ', idx == _list.Selected ? selected : normal);
            // keywords (and a language server's kinds: m, fld, cls...) are marked on the right
            var tag = idx < _items.Count ? (_items[idx].Keyword ? "kw" : _items[idx].Tag) : null;
            if (tag is { Length: > 0 })
                s.Text(x + width - tag.Length - 1, y + r, tag, idx == _list.Selected ? selected : th.Ui.MenuShortcut);
        }
    }

    protected override (int X, int Y)? Cursor => _editor.CursorPos;

    protected override void Handle(InputEvent ev)
    {
        switch (ev)
        {
            case KeyInput { Key: Key.Escape }: Closed = true; return;
            case KeyInput { Key: Key.Character, Character: ' ', Mods: Mods.Ctrl }:
                // Ctrl+Space over a list that opened by itself: what Ctrl+Space does — one candidate goes in at once,
                // several stay listed as asked for (Enter then takes the selected one)
                if (_items.Count == 1) Accept(); else _chosen = true;
                return;
            case KeyInput { Key: Key.Enter } k when Auto && !_chosen:
                Closed = true;
                _editor.HandleKey(k);
                return;
            case KeyInput { Key: Key.Enter } or KeyInput { Key: Key.Tab, Mods: Mods.None }: Accept(); return;
            case KeyInput { Key: Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End, Mods: Mods.None }:
                _chosen = true;
                _list.Handle(ev); return;
            case KeyInput { Key: Key.Character } k when (k.Mods & (Mods.Ctrl | Mods.Alt)) == 0:
                _editor.HandleKey(k);
                if (char.IsLetterOrDigit(k.Character) || k.Character == '_' || (k.Character == '-' && _editor.HyphenWords)) Refilter();
                else Closed = true;
                return;
            case KeyInput { Key: Key.Backspace, Mods: Mods.None } k:
                _editor.HandleKey(k);
                if (_editor.WordBeforeCaret() == null) Closed = true; else Refilter();
                return;
            case KeyInput k:
                Closed = true;
                _editor.HandleKey(k);
                return;
            case PasteInput p:
                Closed = true;
                _editor.InsertText(p.Text);
                return;
            case MouseInput { Kind: MouseKind.Down, Button: 0 } m:
                if (_rect.Contains(m.X, m.Y))
                {
                    _list.Selected = Math.Min(_items.Count - 1, _list.Top + m.Y - _rect.Y);
                    Accept();
                }
                else Closed = true;
                return;
            case MouseInput { Kind: MouseKind.WheelUp or MouseKind.WheelDown }:
                _chosen = true;
                _list.Handle(ev); return;
        }
    }
}
