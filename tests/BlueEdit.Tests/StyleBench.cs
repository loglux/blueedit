using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit;

/// <summary>Runs only with BLUEEDIT_STYLE_BENCH set (a file path for the report): a benchmark, not a test.</summary>
public sealed class StyleBenchFactAttribute : FactAttribute
{
    public StyleBenchFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BLUEEDIT_STYLE_BENCH") is not { Length: > 0 }) Skip = "a benchmark: set BLUEEDIT_STYLE_BENCH";
    }
}

/// <summary>
/// Style suggestions (Ctrl+F7, #87) measured through the editor's own code (<see cref="StyleAdvisor.Suggest"/>) on the
/// sets in tools/proofbench/cases with "kind": "style", a table per set: per model, the problems a change touches (per
/// kind), the paragraphs where taking every change loses a fact, term, formula or British spelling the case keeps, the
/// changes on paragraphs that read well, failed answers, the median time. One model at a time.
/// Run: $env:BLUEEDIT_STYLE_BENCH='…\report.md'; dotnet test tests\BlueEdit.Tests --filter StyleBench
///   BLUEEDIT_STYLE_BENCH_MODELS = ';'-separated .gguf names (default: the editor's choice);
///   BLUEEDIT_STYLE_BENCH_FOCUS = ','-separated style focuses (default: the settings' default).
/// </summary>
public sealed class StyleBench
{
    sealed record Problem(string Fragment, string Kind);
    sealed record StyleCase(string Set, string Id, string Lang, string Text, Problem[] Problems, string[] Keep);

    static List<StyleCase> Cases()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "tools", "proofbench", "cases"))) dir = Path.GetDirectoryName(dir);
        var res = new List<StyleCase>();
        foreach (var file in Directory.GetFiles(Path.Combine(dir!, "tools", "proofbench", "cases"), "*.json").Order(StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var set = doc.RootElement;
            if (!set.TryGetProperty("kind", out var kind) || kind.GetString() != "style") continue;
            string lang = set.GetProperty("language").GetString()!, name = set.GetProperty("name").GetString()!;
            foreach (var c in set.GetProperty("cases").EnumerateArray())
                res.Add(new StyleCase(name, c.GetProperty("id").GetString()!, lang, c.GetProperty("text").GetString()!,
                    c.GetProperty("problems").EnumerateArray().Select(p => new Problem(p.GetProperty("fragment").GetString()!, p.GetProperty("kind").GetString()!)).ToArray(),
                    c.GetProperty("keep").EnumerateArray().Select(k => k.GetString()!).ToArray()));
        }
        return res;
    }

    [StyleBenchFact]
    public void Style_suggestions_on_academic_paragraphs()
    {
        var report = Environment.GetEnvironmentVariable("BLUEEDIT_STYLE_BENCH")!;
        var models = Environment.GetEnvironmentVariable("BLUEEDIT_STYLE_BENCH_MODELS") is { Length: > 0 } m ? m.Split(';') : new[] { "" };
        var focus = Environment.GetEnvironmentVariable("BLUEEDIT_STYLE_BENCH_FOCUS") is { Length: > 0 } f
            ? f.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : StyleAdvisor.DefaultFocuses.ToArray();
        var sets = Cases().GroupBy(c => c.Set).ToList();
        Assert.NotEmpty(sets);
        var rows = sets.ToDictionary(s => s.Key, _ => new StringBuilder());
        var json = new List<object>();
        foreach (var model in models)
        {
            using var server = LlmGrammarChecker.ServerFor(LlmGrammarChecker.DefaultDir, model.Length == 0 ? null : model);
            if (server == null) continue;
            var name = LlmGrammarChecker.DisplayName(server.ModelName);
            foreach (var set in sets)
            {
                var problems = set.SelectMany(c => c.Problems).ToList();
                var kinds = problems.Select(p => p.Kind).Distinct().Order(StringComparer.Ordinal).ToList();
                int touched = 0, lost = 0, cleanChanged = 0, cleanChanges = 0, failed = 0;
                var perKind = kinds.ToDictionary(k => k, _ => 0);
                var times = new List<double>();
                foreach (var c in set)
                {
                    var sw = Stopwatch.StartNew();
                    List<StyleChange> changes;
                    string? error = null;
                    try { changes = StyleAdvisor.Suggest(server, c.Text, c.Lang, focus); }
                    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
                    {
                        changes = new();
                        error = ex.Message;
                        failed++;
                    }
                    times.Add(sw.Elapsed.TotalSeconds);
                    var after = StyleAdvisor.Apply(c.Text.TrimEnd(), changes);
                    var missing = c.Keep.Where(k => !after.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (missing.Count > 0) lost++;
                    var hit = c.Problems.Where(p =>
                    {
                        int at = c.Text.IndexOf(p.Fragment, StringComparison.Ordinal);
                        return at >= 0 && changes.Any(x => x.Offset < at + p.Fragment.Length && at < x.Offset + Math.Max(1, x.Length));
                    }).ToList();
                    touched += hit.Count;
                    foreach (var p in hit) perKind[p.Kind]++;
                    if (c.Problems.Length == 0 && changes.Count > 0) { cleanChanged++; cleanChanges += changes.Count; }
                    json.Add(new
                    {
                        model = name, set = set.Key, c.Id, seconds = Math.Round(sw.Elapsed.TotalSeconds, 2), error, after, missing,
                        touched = hit.Select(p => p.Fragment),
                        changes = changes.Select(x => new { from = c.Text.Substring(x.Offset, x.Length), to = x.Replacement, x.Reason }),
                    });
                }
                times.Sort();
                int clean = set.Count(c => c.Problems.Length == 0);
                var kindCells = string.Join(" | ", kinds.Select(k => $"{perKind[k]}/{problems.Count(p => p.Kind == k)}"));
                rows[set.Key].Append(CultureInfo.InvariantCulture,
                    $"| {name} | {touched}/{problems.Count} | {kindCells} | {lost} | {cleanChanged}/{clean} ({cleanChanges}) | {failed} | {times[times.Count / 2]:0.0} |\n");
            }
        }
        var md = new StringBuilder();
        md.Append(CultureInfo.InvariantCulture, $"# Style suggestions — {DateTime.Now:yyyy-MM-dd HH:mm}\n\nFocus: {string.Join(", ", focus)}.\n");
        md.Append("*touched*: problems a change overlaps; *lost*: paragraphs where taking every change loses a kept fact, term, formula or spelling; *clean changed*: paragraphs that read well but got changes (changes in all); *failed*: no usable answer.\n");
        foreach (var set in sets)
        {
            var problems = set.SelectMany(c => c.Problems).ToList();
            var kinds = problems.Select(p => p.Kind).Distinct().Order(StringComparer.Ordinal).ToList();
            int clean = set.Count(c => c.Problems.Length == 0);
            md.Append(CultureInfo.InvariantCulture, $"\n## {set.Key} ({set.First().Lang}): {set.Count() - clean} paragraphs with {problems.Count} problems, {clean} that read well\n\n");
            md.Append("| model | touched | " + string.Join(" | ", kinds) + " | lost | clean changed | failed | median s |\n");
            md.Append("|---|---|" + string.Concat(kinds.Select(_ => "---|")) + "---|---|---|---|\n");
            md.Append(rows[set.Key]);
        }
        File.WriteAllText(report, md.ToString());
        File.WriteAllText(Path.ChangeExtension(report, ".json"), JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
