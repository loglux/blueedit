using System.Runtime.InteropServices;

namespace Tui;

/// <summary>What the host needs from the operating system: Windows (console API) and Unix (termios).</summary>
public interface ITerminalPlatform : IDisposable
{
    /// <summary>Blocks until terminal input is available and copies it (VT sequences, as characters) into the buffer. Returns 0 at end of input.</summary>
    int ReadInput(char[] buffer);

    Stream Output { get; }

    (int Width, int Height) Size { get; }

    /// <summary>Raw mode: no line editing, no echo, VT input and output processing on.</summary>
    void EnterRawMode();

    void RestoreMode();

    /// <summary>Windows: read keys as key events (each with its key) instead of VT text; see <see cref="WindowsKeys"/>.
    /// Elsewhere nothing changes. Can be switched while input is being read.</summary>
    void SetKeyEvents(bool enabled) { }

    /// <summary>True where <see cref="SetKeyEvents"/> works (the Windows console); elsewhere the host asks the terminal
    /// for the kitty keyboard protocol instead.</summary>
    bool HasKeyEvents => false;

    static ITerminalPlatform CreateForCurrentOs() =>
        OperatingSystem.IsWindows() ? new WindowsPlatform()
        : OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD() ? new UnixPlatform()
        : throw new PlatformNotSupportedException("Windows, Linux and macOS are supported.");
}

/// <summary>
/// A Unix terminal: raw mode with termios (cfmakeraw: no line editing, no echo, no signals from Ctrl+C/Ctrl+Z, 8-bit),
/// input read as UTF-8 bytes from stdin and decoded keeping a character split between two reads. The termios
/// structure is handled as opaque bytes (its layout differs between Linux and macOS): it is only read, made raw by
/// libc and written back. The window size comes from .NET's Console (TIOCGWINSZ underneath), polled by the host.
/// </summary>
sealed class UnixPlatform : ITerminalPlatform
{
    const int Stdin = 0, TcsaNow = 0, Eintr = 4, TermiosSize = 256;   // larger than any libc's struct termios

    readonly byte[] _saved = new byte[TermiosSize];
    readonly byte[] _bytes = new byte[4096];
    readonly System.Text.Decoder _utf8 = new System.Text.UTF8Encoding(false).GetDecoder();
    bool _raw;

    static UnixPlatform()
    {
        // "libc" is libc.so.6 on Linux (plain "libc.so" is often a linker script, or missing without -dev packages)
        NativeLibrary.SetDllImportResolver(typeof(UnixPlatform).Assembly, (name, assembly, path) =>
            name == "libc" && OperatingSystem.IsLinux() && NativeLibrary.TryLoad("libc.so.6", out var h) ? h : IntPtr.Zero);
    }

    public unsafe int ReadInput(char[] buffer)
    {
        while (true)
        {
            nint n;
            fixed (byte* p = _bytes) n = UnixMethods.read(Stdin, p, Math.Min(_bytes.Length, buffer.Length));
            if (n < 0 && Marshal.GetLastPInvokeError() == Eintr) continue;          // a signal (a resize) interrupted it
            if (n <= 0) return 0;                                                  // end of input or an error
            int chars = _utf8.GetChars(_bytes, 0, (int)n, buffer, 0);
            if (chars > 0) return chars;                                           // else: only part of a character so far
        }
    }

    public Stream Output { get; } = Console.OpenStandardOutput();

    public (int Width, int Height) Size
    {
        get
        {
            try { return (Math.Max(1, Console.WindowWidth), Math.Max(1, Console.WindowHeight)); }
            catch (IOException) { return (80, 25); }
        }
    }

    public unsafe void EnterRawMode()
    {
        if (_raw) return;
        fixed (byte* saved = _saved)
        {
            if (UnixMethods.tcgetattr(Stdin, saved) != 0) return;                  // not a terminal: leave it as it is
            var raw = new byte[TermiosSize];
            fixed (byte* r = raw)
            {
                Buffer.MemoryCopy(saved, r, TermiosSize, TermiosSize);
                UnixMethods.cfmakeraw(r);
                UnixMethods.tcsetattr(Stdin, TcsaNow, r);
            }
        }
        _raw = true;
    }

    public unsafe void RestoreMode()
    {
        if (!_raw) return;
        fixed (byte* saved = _saved) UnixMethods.tcsetattr(Stdin, TcsaNow, saved);
        _raw = false;
    }

    public void Dispose() => RestoreMode();
}

static partial class UnixMethods
{
#pragma warning disable SA1300, IDE1006 // libc's own names
    [LibraryImport("libc", SetLastError = true)] public static unsafe partial nint read(int fd, byte* buffer, nint count);
    [LibraryImport("libc", SetLastError = true)] public static unsafe partial int tcgetattr(int fd, byte* termios);
    [LibraryImport("libc", SetLastError = true)] public static unsafe partial int tcsetattr(int fd, int when, byte* termios);
    [LibraryImport("libc")] public static unsafe partial void cfmakeraw(byte* termios);
#pragma warning restore SA1300, IDE1006
}

sealed class WindowsPlatform : ITerminalPlatform
{
    const int StdInput = -10, StdOutput = -11;
    const uint VtInput = 0x0200, VtProcessing = 0x0004, NoNewlineAutoReturn = 0x0008;
    // key events: mouse events too, and extended flags without quick edit (else a click selects text in conhost)
    const uint MouseInput = 0x0010, ExtendedFlags = 0x0080;
    const ushort KeyEvent = 1, MouseEvent = 2;
    const uint Utf8 = 65001;

    readonly IntPtr _hin = NativeMethods.GetStdHandle(StdInput);
    readonly IntPtr _hout = NativeMethods.GetStdHandle(StdOutput);
    readonly WindowsKeys _keys = new();
    readonly InputRecord[] _records = new InputRecord[4096];
    readonly System.Text.StringBuilder _out = new();
    uint _inMode, _outMode, _inCp, _outCp;
    bool _raw;

    // Records, not text: in VT input mode every record is a character (vk 0), the same text ReadConsoleW gave; with key
    // events on they carry the key. ReadConsoleInputW (like ReadConsoleW, unlike ReadFile and Console.OpenStandardInput)
    // does not end the input at Ctrl+Z.
    public unsafe int ReadInput(char[] buffer)
    {
        if (_out.Length > 0) return Take(buffer);
        while (true)
        {
            // As many records as there are: a paste comes in hundreds, and one read is what WindowsKeys takes for one
            // paste. What does not fit in the caller's buffer waits in _out.
            uint read;
            fixed (InputRecord* p = _records)
                if (!NativeMethods.ReadConsoleInputW(_hin, p, (uint)_records.Length, out read) || read == 0) return 0;
            for (int i = 0; i < read; i++)
            {
                ref var r = ref _records[i];
                if (r.Type == KeyEvent)
                    _keys.Key(new WindowsKeys.KeyRecord(r.Key.Down != 0, r.Key.Repeat, r.Key.Vk, r.Key.Ch, r.Key.State), _out);
                else if (r.Type == MouseEvent)
                    _keys.Mouse(new WindowsKeys.MouseRecord(r.Mouse.X, r.Mouse.Y, r.Mouse.Buttons, r.Mouse.State, r.Mouse.Flags), _out);
            }
            _keys.Flush(_out);
            if (_out.Length == 0) continue;                                   // only key releases, focus events ...
            return Take(buffer);
        }
    }

    // What did not fit in the caller's buffer waits in _out for the next call.
    int Take(char[] buffer)
    {
        int n = Math.Min(_out.Length, buffer.Length);
        _out.CopyTo(0, buffer, 0, n);
        _out.Remove(0, n);
        return n;
    }

    public void SetKeyEvents(bool enabled)
    {
        _keys.Events = enabled;
        if (_raw) NativeMethods.SetConsoleMode(_hin, enabled ? MouseInput | ExtendedFlags : VtInput);
    }

    public bool HasKeyEvents => true;

    public Stream Output { get; } = Console.OpenStandardOutput();

    public (int Width, int Height) Size
    {
        get
        {
            try { return (Console.WindowWidth, Console.WindowHeight); }
            catch (IOException) { return (80, 25); }
        }
    }

    public void EnterRawMode()
    {
        if (_raw) return;
        NativeMethods.GetConsoleMode(_hin, out _inMode);
        NativeMethods.GetConsoleMode(_hout, out _outMode);
        _inCp = NativeMethods.GetConsoleCP(); _outCp = NativeMethods.GetConsoleOutputCP();
        NativeMethods.SetConsoleCP(Utf8); NativeMethods.SetConsoleOutputCP(Utf8);
        NativeMethods.SetConsoleMode(_hin, VtInput);                                     // no line input, no echo, no Ctrl+C processing
        NativeMethods.SetConsoleMode(_hout, _outMode | VtProcessing | NoNewlineAutoReturn);
        _raw = true;
    }

    public void RestoreMode()
    {
        if (!_raw) return;
        NativeMethods.SetConsoleMode(_hin, _inMode);
        NativeMethods.SetConsoleMode(_hout, _outMode);
        NativeMethods.SetConsoleCP(_inCp); NativeMethods.SetConsoleOutputCP(_outCp);
        _raw = false;
    }

    public void Dispose() => RestoreMode();
}

/// <summary>INPUT_RECORD: the event type, then (after two bytes of padding) a key or a mouse event.</summary>
[StructLayout(LayoutKind.Explicit, Size = 20)]
struct InputRecord
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(4)] public KeyEventRecord Key;
    [FieldOffset(4)] public MouseEventRecord Mouse;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct KeyEventRecord
{
    public int Down;
    public ushort Repeat, Vk, Scan;
    public char Ch;
    public uint State;
}

[StructLayout(LayoutKind.Sequential)]
struct MouseEventRecord
{
    public short X, Y;
    public uint Buttons, State, Flags;
}

static partial class NativeMethods
{
    [LibraryImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool ReadConsoleInputW(IntPtr h, InputRecord* buffer, uint count, out uint read);

    [LibraryImport("kernel32.dll")] public static partial IntPtr GetStdHandle(int n);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetConsoleMode(IntPtr h, out uint mode);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetConsoleMode(IntPtr h, uint mode);
    [LibraryImport("kernel32.dll")] public static partial uint GetConsoleCP();
    [LibraryImport("kernel32.dll")] public static partial uint GetConsoleOutputCP();
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetConsoleCP(uint cp);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetConsoleOutputCP(uint cp);
}
