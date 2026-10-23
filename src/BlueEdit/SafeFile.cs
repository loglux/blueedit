/// <summary>
/// Saving a file so that a failure part-way (power cut, full disk, the process killed) leaves either the old file or
/// the new one, never a cut-off mix: the bytes go to a temporary file beside it, flushed to the disk, which then takes
/// the old file's place in one step. Decisions in docs/tech/editing.md, "Saving".
/// </summary>
static class SafeFile
{
    public static void Write(string path, byte[] bytes)
    {
        var target = Target(path);
        // A rename needs only the folder to be writable, so it would replace a read-only file (Linux) without a word.
        // Opening the old file for writing first fails as writing it in place did: no permission, read-only, or held
        // by a program that shares it with nobody. It changes nothing in the file.
        if (File.Exists(target))
            using (new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
        var dir = Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".";
        var tmp = Path.Combine(dir, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder takes no new files though the file itself may be writable: write in place, as before.
            TryDelete(tmp);
            File.WriteAllBytes(target, bytes);
            return;
        }
        try
        {
            if (File.Exists(target))
            {
                // Windows' ReplaceFile keeps the old file's attributes, permissions and creation time; on Linux a
                // rename does not, so the mode (rwx bits) is copied first.
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, File.GetUnixFileMode(target));
                File.Replace(tmp, target, null, ignoreMetadataErrors: true);
            }
            else File.Move(tmp, target);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    // A symbolic link stays a link: the file it points to is the one replaced.
    static string Target(string path)
    {
        try { return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path; }
        catch (IOException) { return path; }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
