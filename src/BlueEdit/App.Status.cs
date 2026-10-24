using System.Text.Json.Nodes;
using Tui;

sealed partial class App
{
    string Words()
    {
        if (!settings.WordCount) return "";
        int v = editor.EditVersion;
        if (v != wordsVersion)
        {
            wordsVersion = v;
            Task.Delay(500).ContinueWith(_ => app.Post(() => { if (editor.EditVersion == v) wordsShown = editor.Stats().ProseWords; }));
        }
        return wordsShown < 0 ? "" : $"  {wordsShown} {Ui.Tc("status", "w")}";
    }

    string StatusText() =>
        $" {NameOf(editor.Path)}{(editor.Modified ? " *" : "")}   {Ui.Tc("status", "Ln")} {editor.Row + 1}/{editor.Lines.Count}  {Ui.Tc("status", "Col")} {editor.Col + 1}{(signature != null ? "  " + signature : "")}{(note != "" ? "  [" + note + "]" : "")}" +
        $"   {editor.LangName}  {editor.FileFormat}   {(editor.ColumnMode ? Ui.Tc("status", "COLUMN") : Ui.Tc("status", "STREAM"))}{(editor.WrapOn ? "  " + Ui.Tc("status", "WRAP") : "")}{(editor.AutoWrap ? "  " + Ui.Tc("status", "AUTOWRAP") : "")}{Words()}" +
        $"{(editor.IsMarkdown ? "  " + editor.ViewName : "")}{(editor.ReadOnlyFile ? "  " + Ui.Tc("status", "RO") : "")}" +
        $"{(editor.ShowTiming ? $"  [key>frame {editor.LastKeyToDrawMs:F1} ms (handler {editor.LastHandlerMs:F1}), draw {editor.LastDrawMs:F1} ms, last key {editor.LastKey}]" : "")}" +
        $"{(editor.SpellOn ? "  " + Ui.Tc("status", "SPELL") + " " + (spell.Name == "Windows" ? "" : spell.Name + " ") + string.Join("/", spell.Languages.Select(Spell.ShortTag)) : "")}" +
        $"{(editor.GrammarOn && grammar != null ? "  " + Ui.Tc("status", "GRAMMAR") + " " + GrammarState(grammar) : "")}{LspState()}{LatexState()}   ";

    // "LT+LLM", "LT+LLM starting", "LT failed", "LT+LLM, EDGE failed": each checker that did not start is named
    internal static string GrammarState(GrammarCheckers g)
    {
        static string Short(IGrammarChecker c) => c switch { LanguageToolChecker => "LT", BrowserProofreader b => b.Name.ToUpperInvariant(), HarperChecker => "HARPER", ChromeModelChecker => "CHROME-M", _ => "LLM" };
        var ok = string.Join("+", g.All.Where(c => c.Available).Select(Short));
        var failed = string.Join("+", g.All.Where(c => !c.Available).Select(Short));
        return ok + (g.Status == "starting" ? " " + Ui.Tc("status", "starting") : "") + (failed.Length == 0 ? "" : (ok.Length > 0 ? ", " : "") + failed + " " + Ui.Tc("status", "failed"));
    }

    // "  LSP pyright-langserver 2E 1W", "  LSP csharp-ls starting": the current file's server and its findings
    string LspState()
    {
        if (lsp == null || editor.Path == null || lsp.StatusFor(editor.Path, editor.LangName) is not { } st) return "";
        var d = lsp.DiagnosticsFor(editor.Path);
        int errors = d.Count(x => x.Severity == 1), warnings = d.Count(x => x.Severity == 2);
        var shortSt = st.Contains(':', StringComparison.Ordinal) ? st[..st.IndexOf(':', StringComparison.Ordinal)] : st;
        return "  LSP " + shortSt + (errors > 0 ? $" {errors}{Ui.Tc("status", "E")}" : "") + (warnings > 0 ? $" {warnings}{Ui.Tc("status", "W")}" : "");
    }

    // "  TeX 2E 1W": errors in the current file's formulas and the rules of writing they break (LatexCheck.cs)
    string LatexState()
    {
        var d = editor.LatexDiagnostics();
        if (d.Count == 0) return "";
        int errors = d.Count(x => x.Severity == 1), warnings = d.Count - errors;
        return "  TeX" + (errors > 0 ? $" {errors}{Ui.Tc("status", "E")}" : "") + (warnings > 0 ? $" {warnings}{Ui.Tc("status", "W")}" : "");
    }

    void Note(string text) { note = text; noteFresh = true; }

    // F1 opens on the section of the file's kind: Markdown's keys in a note, the language servers' in code
    void ShowHelp() => app.RunModal(new HelpDialog(T("Help"), HelpText.For(Ui.Language, Settings.Directory),
        HelpText.SectionFor(editor.IsMarkdown, LanguageServers.Defaults.ContainsKey(editor.LangName))));

    // "0.1.0+<commit>" as the build stamps it: shown as "0.1.0 (abc1234)"
    internal static string AppVersion()
    {
        var v = typeof(Settings).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "?";
        var plus = v.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? v : $"{v[..plus]} ({v[(plus + 1)..Math.Min(v.Length, plus + 8)]})";
    }

    void About() => Dialogs.ShowText(app, T("About"), new[]
    {
        "",
        "  BlueEdit " + AppVersion(),
        "  A fast console editor for writing, mathematics and code.",
        "",
        "  Writing       Markdown with a live preview and a reading mode",
        "  Mathematics   LaTeX formulas made readable: fractions, roots, matrices",
        "  Checking      spelling, grammar and style as you write",
        "  Code          highlighting, completion, errors from language servers",
        "",
        $"  {System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
        "  F1: all keys and commands.",
        "",
    });

    // File > Info: the file and its counts — lines, paragraphs, words, characters — for the whole file and the selection (#100)
    void ShowInfo()
    {
        static string N(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');
        var lines = new List<string> { "" };
        void Add(string label, string value) => lines.Add($"  {label,-24}{value}");
        Add(T("File"), editor.Path ?? T("(not saved yet)"));
        if (editor.Path != null && File.Exists(editor.Path) && (int)Math.Min(int.MaxValue, new FileInfo(editor.Path).Length) is var bytes)
            Add(T("Size on disk"), Tn(bytes, "{1} byte", "{1} bytes", N(bytes)));
        Add(T("Encoding, line endings"), editor.FileFormat);
        Add(T("Language"), editor.LangName);
        Add(T("Changes not saved"), editor.Modified ? T("yes") : T("no"));
        void Counts(string title, TextStats t)
        {
            lines.Add("");
            lines.Add("  " + title);
            Add("  " + T("Lines"), N(t.Lines));
            Add("  " + T("Paragraphs"), N(t.Paragraphs));
            Add("  " + T("Words"), N(t.Words));
            if (editor.IsMarkdown)
            {
                Add("  " + T("Words of the text"), N(t.ProseWords) + "   " + T("(no markup, formulas or code)"));
                Add("  " + T("Formulas"), N(t.Formulas));
                if (t.CodeLines > 0) Add("  " + T("Lines of code"), N(t.CodeLines));
            }
            Add("  " + T("Characters"), N(t.Chars));
            Add("  " + T("Characters, no spaces"), N(t.CharsNoSpaces));
        }
        Counts(T("Whole file"), editor.Stats());
        if (editor.HasSelection) Counts(T("Selection"), editor.Stats(selection: true));
        lines.Add("");
        Dialogs.ShowText(app, T("Info"), lines);
    }
}
