using Xunit;

public class EdgeProofreaderTests
{
    [Fact]
    public void Edge_is_one_of_the_grammar_checkers_in_the_settings()
    {
        var (s, error) = Settings.Parse("""{ "grammar": ["llm", "edge"], "edgePath": "C:\\Edge\\msedge.exe" }""");
        Assert.Null(error);
        Assert.Equal(new[] { "llm", "edge" }, s.Grammar);
        Assert.Equal(@"C:\Edge\msedge.exe", s.EdgePath);
    }

    [Theory]
    [InlineData("The results were good.", "The results were good", "The results were good.")]     // Chrome dropped it
    [InlineData("The results were good.", "The results was good", "The results was good.")]
    [InlineData("Good?", "Good", "Good")]                                                          // it was not a full stop
    [InlineData("He go home.", "He goes home.", "He goes home.")]
    [InlineData("Two lines.\n", "Two lines\n", "Two lines.\n")]
    public void Chrome_keeps_the_final_full_stop_it_drops(string text, string corrected, string kept) =>
        Assert.Equal(kept, BrowserProofreader.KeepFinalStop(text, corrected));

    [Fact]
    public void Chrome_is_one_of_the_grammar_checkers_and_a_missing_chrome_is_reported()
    {
        var (s, error) = Settings.Parse("""{ "grammar": "chrome" }""");
        Assert.Null(error);
        Assert.Equal(new[] { "chrome" }, s.Grammar);
        using var chrome = BrowserProofreader.Chrome(@"C:\no\such\chrome.exe");
        Assert.Equal("Chrome", chrome.Name);
        Assert.False(chrome.Available);
        Assert.Contains("Chrome not found", chrome.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hidden_browser_gets_pipes_not_the_editors_terminal()
    {
        // Chrome's log lines ("ERROR:google_apis\gcm\...") were drawn over the editor when it inherited the terminal
        var psi = BrowserProofreader.StartInfo(@"C:\Chrome\chrome.exe", @"C:\profile", @"C:\profile\blueedit.html");
        Assert.True(psi.RedirectStandardError && psi.RedirectStandardOutput && psi.RedirectStandardInput);
        Assert.False(psi.UseShellExecute);
        Assert.Contains("--headless=new", psi.ArgumentList);
    }

    [Fact]
    public void A_missing_edge_is_reported_and_checks_nothing()
    {
        using var edge = new BrowserProofreader(@"C:\no\such\msedge.exe");
        Assert.False(edge.Available);
        Assert.Contains("not found", edge.Status, StringComparison.Ordinal);
        Assert.Empty(edge.Issues("He go to school.", "en-GB")!);
    }

    // Stable Edge 154 has Proofreader but never answers availability(): the checker must say so, not stay "starting".
    // Slow (30 s and more), so only with BLUEEDIT_EDGE_STABLE_TEST=1.
    [Fact]
    public void Stable_edge_that_does_not_answer_is_reported()
    {
        var stable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
        if (Environment.GetEnvironmentVariable("BLUEEDIT_EDGE_STABLE_TEST") != "1" || !File.Exists(stable)) return;
        var profile = Path.Combine(Path.GetTempPath(), "be-edge-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var edge = new BrowserProofreader(stable, profile))
            {
                var deadline = DateTime.UtcNow.AddSeconds(90);
                while (DateTime.UtcNow < deadline && edge.Status is "starting" or "downloading its model") Thread.Sleep(500);
                Assert.True(edge.Available || (edge.Status ?? "").Contains("Edge Canary", StringComparison.Ordinal), "status: " + edge.Status);
            }
        }
        finally { try { Directory.Delete(profile, true); } catch (IOException) { } }
    }

    // Real Edge: needs Edge Canary and a profile where its proofreader model is already downloaded (BLUEEDIT_EDGE_PROFILE),
    // so the test does not download 1.2 GB; skipped otherwise.
    [Fact]
    public void Real_edge_corrects_english_and_leaves_russian_alone()
    {
        var profile = Environment.GetEnvironmentVariable("BLUEEDIT_EDGE_PROFILE");
        if (string.IsNullOrEmpty(profile) || BrowserProofreader.EdgeIn(null) == null) return;
        using var edge = new BrowserProofreader(null, profile);
        Assert.Empty(edge.Issues("Я пошли домой.", "ru-RU")!);
        var deadline = DateTime.UtcNow.AddSeconds(120);
        IReadOnlyList<GrammarIssue>? found = null;
        while (DateTime.UtcNow < deadline && (found = edge.Issues("He go to school every day.", "en-GB")) == null && edge.Available)
            Thread.Sleep(250);
        Assert.True(found != null, "no answer: " + edge.Status);
        Assert.True(found.Count == 1, "status: " + edge.Status + "; found " + found.Count);
        var issue = found[0];
        Assert.Equal((3, 2), (issue.Offset, issue.Length));
        Assert.Equal("goes", issue.Replacements[0]);
        Assert.Equal("Edge", issue.Source);
    }
}
