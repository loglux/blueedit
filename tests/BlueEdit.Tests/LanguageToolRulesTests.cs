using System.Diagnostics;
using Xunit;

/// <summary>LanguageTool's rules in the settings and findings never shown (#69).</summary>
// one at a time with the other class that starts a real LanguageTool: two Java servers at once slowed a loaded
// runner, and each class's checks are about its own server
[Collection("Real LanguageTool")]
public sealed class LanguageToolRulesTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-ltrules-" + Guid.NewGuid().ToString("N"));
    public LanguageToolRulesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Typos_stays_on_but_the_dictionary_rules_are_left_out_and_the_old_default_moves_on()
    {
        Assert.True(LanguageToolOptions.IsDictionaryRule("MORFOLOGIK_RULE_EN_GB"));
        Assert.True(LanguageToolOptions.IsDictionaryRule("MORFOLOGIK_RULE_RU_RU"));
        Assert.True(LanguageToolOptions.IsDictionaryRule("GERMAN_SPELLER_RULE"));
        Assert.True(LanguageToolOptions.IsDictionaryRule("HUNSPELL_NO_SUGGEST_RULE"));
        Assert.False(LanguageToolOptions.IsDictionaryRule("COMPARISONS_THEN"));                 // then/than: TYPOS, kept
        Assert.False(LanguageToolOptions.IsDictionaryRule("EN_CONTRACTION_SPELLING"));          // dont → don't
        var (old, _) = Settings.Parse("""{ "languageToolDisabledCategories": ["TYPOS", "BRE_STYLE_OXFORD_SPELLING"] }""");
        Assert.Equal(LanguageToolOptions.DefaultDisabledCategories, old.LanguageToolDisabledCategories);
        var (mine, _) = Settings.Parse("""{ "languageToolDisabledCategories": ["TYPOS"] }""");     // the user's own choice stays
        Assert.Equal(new[] { "TYPOS" }, mine.LanguageToolDisabledCategories);
    }

    [Fact]
    public void The_memory_is_768m_by_default_a_setting_otherwise_and_part_of_the_options_key()
    {
        Assert.Equal("768m", new Settings().LanguageToolMemory);
        var (s, error) = Settings.Parse("""{ "languageToolMemory": "1G" }""");
        Assert.Null(error);
        Assert.Equal("1g", s.LanguageToolMemory);
        Assert.NotEqual(new Settings().LanguageToolOptions.Key, s.LanguageToolOptions.Key);     // a change restarts the server
        var (_, bad) = Settings.Parse("""{ "languageToolMemory": "lots" }""");
        Assert.NotNull(bad);
    }

    [Fact]
    public void Defaults_are_the_rules_that_were_fixed_in_the_code()
    {
        var s = new Settings();
        Assert.Equal(new[] { "WHITESPACE_RULE", "CONSECUTIVE_SPACES", "COMMA_PARENTHESIS_WHITESPACE", "UPPERCASE_SENTENCE_START" }, s.LanguageToolDisabledRules);
        Assert.Equal(new[] { "BRE_STYLE_OXFORD_SPELLING" }, s.LanguageToolDisabledCategories);
        Assert.Empty(s.LanguageToolEnabledRules);
        Assert.Equal("default", s.LanguageToolLevel);
        Assert.Empty(s.IgnoredRules);
        Assert.Equal(LanguageToolOptions.Default.Key, s.LanguageToolOptions.Key);
    }

    [Fact]
    public void Rules_levels_and_ignored_findings_are_read_and_written_back()
    {
        var (s, error) = Settings.Parse("""
            { "languageToolDisabledRules": ["WHITESPACE_RULE", "SENT_START_CONJUNCTIVE_LINKING_ADVERB_COMMA"],
              "languageToolDisabledCategories": [], "languageToolEnabledRules": ["EN_QUOTES"], "languageToolLevel": "Picky",
              "ignoredRules": ["EN_A_VS_AN", "длинна → длина", "say \"hi\""] }
            """);
        Assert.Null(error);
        Assert.Equal(new[] { "WHITESPACE_RULE", "SENT_START_CONJUNCTIVE_LINKING_ADVERB_COMMA" }, s.LanguageToolDisabledRules);
        Assert.Empty(s.LanguageToolDisabledCategories);
        Assert.Equal("picky", s.LanguageToolLevel);
        var back = Settings.Parse(Settings.Template(s)).Settings;
        Assert.Equal(s.LanguageToolOptions.Key, back.LanguageToolOptions.Key);
        Assert.Equal(new[] { "EN_A_VS_AN", "длинна → длина", "say \"hi\"" }, back.IgnoredRules);
    }

    [Theory]
    [InlineData("""{ "languageToolLevel": "strict" }""")]
    [InlineData("""{ "languageToolDisabledRules": "WHITESPACE_RULE" }""")]
    [InlineData("""{ "ignoredRules": [1, 2] }""")]
    public void A_bad_value_is_reported(string json) => Assert.NotNull(Settings.Parse(json).Error);

    sealed class Fixed(params GrammarIssue[] issues) : IGrammarChecker
    {
        public string Name => "f";
        public bool Available => true;
        public string? Status => null;
        public int Version => 1;
        public void Dispose() { }
        public IReadOnlyList<GrammarIssue>? Issues(string p, string l, bool q = true) => issues;
    }

    [Fact]
    public void A_rule_never_shown_is_not_underlined_and_the_others_are()
    {
        var path = Path.Combine(_dir, "a.md");
        File.WriteAllText(path, "Hence the the answer.\n");
        var e = new EditorView();
        e.Load(path);
        e.Grammar = new Fixed(new GrammarIssue(0, 5, "comma", new[] { "Hence," }, "SENT_START_CONJUNCTIVE_LINKING_ADVERB_COMMA", "LanguageTool"),
                              new GrammarIssue(6, 7, "repeat", new[] { "the" }, "ENGLISH_WORD_REPEAT_RULE", "LanguageTool"));
        e.GrammarOn = true;
        e.IgnoredRules = new HashSet<string> { "SENT_START_CONJUNCTIVE_LINKING_ADVERB_COMMA" };
        e.GoToLine(1);
        Assert.Null(e.GrammarIssuesAtCaret());                                      // "Hence" is not shown
        for (int i = 0; i < 7; i++) e.HandleKey(new Tui.KeyInput(Tui.Key.Right));
        Assert.Equal("ENGLISH_WORD_REPEAT_RULE", Assert.Single(e.GrammarIssuesAtCaret()!.Value.Issues).Rule);
    }

    static IReadOnlyList<GrammarIssue> Ask(LanguageToolChecker lt, string text)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(90))
        {
            if (!lt.Available && lt.Status != "starting") Assert.Fail(lt.Status);
            if (lt.Issues(text, "en-GB") is { } found) return found;
            Thread.Sleep(50);
        }
        throw new TimeoutException("LanguageTool did not answer");
    }

    [Fact]
    public void Real_languagetool_switches_a_rule_off_and_tells_its_category()
    {
        if (!LanguageToolChecker.InstalledIn(LanguageToolChecker.DefaultHome)) return;
        const string text = "We saw the the cat.";
        using (var lt = new LanguageToolChecker(LanguageToolChecker.DefaultHome))
        {
            var found = Assert.Single(Ask(lt, text), x => x.Rule == "ENGLISH_WORD_REPEAT_RULE");
            Assert.False(string.IsNullOrEmpty(found.Category));
        }
        var off = LanguageToolOptions.Default with { DisabledRules = LanguageToolOptions.DefaultDisabledRules.Append("ENGLISH_WORD_REPEAT_RULE").ToArray() };
        using (var lt = new LanguageToolChecker(LanguageToolChecker.DefaultHome, off))
            Assert.DoesNotContain(Ask(lt, text), x => x.Rule == "ENGLISH_WORD_REPEAT_RULE");
    }
}
