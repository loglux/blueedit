using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit;

/// <summary>Runs only with BLUEEDIT_MATH_BENCH set (a file path for the report): a benchmark, not a test.</summary>
public sealed class BenchFactAttribute : FactAttribute
{
    public BenchFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BLUEEDIT_MATH_BENCH") is not { Length: > 0 }) Skip = "a benchmark: set BLUEEDIT_MATH_BENCH";
    }
}

/// <summary>
/// Proofing text with formulas (#48), measured with the real checkers through the editor's own code: how each way of
/// handing formulas to a checker does on the sets with formulas in tools/proofbench/cases (errors found and fixed, false alarms), and optionally
/// on folders of notes (BLUEEDIT_MATH_CORPUS, ';'-separated: findings per 1000 words, no ground truth). The ways:
///   plugins     - the formula as text, findings that start inside it hidden (the Obsidian LanguageTool plugins);
///   spaces      - the formula as spaces (BlueEdit before #48);
///   placeholder - a placeholder word, findings touching a formula dropped (BlueEdit now);
///   source      - the formula as it is, findings touching it dropped (an idea for language models).
/// Run: $env:BLUEEDIT_MATH_BENCH='…\report.md'; dotnet test tests\BlueEdit.Tests --filter MathBench
/// Models: BLUEEDIT_MATH_BENCH_MODELS = ';'-separated .gguf names (default: the editor's choice); "none" = LanguageTool only;
/// "server" = only the models of BLUEEDIT_PROOF_BENCH_SERVER (ProofBench.ServerModels), which run with any setting.
/// BLUEEDIT_MATH_BENCH_WAYS = ','-separated ways (default: all four); BLUEEDIT_BENCH_CASES = more folders of sets.
/// The models' answers go to "&lt;report&gt;.answers.jsonl".
/// </summary>
public sealed class MathBench
{
    static readonly (string Name, MathView View, bool PluginFilter)[] Ways =
    {
        ("plugins", MathView.Source, true), ("spaces", MathView.Spaces, false),
        ("placeholder", MathView.Placeholder, false), ("source", MathView.Source, false),
    };

    /// <summary>An error of a case: the wrong text (its nth occurrence, 1-based), its kind, fragments a good fix leaves.</summary>
    internal sealed record CaseError(string Wrong, int Nth, string Kind, string[] Ok);
    // Ext: the file the case is opened as ("md"; "tex" for the LaTeX set, #78)
    internal sealed record Case(string Id, string Lang, string Source, string Text, CaseError[] Errors, string Ext = "md");

    // the sets in tools/proofbench/cases with formulas (or, math: false, those without) — the form of #75, shared with the
    // Obsidian plugin
    internal static List<Case> Cases(bool math = true)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "tools", "proofbench", "cases"))) dir = Path.GetDirectoryName(dir);
        var res = new List<Case>();
        // BLUEEDIT_BENCH_CASES: more folders of sets (the plugin's); a set already read by its name is not read again
        var folders = new[] { Path.Combine(dir!, "tools", "proofbench", "cases") }
            .Concat((Environment.GetEnvironmentVariable("BLUEEDIT_BENCH_CASES") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var names = new HashSet<string>(StringComparer.Ordinal);
        // BLUEEDIT_BENCH_SETS: only these sets (';'-separated names, as "en-us-academic")
        var only = (Environment.GetEnvironmentVariable("BLUEEDIT_BENCH_SETS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var file in folders.SelectMany(f => Directory.GetFiles(f, "*.json").Order(StringComparer.Ordinal)))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var set = doc.RootElement;
            if ((set.TryGetProperty("math", out var m) && m.GetBoolean()) != math) continue;
            if (set.TryGetProperty("kind", out var kind) && kind.GetString() == "style") continue;      // StyleBench's
            string lang = set.GetProperty("language").GetString()!, name = set.GetProperty("name").GetString()!;
            if (only.Length > 0 && !only.Contains(name)) continue;
            if (!names.Add(name)) continue;
            var ext = set.TryGetProperty("file", out var f) ? f.GetString()! : "md";
            foreach (var c in set.GetProperty("cases").EnumerateArray())
                res.Add(new Case(name + "/" + c.GetProperty("id").GetString(), lang,
                    c.TryGetProperty("source", out var src) ? src.GetString()! : "",
                    c.GetProperty("text").GetString()!,
                    c.GetProperty("errors").EnumerateArray().Select(e => new CaseError(e.GetProperty("wrong").GetString()!,
                        e.TryGetProperty("nth", out var nth) ? nth.GetInt32() : 1, e.GetProperty("kind").GetString()!,
                        e.GetProperty("ok").EnumerateArray().Select(o => o.GetString()!).ToArray())).ToArray(), ext));
        }
        return res;
    }

    /// <summary>A finding at <paramref name="offset"/> stands in the sentence of one of the errors: often the rest of the
    /// same fix ("Он поставил оценки и ушёл" → "Она… ушла"), not a false alarm. Counted apart, as the Obsidian plugin's
    /// benchmark does since its #47. A sentence ends at ". ", "! ", "? " or a blank line.</summary>
    internal static bool NextToError(string text, IEnumerable<(int Offset, int Length)> errors, int offset)
    {
        foreach (var (at, length) in errors)
        {
            if (at < 0) continue;
            int start = at, end = Math.Min(text.Length, at + length);
            while (start > 0 && !SentenceEndsBefore(text, start)) start--;
            while (end < text.Length && !SentenceEndsBefore(text, end)) end++;
            if (offset >= start && offset < end) return true;
        }
        return false;
    }

    // a sentence ends just before position i: after ". " (or ! ?) or at a blank line
    static bool SentenceEndsBefore(string text, int i) =>
        i >= 2 && ((text[i - 2] is '.' or '!' or '?' && char.IsWhiteSpace(text[i - 1])) || (text[i - 1] == '\n' && text[i - 2] == '\n'));

    // where the nth occurrence of a case's wrong text is
    internal static int Where(string text, CaseError err)
    {
        int at = -1;
        for (int i = 0; i < err.Nth; i++) at = text.IndexOf(err.Wrong, at + 1, StringComparison.Ordinal);
        return at;
    }

    internal static IReadOnlyList<GrammarIssue> Ask(IGrammarChecker g, string text, string lang)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromMinutes(3))
        {
            // a checker that stopped answers "nothing found" to everything: that must end the run, not count as zero
            if (!g.Available && g.Status != "starting") throw new InvalidOperationException($"{g.Name} stopped: {g.Status}");
            if (g.Issues(text, lang) is { } issues) return issues;
            Thread.Sleep(30);
        }
        throw new TimeoutException($"{g.Name} did not answer");
    }

    // The findings a way keeps for a paragraph, with offsets in the source.
    static List<GrammarIssue> Findings(IGrammarChecker g, EditorView e, int first, int last, (string Name, MathView View, bool PluginFilter) way, string lang)
    {
        var text = e.ParagraphText(first, last, way.View);
        var issues = Ask(g, text, lang);
        var math = e.ParagraphMath(first, last);
        // as the editor does (IssuesOf): a finding on markup alone — spaces to the checker, text in the source — is
        // about the blanking, not the text, and is not shown (a pure insertion, a comma, is kept)
        var plain = e.ParagraphText(first, last);
        var source = e.ParagraphSource(first, last);
        bool OnMarkupOnly(GrammarIssue x) =>
            x.Length > 0 && x.Offset + x.Length <= plain.Length && string.IsNullOrWhiteSpace(plain.Substring(x.Offset, x.Length))
            && !string.IsNullOrWhiteSpace(source.Substring(x.Offset, Math.Min(x.Length, source.Length - x.Offset)));
        return way.Name switch
        {
            "plugins" => issues.Where(x => !math.Any(m => x.Offset >= m.Offset && x.Offset < m.Offset + m.Length)).ToList(),
            "spaces" => issues.ToList(),
            _ => issues.Where(x => !Prose.Touches(math, x.Offset, x.Length) && !OnMarkupOnly(x)).ToList(),
        };
    }

    static List<(int First, int Last)> Paragraphs(EditorView e)
    {
        var res = new List<(int, int)>();
        for (int i = 0; i < e.Lines.Count; i++)
            if (e.ParagraphAt(i) is { } p && !res.Contains(p)) { res.Add(p); i = p.Last; }
        return res;
    }

    static EditorView Open(string dir, string text, string ext = "md")
    {
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + "." + ext);
        File.WriteAllText(path, text);
        var e = new EditorView();
        e.Load(path);
        return e;
    }

    [BenchFact]
    public void Formulas_handed_to_the_checkers_four_ways()
    {
        var report = Environment.GetEnvironmentVariable("BLUEEDIT_MATH_BENCH")!;
        var dir = Path.Combine(Path.GetTempPath(), "be-mathbench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        // made one at a time, each only for its own run: two models at once do not fit in 12 GB of video memory
        var checkers = new List<(string? Name, Func<IGrammarChecker> Make)>();
        var models = Environment.GetEnvironmentVariable("BLUEEDIT_MATH_BENCH_MODELS") is { Length: > 0 } m ? m.Split(';') : new[] { "" };
        // "none": no local model; "server": only the models on a server (ProofBench.ServerModels), no LanguageTool either
        if (models is not ["server"] && LanguageToolChecker.InstalledIn(LanguageToolChecker.DefaultHome))
            checkers.Add((null, () => new LanguageToolChecker(LanguageToolChecker.DefaultHome)));
        if (models is not (["none"] or ["server"]))
            foreach (var model in models)
                if (LlmGrammarChecker.InstalledIn(LlmGrammarChecker.DefaultDir, model.Length == 0 ? null : model))
                    checkers.Add((null, () => new LlmGrammarChecker(LlmGrammarChecker.DefaultDir, model.Length == 0 ? null : model)));
        foreach (var (serverName, make) in ProofBench.ServerModels()) checkers.Add((serverName, make));
        Assert.NotEmpty(checkers);
        // BLUEEDIT_MATH_BENCH_WAYS: ','-separated names of the ways to run (default: all four)
        var ways = Environment.GetEnvironmentVariable("BLUEEDIT_MATH_BENCH_WAYS") is { Length: > 0 } wn
            ? Ways.Where(w => wn.Split(',').Contains(w.Name, StringComparer.Ordinal)).ToArray() : Ways;
        using var answers = new ProofBench.AnswerLog(report);
        var corpus = Environment.GetEnvironmentVariable("BLUEEDIT_MATH_CORPUS");
        var corpusMd = new StringBuilder();
        var md = new StringBuilder();
        var json = new List<object>();
        md.Append(CultureInfo.InvariantCulture, $"# Formulas and proofing — {DateTime.Now:yyyy-MM-dd HH:mm}\n\n");
        try
        {
            var cases = Cases();
            int errorsTotal = cases.Sum(c => c.Errors.Length), clean = cases.Count(c => c.Errors.Length == 0);
            md.Append(CultureInfo.InvariantCulture, $"Cases: {cases.Count} ({errorsTotal} errors to find, {clean} correct texts). ");
            md.Append("*Found*: errors touched by a finding; *fixed*: its first fix leaves one of the case's ok fragments; *false on correct*: findings on the correct texts; *next to an error*: findings in the sentence of an error, not on it (often the rest of the same fix); *off target*: findings elsewhere on the texts with an error.\n\n");
            md.Append("| checker | way | found | fixed | false on correct | next to an error | off target |\n|---|---|---|---|---|---|---|\n");
            foreach (var (serverName, make) in checkers)
            {
                using var g = make();
                string name = serverName ?? (g is LlmGrammarChecker llm ? "model " + LlmGrammarChecker.DisplayName(llm.ModelName) : g.Name);
                foreach (var way in ways)
                {
                    int found = 0, fixedCount = 0, falseClean = 0, nextTo = 0, offTarget = 0;
                    foreach (var c in cases)
                    {
                        var e = Open(dir, c.Text + "\n", c.Ext);
                        string lang = c.Lang;
                        var source = string.Join('\n', e.Lines);
                        var all = Paragraphs(e).SelectMany(p =>
                        {
                            int start = e.Lines.Take(p.First).Sum(l => l.Length + 1);
                            return Findings(g, e, p.First, p.Last, way, lang).Select(x => x with { Offset = x.Offset + start });
                        }).ToList();
                        var targets = c.Errors.Select(err => (Offset: Where(source, err), Length: err.Wrong.Length)).ToList();
                        int hits = targets.Count(t => all.Any(x => Prose.Touches(new[] { t }, x.Offset, x.Length)));
                        // fixed: a finding on the error whose first fix, put in, leaves one of the ok fragments
                        int fixes = c.Errors.Select((err, i) => all.Any(x => Prose.Touches(new[] { targets[i] }, x.Offset, x.Length)
                            && x.Replacements.Count > 0 && err.Ok.Any(ok => source.Remove(x.Offset, x.Length).Insert(x.Offset, x.Replacements[0]).Contains(ok, StringComparison.Ordinal)))).Count(f => f);
                        fixedCount += fixes;
                        var strays = all.Where(x => !Prose.Touches(targets, x.Offset, x.Length)).ToList();
                        found += hits;
                        if (c.Errors.Length == 0) falseClean += strays.Count;
                        else
                        {
                            int near = strays.Count(x => NextToError(source, targets, x.Offset));
                            nextTo += near;
                            offTarget += strays.Count - near;
                        }
                        json.Add(new { checker = name, way = way.Name, c.Id, hits, fixes, errors = c.Errors.Length,
                                       findings = all.Select(x => new { text = source.Substring(x.Offset, Math.Min(x.Length, source.Length - x.Offset)), x.Rule, x.Message }) });
                    }
                    md.Append(CultureInfo.InvariantCulture, $"| {name} | {way.Name} | {found} of {errorsTotal} | {fixedCount} | {falseClean} | {nextTo} | {offTarget} |\n");
                }
                if (corpus is { Length: > 0 }) Corpus(corpus, g, dir, corpusMd, json);
            }
            if (corpus is { Length: > 0 }) md.Append(CorpusHeader(corpus)).Append(corpusMd);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
        File.WriteAllText(report, md.ToString());
        File.WriteAllText(Path.ChangeExtension(report, ".json"), JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    // Notes from disk (not in the repository): every paragraph with a formula; findings per 1000 words and how many
    // sit within three characters of a formula (the ones a formula is likely to cause).
    static List<string> CorpusFiles(string folders) =>
        folders.Split(';').SelectMany(f => Directory.EnumerateFiles(f, "*.md", SearchOption.AllDirectories))
            .Where(f => !f.Contains("Trash", StringComparison.Ordinal) && !f.Contains("_original", StringComparison.Ordinal)
                        && !f.Contains("pandoc", StringComparison.Ordinal) && !f.Contains("Untitled", StringComparison.Ordinal)).ToList();

    static string CorpusHeader(string folders) =>
        string.Create(CultureInfo.InvariantCulture, $"\n## Notes: {CorpusFiles(folders).Count} files from {folders}\n\n")
        + "| checker | way | paragraphs | words | findings | per 1000 words | next to a formula |\n|---|---|---|---|---|---|---|\n";

    // one checker's rows of the notes table
    static void Corpus(string folders, IGrammarChecker g, string dir, StringBuilder md, List<object> json)
    {
        var files = CorpusFiles(folders);
        {
            string name = g is LlmGrammarChecker llm ? "model " + LlmGrammarChecker.DisplayName(llm.ModelName) : g.Name;
            foreach (var way in Ways)
            {
                int paras = 0, words = 0, findings = 0, near = 0;
                var seen = new List<object>();
                foreach (var f in files)
                {
                    var e = Open(dir, File.ReadAllText(f));
                    foreach (var p in Paragraphs(e))
                    {
                        var math = e.ParagraphMath(p.First, p.Last);
                        if (math.Count == 0) continue;
                        var plain = e.ParagraphText(p.First, p.Last);
                        string lang = ParagraphLanguage.Choose(plain, new[] { "en-GB", "ru-RU" });
                        paras++;
                        words += plain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetter));
                        var kept = Findings(g, e, p.First, p.Last, way, lang);
                        findings += kept.Count;
                        var src = e.ParagraphSource(p.First, p.Last);
                        foreach (var x in kept)
                        {
                            int a = Math.Max(0, x.Offset - 40), b = Math.Min(src.Length, x.Offset + x.Length + 40);
                            seen.Add(new { file = Path.GetFileName(f), x.Rule, flagged = src.Substring(x.Offset, Math.Min(x.Length, src.Length - x.Offset)),
                                           around = src[a..b].Replace('\n', ' ') });
                        }
                        near += kept.Count(x => math.Any(m => x.Offset < m.Offset + m.Length + 3 && m.Offset - 3 < x.Offset + Math.Max(1, x.Length)));
                    }
                }
                md.Append(CultureInfo.InvariantCulture, $"| {name} | {way.Name} | {paras} | {words} | {findings} | {1000.0 * findings / Math.Max(1, words):0.0} | {near} |\n");
                if (g is LlmGrammarChecker { LastFailure: { } failure }) md.Append(CultureInfo.InvariantCulture, $"|  | (a paragraph failed: {failure.Replace('|', '/')}) |  |  |  |  |  |\n");
                json.Add(new { corpus = folders, checker = name, way = way.Name, paras, words, findings, near, seen });
            }
        }
    }
}
