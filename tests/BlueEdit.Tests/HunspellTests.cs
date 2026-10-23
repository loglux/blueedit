using Xunit;

public sealed class HunspellTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-hunspell-" + Guid.NewGuid().ToString("N"));

    public HunspellTests()
    {
        Directory.CreateDirectory(_dir);
        // a tiny dictionary in Hunspell's format: "cat" takes the plural suffix S
        File.WriteAllText(Path.Combine(_dir, "xx_YY.aff"), "SET UTF-8\nTRY aotcdgs\n\nSFX S Y 1\nSFX S 0 s .\n");
        File.WriteAllText(Path.Combine(_dir, "xx_YY.dic"), "3\ncat/S\ndog/S\nкот\n");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static HunspellSpell Loaded(HunspellSpell s)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!s.Loaded && sw.ElapsedMilliseconds < 10000) Thread.Sleep(10);
        Assert.True(s.Loaded);
        return s;
    }

    [Fact]
    public void Words_affixes_and_suggestions_from_a_dictionary()
    {
        var s = Loaded(new HunspellSpell(null, new[] { "xx-YY" }, new[] { _dir }));
        Assert.True(s.Available);
        Assert.Equal(new[] { "xx-YY" }, s.Languages);
        Assert.Null(s.Error);
        Assert.True(s.IsCorrect("cats"));
        Assert.True(s.IsCorrect("кот"));
        Assert.False(s.IsCorrect("cta"));
        // Hunspell's suggestion search has a time limit (a quarter of a second): the first call, its code still being
        // compiled, came back empty once while the other tests loaded the computer, so it gets a few tries
        var got = new List<string>();
        for (int i = 0; i < 5 && !got.Contains("cat"); i++) got = s.Suggest("cta");
        Assert.Contains("cat", got);
    }

    [Fact]
    public void Added_and_ignored_words_count_as_correct_and_bump_the_version()
    {
        var user = new UserDictionary(Path.Combine(_dir, "dictionary.txt"));
        var s = Loaded(new HunspellSpell(user, new[] { "xx-YY" }, new[] { _dir }));
        int v = s.Version;
        s.AddToDictionary("BlueEdit");
        s.Ignore("qwz");
        Assert.True(s.IsCorrect("BlueEdit"));
        Assert.True(s.IsCorrect("qwz"));
        Assert.True(s.Version > v);
        Assert.Contains("BlueEdit", File.ReadAllText(Path.Combine(_dir, "dictionary.txt")));
    }

    [Fact]
    public void A_language_without_a_dictionary_is_reported_and_a_bare_language_finds_its_country()
    {
        var s = Loaded(new HunspellSpell(null, new[] { "xx", "zz-ZZ" }, new[] { _dir }));
        Assert.Equal(new[] { "xx" }, s.Languages);
        Assert.Contains("zz-ZZ", s.Error);
        Assert.Equal(new[] { "xx-YY" }, HunspellSpell.InstalledTags(new[] { _dir }));
        var none = new HunspellSpell(null, new[] { "zz-ZZ" }, new[] { _dir });
        Assert.False(none.Available);
        Assert.True(none.IsCorrect("anything"));          // nothing to check with: nothing is underlined
    }

    [Fact]
    public void A_word_in_letters_no_spelling_language_is_written_in_is_not_a_mistake()
    {
        // only a Latin-letter language: Russian words are unchecked, not all underlined (a server with en-US alone)
        var latin = Loaded(new HunspellSpell(null, new[] { "xx-YY" }, new[] { _dir }));
        Assert.True(latin.IsCorrect("мор"));
        Assert.False(latin.IsCorrect("cta"));
        Assert.False(latin.IsCorrect("cаt"));                // a Cyrillic "а" among Latin letters is still checked
        // the same file as a Cyrillic language's: the word is read, and is not in it
        File.Copy(Path.Combine(_dir, "xx_YY.aff"), Path.Combine(_dir, "ru_RU.aff"));
        File.Copy(Path.Combine(_dir, "xx_YY.dic"), Path.Combine(_dir, "ru_RU.dic"));
        var cyrillic = Loaded(new HunspellSpell(null, new[] { "ru-RU" }, new[] { _dir }));
        Assert.False(cyrillic.IsCorrect("мор"));
        Assert.True(cyrillic.IsCorrect("кот"));
    }

    [Theory]
    [InlineData("мир", "en-US", false)]
    [InlineData("мир", "en-US uk-UA", true)]
    [InlineData("world", "ru", false)]
    [InlineData("café", "fr-FR", true)]
    [InlineData("λόγος", "de-DE el-GR", true)]
    [InlineData("пpивет", "en-US", true)]                    // mixed alphabets: checked, never skipped
    [InlineData("中文", "en-US", false)]
    public void Which_words_the_spelling_languages_can_read(string word, string languages, bool readable) =>
        Assert.Equal(readable, Scripts.Readable(languages.Split(' '), word));

    [Fact]
    public void System_dictionaries_check_russian_and_english_where_installed()
    {
        if (!File.Exists("/usr/share/hunspell/ru_RU.dic") || !File.Exists("/usr/share/hunspell/en_GB.dic")) return;
        var s = Loaded(new HunspellSpell(null, new[] { "ru-RU", "en-GB" }));
        Assert.True(s.IsCorrect("привет"));
        Assert.True(s.IsCorrect("colour"));
        Assert.False(s.IsCorrect("превет"));
        // WeCantSpell gives suggestions a time limit (TimeLimitSuggestGlobal): the first, cold call on a loaded computer
        // can stop before it reaches "привет" (seen once on the Linux CI runner), so a few tries
        Assert.Contains(Enumerable.Range(0, 5).Select(_ => s.Suggest("превет")), suggested => suggested.Contains("привет"));
    }
}
