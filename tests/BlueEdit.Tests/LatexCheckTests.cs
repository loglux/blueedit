using Xunit;

/// <summary>Formulas that would not typeset (LatexCheck.cs, #192), and the file's formulas found as errors.</summary>
public sealed class LatexCheckTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-latex-" + Guid.NewGuid().ToString("N"));
    public LatexCheckTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Theory]
    [InlineData(@"$\dfrac{\sqrt{72}}{\sqrt{7}+\sqrt{2}} = 6\left(\sqrt{7}-\sqrt{2}\right)$")]
    [InlineData(@"$$x^{2}_{1} + y_1^2 = \frac12$$")]
    [InlineData(@"$\sqrt[3]{x} \cdot \left\{ a \right\} \cdot \left. \frac{d}{dx} \right|_{x=0}$")]
    [InlineData(@"$$\begin{aligned} a &= b \\ c &= d \end{aligned}$$")]
    [InlineData(@"$\text{area } = 7\,\text{cm}^2, \{x\}, 50\%$")]
    [InlineData(@"$\mathbb R^n, \hat x, \vec{v}_1, x'^2, {x^2}^3$")]
    [InlineData(@"$\left\langle u \middle| v \right\rangle \text{a {nested} group}$")]
    [InlineData(@"\(a^b\)")]
    public void A_formula_that_typesets_has_no_errors(string math) => Assert.Empty(LatexCheck.Check(math));

    [Theory]
    [InlineData(@"$\frac{a}{b$", "Missing close brace", "{")]
    [InlineData(@"$a}$", "Extra close brace or missing open brace", "}")]
    [InlineData(@"$\left( x$", "Missing \\right", "\\left")]
    [InlineData(@"$x \right)$", "Extra \\right", "\\right")]
    [InlineData(@"$\left{ x \right\}$", "Missing or unrecognized delimiter for \\left", "\\left")]
    [InlineData(@"$\frac{a}$", "Missing argument for \\frac", "\\frac")]
    [InlineData(@"$\sqrt$", "Missing argument for \\sqrt", "\\sqrt")]
    [InlineData(@"$x^$", "Missing superscript or subscript argument", "^")]
    [InlineData(@"$x^2^3$", "Double exponent: use braces to clarify", "^")]
    [InlineData(@"$x_1_2$", "Double subscripts: use braces to clarify", "_")]
    [InlineData(@"$x^{2}_1^3$", "Double exponent: use braces to clarify", "^")]
    [InlineData(@"$a & b$", "Misplaced &", "&")]
    [InlineData(@"$$\begin{aligned} a &= b$$", "Missing \\end{aligned}", "\\begin{aligned}")]
    [InlineData(@"$$\begin{aligned} a \end{array}$$", "\\begin{aligned} ended with \\end{array}", "\\end{array}")]
    [InlineData(@"$\text{a {b}$", "Missing close brace", "{")]
    public void A_formula_that_would_not_typeset_is_found(string math, string message, string at)
    {
        var p = Assert.Single(LatexCheck.Check(math));
        Assert.Equal(message, p.Message);
        Assert.Equal(at, math.Substring(p.Offset, p.Length));
    }

    [Theory]
    [InlineData(@"$\sin x + \cos(2x) = \log_2 8, \lim_{n\to\infty}$")]
    [InlineData(@"$x_{max}, T_\text{min}, y^{sup}, \operatorname{cosec} x, \mathrm{max}, \text{cos is fine here}$")]
    [InlineData(@"$z^*, A^{*}, f * g, a \le b, x<-1, n!=120, x \to 0$")]
    [InlineData(@"$\varepsilon>0 *$")]
    [InlineData(@"$...$")]
    [InlineData(@"$5\,\text{cm}, 2m, 3s, 10^{3}\,\mathrm{kg}, \dots, \cos\left(x\right), \cos{x}$")]
    [InlineData(@"$\ln{(a)(b)}, \max_{x} f, \sinh t, \arccos y$")]
    public void Right_writing_breaks_no_rule(string math) => Assert.Empty(LatexCheck.Style(math));

    [Theory]
    [InlineData(@"$cos x$", "latex-function", "cos", @"\cos")]
    [InlineData(@"$2\sin x + ln(y)$", "latex-function", "ln", @"\ln")]
    [InlineData(@"$\cos{(x+1)}$", "latex-function-braces", "{(x+1)}", "(x+1)")]
    [InlineData(@"$2*3$", "latex-times", "*", @"\times")]
    [InlineData(@"$x * 4$", "latex-times", "*", @"\times")]
    [InlineData(@"$3*x$", "latex-times", "*", @"\times ")]
    [InlineData(@"$a <= b$", "latex-operator", "<=", @"\le")]
    [InlineData(@"$x->0$", "latex-operator", "->", @"\to")]
    [InlineData(@"$p => q$", "latex-operator", "=>", @"\Rightarrow")]
    [InlineData(@"$1, 2, ..., n$", "latex-dots", "...", @"\dots")]
    [InlineData(@"$5 cm$", "latex-unit", " cm", @"\,\text{cm}")]
    [InlineData(@"$12kg$", "latex-unit", "kg", @"\,\text{kg}")]
    [InlineData(@"$3\,min$", "latex-unit", @"\,min", @"\,\text{min}")]
    public void A_rule_of_writing_is_found_with_its_fix(string math, string rule, string at, string fix)
    {
        var p = Assert.Single(LatexCheck.Style(math));
        Assert.Equal(rule, p.Rule);
        Assert.Equal(at, math.Substring(p.Offset, p.Length));
        Assert.Equal(fix, p.Fix);
        Assert.Contains(p.Rule, LatexCheck.Rules);
    }

    [Fact]
    public void A_rule_is_a_warning_F7_fixes_and_an_ignored_rule_is_not_shown()
    {
        var e = Open("c.md", "Then $a <= b$ and $cos x$.\n");
        var found = e.LatexDiagnostics();
        Assert.Equal(new[] { "latex-operator", "latex-function" }, found.Select(d => d.Rule));
        Assert.All(found, d => Assert.Equal(2, d.Severity));
        e.FixLatex(found[0]);
        Assert.Equal("Then $a \\le b$ and $cos x$.", e.Lines[0]);
        e.IgnoredRules = new HashSet<string> { "latex-function" };
        Assert.Empty(e.LatexDiagnostics());
        e.IgnoredRules = new HashSet<string>();
        e.LatexStyleOn = false;
        Assert.Empty(e.LatexDiagnostics());
    }

    EditorView Open(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        var e = new EditorView();
        e.Load(path);
        return e;
    }

    [Fact]
    public void Errors_are_found_in_the_files_formulas_inline_and_over_lines_and_not_in_code()
    {
        var e = Open("a.md", "Text $x^2^3$ and `$a}$` here.\n\n$$\n\\frac{a}{b\n\n+ c\n$$\n\n```\n$\\left($\n```\n");
        var found = e.LatexDiagnostics();
        Assert.Equal(2, found.Count);
        Assert.Equal((0, 9, "Double exponent: use braces to clarify"), (found[0].Line, found[0].Col, found[0].Message));
        Assert.Equal((3, 8, "Missing close brace"), (found[1].Line, found[1].Col, found[1].Message));
        Assert.Equal("LaTeX", found[1].Source);
        e.LatexCheckOn = false;
        Assert.Empty(e.LatexDiagnostics());
        Assert.Empty(Open("a.py", "x = '$a}$'\n").LatexDiagnostics());
    }

    [Fact]
    public void Shift_F7_goes_to_a_formula_error()
    {
        var e = Open("b.md", "First line.\n\nThen $\\frac{a}$ here.\n");
        Assert.Equal("Error (LaTeX): Missing argument for \\frac", e.GoToNextProblem());
        Assert.Equal(2, e.Row);
    }
}
