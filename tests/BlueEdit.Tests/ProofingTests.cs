using System.Diagnostics;
using Xunit;

public sealed class ProofingTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-proof-" + Guid.NewGuid().ToString("N"));
    public ProofingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    string DictPath => Path.Combine(_dir, "dictionary.txt");

    // ---------- the editor's own dictionary ----------
    [Fact]
    public void User_dictionary_keeps_words_in_a_file()
    {
        var d = new UserDictionary(DictPath);
        d.Add("ConPTY"); d.Add("blueedit"); d.Add("ConPTY");
        var again = new UserDictionary(DictPath);
        Assert.True(again.Contains("ConPTY"));
        Assert.False(again.Contains("conpty"));                       // added with capitals: exactly so
        Assert.True(again.Contains("blueedit") && again.Contains("Blueedit"));   // lower case: any capitalisation
        Assert.Equal(2, File.ReadAllLines(DictPath).Length);
    }

    [Fact]
    public void Comments_and_blank_lines_in_the_file_are_skipped()
    {
        File.WriteAllLines(DictPath, new[] { "# my words", "", "  qwzxv  " });
        var d = new UserDictionary(DictPath);
        Assert.True(d.Contains("qwzxv"));
        Assert.Single(d.Words);
    }

    // ---------- Windows provider ----------
    [Fact]
    public void Windows_provider_uses_the_editors_dictionary_and_session_ignores()
    {
        var sp = new Spell(new UserDictionary(DictPath), "en-GB");
        if (!sp.Available) return;
        Assert.False(sp.IsCorrect("qwzxvb"));
        int v = sp.Version;
        sp.AddToDictionary("qwzxvb");
        Assert.True(sp.IsCorrect("qwzxvb"));
        Assert.True(sp.Version > v);
        Assert.Contains("qwzxvb", File.ReadAllText(DictPath), StringComparison.Ordinal);
        Assert.False(sp.IsCorrect("zzqqxx"));
        sp.Ignore("zzqqxx");
        Assert.True(sp.IsCorrect("zzqqxx"));
        Assert.True(new Spell(new UserDictionary(DictPath), "en-GB").IsCorrect("qwzxvb"));   // the dictionary stays
        Assert.False(new Spell(new UserDictionary(DictPath), "en-GB").IsCorrect("zzqqxx"));  // ignoring does not
    }

    [Fact]
    public void Spell_provider_setting()
    {
        Assert.Equal("windows", new Settings().SpellProvider);
        Assert.Equal("office", Settings.Parse("{ \"spellProvider\": \"Office\" }").Settings.SpellProvider);
        Assert.NotNull(Settings.Parse("{ \"spellProvider\": \"aspell\" }").Error);
    }

    // ---------- Office provider (needs Microsoft Word) ----------
    static bool? Settle(ISpellProvider sp, string word)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (sp.IsCorrect(word) is bool b) return b;
            Thread.Sleep(50);
        }
        return null;
    }

    [Fact]
    public void Office_answers_in_the_background_and_suggests()
    {
        if (!OfficeSpell.Installed) return;
        int wordsBefore = Process.GetProcessesByName("WINWORD").Length;
        using (var sp = new OfficeSpell(new UserDictionary(DictPath), "en-GB", "ru-RU"))
        {
            Assert.Null(sp.IsCorrect("превет"));                       // not known yet: asked in the background
            Assert.Equal(false, Settle(sp, "превет"));
            Assert.Equal(true, Settle(sp, "привет"));
            Assert.Equal(true, Settle(sp, "colour"));
            Assert.Equal(false, Settle(sp, "recieve"));
            Assert.Null(sp.Error);
            var suggestions = sp.Suggest("превет");
            Assert.True(suggestions.Contains("привет"), "suggested: " + string.Join(", ", suggestions));
            sp.AddToDictionary("превет");
            Assert.Equal(true, sp.IsCorrect("превет"));
        }
        // the Word it started has gone again (one the user had open would have been left alone)
        var sw = Stopwatch.StartNew();
        while (Process.GetProcessesByName("WINWORD").Length > wordsBefore && sw.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(200);
        Assert.Equal(wordsBefore, Process.GetProcessesByName("WINWORD").Length);
    }

    [Fact]
    public void Office_checks_many_words_in_one_go()
    {
        if (!OfficeSpell.Installed) return;
        using var sp = new OfficeSpell(null, "en-GB");
        var words = Enumerable.Range(0, 200).Select(i => i % 2 == 0 ? "house" + new string('s', i % 7) : "garden").Distinct().ToList();
        Settle(sp, "warmup");
        var sw = Stopwatch.StartNew();
        foreach (var w in words) sp.IsCorrect(w);
        foreach (var w in words) Assert.NotNull(Settle(sp, w));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"{words.Count} words took {sw.Elapsed.TotalSeconds:F1} s");
    }
}
