using Xunit;

/// <summary>Which language a paragraph is checked in (ParagraphLanguage.cs, EditorView.Language.cs, #40).</summary>
public sealed class ParagraphLanguageTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-lang-" + Guid.NewGuid().ToString("N"));
    public ParagraphLanguageTests()
    {
        Directory.CreateDirectory(_dir);
        Assert.True(ParagraphLanguage.Load());
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static readonly string[] RussianEnglish = ["en-GB", "ru-RU"];
    static readonly string[] EnglishGerman = ["en-GB", "de-DE"];

    [Theory]
    [InlineData("Мы был в магазине.", "ru-RU")]
    [InlineData("He go to school.", "en-GB")]
    [InlineData("Текст с word внутри", "ru-RU")]
    public void Russian_and_English_are_told_by_their_letters(string text, string language) =>
        Assert.Equal(language, ParagraphLanguage.Choose(text, RussianEnglish));

    [Theory]
    [InlineData("Die Liebe ist nur eine Erfindung, sagte er mir gestern Abend.", "de-DE")]
    [InlineData("Love is only an invention, he told me yesterday evening.", "en-GB")]
    public void Languages_of_one_script_are_told_by_the_model(string text, string language) =>
        Assert.Equal(language, ParagraphLanguage.Choose(text, EnglishGerman));

    [Fact]
    public void A_short_line_takes_the_documents_main_language()
    {
        Assert.Equal("de-DE", ParagraphLanguage.Choose("Einleitung", EnglishGerman, ["de-DE", "en-GB"]));
        Assert.Equal("en-GB", ParagraphLanguage.Choose("Einleitung", EnglishGerman, ["en-GB"]));
    }

    [Fact]
    public void A_letter_a_language_does_not_have_rules_it_out()
    {
        // "ы" is not Bulgarian: fastText alone can call a short Russian line Bulgarian
        Assert.Equal("ru-RU", ParagraphLanguage.DetectAmong("Вы были там?", ["bg-BG", "ru-RU"]));
        Assert.Equal("uk-UA", ParagraphLanguage.DetectAmong("Їжак і кіт", ["ru-RU", "uk-UA"]));
    }

    [Fact]
    public void Text_in_a_script_no_listed_language_has_is_guessed()
    {
        Assert.Equal("ru", ParagraphLanguage.Choose("Мы были в магазине вчера вечером и купили хлеб.", ["en-GB"]));
        Assert.Equal("de", ParagraphLanguage.Choose("Die Liebe ist nur eine Erfindung, sagte er mir gestern Abend.", []));
        // too short to guess from: left to the checkers
        Assert.Equal("auto", ParagraphLanguage.Choose("Hallo", []));
    }

    [Fact]
    public void A_documents_languages_are_those_of_enough_of_its_paragraphs()
    {
        var long1 = new string('a', 30);
        var paragraphs = new[] { (long1, "de"), (long1, "de"), (long1, "de"), (long1, "en"), (long1, "en"), (long1, "pt"), ("kurz", "fr") };
        Assert.Equal(["de", "en"], ParagraphLanguage.DocumentLanguages(paragraphs));
        Assert.Equal(["en"], ParagraphLanguage.DocumentLanguages([(long1, "en")]));
    }

    [Theory]
    [InlineData("---\nlanguage: de\n---\ntext", new[] { "de" })]
    [InlineData("---\ntitle: x\nlang: ru, en\n---\n", new[] { "ru", "en" })]
    [InlineData("---\nlanguage: [\"de\", fr]\n---\n", new[] { "de", "fr" })]
    [InlineData("---\nlanguage:\n  - de\n  - en\n---\n", new[] { "de", "en" })]
    [InlineData("language: de\n", new string[0])]
    public void A_document_states_its_languages_in_its_front_matter(string text, string[] languages) =>
        Assert.Equal(languages, ParagraphLanguage.Stated(text.Split('\n')));

    EditorView Open(string name, string text, params string[] languages)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        var e = new EditorView { GrammarLanguages = languages };
        e.Load(path);
        return e;
    }

    [Fact]
    public void The_editor_takes_the_stated_language_spelled_as_a_spelling_language()
    {
        var e = Open("a.md", "---\nlanguage: de\n---\n\nHello there, this is written in English.\n", "en-GB", "de-DE");
        Assert.Equal("de-DE", e.LanguageOf("Hello there, this is written in English."));
    }

    [Fact]
    public void In_the_editor_a_heading_takes_the_language_of_the_documents_paragraphs()
    {
        var e = Open("b.md", "# Einleitung\n\nDie Liebe ist nur eine Erfindung, sagte er mir gestern Abend.\n\n" +
            "Wir gehen morgen früh zusammen in den großen Park am Fluss.\n\nDas Wetter war die ganze Woche sehr schön und warm.\n",
            "en-GB", "de-DE");
        Assert.Equal(["de-DE"], e.DocumentLanguages(e.GrammarLanguages));
        Assert.Equal("de-DE", e.LanguageOf("Einleitung"));
    }

    [Fact]
    public void Russian_and_English_never_need_the_whole_document() =>
        Assert.False(ParagraphLanguage.NeedsDocument(RussianEnglish));
}
