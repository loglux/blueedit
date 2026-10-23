namespace Tui;

/// <summary>What the app needs from a terminal. <see cref="ConsoleHost"/> is the real one; tests use a scripted fake.</summary>
public interface ITerminal
{
    Screen Screen { get; }
    bool TryNext(out InputEvent? ev, int timeoutMs);
    void Present();
    void ApplyResize(int width, int height);
    /// <summary>Makes a waiting <see cref="TryNext"/> return at once (with a <see cref="WakeInput"/>); terminals that
    /// cannot just let the loop find the posted work within its usual wait.</summary>
    void Wake() { }
}

public readonly record struct Rect(int X, int Y, int W, int H)
{
    public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>Colours of the classic blue DOS look. One place to change for themes later.</summary>
public sealed class Theme
{
    public Style Bar { get; init; } = Style.Rgb(0x000000, 0x00AAAA);
    public Style BarHotkey { get; init; } = Style.Rgb(0xAA0000, 0x00AAAA);
    public Style BarSelected { get; init; } = Style.Rgb(0xFFFFFF, 0x000000);
    public Style Menu { get; init; } = Style.Rgb(0x000000, 0xAAAAAA);
    public Style MenuHotkey { get; init; } = Style.Rgb(0xAA0000, 0xAAAAAA);
    public Style MenuSelected { get; init; } = Style.Rgb(0xFFFFFF, 0x000000);
    public Style MenuShortcut { get; init; } = Style.Rgb(0x555555, 0xAAAAAA);
    public Style Dialog { get; init; } = Style.Rgb(0x000000, 0xAAAAAA);
    public Style DialogTitle { get; init; } = Style.Rgb(0xFFFFFF, 0xAAAAAA, Attr.Bold);
    public Style Input { get; init; } = Style.Rgb(0xFFFFFF, 0x0000AA);
    public Style ListSelected { get; init; } = Style.Rgb(0xFFFFFF, 0x0000AA);
    public Style Button { get; init; } = Style.Rgb(0x000000, 0x00AAAA);
    public Style ButtonFocused { get; init; } = Style.Rgb(0xFFFFFF, 0x0000AA, Attr.Bold);
    public Style Shadow { get; init; } = Style.Rgb(0x555555, 0x000000);
}

/// <summary>The main screen content (the editor with its bars). Drawn first, under menus and dialogs.</summary>
public interface IRootView
{
    void Draw(Screen screen);
    /// <summary>Returns true when the event was used.</summary>
    bool Handle(InputEvent ev);
    /// <summary>Where the text cursor should be, or null to hide it.</summary>
    (int X, int Y)? Cursor { get; }
}

/// <summary>A dialog or other window that takes all input until it closes.</summary>
public abstract class Modal
{
    public bool Closed { get; protected set; }
    protected internal abstract void Draw(Screen s, Theme t);
    protected internal abstract void Handle(InputEvent ev);
    protected internal virtual (int X, int Y)? Cursor => null;
}

/// <summary>
/// Event loop: draws the root view, the menu bar and any open dialogs into the screen, presents the frame,
/// then waits for the next input event. Dialogs run in a nested loop, so <see cref="RunModal"/> returns their result directly.
/// </summary>
public sealed class TuiApp
{
    readonly ITerminal _term;
    readonly List<Modal> _modals = new();
    bool _stop;

    public Theme Theme { get; set; } = new();
    public IRootView? Root { get; set; }
    public MenuBar? Menu { get; set; }

    public TuiApp(ITerminal terminal) => _term = terminal;

    public Screen Screen => _term.Screen;

    public void RequestStop() => _stop = true;

    /// <summary>Called from the main loop about every <see cref="TickMs"/>, whether keys arrive or not, and never while
    /// a dialog is open. It may open dialogs itself.</summary>
    public Action? Tick { get; set; }
    public int TickMs { get; set; } = 500;

    /// <summary>The window's title, asked for with every frame (sent only when it changed); null leaves it alone.</summary>
    public Func<string>? Title { get; set; }

    /// <summary>Longest time spent on input that is already waiting before a frame is drawn anyway.</summary>
    public int CoalesceMs { get; set; } = 40;

    readonly System.Collections.Concurrent.ConcurrentQueue<Action> _posted = new();

    /// <summary>Runs <paramref name="work"/> on the main loop's thread as soon as it is free (from any thread): how an
    /// answer that arrived in the background (a language server's completions) gets to the screen.</summary>
    public void Post(Action work)
    {
        _posted.Enqueue(work);
        _term.Wake();
    }

    void RunPosted()
    {
        while (_posted.TryDequeue(out var work)) work();
    }

    /// <summary>Runs until <see cref="RequestStop"/> is called.</summary>
    public void Run()
    {
        _stop = false;
        long lastTick = Environment.TickCount64;
        while (!_stop)
        {
            RunPosted();
            Render();
            bool got = _term.TryNext(out var ev, Math.Min(250, TickMs));
            if (got && ev is WakeInput) continue;
            if (got && ev != null)
            {
                Dispatch(ev);
                // Input that is already waiting (a held arrow key, a fast typist) is handled before the next frame,
                // so the screen never falls behind the keyboard. Capped so a flood still shows progress.
                long until = Environment.TickCount64 + CoalesceMs;
                while (!_stop && _modals.Count == 0 && Environment.TickCount64 < until && _term.TryNext(out var more, 0) && more != null)
                    Dispatch(more);
            }
            if (Tick != null && Menu?.IsOpen != true && !_stop && Environment.TickCount64 - lastTick >= TickMs)
            {
                lastTick = Environment.TickCount64;
                Tick();
            }
        }
    }

    /// <summary>Shows a dialog and blocks until it closes.</summary>
    public T RunModal<T>(T modal) where T : Modal
    {
        _modals.Add(modal);
        try
        {
            while (!modal.Closed && !_stop)
            {
                RunPosted();
                if (modal.Closed) break;
                Render();
                if (!_term.TryNext(out var ev, 250) || ev == null || ev is WakeInput) continue;
                if (ev is ResizeInput r) { _term.ApplyResize(r.Width, r.Height); continue; }
                modal.Handle(Normalize(ev));
            }
        }
        finally { _modals.Remove(modal); }
        return modal;
    }

    /// <summary>Alt/Ctrl + a letter of the Russian layout becomes Alt/Ctrl + the key's Latin letter, so hotkeys
    /// (Alt+F, Alt+Z, ...) work whichever layout is active.</summary>
    public static InputEvent Normalize(InputEvent ev) =>
        ev is KeyInput { Key: Key.Character } k && (k.Mods & (Mods.Alt | Mods.Ctrl)) != 0 && k.Character > '\u007f'
            && KeyboardLayout.Latin(k.Character) is var latin && latin <= '\u007f'
            ? k with { Character = latin }
            : ev;

    void Dispatch(InputEvent ev)
    {
        if (ev is WakeInput) return;
        ev = Normalize(ev);
        if (ev is ResizeInput r) { _term.ApplyResize(r.Width, r.Height); Root?.Handle(ev); return; }
        if (Menu != null && Menu.TryHandle(ev, this)) return;
        Root?.Handle(ev);
    }

    /// <summary>Draws everything and sends the changes to the terminal.</summary>
    public void Render()
    {
        var s = _term.Screen;
        if (Title != null) s.Title = Title();
        Root?.Draw(s);
        Menu?.DrawBar(s, Theme);
        (int X, int Y)? cursor = Root?.Cursor;
        if (Menu?.IsOpen == true) { Menu.DrawDropDown(s, Theme); cursor = null; }
        foreach (var m in _modals) { m.Draw(s, Theme); cursor = m.Cursor; }
        if (cursor is { } c) s.SetCursor(c.X, c.Y, true); else s.SetCursor(0, 0, false);
        _term.Present();
    }
}

public static class Draw
{
    /// <summary>Word wrap: lines of at most width characters, breaking at spaces where possible; '\n' starts a new one.</summary>
    public static List<string> Wrap(string text, int width)
    {
        var res = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var rest = para;
            while (rest.Length > width)
            {
                int cut = rest.LastIndexOf(' ', width);
                if (cut <= 0) cut = width;
                res.Add(rest[..cut]);
                rest = rest[cut..].TrimStart();
            }
            res.Add(rest);
        }
        return res;
    }

    /// <summary>Heavy box with the title set into the top edge: ┏┥Title┝━━┓. Fills the inside.</summary>
    public static void Box(Screen s, Rect r, string title, Style style, Style titleStyle)
    {
        s.Fill(r.X, r.Y, r.W, r.H, ' ', style);
        for (int x = r.X + 1; x < r.X + r.W - 1; x++) { s.Set(x, r.Y, '━', style); s.Set(x, r.Y + r.H - 1, '━', style); }
        for (int y = r.Y + 1; y < r.Y + r.H - 1; y++) { s.Set(r.X, y, '┃', style); s.Set(r.X + r.W - 1, y, '┃', style); }
        s.Set(r.X, r.Y, '┏', style); s.Set(r.X + r.W - 1, r.Y, '┓', style);
        s.Set(r.X, r.Y + r.H - 1, '┗', style); s.Set(r.X + r.W - 1, r.Y + r.H - 1, '┛', style);
        if (title.Length > 0 && r.W > 6)
        {
            string t = title.Length > r.W - 6 ? title[..(r.W - 6)] : title;
            s.Set(r.X + 1, r.Y, '┥', style);
            s.Text(r.X + 2, r.Y, t, titleStyle);
            s.Set(r.X + 2 + t.Length, r.Y, '┝', style);
        }
    }

    /// <summary>Dark cells right of and below a window, as DOS dialogs had.</summary>
    public static void Shadow(Screen s, Rect r, Style style)
    {
        for (int y = r.Y + 1; y <= r.Y + r.H; y++)
            for (int x = r.X + r.W; x < r.X + r.W + 2; x++) s.Set(x, y, s.CharAt(x, y), style);
        for (int x = r.X + 2; x < r.X + r.W + 2; x++) s.Set(x, r.Y + r.H, s.CharAt(x, r.Y + r.H), style);
    }

    /// <summary>Text clipped to a width and padded with spaces.</summary>
    public static void Field(Screen s, int x, int y, int width, string text, Style style)
    {
        for (int i = 0; i < width; i++) s.Set(x + i, y, i < text.Length ? text[i] : ' ', style);
    }

    /// <summary>A box of w × h in the middle of the screen; with its title, wide enough to show it whole (<see cref="Box"/>
    /// cuts a title longer than the width less 6), within the screen.</summary>
    public static Rect Centered(Screen s, int w, int h, string title = "")
    {
        if (title.Length > 0) w = Math.Max(w, Math.Min(s.Width - 2, title.Length + 6));
        w = Math.Min(w, s.Width); h = Math.Min(h, s.Height);
        return new Rect((s.Width - w) / 2, (s.Height - h) / 2, w, h);
    }
}
