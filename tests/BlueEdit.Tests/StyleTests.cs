using System.Text.Json;
using Xunit;

public class StyleTests
{
    static string Answer(string improved, params (string Original, string Replacement, string Reason)[] changes) =>
        JsonSerializer.Serialize(new
        {
            improved,
            changes = changes.Select(c => new { original = c.Original, replacement = c.Replacement, reason = c.Reason }),
        });

    [Fact]
    public void Changes_have_positions_and_reasons_and_nearby_ones_join()
    {
        const string text = "Мы провели анализ данных и по результатам анализа данных выяснили, что данные содержат ошибки.";
        var changes = StyleAdvisor.ChangesFrom(text,
            Answer("Мы проанализировали данные и выяснили, что данные содержат ошибки.",
                ("провели анализ данных и по результатам анализа данных", "проанализировали данные и", "повтор")), "ru-RU");
        var c = Assert.Single(changes);
        Assert.Equal("провели анализ данных и по результатам анализа данных", text.Substring(c.Offset, c.Length));
        Assert.Equal("проанализировали данные и", c.Replacement);
        Assert.Equal("повтор", c.Reason);
        Assert.Equal("Мы проанализировали данные и выяснили, что данные содержат ошибки.", StyleAdvisor.Apply(text, changes));
    }

    [Fact]
    public void A_reordered_sentence_is_one_change_not_pieces_that_only_work_together()
    {
        // the Obsidian plugin's case (#96): Qwen3 14B reordered the sentence; it came as five pieces several words apart
        const string text = "It should be noted that, in order to carry out the verification of the solution, the value $x=2$ was "
            + "substituted by us into the original equation, and a true equality was obtained as a result of this substitution.";
        const string improved = "It should be noted that, in order to verify the solution, we substituted the value $x=2$ into the "
            + "original equation and obtained a true equality.";
        var changes = StyleAdvisor.ChangesFrom(text, Answer(improved), "en-GB");
        Assert.Single(changes);
        Assert.Equal(improved, StyleAdvisor.Apply(text, changes));
    }

    [Theory]
    [InlineData("organisation analysed", "organization analyzed", true)]
    [InlineData("behaviour", "behavior", true)]
    [InlineData("centres", "centers", true)]
    [InlineData("travelled", "traveled", true)]
    [InlineData("defence", "defense", true)]
    [InlineData("programme", "program", true)]
    [InlineData("carried out an analysis of", "analysed", false)]          // a real rewording
    [InlineData("organisation", "organisation", false)]
    [InlineData("big behaviour", "large behaviour", false)]
    public void American_spellings_of_british_words_are_told_apart(string from, string to, bool americanised) =>
        Assert.Equal(americanised, StyleAdvisor.Americanised(from, to));

    [Fact]
    public void In_british_text_a_change_to_american_spelling_is_not_offered()
    {
        const string text = "The organisation analysed its members' behaviour, and in order to finish it stopped.";
        var changes = StyleAdvisor.ChangesFrom(text, Answer("The organization analyzed its members' behavior, and to finish it stopped."), "en-GB");
        var c = Assert.Single(changes);                                       // only the wording, not the spellings
        Assert.Equal("in order ", text.Substring(c.Offset, c.Length));        // "in order to finish" → "to finish"
        // American English is another matter: there the same change stays
        const string american = "The organization analyzed its members' behavior, and to finish it stopped.";
        Assert.Equal(american, StyleAdvisor.Apply(text, StyleAdvisor.ChangesFrom(text, Answer(american), "en-US")));
    }

    [Fact]
    public void The_academic_register_is_an_option_and_the_british_line_only_for_british()
    {
        Assert.DoesNotContain("informal", StyleAdvisor.DefaultFocuses);
        Assert.Contains("informal", StyleAdvisor.AllFocuses);
        Assert.DoesNotContain("conversational", StyleAdvisor.Instructions(StyleAdvisor.DefaultFocuses.ToArray(), "en-GB"), StringComparison.Ordinal);
        Assert.Contains("conversational", StyleAdvisor.Instructions(new[] { "informal" }, "en-GB"), StringComparison.Ordinal);
        Assert.Contains("Keep British spelling", StyleAdvisor.Instructions(StyleAdvisor.DefaultFocuses.ToArray(), "en-GB"), StringComparison.Ordinal);
        Assert.DoesNotContain("Keep British spelling", StyleAdvisor.Instructions(StyleAdvisor.DefaultFocuses.ToArray(), "ru-RU"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_that_drops_a_latex_command_is_not_offered()
    {
        const string text = @"In order to prove it, we follow~\cite{rudin}. It is \emph{very} clear.";
        var changes = StyleAdvisor.ChangesFrom(text, Answer(@"To prove it, we follow Rudin. It is very clear."), "en-GB");
        var c = Assert.Single(changes);                                       // only "In order to" → "To"
        Assert.StartsWith("In order", text.Substring(c.Offset, c.Length), StringComparison.Ordinal);
    }

    [Fact]
    public void Separate_changes_can_be_taken_one_by_one()
    {
        const string text = "It should be noted that the results were good. In order to finish, we stopped.";
        var changes = StyleAdvisor.ChangesFrom(text, Answer("The results were good. To finish, we stopped."), "en-GB");
        Assert.Equal(2, changes.Count);
        Assert.Equal("The results were good. In order to finish, we stopped.", StyleAdvisor.Apply(text, changes.Take(1)));
        Assert.Equal("It should be noted that the results were good. To finish, we stopped.", StyleAdvisor.Apply(text, changes.Skip(1)));
    }

    [Fact]
    public void A_good_paragraph_gives_no_changes()
    {
        Assert.Empty(StyleAdvisor.ChangesFrom("Вчера мы гуляли по парку.", Answer("Вчера мы гуляли по парку."), "ru-RU"));
        Assert.Empty(StyleAdvisor.ChangesFrom("Вчера  мы гуляли.\n", Answer("Вчера мы гуляли."), "ru-RU"));   // spaces only
    }

    [Fact]
    public void A_translation_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StyleAdvisor.ChangesFrom(
            "Нужно проверить документы до пятницы.", Answer("The documents must be checked by Friday."), "ru-RU"));
        Assert.Contains("another language", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_that_loses_markup_or_a_link_is_not_offered()
    {
        const string text = "См. **документацию** по адресу https://example.com/docs — там описан процесс, который является сложным процессом.";
        var changes = StyleAdvisor.ChangesFrom(text,
            Answer("Смотрите документацию по адресу https://example.com/docs — там описан сложный процесс."), "ru-RU");
        Assert.All(changes, c => Assert.DoesNotContain("**", text.Substring(c.Offset, c.Length), StringComparison.Ordinal));
        Assert.Contains(changes, c => c.Replacement.Contains("сложный процесс", StringComparison.Ordinal));
    }

    [Fact]
    public void Replacing_a_paragraph_is_one_undo_step()
    {
        var path = Path.Combine(Path.GetTempPath(), "be-style-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(path, "# T\n\nfirst line of\nthe paragraph\n\nafter\n");
        try
        {
            var e = new EditorView();
            e.Load(path);
            var p = e.ParagraphAt(3)!.Value;
            Assert.Equal((2, 3), p);
            Assert.Equal("first line of\nthe paragraph", e.ParagraphSource(p.First, p.Last));
            e.ReplaceParagraph(p.First, p.Last, "the first line\nof the paragraph", 4);
            Assert.Equal(new[] { "# T", "", "the first line", "of the paragraph", "", "after", "" }, e.Lines);
            Assert.Equal((2, 4), (e.Row, e.Col));
            e.Undo();
            Assert.Equal(new[] { "# T", "", "first line of", "the paragraph", "", "after", "" }, e.Lines);
        }
        finally { File.Delete(path); }
    }

    // A real model, when installed (the lightest one, as in LlmGrammarTests).
    [Fact]
    public void Real_model_suggests_a_wording_in_the_same_language()
    {
        const string phi = "microsoft_Phi-4-mini-instruct-Q4_K_M.gguf";
        if (LlmGrammarChecker.ServerFor(LlmGrammarChecker.DefaultDir, phi) is not { } server) return;
        using (server)
        {
            const string text = "Следует отметить, что в настоящее время имеет место тенденция к увеличению количества пользователей.";
            var changes = StyleAdvisor.Suggest(server, text, "ru-RU");        // throws if the model answered in English
            Assert.NotEmpty(changes);
            Assert.NotEqual(text, StyleAdvisor.Apply(text, changes));
        }
    }
}

public class StyleSettingsTests
{
    [Fact]
    public void Style_focus_is_a_setting_and_steers_the_instructions()
    {
        var (s, error) = Settings.Parse("""{ "styleFocus": ["officialese", "repetition"] }""");
        Assert.Null(error);
        Assert.Equal(new[] { "officialese", "repetition" }, s.StyleFocus);
        Assert.Equal(StyleAdvisor.DefaultFocuses, new Settings().StyleFocus);             // all but the academic register
        Assert.DoesNotContain("informal", new Settings().StyleFocus);
        Assert.NotNull(Settings.Parse("""{ "styleFocus": ["poetry"] }""").Error);

        var only = StyleAdvisor.Instructions(s.StyleFocus.ToArray(), "ru-RU");
        Assert.Contains("канцелярит", only, StringComparison.Ordinal);
        Assert.DoesNotContain("passive", only, StringComparison.Ordinal);
        Assert.Contains("The paragraph is in Russian", only, StringComparison.Ordinal);
        Assert.Contains("passive", StyleAdvisor.Instructions(Array.Empty<string>(), "en-GB"), StringComparison.Ordinal);   // none = all
    }
}
