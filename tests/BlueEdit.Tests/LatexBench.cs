using Xunit;

/// <summary>
/// The LaTeX check (#192) over a folder of notes: how many formulas, how many errors of each kind, and each error with
/// its formula, to read through for false alarms. Runs only with BLUEEDIT_LATEX_BENCH set to the folder; the report
/// goes to BLUEEDIT_LATEX_BENCH_OUT (a file outside the repository: the notes' text stays out of it).
/// </summary>
public sealed class LatexBench
{
    [Fact]
    public void Errors_in_a_folder_of_notes()
    {
        var dir = Environment.GetEnvironmentVariable("BLUEEDIT_LATEX_BENCH");
        var output = Environment.GetEnvironmentVariable("BLUEEDIT_LATEX_BENCH_OUT");
        if (dir == null || output == null) return;
        int files = 0, formulas = 0;
        var kinds = new Dictionary<string, int>();
        var lines = new List<string>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
        {
            var e = new EditorView();
            e.Load(path);
            files++;
            formulas += e.Lines.Sum(l => Prose.MathRx.Matches(l).Count);
            foreach (var d in e.LatexDiagnostics())
            {
                var kind = System.Text.RegularExpressions.Regex.Replace(d.Message, @"\{[^}]*\}", "{…}");
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                lines.Add($"{path}:{d.Line + 1}:{d.Col + 1}: {d.Message}\n    {e.Lines[d.Line]}");
            }
        }
        File.WriteAllLines(output, new[] { $"{files} files, about {formulas} formulas, {lines.Count} errors" }
            .Concat(kinds.OrderByDescending(k => k.Value).Select(k => $"  {k.Value,5}  {k.Key}")).Append("").Concat(lines));
    }
}
