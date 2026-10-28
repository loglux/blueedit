using System.Diagnostics;
using System.Runtime.CompilerServices;
using Xunit;

/// <summary>
/// No control characters in the repository's text files. A scripted edit (a heredoc, a Python one-liner) turned "\b"
/// into a backspace and "\n" into a line break inside string literals again and again; a rule
/// written down did not stop it, so this test does: it runs with every check, locally and in CI.
/// </summary>
public class RepoTextTests
{
    static string Root([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    static readonly HashSet<string> Binary = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".zip", ".gz", ".exe", ".dll", ".ttf", ".otf", ".woff", ".woff2", ".pdf", ".gguf",
    };

    [Fact]
    public void Text_files_have_no_control_characters_but_tab_and_line_breaks()
    {
        var psi = new ProcessStartInfo("git", "ls-files -z") { WorkingDirectory = Root(), RedirectStandardOutput = true, UseShellExecute = false };
        using var git = Process.Start(psi)!;
        var files = git.StandardOutput.ReadToEnd().Split('\0', StringSplitOptions.RemoveEmptyEntries);
        git.WaitForExit();
        Assert.NotEmpty(files);
        var found = new List<string>();
        foreach (var rel in files)
        {
            if (Binary.Contains(Path.GetExtension(rel))) continue;
            var path = Path.Combine(Root(), rel);
            if (!File.Exists(path)) continue;                               // deleted in the working tree
            var bytes = File.ReadAllBytes(path);
            if (Array.IndexOf(bytes, (byte)0) >= 0) continue;               // a binary file of another kind
            int line = 1;
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b == '\n') line++;
                else if ((b < 0x20 && b != '\t' && b != '\r') || b == 0x7F)
                {
                    found.Add($"{rel}:{line}: 0x{b:X2}");
                    break;
                }
            }
        }
        Assert.True(found.Count == 0, "control characters (a broken escape in a scripted edit?):\n" + string.Join("\n", found));
    }
}
