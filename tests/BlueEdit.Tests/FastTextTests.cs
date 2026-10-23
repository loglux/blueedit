using System.Text.Json;
using Xunit;

/// <summary>fastText's prediction (FastText.cs) with the language identification model built into the editor, against
/// fastText itself: the answers of its WebAssembly build (the one the Obsidian plugin runs).</summary>
public sealed class FastTextTests
{
    static readonly FastText Model = Load();

    static FastText Load()
    {
        using var s = typeof(FastText).Assembly.GetManifestResourceStream("lid.176.ftz")!;
        return new FastText(s);
    }

    [Theory]
    [InlineData("Tom spent the night at his grandparents'.", "en", 0.9970373511314392, "it", 0.00024288633721880615)]
    [InlineData("Die Liebe ist nur eine Erfindung.", "de", 0.9999622106552124, "eo", 2.7751520974561572e-05)]
    [InlineData("Hattest du Angst?", "fr", 0.7272183299064636, "de", 0.15766818821430206)]
    [InlineData("Натисни на кнопку, будь ласка.", "ru", 0.5990021228790283, "uk", 0.11688446998596191)]
    [InlineData("日本語のテキスト", "ja", 0.9915789365768433, "zh", 0.008156886324286461)]
    [InlineData("line one line two", "en", 0.9128056764602661, "es", 0.00897197611629963)]
    // nothing but the end of the line, which fastText reads as a word too
    [InlineData("", "en", 0.12450417876243591, "ca", 0.08594832569360733)]
    public void The_two_most_likely_languages_are_fastTexts(string text, string first, double p1, string second, double p2)
    {
        var top = Model.Predict(text, 2);
        Assert.Equal((first, second), (top[0].Label, top[1].Label));
        Assert.Equal(p1, top[0].Probability, 1e-5);
        Assert.Equal(p2, top[1].Probability, 1e-5);
    }

    [Fact]
    public void The_model_knows_176_languages() => Assert.Equal(176, Model.Labels.Count);

    /// <summary>All the texts the reference was made for (tools/language/fasttext-reference.mts, outside the unit
    /// tests): with BLUEEDIT_FASTTEXT_REF set to its output. Measured 2026-10-12: of 1,621 texts the first three
    /// languages the same in all, probabilities within 1e-4; one fifth place swapped between two languages of 0.000092.</summary>
    [Fact]
    public void Answers_as_fastText_itself_on_the_reference()
    {
        var reference = Environment.GetEnvironmentVariable("BLUEEDIT_FASTTEXT_REF");
        if (reference == null) return;
        var bad = new List<string>();
        foreach (var e in JsonDocument.Parse(File.ReadAllText(reference)).RootElement.EnumerateArray())
        {
            var text = e.GetProperty("text").GetString()!;
            var expected = e.GetProperty("top").EnumerateArray().Take(3).Select(p => (p[0].GetString()!, p[1].GetSingle())).ToList();
            var got = Model.Predict(System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " "), 3);
            if (got.Count != expected.Count || got.Zip(expected).Any(p => p.First.Label != p.Second.Item1 || Math.Abs(p.First.Probability - p.Second.Item2) > 1e-4))
                bad.Add($"{text}\n  want {string.Join(" ", expected)}\n  got  {string.Join(" ", got)}");
        }
        Assert.Empty(bad);
    }
}
