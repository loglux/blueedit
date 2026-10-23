using System.Diagnostics;
using Xunit;

public class TextDiffTests
{
    static List<(string From, string To)> Diff(string a, string b) =>
        TextDiff.Changes(a, b).Select(c => (c.OriginalIn(a), c.Replacement)).ToList();

    [Fact]
    public void A_replaced_word() => Assert.Equal(new[] { ("был", "были") }, Diff("Мы был в магазине.", "Мы были в магазине."));

    [Fact]
    public void An_inserted_comma_takes_the_word_before_it_along() =>
        Assert.Equal(new[] { ("сказал", "сказал,") }, Diff("Он сказал что придёт.", "Он сказал, что придёт."));

    [Fact]
    public void Several_changes_and_a_split_word()
    {
        Assert.Equal(new[] { ("was", "were"), ("then", "than"), ("noone", "no one") },
            Diff("The results was better then we expected, and noone complained.", "The results were better than we expected, and no one complained."));
    }

    [Fact]
    public void Deleted_and_moved_words()
    {
        var d = Diff("She don't know where is the station.", "She doesn't know where the station is.");
        Assert.Contains(("don", "doesn"), d);
        var text = "She don't know where is the station.";
        var fixedText = TextDiff.Changes(text, "She doesn't know where the station is.")
            .OrderByDescending(c => c.Offset).Aggregate(text, (s, c) => s.Remove(c.Offset, c.Length).Insert(c.Offset, c.Replacement));
        Assert.Equal("She doesn't know where the station is.", fixedText);       // applying all changes gives the corrected text
    }

    [Fact]
    public void Neighbouring_words_rewritten_at_once_are_one_change()
    {
        // #141: three pieces each wrong alone; taking "чтобы" alone gave "чтобы бы подготовится"
        const string text = "В течении недели я повторял материал, что бы подготовится к экзамену.";
        Assert.Equal(new[] { ("течении", "течение"), ("что бы подготовится", "чтобы подготовиться") },
            Diff(text, "В течение недели я повторял материал, чтобы подготовиться к экзамену."));
    }

    // From models' answers in benchmark runs (docs/tech/proofing-internals.md has the measurement): pieces each wrong
    // alone, "had have known" and "она вычислил", before the joining
    [Theory]
    [InlineData("If I would have known, I would have called.", "If I had known, I would have called.", "would have", "had")]
    [InlineData("Сначала он вычислил дискриминант.", "Сначала она вычислила дискриминант.", "он вычислил", "она вычислила")]
    [InlineData("I have seen him yesterday.", "I saw him yesterday.", "have seen", "saw")]
    public void Real_answers_with_neighbouring_pieces_give_one_change(string text, string corrected, string from, string to) =>
        Assert.Equal(new[] { (from, to) }, Diff(text, corrected));

    [Fact]
    public void A_line_break_put_in_place_of_a_space_is_not_a_change_and_does_not_shift_the_next_fix()
    {
        // Gemma 3 12B began each corrected sentence on a line of its own: ". Красивая" → ".\nКрасивый" before
        Assert.Equal(new[] { ("пошли", "пошёл"), ("Красивая", "Красивый") },
            Diff("Я пошли домой. Красивая дом стоит на холме.", "Я пошёл домой.\nКрасивый дом стоит на холме."));
    }

    [Fact]
    public void Changes_with_a_word_between_them_stay_apart() =>
        Assert.Equal(new[] { ("был", "были"), ("лекцие", "лекции") }, Diff("Мы был на лекцие.", "Мы были на лекции."));

    [Fact]
    public void Same_text_has_no_changes() => Assert.Empty(Diff("Всё верно.", "Всё верно."));

    [Fact]
    public void Changed_share_tells_a_rewrite_from_a_correction()
    {
        var text = "Мы был в магазине и купили хлеб.";
        Assert.True(TextDiff.ChangedShare(text, TextDiff.Changes(text, "Мы были в магазине и купили хлеб.")) < 0.2);
        Assert.True(TextDiff.ChangedShare(text, TextDiff.Changes(text, "Вчера мы заходили в булочную за батоном.")) > 0.5);
    }
}

public class LlmAnswerTests
{
    [Fact]
    public void Answer_becomes_findings_with_positions_and_explanations()
    {
        var text = "Мы был в магазине. Он сказал что придёт.";
        var answer = """
            {"corrected": "Мы были в магазине. Он сказал, что придёт.",
             "errors": [{"original": "Мы был", "replacement": "Мы были", "explanation": "Глагол согласуется с подлежащим «мы»."}]}
            """;
        var issues = LlmGrammarChecker.IssuesFrom(text, answer, "ru-RU");
        Assert.Equal(2, issues.Count);
        Assert.Equal(("был", "были"), (text.Substring(issues[0].Offset, issues[0].Length), issues[0].Replacements[0]));
        Assert.Contains("согласуется", issues[0].Message, StringComparison.Ordinal);
        Assert.Equal(("сказал", "сказал,"), (text.Substring(issues[1].Offset, issues[1].Length), issues[1].Replacements[0]));
        // no explanation given: a plain hint, in the interface's language (English in the tests)
        Assert.Equal(Ui.T("Possibly: {0}", "сказал,"), issues[1].Message);
    }

    [Fact]
    public void Alternatives_become_more_fixes_for_the_same_stretch()
    {
        var text = "Я пошли домой.";
        var answer = """
            {"corrected": "Я пошла домой.", "errors": [{"original": "Я пошли", "replacement": "Я пошла",
             "alternatives": ["Я пошёл"], "explanation": "Глагол согласуется с «я»."}]}
            """;
        var issue = Assert.Single(LlmGrammarChecker.IssuesFrom(text, answer, "ru-RU"));
        Assert.Equal("пошли", text.Substring(issue.Offset, issue.Length));
        Assert.Equal(new[] { "пошла", "пошёл" }, issue.Replacements);
    }

    [Fact]
    public void Latex_spoilt_by_json_escapes_in_the_answer_is_put_back()
    {
        // the model copies \frac, \times, \beta, \neq, \theta into JSON without doubling the backslash
        const string text = """We have $\frac{1}{2} \times \beta \neq \theta$ and it are small.""";
        const string answer = """{"corrected": "We have $\frac{1}{2} \times \beta \neq \theta$ and it is small.", "errors": []}""";
        var issue = Assert.Single(LlmGrammarChecker.IssuesFrom(text, answer, "en-GB"));
        Assert.Equal("are", text.Substring(issue.Offset, issue.Length));
        Assert.Equal("is", issue.Replacements[0]);
        Assert.Equal(@"x \neq y and \to", LlmGrammarChecker.RepairLatexEscapes(@"x \neq y and \to", "x \neq y and \to"));
        Assert.Equal("one\ntwo", LlmGrammarChecker.RepairLatexEscapes(@"$\neq$", "one\ntwo"));     // real line breaks stay
    }

    [Fact]
    public void An_answer_whose_changes_overlap_is_read_without_a_crash()
    {
        // Gemma 3 12B on a text with its formulas blanked (MathBench, 23 October 2026): the word diff gave changes out
        // of order inside the note's fragment, and joining them threw on a negative count, on the checker's thread,
        // which ended the whole editor
        const string text = "The X  -intercept is the value of X   when X    , so setting X     gives";
        const string answer = """{"corrected": "The X-intercept is the value of X when y = 0, so setting y = 0 gives", "errors": [{"original": "X   when X    ", "replacement": "y = 0, so setting", "explanation": "The context suggests that 'X' refers to the y-axis value."}]}""";
        _ = LlmGrammarChecker.IssuesFrom(text, answer, "en-GB");
    }

    [Fact]
    public void Changes_of_spaces_only_are_not_findings() =>
        // a heading's "# " reaches the model as spaces, and it trims them
        Assert.Empty(LlmGrammarChecker.IssuesFrom("  Проверка правописания", """{"corrected": "Проверка правописания", "errors": []}""", "ru-RU"));

    [Fact]
    public void Addresses_mail_and_paths_are_hidden_from_grammar_checkers()
    {
        var line = "see https://exampel.com/docz or name@mial.ru, C:\\Usrs\\x.md now";
        var plain = Prose.Plain(line, markdown: false);
        Assert.Equal(line.Length, plain.Length);                           // offsets stay source columns
        Assert.DoesNotContain("exampel", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("mial", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Usrs", plain, StringComparison.Ordinal);
        Assert.StartsWith("see ", plain, StringComparison.Ordinal);
        Assert.EndsWith(" now", plain, StringComparison.Ordinal);
        Assert.Contains(" or ", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sentence_repeated_in_quotes_is_no_finding_and_the_fix_next_to_it_stays()
    {
        // YandexGPT 5 Lite's answers in the benchmark runs
        var issues = LlmGrammarChecker.IssuesFrom("Анна пришла домой поздно. Потом сразу лёг спать.",
            """{"corrected": "Анна пришла домой поздно. «Анна пришла домой поздно.» Потом сразу легла спать.", "errors": []}""", "ru-RU");
        Assert.Equal(new[] { ("лёг", "легла") }, issues.Select(i => ("Анна пришла домой поздно. Потом сразу лёг спать.".Substring(i.Offset, i.Length), i.Replacements[0])));
        const string text = "Ему нравиться решать задачи по геометрии. Нужно научится проверять ответ.";
        issues = LlmGrammarChecker.IssuesFrom(text,
            """{"corrected": "Ему нравится решать задачи по геометрии. «Ему нравится решать задачи по геометрии.» Нужно научиться проверять ответ.", "errors": []}""", "ru-RU");
        Assert.Equal(new[] { ("нравиться", "нравится"), ("научится", "научиться") }, issues.Select(i => (text.Substring(i.Offset, i.Length), i.Replacements[0])));
    }

    [Fact]
    public void A_stretch_of_the_paragraph_copied_in_again_is_no_finding()
    {
        const string text = "Он сказал что придёт завтра. Мы ждали его весь вечер.";
        var issues = LlmGrammarChecker.IssuesFrom(text,
            """{"corrected": "Он сказал, что придёт завтра. Мы ждали его весь вечер. Он сказал что придёт завтра.", "errors": []}""", "ru-RU");
        Assert.Equal(new[] { ("сказал", "сказал,") }, issues.Select(i => (text.Substring(i.Offset, i.Length), i.Replacements[0])));
    }

    [Fact]
    public void A_quotation_of_the_text_stays_a_quotation() =>
        Assert.Equal("Она сказала: «Я приду завтра вечером, если успею». Он кивнул.",
            LlmGrammarChecker.DropQuotedRepeats("Она сказала: «Я приду завтра вечером если успею». Он кивнул.",
                "Она сказала: «Я приду завтра вечером, если успею». Он кивнул."));

    [Fact]
    public void A_rewritten_paragraph_is_ignored() =>
        Assert.Empty(LlmGrammarChecker.IssuesFrom("Мы был в магазине и купили хлеб.",
            """{"corrected": "Вчера мы заходили в булочную за батоном.", "errors": []}""", "ru-RU"));

    [Theory]
    [InlineData(@"Previous studies \cite{smith2010} has shown that.", true)]
    [InlineData("We define f by\n\\begin{equation}\nf(x) = x,\n\\end{equation}\nwhich is continuous.", true)]
    [InlineData(@"The roots of $x^2 - 5 = 0$ are $\pm\sqrt{5}$.", false)]            // Markdown: LaTeX inside dollars
    [InlineData("Area:\n$$\n\\frac{a}{b}\n$$\nafter", false)]
    [InlineData("No commands at all.", false)]
    public void The_latex_line_of_the_instructions_is_for_text_of_a_latex_document(string text, bool latex) =>
        Assert.Equal(latex, LlmGrammarChecker.LooksLikeLatex(text));

    [Fact]
    public void A_change_that_puts_in_a_latex_command_is_no_finding() =>
        // Gemma 3 12B put "\\" between the rows of an align
        Assert.Empty(LlmGrammarChecker.IssuesFrom("a &= b + c\nd &= e",
            """{"corrected": "a &= b + c \\\\ d &= e", "errors": []}""", "en-GB"));

    [Fact]
    public void A_change_that_only_puts_in_or_takes_out_latex_braces_is_no_finding()
    {
        // Gemini 3.5 Flash-Lite put a "}" after a display formula that was right (found by BlueProof for Obsidian, #192)
        Assert.Empty(LlmGrammarChecker.IssuesFrom("We get\n$$\nx = \\frac{1}{2}\n$$\nso it holds.",
            """{"corrected": "We get\n$$\nx = \\frac{1}{2}\n$$}\nso it holds.", "errors": []}""", "en-GB"));
        Assert.Empty(LlmGrammarChecker.IssuesFrom("Then $x^2$ is it.", """{"corrected": "Then $x^{2}$ is it.", "errors": []}""", "en-GB"));
        Assert.Single(LlmGrammarChecker.IssuesFrom("Then $x$ are it.", """{"corrected": "Then $x$ is it.", "errors": []}""", "en-GB"));
    }

    [Fact]
    public void A_listed_error_that_changes_nothing_is_no_finding() =>
        Assert.Empty(LlmGrammarChecker.IssuesFrom("All good here.", """{"corrected": "All good here.\n", "errors": [{"original":"good","replacement":"good","explanation":"x"}]}""", "en-GB"));

    [Fact]
    public void An_error_listed_but_left_in_the_corrected_text_is_found_where_its_fragment_stands_once()
    {
        // Gemma 3 12B on a LaTeX footnote: "were" → "was" in the list, "were" still in the corrected text
        const string text = "The data was collected in 2019.          The survey were repeated a year later. ";
        var issue = Assert.Single(LlmGrammarChecker.IssuesFrom(text, """
            {"corrected": "The data was collected in 2019.\nThe survey were repeated a year later.",
             "errors": [{"original": "were", "replacement": "was", "explanation": "Singular subject."}]}
            """, "en-GB"));
        Assert.Equal(("were", "was", "Singular subject."), (text.Substring(issue.Offset, issue.Length), issue.Replacements[0], issue.Message));
        // a fragment that stands twice could be either: not shown
        Assert.Empty(LlmGrammarChecker.IssuesFrom("It is what it is.",
            """{"corrected": "It is what it is.", "errors": [{"original": "is", "replacement": "was", "explanation": "x"}]}""", "en-GB"));
        // the gap of blanked markup ("\emph{and} takes") is not a typo
        Assert.Empty(LlmGrammarChecker.IssuesFrom("It costs more       and  takes long.",
            """{"corrected": "It costs more       and  takes long.", "errors": [{"original": "and  takes", "replacement": "andtakes", "explanation": "Remove the extra space."}]}""", "en-GB"));
        // one the corrected text already has is not shown twice
        Assert.Single(LlmGrammarChecker.IssuesFrom("They was here.",
            """{"corrected": "They were here.", "errors": [{"original": "They was", "replacement": "They were", "explanation": "x"}]}""", "en-GB"));
    }

    static List<string> Found(string text, string corrected, string lang = "ru-RU") =>
        LlmGrammarChecker.IssuesFrom(text, System.Text.Json.JsonSerializer.Serialize(new { corrected, errors = Array.Empty<object>() }), lang)
            .Select(i => text.Substring(i.Offset, i.Length)).ToList();

    [Fact]
    public void A_word_put_into_the_other_script_is_a_translation_not_a_fix_and_the_fix_next_to_it_stays()
    {
        // #123 (the Obsidian plugin's cases): Gemma 3 12B translated English terms in Russian text
        Assert.Equal(new[] { "сошлись" }, Found("Мы использовали gradient descent, и он сошлись за десять шагов.",
            "Мы использовали градиентный спуск, и он сошёлся за десять шагов."));
        Assert.Equal(new[] { "were" }, Found("The result were correct, и ответ совпал.", "The result was correct, and the answer matched.", "en-GB"));
        // a word typed in both scripts at once ("Pис" with a Latin P) is put right
        Assert.Equal(new[] { "Pис" }, Found("Pис. 3 показывает график.", "Рис. 3 показывает график."));
    }

    [Fact]
    public void A_change_of_a_number_is_no_finding()
    {
        Assert.Empty(Found("The volume came to about 314 cubic centimetres, which is close.",
            "The volume came to about 209 cubic centimetres, which is close.", "en-GB"));
        Assert.Equal(new[] { "were" }, Found("It were 314 cubic centimetres.", "It was 314 cubic centimetres.", "en-GB"));
    }

    [Fact]
    public void American_english_is_asked_for_american_spelling_and_the_rest_keeps_the_british_prompt()
    {
        // #124: en-US text was asked for British spelling
        Assert.Contains("American English (en-US spelling)", LlmGrammarChecker.InstructionsFor("en-US"), StringComparison.Ordinal);
        Assert.DoesNotContain("British", LlmGrammarChecker.InstructionsFor("en-US"), StringComparison.Ordinal);
        Assert.Contains("British English (en-GB spelling)", LlmGrammarChecker.InstructionsFor("en-GB"), StringComparison.Ordinal);
        Assert.Contains("travelled, learnt", LlmGrammarChecker.InstructionsFor("en"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru-RU", "Russian", "English")]
    [InlineData("en-GB", "British English", "Russian")]
    [InlineData("en-US", "American English", "Russian")]
    [InlineData("de-DE", "German", "Russian")]
    [InlineData("pt-BR", "Portuguese (Brazil)", "English")]
    [InlineData("en-AU", "English (Australia) (en-AU spelling)", "British")]
    public void Only_the_paragraphs_own_language_is_named(string lang, string named, string notNamed)
    {
        // until October 2026 a Russian or English paragraph was told "Russian and British English" whatever it was
        var grammar = LlmGrammarChecker.InstructionsFor(lang);
        var style = StyleAdvisor.Instructions(StyleAdvisor.DefaultFocuses.ToArray(), lang);
        Assert.Contains($"proofreader of {named}", grammar, StringComparison.Ordinal);
        Assert.DoesNotContain(notNamed, grammar, StringComparison.Ordinal);
        Assert.Contains($"editor of {named.Replace(" (en-AU spelling)", "", StringComparison.Ordinal)} prose", style, StringComparison.Ordinal);
        Assert.DoesNotContain(notNamed, style, StringComparison.Ordinal);
        Assert.Equal(lang == "ru-RU", style.Contains("канцелярит", StringComparison.Ordinal));
    }

    [Fact]
    public void A_paragraph_of_no_language_found_is_kept_in_its_own()
    {
        var grammar = LlmGrammarChecker.InstructionsFor("auto");
        Assert.Contains("Keep the text in its own language", grammar, StringComparison.Ordinal);
        Assert.Contains("explanation in the language of the text", grammar, StringComparison.Ordinal);
        Assert.DoesNotContain("Russian", grammar.Replace("я пошёл", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_takes_the_note_about_it_not_the_first_that_holds_its_text()
    {
        // #130: the deleted comma took "Agreement of the verb." from the first note with a comma in it
        const string text = "Yes, he go home, said that.";
        var issues = LlmGrammarChecker.IssuesFrom(text, """
            {"corrected": "Yes, he goes home said that.", "errors": [
             {"original": "Yes, he go", "replacement": "Yes, he goes", "explanation": "Agreement of the verb."},
             {"original": "home,", "replacement": "home", "explanation": "No comma here."}]}
            """, "en-GB");
        Assert.Equal(new[] { ("go", "Agreement of the verb."), (",", "No comma here.") },
            issues.Select(i => (text.Substring(i.Offset, i.Length), i.Message)));
        // a short change no note is about gets none
        Assert.Equal(-1, LlmGrammarChecker.NoteFor([("Yes, he go", "Yes, he goes")], ",", ""));
        Assert.Equal(0, LlmGrammarChecker.NoteFor([("the commitee", "the committee")], "commitee", "committee"));
        // a short word counts as a word, not as letters inside another ("a" → "an" is not about "and the integral are")
        Assert.Equal(-1, LlmGrammarChecker.NoteFor([("and the integral are", "and the integral is")], "a", "an"));
        Assert.Equal(1, LlmGrammarChecker.NoteFor([("it's graph", "its graph"), ("He explained me the rule", "He explained the rule to me")], "me ", ""));
    }

    [Fact]
    public void A_moved_word_inside_one_listed_error_is_one_finding()
    {
        // gpt-5.4-mini (#149): the word diff gives "is " → "" and "station" → "station is", each wrong alone
        const string text = "She dont know where is the station.";
        var issues = LlmGrammarChecker.IssuesFrom(text, """
            {"corrected": "She doesn't know where the station is.", "errors": [
             {"original": "dont", "replacement": "doesn't", "explanation": "Third person singular."},
             {"original": "where is the station", "replacement": "where the station is", "explanation": "Statement word order in an indirect question."}]}
            """, "en-GB");
        Assert.Equal(new[] { ("dont", "doesn't", "Third person singular."), ("is the station", "the station is", "Statement word order in an indirect question.") },
            issues.Select(i => (text.Substring(i.Offset, i.Length), i.Replacements[0], i.Message)));
        // without a listed error that holds them, the pieces stay apart
        Assert.Equal(2, LlmGrammarChecker.IssuesFrom(text.Replace("dont", "doesn't", StringComparison.Ordinal),
            """{"corrected": "She doesn't know where the station is.", "errors": []}""", "en-GB").Count);
        // two errors in one listed fragment, nothing moved (Phi-4-mini): two fixes, each right alone
        Assert.Equal(new[] { "were", "than" }, LlmGrammarChecker.IssuesFrom("The results was better then we expected.", """
            {"corrected": "The results were better than we expected.", "errors": [
             {"original": "results was better then", "replacement": "results were better than", "explanation": "Plural subject."}]}
            """, "en-GB").Select(i => i.Replacements[0]));
    }

    [Fact]
    public void Findings_are_signed_with_the_model_name()
    {
        var issue = Assert.Single(LlmGrammarChecker.IssuesFrom("Я пошли домой.",
            """{"corrected": "Я пошёл домой.", "errors": []}""", "ru-RU", LlmGrammarChecker.DisplayName(@"C:\m\google_gemma-3-12b-it-Q4_K_M.gguf")));
        Assert.Equal("Gemma 3 12B", issue.Source);
        Assert.Equal("Phi-4-mini", LlmGrammarChecker.DisplayName("Phi-4-mini-instruct-Q4_K_M.gguf"));
        Assert.Equal("YandexGPT 5 Lite", LlmGrammarChecker.DisplayName("YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf"));
        Assert.Equal("Qwen3 14B", LlmGrammarChecker.DisplayName("Qwen_Qwen3-14B-Q4_K_M.gguf"));
        Assert.Equal("my-model", LlmGrammarChecker.DisplayName("my-model-Q8_0.gguf"));
    }

    [Fact]
    public void Model_choice_prefers_the_named_file_then_the_best_present()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bp-llm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "models"));
        try
        {
            Assert.Null(LlmGrammarChecker.ModelIn(dir, ""));
            File.WriteAllText(Path.Combine(dir, "models", "some-other.gguf"), "x");
            Assert.EndsWith("some-other.gguf", LlmGrammarChecker.ModelIn(dir, ""));
            File.WriteAllText(Path.Combine(dir, "models", "microsoft_Phi-4-mini-instruct-Q4_K_M.gguf"), "x");
            Assert.EndsWith("Phi-4-mini-instruct-Q4_K_M.gguf", LlmGrammarChecker.ModelIn(dir, ""));       // a known one wins
            Assert.EndsWith("some-other.gguf", LlmGrammarChecker.ModelIn(dir, "some-other.gguf"));
            Assert.Null(LlmGrammarChecker.ModelIn(dir, "missing.gguf"));
            File.WriteAllText(Path.Combine(dir, "models", "google_gemma-3-12b-it-Q4_K_M.gguf"), "x");
            File.WriteAllText(Path.Combine(dir, "models", "YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf"), "x");
            Assert.Equal(new[] { "YandexGPT 5 Lite", "Gemma 3 12B", "Phi-4-mini", "some-other" },        // the menu's order: best first
                LlmGrammarChecker.ModelsIn(dir).Select(LlmGrammarChecker.DisplayName));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class GrammarCheckersTests
{
    sealed class Fixed(params GrammarIssue[] issues) : IGrammarChecker
    {
        public bool Pending;
        public string Name => "f";
        public bool Available => true;
        public string? Status => null;
        public int Version => 1;
        public void Dispose() { }
        public IReadOnlyList<GrammarIssue>? Issues(string p, string l, bool q = true) => Pending ? null : issues;
    }

    static GrammarIssue At(int offset, int length, string rule) => new(offset, length, rule, new[] { "x" }, rule);

    [Fact]
    public void Findings_of_several_checkers_are_merged_overlaps_included_exact_repeats_once()
    {
        var both = new GrammarCheckers(new IGrammarChecker[] { new Fixed(At(0, 5, "LT1")), new Fixed(At(2, 3, "LLM-overlap"), At(10, 4, "LLM2"), At(0, 5, "LT1-again")) });
        // the overlap stays (F7 offers its fix too); the same stretch with the same fix is shown once
        Assert.Equal(new[] { "LT1", "LLM-overlap", "LLM2" }, both.Issues("p", "en")!.Select(i => i.Rule));
    }

    [Fact]
    public void F7_gets_the_findings_of_every_checker_at_the_caret()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bp-f7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = Path.Combine(dir, "a.md");
            File.WriteAllText(p, "Я пошли домой.\n");
            var e = new EditorView { DefaultMdView = MdView.Source };
            e.Load(p);
            var explains = new GrammarIssue(0, 7, "Неверное спряжение", Array.Empty<string>(), "RU_VERB");
            var fixes = new GrammarIssue(2, 5, "Глагол согласуется с «я»", new[] { "пошёл" }, "LLM:пошли→пошёл");
            e.Grammar = new GrammarCheckers(new IGrammarChecker[] { new Fixed(explains), new Fixed(fixes) });
            e.GrammarOn = true;
            e.GoToLine(1, 4);
            var at = e.GrammarIssuesAtCaret()!.Value;
            Assert.Equal(new[] { "RU_VERB", "LLM:пошли→пошёл" }, at.Issues.Select(i => i.Rule));
            e.FixGrammar(at.Issues[1], at.First, "пошёл");
            Assert.Equal("Я пошёл домой.", e.Lines[0]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Answers_show_as_they_come_and_nothing_known_is_null()
    {
        var slow = new Fixed(At(0, 1, "slow")) { Pending = true };
        var both = new GrammarCheckers(new IGrammarChecker[] { slow, new Fixed(At(5, 1, "fast")) });
        Assert.Equal(new[] { "fast" }, both.Issues("p", "en")!.Select(i => i.Rule));
        Assert.Null(new GrammarCheckers(new IGrammarChecker[] { slow }).Issues("p", "en"));
    }

    [Theory]
    [InlineData("{ \"grammar\": \"off\" }", "")]
    [InlineData("{ \"grammar\": \"languagetool\" }", "languagetool")]
    [InlineData("{ \"grammar\": [\"languagetool\", \"llm\"] }", "languagetool,llm")]
    [InlineData("{ \"grammar\": [\"llm\"] }", "llm")]
    [InlineData("{ }", "")]
    public void Grammar_setting_takes_one_name_or_a_list(string json, string expected) =>
        Assert.Equal(expected, string.Join(",", Settings.Parse(json).Settings.Grammar));

    [Fact]
    public void An_unknown_grammar_checker_is_reported() => Assert.NotNull(Settings.Parse("{ \"grammar\": [\"grammarly\"] }").Error);

    [Fact]
    public void Grammar_list_survives_the_template() =>
        Assert.Equal(new[] { "languagetool", "llm" }, Settings.Parse(Settings.Template(new Settings { Grammar = new[] { "languagetool", "llm" }, LlmModel = "m.gguf" })).Settings.Grammar);
}

public class LlmIntegrationTests
{
    const string Phi = "microsoft_Phi-4-mini-instruct-Q4_K_M.gguf";

    [Fact]
    public void A_local_model_corrects_russian_and_english_and_stops_with_the_editor()
    {
        if (!LlmGrammarChecker.InstalledIn(LlmGrammarChecker.DefaultDir, Phi)) return;      // llama.cpp + Phi-4-mini not here
        // its own server, by process id: counting llama-server processes failed when an open editor started one meanwhile
        int? pid;
        using (var llm = new LlmGrammarChecker(LlmGrammarChecker.DefaultDir, Phi))
        {
            IReadOnlyList<GrammarIssue>? Wait(string text, string lang)
            {
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed < TimeSpan.FromSeconds(120)) { if (llm.Issues(text, lang) is { } r) return r; Thread.Sleep(100); }
                return null;
            }
            var ru = Wait("Мы был в магазине и купили хлеб.", "ru-RU");
            Assert.Contains(ru!, i => i.Replacements.Contains("были"));
            var en = Wait("The results was better then we expected.", "en-GB");
            Assert.Contains(en!, i => i.Replacements.Contains("were"));
            Assert.Empty(Wait("This sentence is perfectly fine.", "en-GB")!);
            Assert.Null(llm.Status);
            pid = llm.ServerProcessId;
            Assert.NotNull(pid);
        }
        static bool Running(int id) { try { using var p = Process.GetProcessById(id); return !p.HasExited; } catch (ArgumentException) { return false; } }
        var sw2 = Stopwatch.StartNew();
        while (Running(pid!.Value) && sw2.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(200);
        Assert.False(Running(pid.Value), "the model's server is still running after the checker was disposed");
    }

    [Fact]
    public void Formulas_become_one_word_and_findings_on_the_short_text_move_back_or_go_when_they_touch_one()
    {
        const string text = "So $x=1$ and\n$$\na+b\n$$\nwe has \\begin{equation}\ny=2\n\\end{equation} it\\(z\\) are";
        var (s, formulas) = LlmGrammarChecker.WithoutFormulas(text);
        Assert.Equal("So X and\nX\nwe has X itX are", s);
        var issues = new[]
        {
            new GrammarIssue(s.IndexOf("has", StringComparison.Ordinal), 3, "", new[] { "have" }, "r"),
            new GrammarIssue(s.IndexOf("are", StringComparison.Ordinal), 3, "", new[] { "is" }, "r"),
            new GrammarIssue(s.IndexOf("itX", StringComparison.Ordinal), 3, "", new[] { "it X" }, "r"),    // on a formula
            new GrammarIssue(s.IndexOf("So X", StringComparison.Ordinal) + 4, 0, "", new[] { "," }, "r"),  // after one
        };
        var back = LlmGrammarChecker.MapBack(issues, formulas);
        Assert.Equal(new[] { "has", "are", "" }, back.Select(x => text.Substring(x.Offset, x.Length)));
        Assert.Equal("So $x=1$", text[..back[2].Offset]);
    }
}
