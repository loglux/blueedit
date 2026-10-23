using System.Diagnostics;
using System.Text;

/// <summary>
/// Formatting by a separate program (ruff, black…) for a language whose server has no formatter (pyright): the text
/// goes to the program's standard input, the formatted text comes back on its output. The program is run in the
/// file's folder and told the file's path, so it finds the project's own settings (pyproject.toml, ruff.toml).
/// Setting "formatters" adds or replaces one, "off" turns a language's off; a formatter named there is used before
/// the language server's.
/// </summary>
static class Formatters
{
    /// <summary>Tried in order when the settings name none; the first installed one formats. "{file}": the file's path.</summary>
    public static readonly IReadOnlyDictionary<string, ServerCommand[]> Defaults = new Dictionary<string, ServerCommand[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Python"] = [new("ruff", ["format", "--stdin-filename", "{file}", "-"]), new("black", ["-q", "--stdin-filename", "{file}", "-"])],
    };

    /// <summary>What to install for a language that has no formatter, as a hint for the status line; null when none is known.</summary>
    public static string? InstallHint(string? language) => language?.ToLowerInvariant() switch
    {
        "python" => "no formatter: Tools > Code tools installs ruff",
        _ => null,
    };

    /// <summary>The formatter for a language: the one in the settings (null there: off), otherwise the first default
    /// that is installed; null when there is none. <paramref name="configured"/> says whether it came from the settings.</summary>
    public static (ServerCommand Command, bool Configured)? For(string? language, IReadOnlyDictionary<string, ServerCommand?> settings)
    {
        if (language == null) return null;
        if (settings.TryGetValue(language, out var mine)) return mine is { } m && LspClient.Resolve(m.Command) != null ? (m, true) : null;
        return Defaults.TryGetValue(language, out var list) && list.FirstOrDefault(c => LspClient.Resolve(c.Command) != null) is { } c
            ? (c, false) : null;
    }

    /// <summary>The program's name for messages: "ruff", "black".</summary>
    public static string Name(ServerCommand c) => Path.GetFileNameWithoutExtension(c.Command);

    /// <summary>Runs the formatter on <paramref name="text"/> (lines joined by "\n") in the background: the formatted
    /// text, or the program's complaint (its first line of errors: a syntax error, say) when it failed.</summary>
    public static async Task<(string? Text, string? Error)> Run(ServerCommand command, string path, string text, TimeSpan timeout)
    {
        var args = command.Args.Select(a => a.Replace("{file}", path, StringComparison.Ordinal)).ToList();
        ProcessStartInfo psi;
        try { psi = LspClient.StartInfo(command.Command, args); }
        catch (FileNotFoundException ex) { return (null, ex.Message); }
        psi.WorkingDirectory = Path.GetDirectoryName(path) ?? "";
        psi.UseShellExecute = false; psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        psi.StandardInputEncoding = new UTF8Encoding(false);
        psi.StandardOutputEncoding = psi.StandardErrorEncoding = Encoding.UTF8;
        psi.Environment["PYTHONIOENCODING"] = "utf-8";                  // black on Windows writes the console's code page otherwise
        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (System.ComponentModel.Win32Exception ex) { return (null, Name(command) + ": " + ex.Message); }
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var output = p.StandardOutput.ReadToEndAsync(cts.Token);
            var errors = p.StandardError.ReadToEndAsync(cts.Token);
            await p.StandardInput.WriteAsync(text.AsMemory(), cts.Token).ConfigureAwait(false);
            p.StandardInput.Close();
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var outText = (await output.ConfigureAwait(false)).Replace("\r\n", "\n", StringComparison.Ordinal);
            if (p.ExitCode == 0) return (outText, null);
            var err = (await errors.ConfigureAwait(false)).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            return (null, Name(command) + ": " + (err ?? "exit code " + p.ExitCode));
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            return (null, $"{Name(command)}: no answer in {timeout.TotalSeconds:0} s");
        }
        catch (IOException ex) { return (null, Name(command) + ": " + ex.Message); }
    }

    /// <summary>The change from <paramref name="before"/> to <paramref name="after"/> as one edit: the part between
    /// what they share at the start and at the end (so the caret outside it stays put); empty when they are equal.</summary>
    internal static IReadOnlyList<LspEdit> Diff(string before, string after)
    {
        if (before == after) return [];
        int s = 0;
        while (s < before.Length && s < after.Length && before[s] == after[s]) s++;
        int e = 0;
        while (e < before.Length - s && e < after.Length - s && before[^(e + 1)] == after[^(e + 1)]) e++;
        static (int Line, int Col) Pos(string t, int offset)
        {
            int line = 0, start = 0;
            for (int i = 0; i < offset; i++) if (t[i] == '\n') { line++; start = i + 1; }
            return (line, offset - start);
        }
        var (l0, c0) = Pos(before, s);
        var (l1, c1) = Pos(before, before.Length - e);
        return [new LspEdit(l0, c0, l1, c1, after[s..^e])];
    }
}
