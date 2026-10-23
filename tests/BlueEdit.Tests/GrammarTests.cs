using System.Diagnostics;
using Tui;
using Xunit;

// one at a time with the other class that starts a real LanguageTool: two Java servers at once slowed a loaded
// runner, and each class's checks are about its own server
[Collection("Real LanguageTool")]
public sealed class GrammarTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-gram-" + Guid.NewGuid().ToString("N"));
    public GrammarTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>Finds a fixed phrase, instantly, and remembers what it was asked.</summary>
    sealed class FakeChecker(string phrase, string message = "Wrong form", params string[] fixes) : IGrammarChecker
    {
        public readonly List<(string Text, string Lang, bool Queue)> Asked = new();
        public string Name => "fake";
        public bool Available => true;
        public string? Status => null;
        public int Version => 1;
        public void Dispose() { }
        public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true)
        {
            Asked.Add((paragraph, language, queue));
            int at = paragraph.IndexOf(phrase, StringComparison.Ordinal);
            return at < 0 ? Array.Empty<GrammarIssue>() : new[] { new GrammarIssue(at, phrase.Length, message, fixes, "FAKE") };
        }
    }

    (EditorView E, Screen S) Show(string text, IGrammarChecker g, string name = "a.md", MdView view = MdView.Reading)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView { DefaultMdView = view };
        e.Load(p);
        e.Grammar = g; e.GrammarOn = true; e.GrammarLanguages = new[] { "en-GB", "ru-RU" };
        var s = new Screen(60, 12);
        e.SetArea(new Rect(0, 0, 60, 12));
        e.ApplyTheme(Themes.Get("dark"));
        e.Draw(s);
        return (e, s);
    }

    static string Underlined(Screen s, int y) =>
        new string(Enumerable.Range(0, s.Width).Select(x => s.StyleAt(x, y).Attr.HasFlag(Attr.Underline) ? '^' : ' ').ToArray()).TrimEnd();

    // ---------- what the checker is given ----------
    [Theory]
    [InlineData("**bold** and `code`", "  bold   and  code ")]
    [InlineData("# Heading here", "  Heading here")]
    [InlineData("- [link](http://x.y) text", "   link              text")]
    [InlineData("plain text", "plain text")]
    public void Markup_becomes_spaces_of_the_same_length(string line, string plain)
    {
        Assert.Equal(plain, Prose.Plain(line, markdown: true));
        Assert.Equal(line.Length, Prose.Plain(line, markdown: true).Length);
    }

    [Fact]
    public void Paragraphs_skip_code_tables_and_split_at_items_and_headings()
    {
        var md = "# Title\nfirst line\nsecond line\n\n- item one\n- item two\n  continued\n\n```cs\nint x;\n```\n| a | b |\n|---|---|\n| 1 | 2 |\n";
        var (e, _) = Show(md, new FakeChecker("zz"));
        Assert.Equal((0, 0), e.ParagraphAt(0));          // a heading alone
        Assert.Equal((1, 2), e.ParagraphAt(2));
        Assert.Equal((4, 4), e.ParagraphAt(4));          // each list item on its own
        Assert.Equal((5, 6), e.ParagraphAt(6));          // with its continuation line
        Assert.Null(e.ParagraphAt(9));                   // code
        Assert.Null(e.ParagraphAt(12));                  // table
        Assert.Null(e.ParagraphAt(3));                   // blank
    }

    // ---------- showing and fixing ----------
    [Fact]
    public void Findings_are_underlined_in_the_grammar_colour_across_lines()
    {
        var (_, s) = Show("intro\n\nThey was here and\nwent home.\n", new FakeChecker("was here and\nwent"));
        Assert.Equal("     ^^^^^^^^^^^^", Underlined(s, 2));
        Assert.Equal("^^^^", Underlined(s, 3));
        Assert.Equal(Themes.Get("dark").Grammar, s.StyleAt(6, 2).Fg);
    }

    [Fact]
    public void F7_fix_replaces_the_finding_and_is_one_undo_step()
    {
        var (e, _) = Show("x\n\nWe was here.\n", new FakeChecker("was", "Use were", "were"), view: MdView.Source);
        e.GoToLine(3, 5);
        var hit = e.GrammarIssueAtCaret();
        Assert.NotNull(hit);
        Assert.Equal("Use were", hit!.Value.Issue.Message);
        e.FixGrammar(hit.Value.Issue, hit.Value.First, "were");
        Assert.Equal("We were here.", e.Lines[2]);
        e.Undo();
        Assert.Equal("We was here.", e.Lines[2]);
    }

    /// <summary>One general rule (as LanguageTool's are) for every "was" and "then".</summary>
    sealed class OneRuleChecker : IGrammarChecker
    {
        public string Name => "one rule";
        public bool Available => true;
        public string? Status => null;
        public int Version => 1;
        public void Dispose() { }
        public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true) =>
            System.Text.RegularExpressions.Regex.Matches(paragraph, @"\b(was|then)\b")
                .Select(m => new GrammarIssue(m.Index, m.Length, "Check this", Array.Empty<string>(), "ONE_RULE_129")).ToList();
    }

    [Fact]
    public void Ignore_hides_the_rule_at_that_text_not_everywhere()
    {
        // #129: Ignore on a LanguageTool finding hid its rule everywhere
        var (e, _) = Show("We was here then.\n\nThey was there.\n", new OneRuleChecker(), view: MdView.Source);
        e.GoToLine(1, 4);
        var hit = e.GrammarIssuesAtCaret()!.Value;
        e.IgnoreHere(hit.Issues, hit.First, hit.Last);
        Assert.Null(e.GrammarIssuesAtCaret());                         // "was" here
        e.GoToLine(3, 7);
        Assert.Null(e.GrammarIssuesAtCaret());                         // and "was" in another paragraph
        e.GoToLine(1, 14);
        Assert.Equal("then", e.Lines[0].Substring(e.GrammarIssuesAtCaret()!.Value.Issues[0].Offset, 4));   // the same rule elsewhere stays
    }

    [Fact]
    public void A_paragraph_being_typed_is_not_sent_until_it_has_been_quiet()
    {
        var fake = new FakeChecker("zz");
        var (e, s) = Show("Some text here\n", fake, view: MdView.Source);
        fake.Asked.Clear();
        e.HandleKey(new KeyInput(Key.Character, 'a'));
        e.Draw(s);
        Assert.All(fake.Asked, a => Assert.False(a.Queue));      // asked only for what is known, not queued
    }

    [Fact]
    public void Code_files_get_no_grammar()
    {
        var fake = new FakeChecker("was");
        Show("// We was here\nint x;\n", fake, name: "a.cs");
        Assert.Empty(fake.Asked);
    }

    // ---------- the real LanguageTool (when installed) ----------
    [Fact]
    public void LanguageTool_finds_russian_and_english_grammar_and_stops_with_the_editor()
    {
        if (!LanguageToolChecker.InstalledIn(LanguageToolChecker.DefaultHome)) return;
        int? pid;
        using (var lt = new LanguageToolChecker(LanguageToolChecker.DefaultHome))
        {
            // each step says what went wrong: a failure on a loaded runner showed only "[FAIL]" (11 October 2026)
            IReadOnlyList<GrammarIssue> Wait(string text, string lang)
            {
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed < TimeSpan.FromSeconds(90))
                {
                    if (lt.Issues(text, lang) is { } r) return r;
                    Thread.Sleep(100);
                }
                Assert.Fail($"no answer for \"{text}\" ({lang}) in 90 s; status: {lt.Status ?? "none"}");
                return null!;
            }
            static string Rules(IReadOnlyList<GrammarIssue> found) => found.Count == 0 ? "nothing" : string.Join(", ", found.Select(i => i.Rule));
            var ru = Wait("Мы был в магазине.", "ru-RU");
            Assert.True(ru.Any(i => i.Rule == "RU_VERB_CONJUGATION"), "Russian: found " + Rules(ru));
            var en = Wait("He go to school every days.", "en-GB");
            Assert.True(en.Any(i => i.Replacements.Contains("goes")), "English: found " + Rules(en));
            var clean = Wait("I like the color of this.", "en-GB");                // spelling is not its job here
            Assert.True(clean.Count == 0, "a correct sentence: found " + Rules(clean));
            Assert.True(lt.Status == null, "status: " + lt.Status);
            pid = lt.ServerProcessId;
        }
        // its own server is gone: counting java processes failed when another test's LanguageTool was running
        Assert.NotNull(pid);
        var sw2 = Stopwatch.StartNew();
        while (Running(pid!.Value) && sw2.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(200);
        Assert.False(Running(pid.Value), $"the LanguageTool server (process {pid}) still runs after the editor stopped it");

        static bool Running(int id)
        {
            try { using var p = Process.GetProcessById(id); return !p.HasExited; }
            catch (ArgumentException) { return false; }
        }
    }
}
