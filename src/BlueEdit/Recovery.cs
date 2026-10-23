using System.Diagnostics;
using System.Text;
using System.Text.Json;

/// <summary>
/// AutoRecover, as in Word: copies of the documents with unsaved edits are written now and then into a folder of
/// their own, never over the files themselves; a normal exit removes them, and after an editor ended any other way
/// the next start offers them back. One file per running editor, named by its process id. Decisions in
/// docs/tech/editing.md, "Recovery".
/// </summary>
sealed class Recovery
{
    /// <summary>One document's unsaved text. <see cref="Path"/> is null for a document never saved.</summary>
    public sealed record Item(string? Path, string Text, string Encoding, bool CrLf);

    /// <summary>The copies one editor left: its file, when it last wrote them, the documents.</summary>
    public sealed record Session(string File, DateTime Written, IReadOnlyList<Item> Items);

    // Pid and Started together name the editor that wrote it: the system reuses process ids, and in the UI tests (dozens
    // of editors started in parallel) a killed editor's id went to another BlueEdit within seconds.
    sealed record Stored(int Pid, DateTime Started, DateTime Written, List<Item> Items);

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>%LOCALAPPDATA%\BlueEdit\recovery (~/.local/share/BlueEdit/recovery on Linux); BLUEEDIT_RECOVERY_DIR
    /// moves it (the UI tests give each test its own).</summary>
    public static string DefaultDir =>
        Environment.GetEnvironmentVariable("BLUEEDIT_RECOVERY_DIR") is { Length: > 0 } dir ? dir
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit", "recovery");

    readonly string _file;
    readonly int _pid;
    readonly DateTime _started;
    readonly object _gate = new();
    Task _writing = Task.CompletedTask;
    string? _last;                        // what the file holds now, to skip writing the same again

    /// <summary>The copies of the editor running as process <paramref name="pid"/>, started at
    /// <paramref name="started"/> (UTC). Each start gets a file of its own, so an editor given the id of one that
    /// ended never overwrites that one's copies.</summary>
    public Recovery(string dir, int pid, DateTime started)
    {
        Dir = dir;
        _pid = pid; _started = started;
        _file = System.IO.Path.Combine(dir, $"session-{pid}-{Guid.NewGuid():N}.json");
    }

    /// <summary>For this process.</summary>
    public static Recovery ForThisProcess(string dir)
    {
        using var me = Process.GetCurrentProcess();
        return new Recovery(dir, me.Id, me.StartTime.ToUniversalTime());
    }

    public string Dir { get; }

    /// <summary>Writes the copies in the background; while an earlier write is still going, this one is skipped
    /// (the next call brings it up to date). No items: the file is removed.</summary>
    public void Store(IReadOnlyList<Item> items)
    {
        if (!_writing.IsCompleted) return;
        _writing = Task.Run(() => Write(items));
    }

    /// <summary>Writes the copies now, on this thread: the editor is ending on an error.</summary>
    public void StoreNow(IReadOnlyList<Item> items)
    {
        try { _writing.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        Write(items);
    }

    /// <summary>A normal exit: nothing to recover.</summary>
    public void Clear() => StoreNow(Array.Empty<Item>());

    // Failing to write a copy must never disturb editing: the reason is dropped, the next write tries again.
    void Write(IReadOnlyList<Item> items)
    {
        lock (_gate)
        {
            try
            {
                if (items.Count == 0)
                {
                    if (_last != null || File.Exists(_file)) File.Delete(_file);
                    _last = null;
                    return;
                }
                var text = JsonSerializer.Serialize(items, Json);
                if (text == _last) return;
                Directory.CreateDirectory(Dir);
                var stored = JsonSerializer.Serialize(new Stored(_pid, _started, DateTime.UtcNow, items.ToList()), Json);
                SafeFile.Write(_file, Encoding.UTF8.GetBytes(stored));
                _last = text;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Copies left by editors that are no longer running, oldest first. A copy of a file saved after the copy
    /// was written, or one that says what the file already says, is left out: there is nothing in it to recover.</summary>
    public static List<Session> Left(string dir)
    {
        var found = new List<Session>();
        if (!Directory.Exists(dir)) return found;
        foreach (var file in Directory.GetFiles(dir, "session-*.json"))
        {
            Stored? s;
            try { s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(file), Json); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { continue; }
            if (s?.Items == null || Running(s.Pid, s.Started)) continue;
            var items = s.Items.Where(i => Worth(i, s.Written)).ToList();
            found.Add(new Session(file, s.Written, items));
        }
        return found.OrderBy(s => s.Written).ToList();
    }

    static bool Worth(Item i, DateTime written)
    {
        if (i.Path == null || !File.Exists(i.Path)) return true;
        try
        {
            if (File.GetLastWriteTimeUtc(i.Path) > written) return false;
            var (disk, _) = FileEncodings.Detect(File.ReadAllBytes(i.Path));
            return disk.Replace("\r\n", "\n") != i.Text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    // A process id is reused by the system: the copies' owner is the process with that id, started at that time, and
    // a BlueEdit (or this process). GetProcessById finds running processes only. HasExited is not asked: it needs a
    // handle that another user's process (or the system's, id 0) refuses with "Access is denied". A start time that
    // cannot be read counts as a match: better to keep a running editor's copies than to offer them.
    static bool Running(int pid, DateTime started)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (pid != Environment.ProcessId && !p.ProcessName.Equals("be", StringComparison.OrdinalIgnoreCase)) return false;
            try { return Math.Abs((p.StartTime.ToUniversalTime() - started).TotalSeconds) < 1; }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception) { return true; }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>Removes a session's copies (recovered or declined).</summary>
    public static void Remove(Session s)
    {
        try { File.Delete(s.File); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
