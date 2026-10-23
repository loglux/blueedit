using Xunit;

/// <summary>Proofing text with formulas (#48): what the checkers are given, and what of their answer is kept.</summary>
public sealed class MathProofingTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-math-" + Guid.NewGuid().ToString("N"));
    public MathProofingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>Remembers what it was asked; flags every occurrence of the given phrases.</summary>
    sealed class Recorder(params string[] phrases) : IGrammarChecker
    {
        public readonly List<string> Asked = new();
        public string Name => "rec";
        public bool Available => true;
        public string? Status => null;
        public int Version => 1;
        public MathView Formulas { get; init; } = MathView.Source;
        public void Dispose() { }
        public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true)
        {
            Asked.Add(paragraph);
            var found = new List<GrammarIssue>();
            foreach (var p in phrases)
                for (int at = paragraph.IndexOf(p, StringComparison.Ordinal); at >= 0; at = paragraph.IndexOf(p, at + 1, StringComparison.Ordinal))
                    found.Add(new GrammarIssue(at, p.Length, "flagged", new[] { "fix" }, "REC"));
            return found;
        }
    }

    EditorView Open(string text, IGrammarChecker g)
    {
        var path = Path.Combine(_dir, "a.md");
        File.WriteAllText(path, text);
        var e = new EditorView();
        e.Load(path);
        e.Grammar = g; e.GrammarOn = true; e.GrammarLanguages = new[] { "en-GB", "ru-RU" };
        return e;
    }

    static string Text(EditorView e, int line) => e.ParagraphAt(line) is { } p ? e.ParagraphText(p.First, p.Last) : "(no paragraph)";

    [Theory]
    [InlineData("Here $x^2+1$ is positive.", "Here X       is positive.")]
    [InlineData("где $x$ — длина отрезка.", "где X   — длина отрезка.")]
    [InlineData("so $$E = mc^2$$ holds.", "so X            holds.")]
    [InlineData("A price of \\$5 and $a$.", "A price of  $5 and X  .")]           // \$ is a dollar, not a formula
    [InlineData("Если $x>0$, то $y$; иначе нет.", "Если X    , то X  ; иначе нет.")]   // the comma after it is checked
    [InlineData("It costs $ 5 and $ 6.", "It costs $ 5 and $ 6.")]               // spaces inside: not math (as Obsidian)
    public void A_formula_is_a_placeholder_word_of_the_same_length(string line, string expected)
    {
        var e = Open(line + "\n", new Recorder());
        Assert.Equal(expected, Text(e, 0));
        Assert.Equal(line.Length, Text(e, 0).Length);
    }

    [Theory]
    [InlineData(@"We have \(x^2+1>0\) for every real \(x\).", new[] { @"\(x^2+1>0\)", @"\(x\)" })]
    [InlineData(@"so \[ y = 2x \] holds.", new[] { @"\[ y = 2x \]" })]
    [InlineData("Since $(x+3)(4-3x)$ is non-zero and $x$ is real.", new[] { "$(x+3)(4-3x)$", "$x$" })]
    [InlineData("    $(addprefix --profile ,$(PROD_PROFILES)) up -d", new string[0])]        // shell code, not maths
    [InlineData("    $(PROD_COMPOSE) --env-file $(PROD_ENV)", new string[0])]
    [InlineData("Two side by side $a$$b$ and then words.", new[] { "$a$", "$b$" })]               // #128: as Obsidian draws them
    [InlineData("so $$x$$ and $y$ hold.", new[] { "$$x$$", "$y$" })]
    [InlineData(@"see \[1\] and \[ for more", new[] { @"\[1\]" })]                              // an escaped bracket runs nowhere
    [InlineData(@"costs $\$ 90$ and $100 \%$, not \$5 or $5\$.", new[] { @"$\$ 90$", @"$100 \%$" })]   // \$ inside is a dollar
    public void Formulas_in_every_notation_and_shell_code_that_is_not_one(string line, string[] formulas)
    {
        var e = Open(line + "\n", new Recorder());
        Assert.Equal(formulas, e.MathIn(0).Select(m => line.Substring(m.Start, m.Length)));
    }

    [Fact]
    public void A_display_formula_does_not_split_the_sentence_and_the_punctuation_after_it_is_kept()
    {
        var e = Open("Substituting gives\n$$\nx = 3\n$$\nso the answer is 3.\n\n$$\ny = 1\n$$.\n", new Recorder());
        Assert.Equal((0, 4), e.ParagraphAt(0));
        Assert.Equal((0, 4), e.ParagraphAt(4));
        // no blank lines inside: LanguageTool would end the sentence there
        // "$$", " ", "x = 3", " ", "$$": the placeholder and ten spaces
        Assert.Equal("Substituting gives\nX" + new string(' ', 10) + "\nso the answer is 3.", Text(e, 0));
        Assert.Null(e.ParagraphAt(7));                                              // only a formula: nothing to check
        Assert.Equal("We get\nX        .", Text(Open("We get\n$$x = 1$$.\n", new Recorder()), 0));
    }

    [Fact]
    public void A_bracket_display_formula_over_several_lines_is_a_formula_and_the_text_after_it_is_checked()
    {
        // #128: as Mathpix and pandoc write it; before, its lines were checked as prose
        var e = Open("Substituting gives\n\\[\nx = 3 \\quad and\n\\]\nso the answer is 3.\n", new Recorder());
        Assert.Equal((0, 4), e.ParagraphAt(0));
        Assert.Equal("Substituting gives\nX" + new string(' ', 20) + "\nso the answer is 3.", Text(e, 0));     // \[, the line, \]: 21
        // after "$a$$b$" the words are prose again, and the next line is not a formula
        e = Open("Two side by side $a$$b$ and then words.\nNext line here.\n", new Recorder());
        Assert.Equal("Two side by side X  X   and then words.\nNext line here.", Text(e, 0));
    }

    // ---------- display formulas with blank lines around them (#70) ----------

    [Fact]
    public void A_formula_after_unfinished_text_joins_it_with_blank_lines_and_lower_case_text_goes_on()
    {
        // the user's usual style: text that has not ended, a blank line, the formula, a blank line, "where ..."
        var e = Open("So putting $y=0$ gives\n\n$$\n0 = 3x - 6,\n$$\n\nwhere the intercept is at $x=2$.\n\nThe gradient is 3.\n", new Recorder());
        Assert.Equal((0, 6), e.ParagraphAt(0));
        Assert.Equal((0, 6), e.ParagraphAt(6));                                     // the same seen from below
        Assert.Equal((8, 8), e.ParagraphAt(8));                                     // a capital starts a new sentence
        var text = e.ParagraphText(0, 6, MathView.Source);
        Assert.DoesNotContain("\n\n", text, StringComparison.Ordinal);              // no blank line: one sentence for LanguageTool
        Assert.DoesNotContain("\n \n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_calculation_of_several_formulas_stays_with_its_sentence()
    {
        var e = Open("The volume is\n\n$$V=\\frac{1}{3}\\pi r^{2}h$$\n\n$$=392\\pi$$\n\nso it is about $1230$ cubic centimetres.\n", new Recorder());
        Assert.Equal((0, 6), e.ParagraphAt(0));
        Assert.Equal((0, 6), e.ParagraphAt(6));
    }

    [Fact]
    public void A_formula_after_a_full_stop_or_before_a_capital_is_not_joined()
    {
        var e = Open("This is done.\n\n$$x = 1$$\n\nWe are given the radius.\n", new Recorder());
        Assert.Equal((0, 0), e.ParagraphAt(0));
        Assert.Equal((4, 4), e.ParagraphAt(4));
        var f = Open("To find the area we can use the formula\n\n$$S=\\pi r^{2}$$\n\nWe are given the radius.\n", new Recorder());
        Assert.Equal((0, 2), f.ParagraphAt(0));                                    // the formula ends that sentence
        Assert.Equal((4, 4), f.ParagraphAt(4));
    }

    [Fact]
    public void Text_after_a_display_formula_on_its_line_is_checked()
    {
        var e = Open("We are given the radius of the cone\n\n$$r=7\\,\\text{cm}$$ and slant height\n\n$$l=25\\,\\text{cm}$$\n", new Recorder());
        Assert.NotNull(e.ParagraphAt(2));                                           // was not prose at all
        Assert.Equal((0, 4), e.ParagraphAt(0));
    }

    sealed class Fixed(params GrammarIssue[] issues) : IGrammarChecker
    {
        public string Name => "fixed";
        public bool Available => true;
        public string? Status => null;
        public int Version => 1;
        public void Dispose() { }
        public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true) => issues;
    }

    [Fact]
    public void A_finding_on_markup_alone_is_dropped_and_an_insertion_kept()
    {
        // "From **part one** we know it.": the model's change on the "**" (it saw spaces there), and a comma it inserts
        var e = Open("From **part one** we know it.\n", new Fixed(
            new GrammarIssue(5, 2, "markup", new[] { "" }, "LLM:**→"), new GrammarIssue(17, 0, "comma", new[] { "," }, "LLM:→,")));
        e.GoToLine(1);
        while (e.Col < 6) e.HandleKey(new Tui.KeyInput(Tui.Key.Right));
        Assert.Null(e.GrammarIssuesAtCaret());
        while (e.Col < 17) e.HandleKey(new Tui.KeyInput(Tui.Key.Right));
        Assert.Equal("comma", Assert.Single(e.GrammarIssuesAtCaret()!.Value.Issues).Message);
    }

    [Fact]
    public void Findings_that_touch_a_formula_are_dropped_and_the_ones_next_to_it_kept()
    {
        var rec = new Recorder("$x$", "the the");
        var e = Open("We substitute the the value $x$ into it.\n", rec);
        e.GoToLine(1);
        var at = e.Lines[0].IndexOf("the the", StringComparison.Ordinal);
        for (int i = 0; i < at + 1; i++) e.HandleKey(new Tui.KeyInput(Tui.Key.Right));
        Assert.Equal("the the", Assert.Single(e.GrammarIssuesAtCaret()!.Value.Issues) is var x ? e.Lines[0].Substring(x.Offset, x.Length) : "");
        var dollar = e.Lines[0].IndexOf('$', StringComparison.Ordinal);
        while (e.Col < dollar + 1) e.HandleKey(new Tui.KeyInput(Tui.Key.Right));
        Assert.Null(e.GrammarIssuesAtCaret());                                       // "$x$" was flagged: a formula
    }

    [Fact]
    public void Without_placeholders_a_formula_is_spaces_as_before()
    {
        var e = Open("Here $x$ is positive.\n", new Recorder());
        Assert.Equal("Here     is positive.", e.ParagraphText(0, 0, MathView.Spaces));
        Assert.Equal("Here $x$ is positive.", e.ParagraphText(0, 0, MathView.Source));
    }

    sealed class ReadsFormulas : IGrammarChecker
    {
        public readonly List<string> Asked = new();
        public string Name => "model";
        public bool Available => true;
        public string? Status => null;
        public int Version => 1;
        public MathView Formulas => MathView.Source;
        public void Dispose() { }
        public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true)
        {
            Asked.Add(paragraph);
            int at = paragraph.IndexOf("x^2", StringComparison.Ordinal);      // a finding inside the formula: dropped
            return at < 0 ? Array.Empty<GrammarIssue>() : new[] { new GrammarIssue(at, 3, "inside", new[] { "x²" }, "M") };
        }
    }

    [Fact]
    public void Each_checker_gets_formulas_in_the_form_it_asks_for_and_nothing_inside_them_is_kept()
    {
        var lt = new Recorder { Formulas = MathView.Placeholder };      // a checker may ask for a placeholder
        var model = new ReadsFormulas();
        var e = Open("Here $x^2$ is positive.\n", new GrammarCheckers(new IGrammarChecker[] { lt, model }));
        e.GoToLine(1);
        Assert.Null(e.GrammarIssuesAtCaret());
        Assert.Equal("Here X     is positive.", Assert.Single(lt.Asked.Distinct()));
        Assert.Equal("Here $x^2$ is positive.", Assert.Single(model.Asked.Distinct()));
        while (e.Col < 7) e.HandleKey(new Tui.KeyInput(Tui.Key.Right));
        Assert.Null(e.GrammarIssuesAtCaret());                                      // the model's change inside $x^2$
    }

    /// <summary>A model that answers with this corrected text (and these notes), read as LlmGrammarChecker reads it.</summary>
    sealed class Answers(string corrected, string errors = "[]") : IGrammarChecker
    {
        public string Name => "model";
        public bool Available => true;
        public string? Status => null;
        public int Version => 1;
        public MathView Formulas => MathView.Source;
        public void Dispose() { }
        public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true) =>
            LlmGrammarChecker.IssuesFrom(paragraph, "{\"corrected\": " + System.Text.Json.JsonSerializer.Serialize(corrected) + ", \"errors\": " + errors + "}", language);
    }

    List<GrammarIssue> Kept(string text, string corrected, string errors = "[]")
    {
        var e = Open(text + "\n", new Answers(corrected, errors));
        e.GoToLine(1);
        var all = new List<GrammarIssue>();
        for (int i = 0; i <= text.Length; i++, e.HandleKey(new Tui.KeyInput(Tui.Key.Right)))
            if (e.GrammarIssuesAtCaret() is { } h) all.AddRange(h.Issues.Where(x => !all.Any(y => (y.Offset, y.Length, y.Rule) == (x.Offset, x.Length, x.Rule))));
        return all;
    }

    // #158, the cases BlueProof for Obsidian tests (test/llm-answer.test.ts): a mark put in or taken out next to a
    // formula is a fix of the prose; one put in inside it, or a change inside it, is not kept
    [Theory]
    [InlineData("Пусть $a=b$ тогда $a-b=0$.", "Пусть $a=b$, тогда $a-b=0$.", ",")]
    [InlineData("Тогда $x=2$", "Тогда $x=2$.", ".")]
    [InlineData("Пусть $a=b$, и $c=d$.", "Пусть $a=b$ и $c=d$.", "")]
    [InlineData("Пусть $a=b$, тогда $a-b=0$.", "Пусть $a=b,$ тогда $a-b=0$.", null)]
    [InlineData("Пусть $a=b$ тогда.", "Пусть $a=c$ тогда.", null)]
    public void A_mark_next_to_a_formula_is_kept_and_one_inside_it_is_not(string text, string corrected, string? mark)
    {
        var kept = Kept(text, corrected);
        if (mark == null) { Assert.Empty(kept); return; }
        var x = Assert.Single(kept);
        var at = text.IndexOf('$', text.IndexOf('$', StringComparison.Ordinal) + 1);       // the first formula's closing "$"
        var fixedText = text[..x.Offset] + x.Replacements[0] + text[(x.Offset + x.Length)..];
        Assert.Equal(corrected, fixedText);
        Assert.Equal(text[..(at + 1)] + mark, fixedText[..(at + 1 + mark.Length)]);
    }

    [Fact]
    public void A_full_stop_moved_into_a_formula_is_not_shown_as_removed()
    {
        // gpt-5.4-mini's answer, 3 of 3 times (found by BlueProof for Obsidian, #158)
        const string text = @"Therefore, $\dfrac{\sqrt{72}}{\sqrt{7}+\sqrt{2}} = 6\sqrt{7}-6\sqrt{2} = 6\left(\sqrt{7}-\sqrt{2}\right)$.";
        var errors = """[{"original": "\\right)$.", "replacement": "\\right).$", "explanation": "The closing punctuation should be inside the math expression."}]""";
        Assert.Empty(Kept(text, text[..^2] + ".$", errors));
        Assert.Single(Kept("Hence $x=2$, done.", "Hence $x=2$ done."));      // with nothing changed inside the formula it stays
    }

    [Fact]
    public void Formula_spans_of_a_paragraph_are_offsets_in_its_text()
    {
        var e = Open("ab $x$ cd\n$$\ny\n$$.\n", new Recorder());
        Assert.Equal(new[] { (3, 3), (10, 2), (13, 1), (15, 2) }, e.ParagraphMath(0, 3));
    }
}
