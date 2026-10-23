using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit;

/// <summary>Runs only with BLUEEDIT_PROOF_BENCH set (a file path for the report): a benchmark, not a test.</summary>
public sealed class ProofBenchFactAttribute : FactAttribute
{
    public ProofBenchFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BLUEEDIT_PROOF_BENCH") is not { Length: > 0 }) Skip = "a benchmark: set BLUEEDIT_PROOF_BENCH";
    }
}

/// <summary>
/// The checkers side by side on prose without formulas: the sets in tools/proofbench/cases with "math": false (British
/// academic English first), each checker through the editor's own code — the spelling providers as the editor
/// underlines words, the grammar checkers as it asks them. Per kind of error: found (a finding touches it) and fixed (its
/// first replacement leaves one of the case's ok fragments); false alarms on the correct texts; findings off target on
/// the others; the median time of a case.
/// Run: $env:BLUEEDIT_PROOF_BENCH='…\report.md'; dotnet test tests\BlueEdit.Tests --filter ProofBench
///   BLUEEDIT_PROOF_BENCH_CHECKERS = ','-separated of windows, office, hunspell, languagetool, llm, edge, harper, server, chrome, chromemodel
///   (default: every one installed); BLUEEDIT_PROOF_BENCH_MODELS = ';'-separated .gguf names for llm (default: the
///   editor's choice); BLUEEDIT_PROOF_BENCH_SERVER and _KEY for server (<see cref="ServerModels"/>);
///   BLUEEDIT_BENCH_CASES = ';'-separated folders of more sets in the same form (the Obsidian plugin's
///   scripts/bench/cases); BLUEEDIT_BENCH_SETS = ';'-separated set names to run only those. The models' answers go
///   to "&lt;report&gt;.answers.jsonl" (<see cref="AnswerLog"/>).
/// </summary>
public sealed class ProofBench
{
    sealed record Bencher(string Name, Func<string, string, IReadOnlyList<GrammarIssue>> Check, IDisposable Owner);

    static readonly string[] Kinds = { "grammar", "punctuation", "spelling" };

    // the words the editor would underline, with the provider's suggestions
    static IReadOnlyList<GrammarIssue> Misspelled(ISpellProvider sp, string text)
    {
        var res = new List<GrammarIssue>();
        foreach (System.Text.RegularExpressions.Match m in EditorView.WordRx.Matches(text))
        {
            var sw = Stopwatch.StartNew();
            while (sp.IsCorrect(m.Value) == null && sw.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(10);   // a background provider
            if (!EditorView.IsCorrectWord(sp, m.Value)) res.Add(new GrammarIssue(m.Index, m.Length, "spelling", sp.Suggest(m.Value), "SPELLING", sp.Name));
        }
        return res;
    }

    // made one at a time, each only for its own run: six models at once do not fit in video memory
    static List<Func<Bencher?>> Checkers(string lang)
    {
        var want = (Environment.GetEnvironmentVariable("BLUEEDIT_PROOF_BENCH_CHECKERS") is { Length: > 0 } w ? w : "windows,office,hunspell,languagetool,llm,edge,harper")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var res = new List<Func<Bencher?>>();
        static Bencher? Spelling(ISpellProvider sp)
        {
            if (sp.Available) return new Bencher("spelling: " + sp.Name, (text, _) => Misspelled(sp, text), sp);
            sp.Dispose();
            return null;
        }
        static Bencher Grammar(IGrammarChecker g, string? name = null) => new(name ?? g.Name, (text, l) => MathBench.Ask(g, text, l), g);
        if (want.Contains("windows") && OperatingSystem.IsWindows()) res.Add(() => Spelling(new Spell(lang)));
        if (want.Contains("office") && OperatingSystem.IsWindows() && OfficeSpell.Installed) res.Add(() => Spelling(new OfficeSpell(null, lang)));
        if (want.Contains("hunspell"))
            res.Add(() =>
            {
                var h = new HunspellSpell(null, new[] { lang });
                var sw = Stopwatch.StartNew();
                while (!h.Loaded && sw.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(10);
                return Spelling(h);
            });
        if (want.Contains("languagetool") && LanguageToolChecker.InstalledIn(LanguageToolChecker.DefaultHome))
            res.Add(() => Grammar(new LanguageToolChecker(LanguageToolChecker.DefaultHome)));
        if (want.Contains("llm"))
        {
            var models = Environment.GetEnvironmentVariable("BLUEEDIT_PROOF_BENCH_MODELS") is { Length: > 0 } m ? m.Split(';') : new[] { "" };
            foreach (var model in models)
                if (LlmGrammarChecker.InstalledIn(LlmGrammarChecker.DefaultDir, model.Length == 0 ? null : model))
                    res.Add(() =>
                    {
                        var g = new LlmGrammarChecker(LlmGrammarChecker.DefaultDir, model.Length == 0 ? null : model);
                        return Grammar(g, "model " + LlmGrammarChecker.DisplayName(g.ModelName));
                    });
        }
        if (want.Contains("server"))
            foreach (var (name, make) in ServerModels())
                res.Add(() => Grammar(make(), name));
        if (want.Contains("edge") && BrowserProofreader.EdgeIn(null) != null) res.Add(() => Grammar(new BrowserProofreader(null)));
        if (want.Contains("chrome") && BrowserProofreader.ChromeIn(null) != null) res.Add(() => Grammar(BrowserProofreader.Chrome(null)));
        if (want.Contains("chromemodel") && ChromeModelChecker.PythonIn(ChromeModelChecker.DefaultHome) != null) res.Add(() => Grammar(new ChromeModelChecker()));
        if (want.Contains("harper") && HarperChecker.ServerIn(HarperChecker.DefaultHome) != null) res.Add(() => Grammar(new HarperChecker(HarperChecker.DefaultHome)));
        return res;
    }

    /// <summary>Models on a server at an address (#95): BLUEEDIT_PROOF_BENCH_SERVER = "url|model;model...", the key in
    /// BLUEEDIT_PROOF_BENCH_KEY ("file:~/…" as in the settings). MathBench takes them too.</summary>
    internal static List<(string Name, Func<LlmGrammarChecker> Make)> ServerModels()
    {
        var res = new List<(string, Func<LlmGrammarChecker>)>();
        if (Environment.GetEnvironmentVariable("BLUEEDIT_PROOF_BENCH_SERVER") is { Length: > 0 } at && at.Split('|') is [var url, var names])
            foreach (var model in names.Split(';', StringSplitOptions.RemoveEmptyEntries))
                res.Add(($"{model} at {new LlmEndpoint(url, model, null).Host}",
                    () => new LlmGrammarChecker(LlmServer.At(new LlmEndpoint(url, model, LlmEndpoint.KeyFrom(Environment.GetEnvironmentVariable("BLUEEDIT_PROOF_BENCH_KEY") ?? ""))), "")));
        return res;
    }

    /// <summary>
    /// Keeps every answer of a language model, with its paragraph, in "&lt;report&gt;.answers.jsonl": how answers become
    /// findings (TextDiff, IssuesFrom) can then be measured again on them without asking the models again.
    /// </summary>
    internal sealed class AnswerLog : IDisposable
    {
        readonly StreamWriter _out;
        public AnswerLog(string report)
        {
            _out = new StreamWriter(Path.ChangeExtension(report, ".answers.jsonl"), append: true) { AutoFlush = true };
            LlmGrammarChecker.AnswerSeen = (g, text, answer) =>
            {
                lock (_out) _out.WriteLine(JsonSerializer.Serialize(new { model = g.ModelName, text, answer }, Relaxed));
            };
        }
        public void Dispose() { LlmGrammarChecker.AnswerSeen = null; _out.Dispose(); }
    }

    internal static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [ProofBenchFact]
    public void Checkers_side_by_side_on_prose()
    {
        var report = Environment.GetEnvironmentVariable("BLUEEDIT_PROOF_BENCH")!;
        using var answers = new AnswerLog(report);
        var cases = MathBench.Cases(math: false);
        Assert.NotEmpty(cases);
        var md = new StringBuilder();
        var json = new List<object>();
        md.Append(CultureInfo.InvariantCulture, $"# Checkers side by side — {DateTime.Now:yyyy-MM-dd HH:mm}\n\n");
        foreach (var set in cases.GroupBy(c => (Set: c.Id[..c.Id.IndexOf('/', StringComparison.Ordinal)], c.Lang)))
        {
            var list = set.ToList();
            var errors = list.SelectMany(c => c.Errors).ToList();
            int clean = list.Count(c => c.Errors.Length == 0);
            md.Append(CultureInfo.InvariantCulture, $"## {set.Key.Set} ({set.Key.Lang}): {list.Count} texts, {errors.Count} errors, {clean} correct\n\n");
            md.Append("*found / fixed* per kind of error; *false*: findings on the correct texts; *next*: findings in the sentence of an error, not on it (often the rest of the same fix: \"Он поставил… ушёл\" → \"Она… ушла\"); *off*: findings elsewhere on the texts with an error.\n\n");
            md.Append("| checker | " + string.Join(" | ", Kinds.Select(k => $"{k} ({errors.Count(e => e.Kind == k)})")) + " | false | next | off | median ms |\n");
            md.Append("|---|" + string.Concat(Kinds.Select(_ => "---|")) + "---|---|---|---|\n");
            var checkers = Checkers(set.Key.Lang);
            Assert.NotEmpty(checkers);
            foreach (var make in checkers)
            {
                if (make() is not { } b) continue;
                try
                {
                    var found = Kinds.ToDictionary(k => k, _ => 0);
                    var fixedCount = Kinds.ToDictionary(k => k, _ => 0);
                    int falseAlarms = 0, nextTo = 0, offTarget = 0;
                    var times = new List<double>();
                    foreach (var c in list)
                    {
                        var sw = Stopwatch.StartNew();
                        var all = b.Check(c.Text, c.Lang);
                        times.Add(sw.Elapsed.TotalMilliseconds);
                        var targets = c.Errors.Select(err => (Offset: MathBench.Where(c.Text, err), Length: err.Wrong.Length)).ToList();
                        for (int i = 0; i < c.Errors.Length; i++)
                        {
                            var err = c.Errors[i];
                            var on = all.Where(x => Prose.Touches(new[] { targets[i] }, x.Offset, x.Length)).ToList();
                            if (on.Count == 0) continue;
                            found[err.Kind]++;
                            if (on.Any(x => x.Replacements.Count > 0 && err.Ok.Any(ok =>
                                    c.Text.Remove(x.Offset, x.Length).Insert(x.Offset, x.Replacements[0]).Contains(ok, StringComparison.Ordinal))))
                                fixedCount[err.Kind]++;
                        }
                        var strays = all.Where(x => !Prose.Touches(targets, x.Offset, x.Length)).ToList();
                        if (c.Errors.Length == 0) falseAlarms += strays.Count;
                        else
                        {
                            int near = strays.Count(x => MathBench.NextToError(c.Text, targets, x.Offset));
                            nextTo += near;
                            offTarget += strays.Count - near;
                        }
                        json.Add(new
                        {
                            checker = b.Name, c.Id, ms = Math.Round(sw.Elapsed.TotalMilliseconds),
                            findings = all.Select(x => new { text = c.Text.Substring(x.Offset, Math.Min(x.Length, c.Text.Length - x.Offset)), fix = x.Replacements.FirstOrDefault(), x.Rule, x.Message }),
                        });
                    }
                    times.Sort();
                    var perKind = string.Join(" | ", Kinds.Select(k => $"{found[k]} / {fixedCount[k]}"));
                    md.Append(CultureInfo.InvariantCulture, $"| {b.Name} | {perKind} | {falseAlarms} | {nextTo} | {offTarget} | {times[times.Count / 2]:0} |\n");
                }
                finally { b.Owner.Dispose(); }
            }
            md.Append('\n');
        }
        File.WriteAllText(report, md.ToString());
        File.WriteAllText(Path.ChangeExtension(report, ".json"), JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
