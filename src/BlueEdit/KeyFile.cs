using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// A key in a file of its own, kept as the system's own means keep such things, nothing installed: on Windows
/// encrypted with DPAPI (CryptProtectData: readable only by this user on this computer, as Windows keeps saved
/// passwords); elsewhere plain text readable only by this user (mode 600), as command-line tools keep tokens where no
/// keyring runs. A file someone wrote by hand (plain text) is read as it is. docs/tech/proofing-internals.md
/// </summary>
static partial class KeyFile
{
    public static void Write(string path, string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        if (OperatingSystem.IsWindows()) bytes = Protect(bytes);
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
        File.Delete(path);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var f = new FileStream(path, options);
        f.Write(bytes);
    }

    /// <summary>The key in the file; "" when it is missing or cannot be read.</summary>
    public static string Read(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return ""; }
        if (OperatingSystem.IsWindows() && Unprotect(bytes) is { } plain) bytes = plain;
        return Encoding.UTF8.GetString(bytes);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public int Size; public IntPtr Data; }

    const int UiForbidden = 1;   // CRYPTPROTECT_UI_FORBIDDEN: never a dialog

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);

    static byte[] Protect(byte[] data) =>
        Call(data, (ref Blob i, out Blob o) => CryptProtectData(ref i, "BlueEdit", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out o))
        ?? throw new IOException($"the key could not be encrypted (Windows error {Marshal.GetLastPInvokeError()})");

    // null: not something DPAPI wrote for this user (a key typed into the file by hand)
    static byte[]? Unprotect(byte[] data) =>
        Call(data, (ref Blob i, out Blob o) => CryptUnprotectData(ref i, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out o));

    delegate bool Crypt(ref Blob input, out Blob output);

    static byte[]? Call(byte[] data, Crypt crypt)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
            if (!crypt(ref input, out var output)) return null;
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally { handle.Free(); }
    }
}
