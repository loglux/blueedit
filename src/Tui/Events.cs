namespace Tui;

public enum Key
{
    None, Character, Enter, Tab, Backspace, Escape,
    Up, Down, Left, Right, Home, End, PageUp, PageDown, Insert, Delete,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
}

[Flags]
public enum Mods { None = 0, Shift = 1, Alt = 2, Ctrl = 4 }

public enum MouseKind { Down, Up, Drag, Move, WheelUp, WheelDown, WheelLeft, WheelRight }

/// <summary>Anything the terminal can tell us: a key, a mouse action, pasted text, a size change.</summary>
public abstract record InputEvent;

/// <summary>A key press. <see cref="Character"/> is meaningful when <see cref="Key"/> is <see cref="Key.Character"/>; a letter pressed with Ctrl arrives as the lowercase letter plus <see cref="Mods.Ctrl"/>.</summary>
public sealed record KeyInput(Key Key, char Character = '\0', Mods Mods = Mods.None) : InputEvent
{
    public bool Ctrl => (Mods & Mods.Ctrl) != 0;
    public bool Alt => (Mods & Mods.Alt) != 0;
    public bool Shift => (Mods & Mods.Shift) != 0;

    public bool Is(char c, Mods mods = Mods.None) => Key == Key.Character && Character == c && Mods == mods;
    public bool Is(Key key, Mods mods = Mods.None) => Key == key && Mods == mods;
}

/// <summary>A mouse event; X and Y are zero-based cells. Button is 0 left, 1 middle, 2 right.</summary>
public sealed record MouseInput(MouseKind Kind, int Button, int X, int Y, Mods Mods = Mods.None) : InputEvent;

public sealed record PasteInput(string Text) : InputEvent;

public sealed record ResizeInput(int Width, int Height) : InputEvent;

/// <summary>Not input: the terminal's answer to a query. <see cref="Final"/> 'c' is its device attributes (CSI ? … c,
/// the numbers after "?"), 't' a size report (CSI 6 ; h ; w t: a cell in pixels, 4: the window, 8: rows and columns).
/// <see cref="ConsoleHost"/> keeps these to itself.</summary>
public sealed record TerminalReply(char Final, IReadOnlyList<int> Values) : InputEvent;

/// <summary>Not input: wakes the main loop so work posted from another thread (<see cref="TuiApp.Post"/>) runs now.</summary>
public sealed record WakeInput : InputEvent;
