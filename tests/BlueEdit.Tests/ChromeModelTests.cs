using System.Diagnostics;
using Xunit;

/// <summary>Chrome's proofreader model without Chrome (#104).</summary>
public class ChromeModelTests
{
    [Fact]
    public void Without_LiteRT_LM_it_says_how_to_install_it_and_checks_nothing()
    {
        var home = Path.Combine(Path.GetTempPath(), "be-nolitert-" + Guid.NewGuid().ToString("N"));
        using var c = new ChromeModelChecker(home);
        Assert.False(c.Available);
        Assert.Contains("install.ps1 -ChromeModel", c.Status, StringComparison.Ordinal);
        Assert.Empty(c.Issues("He go to school.", "en-GB")!);
    }

    // the real model, where install.ps1 -ChromeModel ran and Chrome has downloaded its proofreader
    [Fact]
    public void The_real_model_corrects_english_and_leaves_other_languages()
    {
        if (ChromeModelChecker.PythonIn(ChromeModelChecker.DefaultHome) == null || ChromeModelChecker.ModelInChrome() == null) return;
        using var c = new ChromeModelChecker();
        IReadOnlyList<GrammarIssue>? Ask(string text, string lang)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(90))
            {
                if (c.Issues(text, lang) is { } issues) return issues;
                Assert.True(c.Available, c.Status);
                Thread.Sleep(50);
            }
            return null;
        }
        const string text = "The results was better than we expected.";
        var found = Ask(text, "en-GB");
        Assert.NotNull(found);
        var fix = Assert.Single(found);
        Assert.Equal("was", text.Substring(fix.Offset, fix.Length));
        Assert.Contains("were", fix.Replacements);
        Assert.Equal("Chrome model", fix.Source);
        Assert.Empty(Ask("Он сказал что придёт.", "ru-RU")!);
    }
}
