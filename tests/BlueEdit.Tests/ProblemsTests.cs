using Tui;
using Xunit;

public sealed class ProblemsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-probs-" + Guid.NewGuid().ToString("N"));
    public ProblemsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static readonly Spell Windows = new("en-GB", "ru-RU");

    sealed class Grammar1(string phrase) : IGrammarChecker
    {
        public string Name => "fake";
        public bool Available => true;
        public string? Status => null;
        public int Version => 1;
        public void Dispose() { }
        public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true)
        {
            int at = paragraph.IndexOf(phrase, StringComparison.Ordinal);
            return at < 0 ? Array.Empty<GrammarIssue>() : new[] { new GrammarIssue(at, phrase.Length, "Agreement", new[] { "x" }, "F", "Fake") };
        }
    }

    (EditorView E, Screen S) Show(string text, IGrammarChecker? g = null, string name = "a.txt")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView { Spell = Windows, SpellOn = true };
        e.Load(p);
        if (g != null) { e.Grammar = g; e.GrammarOn = true; }
        var s = new Screen(80, 10);
        e.SetArea(new Rect(0, 0, 80, 10));
        e.Draw(s);
        return (e, s);
    }

    static string Underlined(Screen s, int y) =>
        new string(Enumerable.Range(0, s.Width).Select(x => s.StyleAt(x, y).Attr.HasFlag(Attr.Underline) ? '^' : ' ').ToArray()).TrimEnd();

    // ---------- mixed layouts ----------
    [Theory]
    [InlineData("пpивет", "привет")]          // a Latin p in place of the Cyrillic р
    [InlineData("рrивет", null)]              // a Latin r looks like no Cyrillic letter: flagged, not repairable
    [InlineData("Cобака", "Собака")]          // a Latin C at the start
    [InlineData("hеllo", "hello")]            // a Cyrillic е in an English word
    [InlineData("hжllo", null)]               // ж has no Latin twin: a real mix, nothing to repair
    public void Lookalike_letters_are_put_into_one_alphabet(string word, string? fixedWord)
    {
        Assert.True(Lookalike.IsMixed(word));
        Assert.Equal(fixedWord, Lookalike.Fix(word));
    }

    [Fact]
    public void Plain_words_are_not_mixed()
    {
        Assert.False(Lookalike.IsMixed("привет"));
        Assert.False(Lookalike.IsMixed("hello"));
    }

    [Fact]
    public void A_mixed_word_is_underlined()
    {
        if (!Windows.Available) return;
        var (_, s) = Show("Это рrивет и привет\n");
        Assert.Equal("    ^^^^^^", Underlined(s, 0));
    }

    // ---------- addresses and paths ----------
    [Fact]
    public void Addresses_mail_and_paths_are_not_spell_checked()
    {
        if (!Windows.Available) return;
        var (_, s) = Show("see https://exampel.com/docz name@mial.ru C:\\Usrs\\tmpp docz/nots.md speling\n");
        var marks = Underlined(s, 0);
        Assert.Equal(new string(' ', marks.Length - 7) + "^^^^^^^", marks);    // only the last word
    }

    // ---------- next problem ----------
    [Fact]
    public void Next_problem_goes_through_typos_and_grammar_in_order_and_round()
    {
        if (!Windows.Available) return;
        var (e, _) = Show("one speling here\nthey was ok\nanother mistaek\n", new Grammar1("they was"));
        Assert.Equal("Spelling (Windows): speling", e.GoToNextProblem());
        Assert.Equal((0, 11), (e.Row, e.Col));                    // selected, caret at its end
        Assert.Equal("Grammar (Fake): Agreement", e.GoToNextProblem());
        Assert.Equal("Spelling (Windows): mistaek", e.GoToNextProblem());
        Assert.Equal("Spelling (Windows): speling", e.GoToNextProblem());   // round again
        Assert.Equal("Spelling (Windows): mistaek", e.GoToNextProblem(backward: true));
        Assert.Equal("Grammar (Fake): Agreement", e.GoToNextProblem(backward: true));
    }

    [Fact]
    public void No_problems_says_so()
    {
        if (!Windows.Available) return;
        var (e, _) = Show("all fine here\n");
        Assert.Null(e.GoToNextProblem());
    }
}
