using System.Runtime.InteropServices;

/// <summary>Plain-text access to the system clipboard.</summary>
interface IClipboardService
{
    string? GetText();
    void SetText(string text);
}

/// <summary>The Windows clipboard (CF_UNICODETEXT) through user32.</summary>
sealed partial class WindowsClipboard : IClipboardService
{
    const uint UnicodeText = 13, Moveable = 0x0002;

    public string? GetText()
    {
        if (!IsClipboardFormatAvailable(UnicodeText) || !Open()) return null;
        try
        {
            var handle = GetClipboardData(UnicodeText);
            if (handle == IntPtr.Zero) return null;
            var p = GlobalLock(handle);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    public void SetText(string text)
    {
        if (!Open()) return;
        try
        {
            EmptyClipboard();
            var handle = GlobalAlloc(Moveable, (nuint)((text.Length + 1) * 2));
            if (handle == IntPtr.Zero) return;
            var p = GlobalLock(handle);
            Marshal.Copy(text.ToCharArray(), 0, p, text.Length);
            Marshal.WriteInt16(p, text.Length * 2, 0);
            GlobalUnlock(handle);
            if (SetClipboardData(UnicodeText, handle) == IntPtr.Zero) GlobalFree(handle);   // on success the clipboard owns the memory
        }
        finally { CloseClipboard(); }
    }

    // another program may hold the clipboard for a moment
    static bool Open()
    {
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool OpenClipboard(IntPtr owner);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseClipboard();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EmptyClipboard();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsClipboardFormatAvailable(uint format);
    [LibraryImport("user32.dll")] private static partial IntPtr GetClipboardData(uint format);
    [LibraryImport("user32.dll")] private static partial IntPtr SetClipboardData(uint format, IntPtr handle);
    [LibraryImport("kernel32.dll")] private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport("kernel32.dll")] private static partial IntPtr GlobalLock(IntPtr handle);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GlobalUnlock(IntPtr handle);
    [LibraryImport("kernel32.dll")] private static partial IntPtr GlobalFree(IntPtr handle);
}

/// <summary>
/// The clipboard on Linux and macOS through the desktop's own tools: pbcopy/pbpaste (macOS), wl-copy/wl-paste
/// (Wayland), xclip or xsel (X11), the first that works. With none (a console, SSH) a copy goes to the terminal as
/// OSC 52, which most terminals put on their own clipboard; paste then uses the editor's own copy.
/// </summary>
sealed class UnixClipboard : IClipboardService
{
    readonly Action<string>? _toTerminal;
    readonly List<(string Copy, string Paste)> _tools = new();

    /// <param name="toTerminal">Writes a control sequence to the terminal (for OSC 52).</param>
    /// <param name="desktopTools">False: OSC 52 only (tests).</param>
    public UnixClipboard(Action<string>? toTerminal, bool desktopTools = true)
    {
        _toTerminal = toTerminal;
        if (!desktopTools) return;
        if (OperatingSystem.IsMacOS()) Add("pbcopy", "pbpaste", "pbcopy", "pbpaste");
        if (WaylandSocket(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")) is { } socket
            && File.Exists(socket))
            Add("wl-copy", "wl-paste", "wl-copy", "wl-paste --no-newline");
        if (Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 })
        {
            Add("xclip", "xclip", "xclip -selection clipboard", "xclip -selection clipboard -o");
            Add("xsel", "xsel", "xsel --clipboard --input", "xsel --clipboard --output");
        }
    }

    /// <summary>The Wayland socket the variables point at: WAYLAND_DISPLAY itself when it is a path, else inside
    /// XDG_RUNTIME_DIR. wl-copy answers "done" before it connects (it goes on in the background), so a missing socket
    /// has to be seen here: WSL sets WAYLAND_DISPLAY=wayland-0 while the socket is elsewhere, and copies were lost.</summary>
    internal static string? WaylandSocket(string? display, string? runtimeDir) =>
        display is not { Length: > 0 } ? null
        : Path.IsPathRooted(display) ? display
        : runtimeDir is { Length: > 0 } ? Path.Combine(runtimeDir, display) : null;

    void Add(string copyTool, string pasteTool, string copy, string paste)
    {
        if (LspClient.Resolve(copyTool) != null && LspClient.Resolve(pasteTool) != null) _tools.Add((copy, paste));
    }

    /// <summary>The tools found, for the status of the clipboard ("wl-copy", "xclip", or OSC 52).</summary>
    public string Kind => _tools.Count > 0 ? _tools[0].Copy.Split(' ')[0] : "OSC 52";

    public string? GetText()
    {
        foreach (var (_, paste) in _tools)
            if (Run(paste, null) is { } text) return text;
        return null;
    }

    public void SetText(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var (copy, _) in _tools)
            if (Run(copy, text) != null) return;
        // ESC ] 52 ; c ; base64 BEL - the terminal sets its clipboard (xterm, kitty, WezTerm, Windows Terminal, tmux...)
        _toTerminal?.Invoke("\u001b]52;c;" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)) + "\u0007");
    }

    /// <summary>Runs a tool through sh: the text (if any) on its stdin; its output, or null when it failed or took
    /// over a second. wl-copy and xclip stay in the background to serve the clipboard, so a copy's output goes to
    /// /dev/null (a pipe they keep open would never end), and errors never reach the editor's screen.</summary>
    static string? Run(string command, string? input)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardInput = true };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(input == null ? "exec " + command + " 2>/dev/null" : "exec " + command + " >/dev/null 2>&1");
        psi.RedirectStandardOutput = input == null;
        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return null;
            p.StandardInput.Write(input ?? "");
            p.StandardInput.Close();
            var output = input == null ? p.StandardOutput.ReadToEndAsync() : Task.FromResult("");
            if (!p.WaitForExit(1000) || !output.Wait(1000)) { try { p.Kill(true); } catch (InvalidOperationException) { } return null; }
            return p.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { return null; }
    }
}
