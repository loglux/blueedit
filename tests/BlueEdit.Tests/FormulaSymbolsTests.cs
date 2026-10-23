using Xunit;

[CollectionDefinition("formula symbols", DisableParallelization = true)]
public sealed class FormulaSymbolsCollection;

/// <summary>"formulaSymbols": "basic" — formulas as text without the characters most fonts lack. The switch is global,
/// so these run alone, not beside the other formula tests.</summary>
[Collection("formula symbols")]
public sealed class FormulaSymbolsTests : IDisposable
{
    public FormulaSymbolsTests() => Conceal.ClearCaches();
    public void Dispose() { Latex.BasicSymbols = false; Conceal.ClearCaches(); }

    [Fact]
    public void Full_symbols_are_the_default_and_keep_the_slanted_signs_and_letter_subscripts()
    {
        Assert.Equal("cₙ ⩽ α", Latex.ToUnicode(@"c_{n} \leqslant \alpha"));
    }

    [Fact]
    public void Basic_symbols_write_letter_subscripts_out_and_slanted_signs_as_plain_ones()
    {
        Latex.BasicSymbols = true;
        Assert.Equal("c_n ≤ α ≥ x₁ + aₑ", Latex.ToUnicode(@"c_{n} \leqslant \alpha \geqslant x_1 + a_e"));   // ₑ is in the common fonts
        Assert.Equal(new[] { "c  ≤ 1", " n" }, LatexLayout.Lay(@"c_{n} \leqslant 1").Rows.Select(r => r.TrimEnd()).ToArray());
    }
}
