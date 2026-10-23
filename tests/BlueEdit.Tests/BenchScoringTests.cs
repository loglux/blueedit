using Xunit;

/// <summary>How the benchmarks count a finding that is not on an expected error.</summary>
public class BenchScoringTests
{
    [Fact]
    public void A_finding_in_the_sentence_of_an_error_is_next_to_it_one_in_another_sentence_is_not()
    {
        const string text = "Мария проверила задачу. Сначала он построил график и ушёл домой. Потом всё было хорошо.";
        var errors = new[] { (text.IndexOf("он построил", StringComparison.Ordinal), 2) };
        Assert.True(MathBench.NextToError(text, errors, text.IndexOf("ушёл", StringComparison.Ordinal)));      // the rest of the same fix
        Assert.False(MathBench.NextToError(text, errors, text.IndexOf("проверила", StringComparison.Ordinal)));
        Assert.False(MathBench.NextToError(text, errors, text.IndexOf("хорошо", StringComparison.Ordinal)));
        Assert.False(MathBench.NextToError(text, new[] { (-1, 2) }, 0));                                       // an error not found
    }
}
