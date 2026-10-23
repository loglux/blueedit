namespace Tui;

/// <summary>Asks for one line of text. Enter accepts, Esc cancels (Result stays null).</summary>
/// <summary>A line of text to type; <paramref name="replaceOnType"/>: the initial text goes when typing starts (as a
/// name to rename), arrows and editing keys keep it.</summary>
public sealed class PromptDialog(string title, string label, string initial = "", bool replaceOnType = false) : Modal
{
    readonly TextInput _input = new(initial, replaceOnType);
    (int X, int Y) _caret;

    public string? Result { get; private set; }

    protected internal override void Draw(Screen s, Theme t)
    {
        var r = Tui.Draw.Centered(s, 50, 7, title);
        Tui.Draw.Box(s, r, title, t.Dialog, t.DialogTitle);
        s.Text(r.X + 2, r.Y + 1, label, t.Dialog);
        _caret = _input.Draw(s, r.X + 2, r.Y + 2, r.W - 4, t.Input);
        s.Text(r.X + 2, r.Y + 4, Strings.T("Enter = OK    Esc = Cancel"), t.Dialog);
        Tui.Draw.Shadow(s, r, t.Shadow);
    }

    protected internal override (int X, int Y)? Cursor => _caret;

    protected internal override void Handle(InputEvent ev)
    {
        switch (ev)
        {
            case KeyInput { Key: Key.Enter }: Result = _input.Text; Closed = true; return;
            case KeyInput { Key: Key.Escape }: Closed = true; return;
            default: _input.Handle(ev); return;
        }
    }
}

/// <summary>
/// A list of on/off items: Space or a click switches the highlighted one, Enter accepts, Esc cancels
/// (<see cref="Checked"/> stays null). With <paramref name="firstIsExclusive"/> the first item ("Auto") and the
/// others exclude each other. With <paramref name="details"/>, the selected item's full text (wrapped) is shown under
/// the list, for items too long for one line. With <paramref name="flip"/>, switching an item is the caller's rule:
/// it gets the states and the item's index and changes the states.
/// </summary>
public sealed class CheckListDialog(string title, IReadOnlyList<string> items, IReadOnlyList<bool> initial, bool firstIsExclusive = false,
    string hint = "Space = on/off   Enter = OK   Esc = cancel", Func<int, string>? details = null, Action<bool[], int>? flip = null) : Modal
{
    const int DetailRows = 6;

    readonly ListBox _list = new() { Items = items.ToList() };
    readonly bool[] _on = initial.ToArray();
    Rect _listRect;

    public bool[]? Checked { get; private set; }

    void Flip(int i)
    {
        if ((uint)i >= (uint)_on.Length) return;
        if (flip != null) { flip(_on, i); return; }        // the caller's rule
        _on[i] = !_on[i];
        if (!firstIsExclusive || !_on[i]) return;
        if (i == 0) Array.Fill(_on, false, 1, _on.Length - 1);
        else _on[0] = false;
    }

    protected internal override void Draw(Screen s, Theme t)
    {
        var shown = Strings.T(hint);
        int w = Math.Min(s.Width - 2, Math.Max(Math.Max(40, items.Max(x => x.Length) + 10), shown.Length + 4));
        int extra = details == null ? 0 : DetailRows + 1;
        int rows = Math.Min(items.Count, Math.Max(3, s.Height - 8 - extra));
        var r = Tui.Draw.Centered(s, w, rows + 4 + extra, title);
        Tui.Draw.Box(s, r, title, t.Dialog, t.DialogTitle);
        _listRect = new Rect(r.X + 2, r.Y + 1, r.W - 4, rows);
        _list.Items = items.Select((x, i) => $"[{(_on[i] ? 'x' : ' ')}] {x}").ToList();
        _list.Draw(s, _listRect, t.Dialog, t.ListSelected);
        if (details != null && (uint)_list.Selected < (uint)items.Count)
        {
            var lines = Wrap(details(_list.Selected), _listRect.W);
            for (int i = 0; i < DetailRows; i++)
                s.Text(_listRect.X, _listRect.Y + rows + 1 + i, (i < lines.Count ? lines[i] : "").PadRight(_listRect.W)[.._listRect.W], t.Dialog);
        }
        s.Text(r.X + 2, r.Y + r.H - 2, shown, t.Dialog);
        Tui.Draw.Shadow(s, r, t.Shadow);
    }

    static List<string> Wrap(string text, int width) => Tui.Draw.Wrap(text, width);

    protected internal override void Handle(InputEvent ev)
    {
        switch (ev)
        {
            case KeyInput { Key: Key.Enter }: Checked = _on.ToArray(); Closed = true; return;
            case KeyInput { Key: Key.Escape }: Closed = true; return;
            case KeyInput { Key: Key.Character, Character: ' ' }: Flip(_list.Selected); return;
            case MouseInput { Kind: MouseKind.Down, Button: 0 } m when _listRect.Contains(m.X, m.Y):
                _list.Selected = _list.Top + m.Y - _listRect.Y;
                Flip(_list.Selected);
                return;
            default: _list.Handle(ev); return;
        }
    }
}

/// <summary>One setting of an <see cref="OptionsDialog"/>: its name, the values it can take, the current one and a
/// line or two saying what it does.</summary>
public sealed record OptionRow(string Label, IReadOnlyList<string> Values, int Initial, string Help = "");

/// <summary>
/// Settings as a list, as Far's configuration dialogs are: each row a name and its value; Space or Right takes the
/// next value, Left the one before, a click the next; the selected row's help is shown under the list. Enter accepts
/// (<see cref="Chosen"/>: the value index of every row), Esc cancels. With <paramref name="moreLabel"/> a last row
/// leads on (to the settings file): Enter on it, or a click, closes with <see cref="More"/> set and the choices kept.
/// </summary>
public sealed class OptionsDialog(string title, IReadOnlyList<OptionRow> rows, string? moreLabel = null) : Modal
{
    const int HelpRows = 3;
    const string HintText = "Space, ← → = change   Enter = save   Esc = cancel";

    readonly ListBox _list = new();
    readonly int[] _value = rows.Select(r => r.Initial).ToArray();
    Rect _listRect;

    public int[]? Chosen { get; private set; }
    public bool More { get; private set; }

    int Count => rows.Count + (moreLabel == null ? 0 : 1);

    void Step(int i, int by)
    {
        if ((uint)i >= (uint)rows.Count) return;
        int n = rows[i].Values.Count;
        _value[i] = ((_value[i] + by) % n + n) % n;
    }

    protected internal override void Draw(Screen s, Theme t)
    {
        int labelW = rows.Max(r => r.Label.Length), valueW = rows.Max(r => r.Values.Max(v => v.Length));
        int w = Math.Min(s.Width - 2, Math.Max(Math.Max(labelW + valueW + 12, 68), (moreLabel?.Length ?? 0) + 6));
        int listRows = Math.Min(Count, Math.Max(3, s.Height - 8 - HelpRows));
        var r = Tui.Draw.Centered(s, w, listRows + 5 + HelpRows, title);
        Tui.Draw.Box(s, r, title, t.Dialog, t.DialogTitle);
        _listRect = new Rect(r.X + 2, r.Y + 1, r.W - 4, listRows);
        int inner = _listRect.W;
        _list.Items = Enumerable.Range(0, Count).Select(i => i < rows.Count
            ? " " + rows[i].Label.PadRight(inner - valueW - 6) + "‹ " + rows[i].Values[_value[i]].PadRight(valueW) + " ›"
            : " " + moreLabel).ToList();
        _list.Draw(s, _listRect, t.Dialog, t.ListSelected);
        string help = (uint)_list.Selected < (uint)rows.Count ? rows[_list.Selected].Help : "";
        var lines = Wrap(help, inner);
        for (int i = 0; i < HelpRows; i++)
            s.Text(_listRect.X, _listRect.Y + listRows + 1 + i, (i < lines.Count ? lines[i] : "").PadRight(inner)[..inner], t.Dialog);
        s.Text(r.X + 2, r.Y + r.H - 2, Strings.T(HintText), t.Dialog);
        Tui.Draw.Shadow(s, r, t.Shadow);
    }

    static List<string> Wrap(string text, int width) => Tui.Draw.Wrap(text, width);

    protected internal override void Handle(InputEvent ev)
    {
        switch (ev)
        {
            case KeyInput { Key: Key.Enter }:
                Chosen = _value.ToArray();
                More = _list.Selected >= rows.Count;
                Closed = true;
                return;
            case KeyInput { Key: Key.Escape }: Closed = true; return;
            case KeyInput { Key: Key.Character, Character: ' ' }: Step(_list.Selected, 1); return;
            case KeyInput { Key: Key.Right }: Step(_list.Selected, 1); return;
            case KeyInput { Key: Key.Left }: Step(_list.Selected, -1); return;
            case MouseInput { Kind: MouseKind.Down, Button: 0 } m when _listRect.Contains(m.X, m.Y):
                _list.Selected = _list.Top + m.Y - _listRect.Y;
                if (_list.Selected >= rows.Count) { Chosen = _value.ToArray(); More = true; Closed = true; }
                else Step(_list.Selected, 1);
                return;
            default: _list.Handle(ev); return;
        }
    }
}

/// <summary>A labelled field of a <see cref="FormDialog"/>.</summary>
public sealed record FormField(string Label, string Initial = "");

/// <summary>An on/off option of a <see cref="FormDialog"/>, switched with Alt+<see cref="Hotkey"/> or a click. An underscore
/// in the label marks the letter, a key in either layout as a menu's is ("Match _case"; "_Учитывать регистр" is Alt+E).</summary>
public sealed record FormToggle(string Label, bool Initial = false)
{
    public char Hotkey => MenuBar.HotkeyOf(Label);
    public string Text => Label.Replace("_", "", StringComparison.Ordinal);
}

/// <summary>
/// Several text fields plus on/off options, as in a Find or Replace dialog. Tab, Shift+Tab, Up and Down move between
/// fields; Alt+letter switches an option; Enter accepts; Esc cancels (<see cref="Values"/> stays null).
/// A field's initial text is replaced by the first character typed into it.
/// </summary>
public sealed class FormDialog : Modal
{
    readonly string _title, _hint;
    readonly IReadOnlyList<FormField> _fields;
    readonly IReadOnlyList<FormToggle> _toggleDefs;
    readonly TextInput[] _inputs;
    readonly bool[] _on;
    readonly List<(int X, int Y, int W)> _toggleSlots = new();
    int _focus;
    (int X, int Y) _caret;

    public FormDialog(string title, IReadOnlyList<FormField> fields, IReadOnlyList<FormToggle>? toggles = null, string hint = "Enter = OK   Tab = next field   Esc = Cancel")
    {
        _title = title; _fields = fields; _toggleDefs = toggles ?? Array.Empty<FormToggle>(); _hint = Strings.T(hint);
        _inputs = fields.Select(f => new TextInput(f.Initial, replaceOnType: true)).ToArray();
        _on = _toggleDefs.Select(t => t.Initial).ToArray();
    }

    /// <summary>Field texts after Enter; null when cancelled.</summary>
    public string[]? Values { get; private set; }
    public IReadOnlyList<bool> Toggles => _on;

    string ToggleText(int i) => $"[{(_on[i] ? 'x' : ' ')}] {_toggleDefs[i].Text}";

    protected internal override void Draw(Screen s, Theme t)
    {
        int labelW = _fields.Max(f => f.Label.Length) + 1;
        int togglesW = _toggleDefs.Count == 0 ? 0 : _toggleDefs.Select((_, i) => ToggleText(i).Length + 3).Sum();
        int w = Math.Min(s.Width - 2, Math.Max(Math.Max(60, togglesW + 4), _hint.Length + 4));
        int h = _fields.Count + (_toggleDefs.Count > 0 ? 2 : 0) + 4;
        var r = Tui.Draw.Centered(s, w, h, _title);
        Tui.Draw.Box(s, r, _title, t.Dialog, t.DialogTitle);
        for (int i = 0; i < _fields.Count; i++)
        {
            s.Text(r.X + 2, r.Y + 1 + i, _fields[i].Label, t.Dialog);
            var c = _inputs[i].Draw(s, r.X + 2 + labelW, r.Y + 1 + i, r.W - 4 - labelW, t.Input, t.ListSelected);
            if (i == _focus) _caret = c;
        }
        _toggleSlots.Clear();
        if (_toggleDefs.Count > 0)
        {
            int x = r.X + 2, y = r.Y + 2 + _fields.Count;
            for (int i = 0; i < _toggleDefs.Count; i++)
            {
                var text = ToggleText(i);
                int hk = _toggleDefs[i].Label.IndexOf('_', StringComparison.Ordinal) is >= 0 and var u ? u + 4 : -1;
                s.Text(x, y, text, t.Dialog);
                if (hk >= 0) s.Set(x + hk, y, text[hk], t.MenuHotkey with { Bg = t.Dialog.Bg });
                _toggleSlots.Add((x, y, text.Length));
                x += text.Length + 3;
            }
        }
        s.Text(r.X + 2, r.Y + r.H - 2, _hint, t.Dialog);
        Tui.Draw.Shadow(s, r, t.Shadow);
    }

    protected internal override (int X, int Y)? Cursor => _caret;

    void Flip(int i) => _on[i] = !_on[i];

    protected internal override void Handle(InputEvent ev)
    {
        switch (ev)
        {
            case KeyInput { Key: Key.Enter }: Values = _inputs.Select(x => x.Text).ToArray(); Closed = true; return;
            case KeyInput { Key: Key.Escape }: Closed = true; return;
            case KeyInput { Key: Key.Tab } k: _focus = (_focus + (k.Shift ? _inputs.Length - 1 : 1)) % _inputs.Length; return;
            case KeyInput { Key: Key.Up }: _focus = (_focus + _inputs.Length - 1) % _inputs.Length; return;
            case KeyInput { Key: Key.Down }: _focus = (_focus + 1) % _inputs.Length; return;
            case KeyInput { Key: Key.Character, Mods: Mods.Alt } k:
                for (int i = 0; i < _toggleDefs.Count; i++)
                    if (_toggleDefs[i].Hotkey == KeyboardLayout.Latin(k.Character)) { Flip(i); return; }
                return;
            case MouseInput { Kind: MouseKind.Down, Button: 0 } m:
                for (int i = 0; i < _toggleSlots.Count; i++)
                    if (m.Y == _toggleSlots[i].Y && m.X >= _toggleSlots[i].X && m.X < _toggleSlots[i].X + _toggleSlots[i].W) { Flip(i); return; }
                return;
            default: _inputs[_focus].Handle(ev); return;
        }
    }
}

/// <summary>A one-line question in place of the status line that waits for one of a few keys, as DOS editors asked
/// "Replace (Y/N/A)?" while the text with the highlighted match stays in view. Result is the key's (Latin, lower-case)
/// letter, or null on Esc; the same keys work on a Russian layout.</summary>
public sealed class KeyPrompt(string text, string keys) : Modal
{
    public char? Result { get; private set; }

    protected internal override void Draw(Screen s, Theme t)
    {
        int y = Math.Max(0, s.Height - 2);
        Tui.Draw.Field(s, 0, y, s.Width, " " + text, t.ButtonFocused);
    }

    protected internal override (int X, int Y)? Cursor => null;

    protected internal override void Handle(InputEvent ev)
    {
        switch (ev)
        {
            case KeyInput { Key: Key.Escape }: Closed = true; return;
            case KeyInput { Key: Key.Character } k when (k.Mods & (Mods.Ctrl | Mods.Alt)) == 0:
                char c = KeyboardLayout.Latin(k.Character);
                if (keys.Contains(c, StringComparison.Ordinal)) { Result = c; Closed = true; }
                return;
        }
    }
}

/// <summary>A question with buttons. Enter presses the focused button (the first one by default), Esc picks <see cref="CancelIndex"/>.</summary>
public sealed class ButtonDialog : Modal
{
    readonly string title, message;
    readonly IReadOnlyList<string> buttons;
    readonly int cancelIndex;
    int _focus;
    readonly List<(int X, int W)> _slots = new();
    Rect _rect;
    int _buttonsY;

    public ButtonDialog(string title, string message, IReadOnlyList<string> buttons, int cancelIndex)
    {
        this.title = title; this.message = message; this.buttons = buttons; this.cancelIndex = cancelIndex;
        Result = cancelIndex;
    }

    public int CancelIndex => cancelIndex;
    public int Result { get; private set; }

    protected internal override void Draw(Screen s, Theme t)
    {
        int buttonsW = buttons.Sum(b => b.Length + 6) + buttons.Count - 1;     // "[  Save  ]" each, one space between
        // '\n' starts a new line of the message; a line wider than the screen goes on in the next (an error's text)
        var lines = Tui.Draw.Wrap(message, Math.Max(20, s.Width - 6));
        _rect = Tui.Draw.Centered(s, Math.Max(Math.Max(40, lines.Max(l => l.Length) + 6), buttonsW + 6), 6 + lines.Count, title);
        Tui.Draw.Box(s, _rect, title, t.Dialog, t.DialogTitle);
        for (int i = 0; i < lines.Count; i++)
            s.Text(_rect.X + 2, _rect.Y + 1 + i, lines[i].Length > _rect.W - 4 ? lines[i][..(_rect.W - 4)] : lines[i], t.Dialog);
        int x = _rect.X + Math.Max(2, (_rect.W - buttonsW) / 2);         // centred inside the frame
        _buttonsY = _rect.Y + 3 + lines.Count;
        _slots.Clear();
        for (int i = 0; i < buttons.Count; i++)
        {
            string text = i == _focus ? $"[► {buttons[i]} ◄]" : $"[  {buttons[i]}  ]";
            s.Text(x, _buttonsY, text, i == _focus ? t.ButtonFocused : t.Button);
            _slots.Add((x, text.Length));
            x += text.Length + 1;
        }
        Tui.Draw.Shadow(s, _rect, t.Shadow);
    }

    protected internal override void Handle(InputEvent ev)
    {
        switch (ev)
        {
            case KeyInput { Key: Key.Enter }: Result = _focus; Closed = true; return;
            case KeyInput { Key: Key.Escape }: Result = cancelIndex; Closed = true; return;
            case KeyInput { Key: Key.Left } or KeyInput { Key: Key.Tab, Mods: Mods.Shift }: _focus = (_focus + buttons.Count - 1) % buttons.Count; return;
            case KeyInput { Key: Key.Right } or KeyInput { Key: Key.Tab }: _focus = (_focus + 1) % buttons.Count; return;
            case KeyInput { Key: Key.Character } k when (k.Mods & ~Mods.Alt) == 0:
                for (int i = 0; i < buttons.Count; i++)
                    if (KeyboardLayout.Latin(buttons[i][0]) == KeyboardLayout.Latin(k.Character)) { Result = i; Closed = true; return; }
                return;
            case MouseInput { Kind: MouseKind.Down, Button: 0 } m when m.Y == _buttonsY:
                for (int i = 0; i < _slots.Count; i++)
                    if (m.X >= _slots[i].X && m.X < _slots[i].X + _slots[i].W) { Result = i; Closed = true; return; }
                return;
        }
    }
}

/// <summary>Pick one entry from a list. Enter or a double click chooses, Esc cancels (Result stays null).</summary>
public class ListDialog : Modal
{
    readonly string _title;
    readonly string? _header;
    protected ListBox List { get; } = new();
    Rect _listRect;

    public int? Result { get; protected set; }

    public ListDialog(string title, IReadOnlyList<string> items, int selected = 0, string? header = null)
    {
        _title = title; _header = header;
        List.Items = items;
        List.Selected = Math.Clamp(selected, 0, Math.Max(0, items.Count - 1));
        List.Activated += OnActivated;
    }

    protected virtual void OnActivated(int index) { Result = index; Closed = true; }

    protected virtual string? Header => _header;
    protected virtual bool HeaderKeepsEnd => false;
    protected virtual string Hint => "";
    /// <summary>Rows under the list for a subclass's own controls, drawn by <see cref="DrawBelow"/>.</summary>
    protected virtual int RowsBelow => 0;
    protected virtual void DrawBelow(Screen s, Theme t, int x, int y, int width) { }

    /// <summary>Width and height as a share of the screen (0..1), or absolute when greater than 1.</summary>
    protected virtual (double W, double H) Size => (0.6, Math.Min(List.Items.Count + 4 + (Header != null ? 1 : 0) + (Hint.Length > 0 ? 1 : 0), 22));

    protected internal override void Draw(Screen s, Theme t)
    {
        var (sw, sh) = Size;
        int w = sw <= 1 ? (int)(s.Width * sw) : (int)sw;
        // a share of the screen grows to the longest item (and the title), never past the screen: rows are not cut
        if (sw <= 1 && List.Items.Count > 0) w = Math.Max(w, Math.Min(s.Width - 4, Math.Max(List.Items.Max(i => i.Length), _title.Length + 4) + 6));
        int h = sh <= 1 ? (int)(s.Height * sh) : (int)sh;
        var r = Tui.Draw.Centered(s, Math.Max(20, w), Math.Max(5, h), _title);
        Tui.Draw.Box(s, r, _title, t.Dialog, t.DialogTitle);
        int top = r.Y + 1;
        if (Header is { } head)
        {
            // a path too long for the box keeps its end, the folder one is in: "…\projects\notes"
            if (HeaderKeepsEnd && head.Length > r.W - 4) head = "…" + head[^(r.W - 5)..];
            Tui.Draw.Field(s, r.X + 2, top, r.W - 4, head, t.Dialog);
            top++;
        }
        int bottom = r.Y + r.H - 1 - (Hint.Length > 0 ? 1 : 0) - RowsBelow;
        _listRect = new Rect(r.X + 2, top, r.W - 4, Math.Max(1, bottom - top));
        List.Draw(s, _listRect, t.Dialog, t.ListSelected);
        if (RowsBelow > 0) DrawBelow(s, t, r.X + 2, bottom, r.W - 4);
        if (Hint.Length > 0) s.Text(r.X + 2, r.Y + r.H - 2, Hint, t.Dialog);
        Tui.Draw.Shadow(s, r, t.Shadow);
    }

    protected internal override void Handle(InputEvent ev)
    {
        switch (ev)
        {
            case KeyInput { Key: Key.Escape }: Closed = true; return;
            case MouseInput { Kind: MouseKind.Down, Button: 0 } m when _listRect.Contains(m.X, m.Y): List.ClickRow(m.Y - _listRect.Y); return;
            default: List.Handle(ev); return;
        }
    }
}

/// <summary>Read-only scrollable text (help). Esc or Enter closes.</summary>
public sealed class TextDialog : ListDialog
{
    public TextDialog(string title, IReadOnlyList<string> lines) : base(title, lines) => List.ShowSelection = false;

    protected override string Hint => Strings.T("Up/Down, PgUp/PgDn scroll     Esc closes");
    protected override (double W, double H) Size => (0.92, 0.9);
    protected override void OnActivated(int index) => Closed = true;

    // scrolling a read-only page moves the view, not a highlighted row
    protected internal override void Handle(InputEvent ev)
    {
        if (ev is KeyInput { Key: Key.Up }) { List.Selected = Math.Max(0, Math.Min(List.Selected, TopGuess()) - 1); return; }
        if (ev is KeyInput { Key: Key.Down }) { List.Selected = Math.Min(List.Items.Count - 1, Math.Max(List.Selected, TopGuess() + List.VisibleRows - 1) + 1); return; }
        base.Handle(ev);
    }

    int TopGuess() => Math.Max(0, List.Selected - List.VisibleRows + 1);
}

/// <summary>
/// FAR-style file picker: Enter opens a folder or picks a file, ".." goes up (from a drive's root to the list of
/// drives), Alt+F1 shows the drives, F7 makes a folder here, Esc cancels. Given a file name it is a Save dialog: a name
/// field under the list, where typing goes; Tab moves between the list and the field; Enter on a file in the list puts
/// its name in the field; Enter in the field saves — a name in the folder shown, or a full path, or a folder's path to
/// go there. Whether to overwrite, or to make a typed path's missing folder, is the caller's question.
/// </summary>
public sealed class FileDialog : ListDialog
{
    static readonly char Sep = System.IO.Path.DirectorySeparatorChar;
    const string Drives = "";                       // _dir of the drives view

    string _dir;
    List<string> _entries = new();
    readonly TextInput? _name;
    TextInput? _newFolder;
    bool _onName;
    string? _message;
    (int X, int Y) _caret;

    public string? Path { get; private set; }

    public FileDialog(string title, string? startDir, string? saveName = null) : base(title, Array.Empty<string>())
    {
        _dir = startDir != null && Directory.Exists(startDir) ? startDir : Directory.GetCurrentDirectory();
        if (saveName != null) { _name = new TextInput(saveName); _onName = true; }
        Refresh();
    }

    bool Saving => _name != null;

    protected override string? Header => _dir == Drives ? Strings.T("Drives") : _dir;
    protected override bool HeaderKeepsEnd => true;
    protected override (double W, double H) Size => (0.7, 0.8);
    protected override int RowsBelow => Saving || _newFolder != null || _message != null ? 2 : 0;
    protected override string Hint => Saving
        ? Strings.T("Tab list/name   Enter save   F7 new folder   Alt+F1 drives   Esc cancel")
        : Strings.T("Enter open   F7 new folder   Alt+F1 drives   Esc cancel");

    /// <summary>The drives: on Windows their roots (C:\, D:\…); elsewhere the home folder, / and mounted disks.</summary>
    static List<string> DriveList()
    {
        var res = new List<string>();
        if (!OperatingSystem.IsWindows()) res.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (!OperatingSystem.IsWindows() && d.Name != "/" && (d.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network) || d.TotalSize == 0)) continue;
                res.Add(d.Name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return res.Distinct().ToList();
    }

    void Go(string dir, string? select = null)
    {
        _dir = dir;
        _message = null;
        Refresh();
        if (select != null && _entries.IndexOf(select) is >= 0 and var i) List.Selected = i;
    }

    void Refresh()
    {
        _entries = new List<string>();
        if (_dir == Drives) _entries.AddRange(DriveList());
        else
        {
            _entries.Add("..");
            try
            {
                _entries.AddRange(Directory.GetDirectories(_dir).Select(d => System.IO.Path.GetFileName(d) + Sep)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                _entries.AddRange(Directory.GetFiles(_dir).Select(System.IO.Path.GetFileName).OfType<string>()
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            }
            catch (UnauthorizedAccessException) { _message = Strings.T("No access to this folder"); }
            catch (IOException ex) { _message = ex.Message; }
        }
        List.Items = _entries;
        List.Selected = 0;
    }

    protected override void OnActivated(int index)
    {
        var name = _entries[index];
        if (_dir == Drives) { Go(name); return; }
        if (name == "..")
        {
            var parent = Directory.GetParent(_dir);
            var here = System.IO.Path.GetFileName(_dir.TrimEnd(Sep)) + Sep;
            if (parent == null) Go(Drives, _dir); else Go(parent.FullName, here);
            return;
        }
        if (name.EndsWith(Sep)) { Go(System.IO.Path.Combine(_dir, name.TrimEnd(Sep))); return; }
        if (_name != null) { _name.Set(name); _onName = true; return; }
        Path = System.IO.Path.Combine(_dir, name);
        Result = index;
        Closed = true;
    }

    // Enter in the name field: a folder goes there, anything else is the file to save
    void Accept()
    {
        var text = _name!.Text.Trim();
        if (text.Length == 0) return;
        var full = System.IO.Path.IsPathRooted(text) || _dir == Drives ? text : System.IO.Path.Combine(_dir, text);
        if (Directory.Exists(full)) { Go(System.IO.Path.GetFullPath(full)); _name.Set(""); return; }
        if (text.EndsWith(Sep) || text.EndsWith('/')) { _message = Strings.T("No such folder: ") + text; return; }
        Path = System.IO.Path.GetFullPath(full);
        Result = 0;
        Closed = true;
    }

    void MakeFolder()
    {
        var name = _newFolder!.Text.Trim();
        _newFolder = null;
        if (name.Length == 0) return;
        try
        {
            var dir = System.IO.Path.Combine(_dir, name);
            Directory.CreateDirectory(dir);
            Go(System.IO.Path.GetFullPath(dir));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _message = Strings.T("Could not make the folder: ") + ex.Message;
        }
    }

    protected override void DrawBelow(Screen s, Theme t, int x, int y, int width)
    {
        if (_message != null) s.Text(x, y, _message.Length > width ? _message[..width] : _message.PadRight(width), t.Dialog);
        var (label, input) = _newFolder != null ? (Strings.T("New folder: "), _newFolder) : (Strings.T("File name: "), _name);
        if (input == null) return;
        s.Text(x, y + 1, label, t.Dialog);
        var caret = input.Draw(s, x + label.Length, y + 1, width - label.Length, t.Input);
        if (_newFolder != null || _onName) _caret = caret;
    }

    protected internal override (int X, int Y)? Cursor => _newFolder != null || (Saving && _onName) ? _caret : null;

    protected internal override void Handle(InputEvent ev)
    {
        if (_newFolder != null)
        {
            switch (ev)
            {
                case KeyInput { Key: Key.Enter }: MakeFolder(); return;
                case KeyInput { Key: Key.Escape }: _newFolder = null; return;
                default: _newFolder.Handle(ev); return;
            }
        }
        switch (ev)
        {
            case KeyInput { Key: Key.F7, Mods: Mods.None } when _dir != Drives: _newFolder = new TextInput(""); _message = null; return;
            case KeyInput { Key: Key.F1, Mods: Mods.Alt }: Go(Drives, _dir == Drives ? null : System.IO.Path.GetPathRoot(_dir)); return;
            case KeyInput { Key: Key.Tab } when Saving: _onName = !_onName; return;
        }
        if (Saving && _onName)
        {
            switch (ev)
            {
                case KeyInput { Key: Key.Enter }: Accept(); return;
                case KeyInput { Key: Key.Escape }: Closed = true; return;
                case KeyInput { Key: Key.Up or Key.Down or Key.PageUp or Key.PageDown }: _onName = false; List.Handle(ev); return;
                case MouseInput: _onName = false; base.Handle(ev); return;
                default: _name!.Handle(ev); return;
            }
        }
        // in the list: typing goes to the name field (Save), Backspace goes up a folder
        if (Saving && ev is KeyInput { Key: Key.Character } k && (k.Mods & ~Mods.Shift) == 0) { _onName = true; _name!.Handle(ev); return; }
        if (ev is KeyInput { Key: Key.Backspace, Mods: Mods.None } && _dir != Drives && _entries.Count > 0) { OnActivated(0); return; }
        base.Handle(ev);
    }
}
