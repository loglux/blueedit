using Xunit;

/// <summary>Grammar and style in .tex files (#78): what is text and what is not.</summary>
public sealed class LatexProofingTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-tex-" + Guid.NewGuid().ToString("N"));
    public LatexProofingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string text)
    {
        var p = Path.Combine(_dir, "a.tex");
        File.WriteAllText(p, text);
        var e = new EditorView();
        e.Load(p);
        return e;
    }

    [Theory]
    [InlineData(@"The \emph{results} was good.", "The       results  was good.")]
    [InlineData(@"\section{Introduction}", "         Introduction ")]
    [InlineData(@"as shown in~\cite{knuth} it is", "as shown in              it is")]
    [InlineData(@"see Section~\ref{sec:a}.", "see Section            .")]
    [InlineData(@"costs 5\% more % a comment", "costs 5 % more            ")]
    [InlineData(@"\textbf[x]{bold} text", "           bold  text")]
    public void Commands_braces_and_comments_become_spaces_and_text_stays(string line, string plain)
    {
        var got = Prose.LatexPlain(line);
        Assert.Equal(line.Length, got.Length);                        // every column stays where it was
        Assert.Equal(plain, got);
    }

    [Fact]
    public void The_preamble_math_environments_figures_and_comments_are_not_prose()
    {
        var e = Open(string.Join('\n', new[]
        {
            @"\documentclass{article}",                                 // 0 preamble
            @"\usepackage{amsmath}",                                    // 1
            @"\begin{document}",                                        // 2
            @"The function was continuous.",                            // 3 prose
            @"\begin{equation}",                                        // 4
            @"f(x) = x^2",                                              // 5
            @"\end{equation}",                                          // 6
            @"where $x$ is real.",                                      // 7 prose
            @"\[",                                                      // 8
            @"a = b",                                                   // 9
            @"\]",                                                      // 10
            @"% a whole line of comment",                               // 11
            @"\begin{figure}",                                          // 12
            @"\caption{A plot of the data.}",                           // 13 inside a figure: not checked
            @"\end{figure}",                                            // 14
            @"\section{Results}",                                       // 15 prose: a paragraph of its own
            @"The results was good.",                                   // 16 prose
            @"\end{document}",                                          // 17
        }) + "\n");
        var prose = Enumerable.Range(0, 18).Where(i => e.ParagraphAt(i) != null).ToList();
        Assert.Equal(new[] { 3, 7, 15, 16 }, prose);
        Assert.Equal((15, 15), e.ParagraphAt(15));                     // the heading does not join the next line
        Assert.Equal((16, 16), e.ParagraphAt(16));
    }

    [Fact]
    public void Formulas_and_references_are_the_formulas_a_finding_must_not_touch()
    {
        var e = Open("Hence $x>0$ by~\\eqref{eq:1}, see \\cite{r}. % $y$\n");
        var math = e.MathIn(0);
        var line = e.Lines[0];
        Assert.Equal(new[] { "$x>0$", @"\eqref{eq:1}", @"\cite{r}" }, math.Select(m => line.Substring(m.Start, m.Length)));
        // the text a checker gets: the formula as it is (Source), the reference too; the comment gone
        var text = e.ParagraphText(0, 0, MathView.Source);
        Assert.StartsWith("Hence $x>0$ by ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$y$", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sentence_goes_on_across_a_display_formula_but_not_across_a_figure()
    {
        var e = Open(string.Join('\n', new[]
        {
            @"We define the function by",                               // 0
            @"\begin{equation}",                                        // 1
            @"f(x) = \sin x,",                                          // 2
            @"\end{equation}",                                          // 3
            @"which are continuous.",                                   // 4
            @"",                                                        // 5
            @"The plot is below.",                                      // 6
            @"\begin{figure}",                                          // 7
            @"\caption{A plot.}",                                       // 8
            @"\end{figure}",                                            // 9
            @"it shows the trend.",                                     // 10
        }) + "\n");
        Assert.Equal((0, 4), e.ParagraphAt(0));                       // one sentence, the formula inside it
        Assert.Equal((0, 4), e.ParagraphAt(4));
        Assert.Equal(new[] { (0, @"\begin{equation}".Length) }, e.MathIn(1));      // the formula's lines: formulas
        Assert.Equal((6, 6), e.ParagraphAt(6));                       // a figure is not part of the sentence
        Assert.Equal((10, 10), e.ParagraphAt(10));
    }

    [Fact]
    public void Plain_text_and_markdown_are_as_before()
    {
        Assert.Equal(@"a \emph{b} 5\% c", Prose.Plain(@"a \emph{b} 5\% c", markdown: false));
    }
}
