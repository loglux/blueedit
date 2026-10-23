using System.Text.RegularExpressions;

/// <summary>One line that matched in a folder search (1-based line and column).</summary>
sealed record FolderHit(string Path, int Line, int Col, int Length, string Text);

/// <summary>
/// Searches the text files under a folder, like Far's Alt+F7 or grep -rn. Skips build and tool folders (.git, bin,
/// obj, node_modules ...), binary files (a zero byte near the start without a UTF-16 BOM) and files over 20 MB.
/// Each file is decoded the way the editor would open it, so cp1251 and cp866 files are searched correctly.
/// </summary>
static class FolderSearch
{
    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".svn", ".hg", ".vs", ".idea", ".vscode", "bin", "obj", "node_modules", "__pycache__", ".venv", "venv", ".pytest_cache" };

    const long MaxFileSize = 20 * 1024 * 1024;

    public sealed record Result(List<FolderHit> Hits, int FilesSearched, bool Truncated, string? Error);

    /// <summary>File masks separated by ';', ',' or spaces; empty means every file.</summary>
    public static string[] Masks(string text)
    {
        var m = text.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return m.Length == 0 ? new[] { "*" } : m;
    }

    public static Result Run(string root, string masks, SearchOptions o, int maxHits = 5000)
    {
        var (rx, error) = o.Build();
        if (rx == null) return new(new(), 0, false, error);
        if (!Directory.Exists(root)) return new(new(), 0, false, $"no such folder: {root}");
        var hits = new List<FolderHit>();
        var maskRx = Masks(masks).Select(m => new Regex("^" + Regex.Escape(m).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        int files = 0;
        var dirs = new Stack<string>();
        dirs.Push(root);
        while (dirs.Count > 0)
        {
            var dir = dirs.Pop();
            string[] entries, subdirs;
            try { entries = Directory.GetFiles(dir); subdirs = Directory.GetDirectories(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            Array.Sort(subdirs, StringComparer.OrdinalIgnoreCase);
            foreach (var f in entries)
            {
                if (!maskRx.Any(m => m.IsMatch(Path.GetFileName(f)))) continue;
                if (SearchFile(f, rx, hits, maxHits)) files++;
                if (hits.Count >= maxHits) return new(hits, files, true, null);
            }
            for (int i = subdirs.Length - 1; i >= 0; i--)                // pushed in reverse: visited in name order
                if (!SkipDirs.Contains(Path.GetFileName(subdirs[i]))) dirs.Push(subdirs[i]);
        }
        return new(hits, files, false, null);
    }

    // Returns false for files that were not searched (unreadable, too big, binary).
    static bool SearchFile(string path, Regex rx, List<FolderHit> hits, int maxHits)
    {
        byte[] bytes;
        try
        {
            if (new FileInfo(path).Length > MaxFileSize) return false;
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        bool utf16 = bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF);
        if (!utf16 && Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0) return false;
        var (text, _) = FileEncodings.Detect(bytes);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        try
        {
            for (int i = 0; i < lines.Length && hits.Count < maxHits; i++)
                foreach (Match m in rx.Matches(lines[i]))
                {
                    if (m.Length == 0) continue;
                    hits.Add(new FolderHit(path, i + 1, m.Index + 1, m.Length, lines[i]));
                    break;                                               // one entry per line
                }
        }
        catch (RegexMatchTimeoutException) { }
        return true;
    }

    /// <summary>"sub\file.md:12:  text" for the results list, relative to the searched folder and trimmed to fit.</summary>
    public static string Format(FolderHit h, string root, int width)
    {
        var rel = Path.GetRelativePath(root, h.Path);
        var head = $"{rel}:{h.Line}: ";
        var text = h.Text.Trim().Replace('\t', ' ');
        var s = head + text;
        return s.Length <= width ? s : s[..Math.Max(0, width - 1)] + "…";
    }
}
