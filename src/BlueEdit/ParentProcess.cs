using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// Which programs started the editor: its parent, grandparent... (names without .exe, lower case). Used to behave
/// like Far's own editor when Far opened the file (F4): Esc then closes. A shell or console host in between (cmd,
/// conhost) is looked through, a few levels up.
/// </summary>
static partial class ParentProcess
{
    [StructLayout(LayoutKind.Sequential)]
    struct BasicInformation
    {
        public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(IntPtr process, int infoClass, ref BasicInformation info, int size, out int returned);

    static int ParentOf(Process p)
    {
        if (!OperatingSystem.IsWindows()) return ProcParentOf(p.Id);
        var info = new BasicInformation();
        return NtQueryInformationProcess(p.Handle, 0, ref info, Marshal.SizeOf<BasicInformation>(), out _) == 0
            ? info.InheritedFromUniqueProcessId.ToInt32() : 0;
    }

    /// <summary>Linux: /proc/&lt;pid&gt;/stat is "pid (name) state ppid ..."; the name may hold spaces and brackets, so the
    /// fields are counted after the last ')'. No /proc (macOS): 0.</summary>
    static int ProcParentOf(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return fields.Length > 1 && int.TryParse(fields[1], System.Globalization.CultureInfo.InvariantCulture, out int ppid) ? ppid : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Names of up to <paramref name="levels"/> ancestors, nearest first; empty if the system will not say.</summary>
    public static List<string> Ancestors(int levels = 4)
    {
        var names = new List<string>();
        try
        {
            using var self = Process.GetCurrentProcess();
            int pid = ParentOf(self);
            for (int i = 0; i < levels && pid > 0; i++)
            {
                using var p = Process.GetProcessById(pid);
                names.Add(p.ProcessName.ToLowerInvariant());
                pid = ParentOf(p);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return names;
    }

    static readonly string[] PassThrough = { "cmd", "conhost", "openconsole", "powershell", "pwsh" };

    /// <summary>Far Manager started the editor, directly or through a shell it ran the command in.</summary>
    public static bool StartedByFar(IReadOnlyList<string> ancestors)
    {
        foreach (var name in ancestors)
        {
            if (name == "far") return true;
            if (!PassThrough.Contains(name)) return false;     // something else (a terminal, Explorer, git...) started it
        }
        return false;
    }
}
