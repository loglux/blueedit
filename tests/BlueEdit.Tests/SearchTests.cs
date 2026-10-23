using System.Text;
using Tui;
using Xunit;

public sealed class SearchTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-search-" + Guid.NewGuid().ToString("N"));
    public SearchTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string text, string name = "a.txt")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView(); e.Load(p);
        return e;
    }

    static string Sel(EditorView e) => e.SelectionOnOneLine ?? "";
    static (int, int) At(EditorView e) => (e.Row, e.Col);

    // ---------- find ----------
    [Fact]
    public void Plain_find_ignores_case_and_moves_on_with_each_call()
    {
        var e = Open("Cat cat\nCAT\n");
        var o = new SearchOptions("cat");
        Assert.True(e.Find(o), "first"); Assert.Equal((0, 3), At(e)); Assert.Equal("Cat", Sel(e));
        Assert.True(e.FindNext(), "second"); Assert.Equal((0, 7), At(e));
        Assert.True(e.FindNext(), "third"); Assert.Equal((1, 3), At(e));
        Assert.False(e.LastFindWrapped);
        Assert.True(e.FindNext(), "wrap"); Assert.Equal((0, 3), At(e));
        Assert.True(e.LastFindWrapped);
    }

    [Fact]
    public void Match_case()
    {
        var e = Open("Cat cat\n");
        Assert.True(e.Find(new SearchOptions("cat", MatchCase: true)));
        Assert.Equal((0, 7), At(e));
    }

    [Theory]
    [InlineData("cat category cat_x scat cat.", "cat", new[] { 0, 24 })]
    [InlineData("мир мирный мир-2 мир", "мир", new[] { 0, 11, 17 })]
    public void Whole_word_skips_matches_inside_words(string line, string word, int[] starts)
    {
        var e = Open(line + "\n");
        var found = new List<int>();
        var o = new SearchOptions(word, WholeWord: true);
        for (int i = 0; i < starts.Length; i++) { Assert.True(e.Find(o)); found.Add(e.Col - word.Length); }
        Assert.Equal(starts, found);
    }

    [Fact]
    public void Regex_finds_patterns_and_a_broken_one_reports_why()
    {
        var e = Open("id 12, id 345\n");
        Assert.True(e.Find(new SearchOptions(@"\d+", UseRegex: true)));
        Assert.Equal("12", Sel(e));
        Assert.False(e.Find(new SearchOptions("(unclosed", UseRegex: true)));
        Assert.NotNull(e.SearchError);
        Assert.False(e.Find(new SearchOptions("(unclosed")));           // as plain text it is a valid search ...
        Assert.Null(e.SearchError);                                      // ... that just finds nothing
    }

    [Fact]
    public void Plain_text_with_regex_characters_is_literal()
    {
        var e = Open("a.b axb a.b\n");
        Assert.True(e.Find(new SearchOptions("a.b")));
        Assert.True(e.FindNext());
        Assert.Equal((0, 11), At(e));
    }

    [Fact]
    public void Find_previous_goes_back_and_wraps()
    {
        var e = Open("x one\nx two\nx three\n");
        e.GoToLine(2, 3);
        var o = new SearchOptions("x");
        Assert.True(e.Find(o, backward: true)); Assert.Equal((1, 1), At(e));
        Assert.True(e.FindNext(backward: true)); Assert.Equal((0, 1), At(e));
        Assert.True(e.FindNext(backward: true)); Assert.Equal((2, 1), At(e));
        Assert.True(e.LastFindWrapped);
    }

    [Fact]
    public void A_single_match_is_found_again_after_wrapping()
    {
        var e = Open("only here\n");
        var o = new SearchOptions("here");
        Assert.True(e.Find(o)); Assert.True(e.FindNext());
        Assert.Equal((0, 9), At(e));
        Assert.True(e.FindNext(backward: true));
        Assert.Equal((0, 9), At(e));
    }

    [Fact]
    public void Not_found()
    {
        var e = Open("abc\n");
        Assert.False(e.Find(new SearchOptions("zzz")));
        Assert.Null(e.SearchError);
        Assert.Equal((0, 0), At(e));
    }

    // ---------- replace ----------
    [Fact]
    public void Replace_all_with_regex_groups_is_one_undo_step()
    {
        var e = Open("2026-10-02 and 1999-01-31\n");
        int n = e.ReplaceAll(new SearchOptions(@"(\d{4})-(\d\d)-(\d\d)", UseRegex: true), "$3.$2.$1");
        Assert.Equal(2, n);
        Assert.Equal("02.10.2026 and 31.01.1999", e.Lines[0]);
        e.Undo();
        Assert.Equal("2026-10-02 and 1999-01-31", e.Lines[0]);
    }

    [Fact]
    public void Plain_replacement_keeps_dollar_signs()
    {
        var e = Open("price\n");
        e.ReplaceAll(new SearchOptions("price"), "$1 each");
        Assert.Equal("$1 each", e.Lines[0]);
    }

    [Fact]
    public void Replace_all_finding_nothing_leaves_no_undo_step()
    {
        var e = Open("abc\n");
        Assert.Equal(0, e.ReplaceAll(new SearchOptions("zzz"), "y"));
        Assert.False(e.Modified);
    }

    static Func<char?> Answers(params char?[] keys) { var q = new Queue<char?>(keys); return () => q.Count > 0 ? q.Dequeue() : null; }

    [Fact]
    public void Interactive_replace_yes_no_and_stop()
    {
        var e = Open("a a a a\n");
        int n = e.ReplaceInteractive(new SearchOptions("a"), "b", Answers('y', 'n', 'y', null));
        Assert.Equal(2, n);
        Assert.Equal("b a b a", e.Lines[0]);
    }

    [Fact]
    public void Interactive_all_replaces_the_rest_of_the_round_from_the_caret()
    {
        var e = Open("a1\na2\na3\na4\n");
        e.GoToLine(3);
        int n = e.ReplaceInteractive(new SearchOptions("a"), "b", Answers('n', 'a'));
        Assert.Equal(3, n);                                         // a4, then (wrapped) a1 and a2; a3 was skipped
        Assert.Equal(new[] { "b1", "b2", "a3", "b4", "" }, e.Lines);
        e.Undo();                                                   // "all" is one step
        Assert.Equal(new[] { "a1", "a2", "a3", "b4", "" }, e.Lines);
    }

    [Fact]
    public void Interactive_replace_with_text_containing_the_pattern_ends_after_one_round()
    {
        var e = Open("a a\n");
        int n = e.ReplaceInteractive(new SearchOptions("a"), "aa", Answers('y', 'y', 'y', 'y', 'y'));
        Assert.Equal(2, n);
        Assert.Equal("aa aa", e.Lines[0]);
    }

    [Fact]
    public void Interactive_all_with_text_containing_the_pattern_also_stops()
    {
        var e = Open("a a\na\n");
        int n = e.ReplaceInteractive(new SearchOptions("a"), "aa", Answers('a'));
        Assert.Equal(3, n);
        Assert.Equal(new[] { "aa aa", "aa", "" }, e.Lines);
    }

    // ---------- selection as the search text ----------
    [Fact]
    public void Selection_within_a_line_is_offered_for_search()
    {
        var e = Open("hello world\nsecond\n");
        Assert.Null(e.SelectionOnOneLine);
        e.SelectAt(1, 7, 5);
        Assert.Equal("world", e.SelectionOnOneLine);
    }

    // ---------- block indent ----------
    static readonly KeyInput ShiftDown = new(Key.Down, '\0', Mods.Shift);
    static readonly KeyInput Tab = new(Key.Tab);
    static readonly KeyInput ShiftTab = new(Key.Tab, '\0', Mods.Shift);

    [Fact]
    public void Tab_on_selected_lines_indents_them_and_keeps_the_selection()
    {
        var e = Open("one\n\ntwo\nthree\n");
        e.HandleKey(ShiftDown); e.HandleKey(ShiftDown); e.HandleKey(ShiftDown);   // lines 1-3, caret at start of line 4
        e.HandleKey(Tab);
        Assert.Equal(new[] { "    one", "", "    two", "three", "" }, e.Lines);
        e.HandleKey(Tab);                                          // again: the selection is still there
        Assert.Equal("        one", e.Lines[0]);
        e.HandleKey(ShiftTab); e.HandleKey(ShiftTab); e.HandleKey(ShiftTab);
        Assert.Equal(new[] { "one", "", "two", "three", "" }, e.Lines);
    }

    [Fact]
    public void Shift_tab_without_a_selection_unindents_the_caret_line()
    {
        var e = Open("      x\n\tt\n");
        e.GoToLine(1, 7);
        e.HandleKey(ShiftTab);
        Assert.Equal(("  x", 2), (e.Lines[0], e.Col));
        e.GoToLine(2);
        e.HandleKey(ShiftTab);
        Assert.Equal("t", e.Lines[1]);
    }

    [Fact]
    public void Unindent_with_nothing_to_remove_changes_nothing()
    {
        var e = Open("x\n");
        e.HandleKey(ShiftTab);
        Assert.False(e.Modified);
    }

    [Fact]
    public void Tab_inside_one_line_still_inserts_spaces()
    {
        var e = Open("ab\n");
        e.GoToLine(1, 2);
        e.HandleKey(Tab);
        Assert.Equal("a    b", e.Lines[0]);
    }

    [Fact]
    public void Indent_is_one_undo_step()
    {
        var e = Open("a\nb\n");
        e.HandleKey(ShiftDown); e.HandleKey(ShiftDown);
        e.HandleKey(Tab);
        e.Undo();
        Assert.Equal(new[] { "a", "b", "" }, e.Lines);
    }

    // ---------- folder search ----------
    void FileIn(string rel, byte[] bytes)
    {
        var p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, bytes);
    }

    [Fact]
    public void Folder_search_reads_every_encoding_and_skips_binaries_and_build_folders()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        FileIn("a.md", Encoding.UTF8.GetBytes("# Заметка\nпривет мир\n"));
        FileIn(Path.Combine("sub", "b.txt"), Encoding.GetEncoding(1251).GetBytes("строка\nещё привет\n"));
        FileIn(Path.Combine("sub", "dos.txt"), Encoding.GetEncoding(866).GetBytes("Привет из DOS\n"));
        FileIn(Path.Combine("bin", "skip.txt"), Encoding.UTF8.GetBytes("привет\n"));
        FileIn("data.bin", new byte[] { 1, 0, 2, 0, 0x70 });
        var r = FolderSearch.Run(_dir, "*", new SearchOptions("привет"));
        Assert.Null(r.Error);
        var found = r.Hits.Select(h => (Path.GetRelativePath(_dir, h.Path), h.Line, h.Col)).ToList();
        Assert.Equal(new[] { ("a.md", 2, 1), (Path.Combine("sub", "b.txt"), 2, 5), (Path.Combine("sub", "dos.txt"), 1, 1) }, found);
    }

    [Fact]
    public void Folder_search_masks_and_options()
    {
        FileIn("a.cs", Encoding.UTF8.GetBytes("int Count;\nint count;\n"));
        FileIn("b.md", Encoding.UTF8.GetBytes("Count\n"));
        var r = FolderSearch.Run(_dir, "*.cs", new SearchOptions("Count", MatchCase: true));
        Assert.Equal(new[] { 1 }, r.Hits.Select(h => h.Line));
        Assert.Single(r.Hits.Select(h => h.Path).Distinct());
        Assert.Equal(new[] { "*.md", "*.cs" }, FolderSearch.Masks("*.md; *.cs"));
        Assert.NotNull(FolderSearch.Run(Path.Combine(_dir, "nope"), "*", new SearchOptions("x")).Error);
    }

    [Fact]
    public void Folder_search_stops_at_the_limit()
    {
        FileIn("many.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("hit\n", 50))));
        var r = FolderSearch.Run(_dir, "*", new SearchOptions("hit"), maxHits: 10);
        Assert.Equal(10, r.Hits.Count);
        Assert.True(r.Truncated);
    }
}
