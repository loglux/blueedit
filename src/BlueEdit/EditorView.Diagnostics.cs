using Tui;

// Errors and warnings from a language server (Lsp/LanguageServers.cs): underlined in the text, walked through with
// Shift+F7, explained by F7. The editor only asks for the current file's list and counts its edits; talking to the
// server is the program's business.
partial class EditorView
{
    /// <summary>Goes up with every edit of the text (the language server is told about the text when it changed).</summary>
    public int EditVersion { get; private set; }

    /// <summary>The diagnostics a language server published for a file (by its path); null when there is no server.</summary>
    public Func<string, IReadOnlyList<LspDiagnostic>>? Diagnostics { get; set; }

    Style _errorStyle = Themes.Get("dos-blue").ErrorStyle, _warningStyle = Themes.Get("dos-blue").WarningStyle;

    IReadOnlyList<LspDiagnostic> CurrentDiagnostics()
    {
        var server = Path != null && Diagnostics != null ? Diagnostics(Path) : Array.Empty<LspDiagnostic>();
        var latex = LatexDiagnostics();
        return latex.Count == 0 ? server : server.Count == 0 ? latex : server.Concat(latex).ToList();
    }

    // Per line: 0 nothing, 1 error, 2 warning — the worse wins where they overlap. Information and hints are not drawn.
    byte[]? DiagnosticMaskFor(int li, IReadOnlyList<LspDiagnostic> all)
    {
        byte[]? mask = null;
        int len = Lines[li].Length;
        foreach (var d in all)
        {
            if (d.Severity > 2 || li < d.Line || li > d.EndLine) continue;
            int s = li == d.Line ? d.Col : 0;
            int e = li == d.EndLine ? d.EndCol : len;
            if (e <= s) { e = s + 1; if (s >= len && len > 0) { s = len - 1; e = len; } }   // an empty range marks one character
            s = Math.Clamp(s, 0, len); e = Math.Clamp(e, 0, len);
            if (e <= s) continue;
            mask ??= new byte[len];
            for (int i = s; i < e; i++)
                if (mask[i] == 0 || d.Severity < mask[i]) mask[i] = (byte)d.Severity;
        }
        return mask;
    }

    /// <summary>The diagnostics covering the caret (or just before it), worst first.</summary>
    public IReadOnlyList<LspDiagnostic> DiagnosticsAtCaret()
    {
        int r = Row, c = Col;
        return CurrentDiagnostics()
            .Where(d => (r > d.Line || r == d.Line && c >= d.Col) && (r < d.EndLine || r == d.EndLine && c <= Math.Max(d.EndCol, d.Col + 1)))
            .OrderBy(d => d.Severity).ToList();
    }

    /// <summary>The diagnostics starting on a line, for Shift+F7.</summary>
    IEnumerable<LspDiagnostic> DiagnosticsStartingOn(int li) => CurrentDiagnostics().Where(d => d.Line == li && d.Severity <= 2);

    internal static string FirstLine(string message) => message.Split('\n')[0].TrimEnd('\r');

    internal static string SeverityName(int severity) => Ui.T(severity switch { 1 => "Error", 2 => "Warning", 3 => "Info", _ => "Hint" });
}
