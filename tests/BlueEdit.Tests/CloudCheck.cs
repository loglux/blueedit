using Xunit;
using Xunit.Abstractions;

public sealed class CloudCheckFactAttribute : FactAttribute
{
    public CloudCheckFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BLUEEDIT_CLOUD_CHECK") is not { Length: > 0 }) Skip = "asks cloud services: set BLUEEDIT_CLOUD_CHECK";
    }
}

/// <summary>
/// The cloud services, asked for real through the editor's own code (LlmServer.Cloud.cs): one English and one Russian
/// paragraph with an error each, to every service in BLUEEDIT_CLOUD_CHECK, "address|model|key;…" (the key as the setting
/// takes it: file:~/keys/anthropic.txt). Not a benchmark (tests/BlueEdit.Tests/ProofBench.cs is): whether each
/// answers, and finds the error. A few requests, a fraction of a cent. docs/tech/proofing-internals.md
/// </summary>
public sealed class CloudCheck(ITestOutputHelper output)
{
    [CloudCheckFact]
    public void Each_service_answers_and_finds_the_error()
    {
        var failed = new List<string>();
        foreach (var entry in Environment.GetEnvironmentVariable("BLUEEDIT_CLOUD_CHECK")!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('|');
            var endpoint = new LlmEndpoint(parts[0], parts[1], LlmEndpoint.KeyFrom(parts.Length > 2 ? parts[2] : ""));
            using var g = new LlmGrammarChecker(LlmServer.At(endpoint), "");
            foreach (var (text, lang, wrong) in new[]
            {
                ("She go to the library every Tuesday after her lectures.", "en-GB", "go"),
                ("Вчера мы ходили в кино, и фильм нам очень понравилась.", "ru-RU", "понравилась"),
            })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string said;
                try
                {
                    var issues = MathBench.Ask(g, text, lang);
                    var hit = issues.Any(i => text.Substring(i.Offset, i.Length).Contains(wrong, StringComparison.Ordinal));
                    said = $"{issues.Count} finding(s), the error {(hit ? "found" : "MISSED")}" + (g.LastFailure is { } f ? $"; failure: {f}" : "")
                        + ": " + string.Join("; ", issues.Select(i => $"{text.Substring(i.Offset, i.Length)} -> {string.Join("/", i.Replacements)}"));
                    if (!hit) failed.Add($"{parts[1]} {lang}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
                {
                    said = "FAILED: " + ex.Message;
                    failed.Add($"{parts[1]} {lang}");
                }
                output.WriteLine($"{parts[1]} at {endpoint.Host}, {lang}, {sw.Elapsed.TotalSeconds:0.0} s: {said}");
            }
        }
        Assert.Empty(failed);
    }
}
