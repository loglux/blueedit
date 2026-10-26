using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Images in Markdown shown as pictures (FormulaImages draws them): a line that is only an image, "![alt](path)" or
/// Obsidian's "![[name.png]]" (with "|300" for its width in pixels), and a file of a picture type found for it. The
/// path is the document's folder's; Obsidian's name is looked for where Obsidian puts attachments (its
/// .obsidian/app.json, "attachmentFolderPath"), then beside the note and at the vault's root — no vault is searched,
/// so a lookup never waits on a large folder. Images on the web are not fetched: they stay text.
/// </summary>
static class MarkdownImages
{
    static readonly Regex Plain = new(@"^\s*!\[[^\]]*\]\(\s*(<[^>]+>|[^)\s]+)(?:\s+[""'(][^)]*)?\s*\)\s*$", RegexOptions.Compiled);
    static readonly Regex Wiki = new(@"^\s*!\[\[([^\]|#]+)(?:#[^\]|]*)?(?:\|(\d+)(?:x\d+)?)?(?:\|[^\]]*)?\]\]\s*$", RegexOptions.Compiled);
    static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    /// <summary>The image a line is (and nothing else), its width in pixels when Obsidian's "|300" gives one; null for
    /// any other line, an image on the web, or a file that is not a picture.</summary>
    public static (string Target, bool Wiki, int? Width)? Parse(string line)
    {
        if (!line.Contains("![", StringComparison.Ordinal)) return null;
        if (Wiki.Match(line) is { Success: true } w)
        {
            var name = w.Groups[1].Value.Trim();
            int? width = w.Groups[2].Success && int.TryParse(w.Groups[2].Value, out var px) && px > 0 ? px : null;
            return Kinds.Contains(System.IO.Path.GetExtension(name)) ? (name, true, width) : null;
        }
        if (Plain.Match(line) is not { Success: true } m) return null;
        var target = m.Groups[1].Value.Trim('<', '>').Trim();
        if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        try { target = Uri.UnescapeDataString(target); } catch (UriFormatException) { }
        return Kinds.Contains(System.IO.Path.GetExtension(target)) ? (target, false, null) : null;
    }

    // What a target was found to be, looked at again after a while (a file saved later, or changed)
    static readonly Dictionary<(string Dir, string Target, bool Wiki), (string? Path, long Stamp, long At)> Found = new();
    const long Recheck = 2000;

    /// <summary>The file an image of a document at <paramref name="docPath"/> shows, and its time (a changed file is a
    /// new picture); null when there is none.</summary>
    public static (string Path, long Stamp)? Resolve(string? docPath, string target, bool wiki)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(docPath ?? "x")) ?? "";
        var key = (dir, target, wiki);
        long now = Environment.TickCount64;
        if (Found.TryGetValue(key, out var hit) && now - hit.At < Recheck) return hit.Path is { } p ? (p, hit.Stamp) : null;
        if (Found.Count > 2000) Found.Clear();
        var file = (wiki ? Candidates(dir, target) : [Combine(dir, target)]).FirstOrDefault(File.Exists);
        long stamp = file == null ? 0 : File.GetLastWriteTimeUtc(file).Ticks;
        Found[key] = (file, stamp, now);
        return file is { } f ? (f, stamp) : null;
    }

    static string Combine(string dir, string target)
    {
        try { return System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, target)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return target; }
    }

    // Obsidian's places for "![[name]]", in its order: the attachment folder its settings name, beside the note, the
    // vault's root; a name with a folder in it is from the vault's root
    static IEnumerable<string> Candidates(string dir, string name)
    {
        var vault = VaultOf(dir);
        if (vault != null && name.Contains('/', StringComparison.Ordinal)) yield return Combine(vault, name);
        if (vault != null && AttachmentFolder(vault) is { } folder)
            yield return folder == "./" ? Combine(dir, name)
                : folder.StartsWith("./", StringComparison.Ordinal) ? Combine(Combine(dir, folder[2..]), name)
                : folder == "/" ? Combine(vault, name)
                : Combine(Combine(vault, folder), name);
        yield return Combine(dir, name);
        if (vault != null) yield return Combine(vault, name);
    }

    // The folder above with an .obsidian folder in it: the vault (at most 12 levels up)
    static string? VaultOf(string dir)
    {
        var d = dir;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(d); i++, d = System.IO.Path.GetDirectoryName(d))
            if (Directory.Exists(System.IO.Path.Combine(d, ".obsidian"))) return d;
        return null;
    }

    static readonly Dictionary<string, (string? Folder, long At)> Attachments = new();

    static string? AttachmentFolder(string vault)
    {
        long now = Environment.TickCount64;
        if (Attachments.TryGetValue(vault, out var hit) && now - hit.At < Recheck) return hit.Folder;
        string? folder = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(vault, ".obsidian", "app.json")));
            if (doc.RootElement.TryGetProperty("attachmentFolderPath", out var v) && v.ValueKind == JsonValueKind.String) folder = v.GetString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        Attachments[vault] = (folder, now);
        return folder;
    }
}
