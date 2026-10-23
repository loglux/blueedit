using Xunit;

public class LatexTests
{
    [Theory]
    [InlineData(@"x^2 + y^2 = z^2", "x² + y² = z²")]
    [InlineData(@"\angle DEM=40^\circ", "∠ DEM = 40°")]                         // a raised circle is the degree sign
    [InlineData(@"y^{2}<4", "y² < 4")]                                         // spaced as in print, whatever the source has
    [InlineData(@"3 y-1>0", "3y - 1 > 0")]
    [InlineData(@"-y+(-1)", "-y + (-1)")]                                     // signs stay tight
    [InlineData(@"e^{-x+1}", "e⁻ˣ⁺¹")]                                        // exponents are tight
    [InlineData(@"\text{well-known}=1", "well-known = 1")]
    [InlineData(@"\tan 40^{\circ}", "tan 40°")]
    [InlineData(@"\alpha + \beta \le \gamma", "α + β ≤ γ")]
    [InlineData(@"\frac{1}{2}", "½")]
    [InlineData(@"\frac{a+b}{c}", "(a+b)/c")]
    [InlineData(@"\frac{x}{y}", "x/y")]
    [InlineData(@"\frac{x^3}{3}", "x³/3")]                                     // x³ is one factor
    [InlineData(@"\frac{6}{7}x^{7/2} + \frac{x^3}{3}", "(6/7)x^(7/2) + x³/3")]   // not 6/(7x)
    [InlineData(@"\frac{a}{b} \cdot c", "a/b · c")]
    [InlineData(@"\sqrt{x}", "√x")]
    [InlineData(@"\sqrt{x^2+1}", "√(x² + 1)")]
    [InlineData(@"\sqrt[3]{8}", "∛8")]
    [InlineData(@"\sum_{i=1}^{n} i = \frac{n(n+1)}{2}", "∑ᵢ₌₁ⁿ i = (n(n+1))/2")]
    [InlineData(@"\int_0^1 x\,dx", "∫₀¹ x dx")]
    [InlineData(@"e^{i\pi} + 1 = 0", "e^(iπ) + 1 = 0")]
    [InlineData(@"a_{n+1} = a_n + 1", "aₙ₊₁ = aₙ + 1")]
    [InlineData(@"x \in \mathbb{R}", "x ∈ ℝ")]
    [InlineData(@"A \cup B \subseteq C", "A ∪ B ⊆ C")]
    [InlineData(@"f'(x) \to \infty", "f′(x) → ∞")]
    [InlineData(@"\left( \frac{a}{b} \right)", "( a/b )")]
    [InlineData(@"\text{if } x > 0", "if x > 0")]
    [InlineData(@"\binom{n}{k}", "C(n,k)")]
    [InlineData(@"\lim_{x \to 0} \frac{\sin x}{x} = 1", "lim_(x → 0) (sin x)/x = 1")]
    public void Converts_to_unicode(string tex, string expected) => Assert.Equal(expected, Latex.ToUnicode(tex));

    [Theory]
    [InlineData(@"\unknowncmd{x}", @"\unknowncmd{x}")]
    [InlineData(@"\unknowncmd", @"\unknowncmd")]
    public void Unknown_commands_are_kept_as_written(string tex, string expected) => Assert.Equal(expected, Latex.ToUnicode(tex));

    [Fact]
    public void Empty_input_gives_empty_output() => Assert.Equal("", Latex.ToUnicode(""));

    [Fact]
    public void Unbalanced_braces_do_not_throw() => Assert.NotNull(Latex.ToUnicode(@"\frac{1}{2 + {x"));
}
