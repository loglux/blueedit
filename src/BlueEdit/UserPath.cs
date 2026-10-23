using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>The user's PATH on Windows (HKCU\Environment\Path): the editor's own folder put in or taken out, so
/// "be" starts in any new console. No administrator rights: the user's half of PATH is the user's own. The value
/// is read and written as it is stored — %VARIABLES% unexpanded and the value's kind kept — since
/// Environment.SetEnvironmentVariable(…, User) would write it back as plain text with the variables expanded.</summary>
static partial class UserPath
{
    const string Key = "Environment", Name = "Path";

    /// <summary>The folders of the user's PATH as written (%USERPROFILE%\bin stays so).</summary>
    public static List<string> Folders()
    {
        if (!OperatingSystem.IsWindows()) return [];
        using var env = Registry.CurrentUser.OpenSubKey(Key);
        var value = env?.GetValue(Name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>Whether <paramref name="folder"/> is in the user's PATH, written either way (a trailing backslash,
    /// %VARIABLES%, letter case).</summary>
    public static bool Contains(string folder) => Folders().Any(f => Same(f, folder));

    /// <summary>Puts the folder at the end of the user's PATH, or takes every entry of it out.</summary>
    public static void Set(string folder, bool present)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var env = Registry.CurrentUser.CreateSubKey(Key);
        var kind = env.GetValueNames().Contains(Name, StringComparer.OrdinalIgnoreCase) ? env.GetValueKind(Name) : RegistryValueKind.ExpandString;
        var value = env.GetValue(Name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        env.SetValue(Name, Edit(value, folder, present), kind == RegistryValueKind.String ? RegistryValueKind.String : RegistryValueKind.ExpandString);
        Announce();
    }

    /// <summary>A PATH value with <paramref name="folder"/> at its end, or without it: every other entry as it was,
    /// empty ones dropped.</summary>
    internal static string Edit(string value, string folder, bool present)
    {
        var folders = value.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(f => !Same(f, folder)).ToList();
        if (present) folders.Add(folder);
        return string.Join(';', folders);
    }

    internal static bool Same(string entry, string folder)
    {
        static string Norm(string p) => Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')).TrimEnd('\\', '/');
        return string.Equals(Norm(entry), Norm(folder), StringComparison.OrdinalIgnoreCase);
    }

    // Explorer reads the environment again on WM_SETTINGCHANGE "Environment": the consoles it starts from now on
    // (a new Windows Terminal window included) get the new PATH; the ones open keep theirs
    static void Announce() =>
        SendMessageTimeoutW(new IntPtr(0xffff), 0x001A, IntPtr.Zero, "Environment", 0x0002, 1000, out _);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
