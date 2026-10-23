using System.Text;

namespace Tui;

/// <summary>One-line text editor used inside dialogs.</summary>
public sealed class TextInput
{
    readonly StringBuilder _text;
    int _scroll;

    public TextInput(string initial = "", bool replaceOnType = false)
    {
        _text = new StringBuilder(initial);
        Caret = _text.Length;
        Fresh = replaceOnType && initial.Length > 0;
    }

    public string Text => _text.ToString();
    public int Caret { get; private set; }

    /// <summary>Replaces the text, the caret at its end.</summary>
    public void Set(string text)
    {
        _text.Clear().Append(text);
        Caret = _text.Length;
        _scroll = 0;
        Fresh = false;
    }

    /// <summary>The initial text is still untouched and will be replaced by the first typed character or paste
    /// (as the last search term in a Find dialog). Any editing or caret key keeps it instead.</summary>
    public bool Fresh { get; private set; }

    public bool Handle(InputEvent ev)
    {
        if (Fresh)
        {
            Fresh = false;
            if (ev is PasteInput || ev is KeyInput { Key: Key.Character } k0 && (k0.Mods & ~Mods.Shift) == 0) { _text.Clear(); Caret = 0; _scroll = 0; }
        }
        switch (ev)
        {
            case PasteInput p:
                Insert(p.Text.Replace("\r", "", StringComparison.Ordinal).Replace('\n', ' '));
                return true;
            case KeyInput { Key: Key.Character } k when (k.Mods & ~Mods.Shift) == 0:
                Insert(k.Character.ToString());
                return true;
            case KeyInput k:
                switch (k.Key)
                {
                    case Key.Backspace:
                        if (k.Ctrl) { int a = WordLeft(); _text.Remove(a, Caret - a); Caret = a; }
                        else if (Caret > 0) { _text.Remove(Caret - 1, 1); Caret--; }
                        return true;
                    case Key.Delete: if (Caret < _text.Length) _text.Remove(Caret, 1); return true;
                    case Key.Left: Caret = k.Ctrl ? WordLeft() : Math.Max(0, Caret - 1); return true;
                    case Key.Right: Caret = k.Ctrl ? WordRight() : Math.Min(_text.Length, Caret + 1); return true;
                    case Key.Home: Caret = 0; return true;
                    case Key.End: Caret = _text.Length; return true;
                }
                return false;
        }
        return false;
    }

    void Insert(string s) { _text.Insert(Caret, s); Caret += s.Length; }

    int WordLeft()
    {
        int i = Caret;
        while (i > 0 && _text[i - 1] == ' ') i--;
        while (i > 0 && _text[i - 1] != ' ') i--;
        return i;
    }

    int WordRight()
    {
        int i = Caret;
        while (i < _text.Length && _text[i] != ' ') i++;
        while (i < _text.Length && _text[i] == ' ') i++;
        return i;
    }

    /// <summary>Draws the field and returns the screen position of the caret. Text that typing would replace is shown
    /// in <paramref name="freshStyle"/> when given.</summary>
    public (int X, int Y) Draw(Screen s, int x, int y, int width, Style style, Style? freshStyle = null)
    {
        if (Caret < _scroll) _scroll = Caret;
        if (Caret >= _scroll + width) _scroll = Caret - width + 1;
        var visible = _scroll < _text.Length ? _text.ToString(_scroll, Math.Min(width, _text.Length - _scroll)) : "";
        Tui.Draw.Field(s, x, y, width, "", style);
        s.Text(x, y, visible, Fresh && freshStyle is { } fs ? fs : style);
        return (x + Caret - _scroll, y);
    }
}

/// <summary>Scrollable list with a highlighted row. Enter and double click activate.</summary>
public sealed class ListBox
{
    IReadOnlyList<string> _items = Array.Empty<string>();
    int _top;

    public int Selected { get; set; }
    public int VisibleRows { get; private set; } = 10;
    /// <summary>Index of the first visible item (after the last <see cref="Draw"/>).</summary>
    public int Top => _top;
    public bool ShowSelection { get; set; } = true;

    public IReadOnlyList<string> Items
    {
        get => _items;
        set { _items = value; Selected = Math.Clamp(Selected, 0, Math.Max(0, value.Count - 1)); _top = 0; }
    }

    /// <summary>Raised on Enter or a click on an item.</summary>
    public event Action<int>? Activated;

    public bool Handle(InputEvent ev)
    {
        int page = Math.Max(1, VisibleRows - 1);
        switch (ev)
        {
            case KeyInput { Key: Key.Up }: Move(-1); return true;
            case KeyInput { Key: Key.Down }: Move(1); return true;
            case KeyInput { Key: Key.PageUp }: Move(-page); return true;
            case KeyInput { Key: Key.PageDown }: Move(page); return true;
            case KeyInput { Key: Key.Home }: Selected = 0; return true;
            case KeyInput { Key: Key.End }: Selected = Math.Max(0, _items.Count - 1); return true;
            case KeyInput { Key: Key.Enter }: if (_items.Count > 0) Activated?.Invoke(Selected); return true;
            case MouseInput { Kind: MouseKind.WheelUp }: Scroll(-3); return true;
            case MouseInput { Kind: MouseKind.WheelDown }: Scroll(3); return true;
        }
        return false;
    }

    /// <summary>Mouse press at a row relative to the list's first visible row.</summary>
    public void ClickRow(int row)
    {
        int i = _top + row;
        if (i < 0 || i >= _items.Count) return;
        bool again = i == Selected;
        Selected = i;
        if (again) Activated?.Invoke(i);
    }

    void Move(int delta) => Selected = Math.Clamp(Selected + delta, 0, Math.Max(0, _items.Count - 1));

    void Scroll(int delta)
    {
        _top = Math.Clamp(_top + delta, 0, Math.Max(0, _items.Count - VisibleRows));
        Selected = Math.Clamp(Selected, _top, Math.Min(_items.Count - 1, _top + VisibleRows - 1));
    }

    public void Draw(Screen s, Rect area, Style normal, Style selected)
    {
        VisibleRows = Math.Max(1, area.H);
        if (Selected < _top) _top = Selected;
        if (Selected >= _top + VisibleRows) _top = Selected - VisibleRows + 1;
        for (int row = 0; row < area.H; row++)
        {
            int i = _top + row;
            var text = i < _items.Count ? _items[i] : "";
            var style = ShowSelection && i == Selected && i < _items.Count ? selected : normal;
            Tui.Draw.Field(s, area.X, area.Y + row, area.W, text, style);
        }
    }
}
