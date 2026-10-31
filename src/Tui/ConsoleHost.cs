using System.Collections.Concurrent;
using System.Text;

namespace Tui;

/// <summary>
/// Owns the terminal for the lifetime of the app: puts it in raw mode, reads input on a dedicated thread that wakes
/// the instant a byte arrives, parses it into events, and writes finished frames in a single call.
/// </summary>
public sealed class ConsoleHost : ITerminal, IDisposable
{
    const int EscapeTimeoutMs = 25;

    readonly ITerminalPlatform _platform;
    readonly InputParser _parser = new();
    readonly BlockingCollection<InputEvent> _events = new();
    readonly object _parserLock = new();
    readonly object _writeLock = new();
    readonly Timer _escTimer;
    readonly Thread _inputThread;
    readonly Thread _sizeThread;
    readonly CancellationTokenSource _stop = new();
    bool _started;

    public Screen Screen { get; private set; }

    public ConsoleHost(ITerminalPlatform? platform = null)
    {
        _platform = platform ?? ITerminalPlatform.CreateForCurrentOs();
        var (w, h) = _platform.Size;
        Screen = new Screen(w, h);
        _escTimer = new Timer(_ => FlushEscape(), null, Timeout.Infinite, Timeout.Infinite);
        _inputThread = new Thread(ReadLoop) { IsBackground = true, Name = "tui-input" };
        _sizeThread = new Thread(SizeLoop) { IsBackground = true, Name = "tui-size" };
    }

    /// <summary>Enters raw mode and the alternate screen and enables mouse and bracketed paste reporting.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _platform.EnterRawMode();
        lock (_modeLock) if (!_awaitingDa) _platform.SetKeyEvents(_keyEvents);
        // the title before ours, put back on exit: on Windows read from the console, elsewhere pushed onto the
        // terminal's title stack (xterm's; a terminal without one ignores it)
        if (OperatingSystem.IsWindows()) try { _oldTitle = Console.Title; } catch (IOException) { }
        Write("\x1b[?1049h\x1b[?2004h\x1b[?1000h\x1b[?1002h\x1b[?1006h\x1b[2J");     // alt screen, paste, mouse (press/drag/SGR)
        if (!OperatingSystem.IsWindows()) Write("\u001b[22;0t");
        lock (_modeLock) ApplyKittyKeys();
        Screen.Invalidate();
        _inputThread.Start();
        _sizeThread.Start();
    }

    /// <summary>The terminal said it shows Sixel graphics (attribute 4 in its device attributes) — after
    /// <see cref="QueryGraphics"/>.</summary>
    public bool Sixel { get; private set; }

    string? _oldTitle;

    /// <summary>A cell's size in the pixels of Sixel graphics, as the terminal reported it (Windows Terminal: 10 × 20,
    /// whatever the font), or null when it did not.</summary>
    public (int W, int H)? CellPixels { get; private set; }

    /// <summary>Raised on the input thread when an answer to <see cref="QueryGraphics"/> arrived.</summary>
    public event Action? GraphicsChanged;

    bool _queried;

    // ---- key events (Windows, #184) ----
    // The console drops the terminal's answer to "ESC [ c" unless VT input is on (measured), so while that answer is
    // awaited VT input is kept on, and key events come back with it or after a second without it.
    readonly object _modeLock = new();
    bool _keyEvents, _awaitingDa;
    Timer? _daTimer;

    /// <summary>Windows: read keys as key events, so shortcuts are found by the key, whatever the keyboard layout
    /// (<see cref="WindowsKeys"/>). Other systems: no effect.</summary>
    public void UseKeyEvents(bool on)
    {
        lock (_modeLock)
        {
            _keyEvents = on;
            if (_started && !_awaitingDa) _platform.SetKeyEvents(on);
            ApplyKittyKeys();
        }
    }

    // ---- the kitty keyboard protocol (other systems, #184) ----
    // Asked for only after the terminal answered "ESC [ ? u" with its flags, so a terminal without it gets nothing it
    // could misread. Flags 1 + 4: keys with Ctrl or Alt as "ESC [ code : shifted : base ; mods u", where base is the
    // key in the Latin layout (measured in Windows Terminal 1.25 over ssh: Ctrl+/ with Russian "ESC [ 1105 :: 47 ; 5 u").
    bool _kittyKnown, _kittyPushed;

    void ApplyKittyKeys()                                  // under _modeLock
    {
        if (!_started || _platform.HasKeyEvents) return;
        if (_keyEvents && !_kittyKnown) Write("\u001b[?u");
        else if (_keyEvents && !_kittyPushed) { Write("\u001b[>5u"); _kittyPushed = true; }
        else if (!_keyEvents && _kittyPushed) { Write("\u001b[<u"); _kittyPushed = false; }
    }

    void EndAwaitingDa()
    {
        lock (_modeLock)
        {
            if (!_awaitingDa) return;
            _awaitingDa = false;
            _platform.SetKeyEvents(_keyEvents);
        }
    }

    /// <summary>Asks the terminal for its device attributes and its cell size in pixels; the answers arrive as input
    /// and land in <see cref="Sixel"/> and <see cref="CellPixels"/>. A terminal that does not know a query ignores it.
    /// On the main thread (it writes).</summary>
    public void QueryGraphics()
    {
        _queried = true;
        lock (_modeLock)
        {
            if (_keyEvents)
            {
                _awaitingDa = true;
                _platform.SetKeyEvents(false);
                _daTimer ??= new Timer(_ => EndAwaitingDa());
                _daTimer.Change(1000, Timeout.Infinite);
            }
        }
        Write("\u001b[c\u001b[16t");
    }

    void Reply(TerminalReply r)
    {
        if (r.Final == 'u')
        {
            lock (_modeLock) { _kittyKnown = true; ApplyKittyKeys(); }
            return;
        }
        if (r.Final == 'c') { EndAwaitingDa(); Sixel = r.Values.Contains(4); }
        else if (r.Final == 't' && r.Values[0] == 6 && r.Values[1] > 0 && r.Values[2] > 0) CellPixels = (r.Values[2], r.Values[1]);
        else return;
        GraphicsChanged?.Invoke();
    }

    /// <summary>Waits for the next input event. Returns false on timeout.</summary>
    public bool TryNext(out InputEvent? ev, int timeoutMs) => _events.TryTake(out ev, timeoutMs);

    public void Wake() => _events.Add(new WakeInput());

    /// <summary>Applies a size change reported by a <see cref="ResizeInput"/>.</summary>
    public void ApplyResize(int width, int height)
    {
        Screen.Resize(width, height);
        if (_queried) Write("\u001b[16t");          // a font zoom changes the cell's pixels in terminals that report real ones
    }

    /// <summary>Sends everything that changed since the last call.</summary>
    public void Present()
    {
        var frame = Screen.Flush();
        if (frame.Length > 0) Write(frame);
    }

    public void Write(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        lock (_writeLock)                                  // the input thread writes too (the kitty protocol's answer)
        {
            _platform.Output.Write(bytes, 0, bytes.Length);
            _platform.Output.Flush();
        }
    }

    void ReadLoop()
    {
        var chars = new char[2048];
        var batch = new List<InputEvent>();
        while (!_stop.IsCancellationRequested)
        {
            int count;
            try { count = _platform.ReadInput(chars); }
            catch (IOException) { break; }
            catch (ObjectDisposedException) { break; }
            if (count <= 0) break;
            batch.Clear();
            bool pending;
            lock (_parserLock)
            {
                _parser.Feed(new string(chars, 0, count), batch);
                pending = _parser.HasPending;
            }
            foreach (var e in batch)
                if (e is TerminalReply r) Reply(r);
                else _events.Add(e);
            if (pending) _escTimer.Change(EscapeTimeoutMs, Timeout.Infinite);
        }
    }

    void FlushEscape()
    {
        var batch = new List<InputEvent>();
        lock (_parserLock) _parser.FlushPending(batch);
        foreach (var e in batch) _events.Add(e);
    }

    // Consoles in VT-input mode do not deliver resize events through the byte stream, so the size is polled.
    void SizeLoop()
    {
        // from the screen, not the platform: a change while the program started up is one the screen has not had
        var last = (Screen.Width, Screen.Height);
        while (!_stop.Token.WaitHandle.WaitOne(60))
        {
            var now = _platform.Size;
            if (now == last) continue;
            last = now;
            _events.Add(new ResizeInput(now.Width, now.Height));
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_started)
        {
            lock (_modeLock) if (_kittyPushed) { Write("\u001b[<u"); _kittyPushed = false; }
            Write("\x1b[?1006l\x1b[?1002l\x1b[?1000l\x1b[?2004l\x1b[0m\x1b[?25h\x1b[?1049l");
            if (OperatingSystem.IsWindows()) { if (_oldTitle != null) Write("\u001b]0;" + _oldTitle + "\u0007"); }
            else Write("\u001b[23;0t");
            _platform.RestoreMode();
        }
        _escTimer.Dispose();
        _daTimer?.Dispose();
        _events.Dispose();
    }
}
