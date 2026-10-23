using System.Text;
using Tui;
using Xunit;

/// <summary>Editing logic exercised without a terminal: the view is created and driven directly.</summary>
public sealed class EditorTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "blueedit-tests-" + Guid.NewGuid().ToString("N"));

    public EditorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    string Write(string name, byte[] bytes) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, bytes); return p; }
    string Write(string name, string text) => Write(name, new UTF8Encoding(false).GetBytes(text));

    EditorView Open(string name, string text)
    {
        var e = new EditorView();
        e.Load(Write(name, text));
        return e;
    }

    // ---------- file safety: what is read is written back unchanged ----------
    [Theory]
    [InlineData("utf8_lf.txt", "Привет\nмир\n")]
    [InlineData("utf8_crlf.txt", "Привет\r\nмир\r\n")]
    [InlineData("plain.txt", "abc\ndef")]
    public void Saving_without_changes_keeps_every_byte(string name, string text)
    {
        var path = Write(name, text);
        var before = File.ReadAllBytes(path);
        var e = new EditorView();
        e.Load(path);
        e.Save();
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Utf8_bom_is_preserved()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Привет\n")).ToArray();
        var path = Write("bom.txt", bytes);
        var e = new EditorView(); e.Load(path); e.Save();
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Legacy_cyrillic_file_is_read_as_cp1251_and_written_back_as_cp1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1251 = Encoding.GetEncoding(1251);
        var bytes = cp1251.GetBytes("Привет, мир\r\nвторая строка\r\n");
        var path = Write("legacy.txt", bytes);
        var e = new EditorView(); e.Load(path);
        Assert.Equal("Привет, мир", e.Lines[0]);
        e.Save();
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Missing_file_opens_as_new_document_and_save_creates_it()
    {
        var path = Path.Combine(_dir, "new.txt");
        var e = new EditorView(); e.Load(path);
        Assert.Equal(new[] { "" }, e.Lines);
        e.Lines[0] = "hello"; e.Save();
        Assert.Equal("hello", File.ReadAllText(path));
    }

    // ---------- saving through a temporary file (SafeFile) ----------
    [Fact]
    public void Save_leaves_no_temporary_file_behind()
    {
        var e = Open("a.md", "one\n");
        e.Lines[0] = "two"; e.Save();
        e.Lines[0] = "three"; e.Save();
        Assert.Equal("three\n", File.ReadAllText(Path.Combine(_dir, "a.md")));
        Assert.Equal(new[] { "a.md" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void A_file_that_cannot_be_replaced_keeps_its_text_and_the_document_stays_unsaved()
    {
        // Windows: a program holding the file open without sharing (as Word does) stops the save
        if (!OperatingSystem.IsWindows()) return;
        var e = Open("locked.md", "old\n");
        var path = Path.Combine(_dir, "locked.md");
        e.HandleKey(new KeyInput(Key.Character, 'x'));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(e.Save);
        Assert.True(e.Modified);
        Assert.Equal("old\n", File.ReadAllText(path));
        Assert.Equal(new[] { "locked.md" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_as_that_fails_keeps_the_old_name()
    {
        var e = Open("named.md", "text\n");
        var old = e.Path;
        Assert.ThrowsAny<IOException>(() => e.SaveAs(Path.Combine(_dir, "no such folder", "x.md")));
        Assert.Equal(old, e.Path);
    }

    [Fact]
    public void Saving_through_a_symbolic_link_keeps_the_link()
    {
        var real = Write("real.md", "old\n");
        var link = Path.Combine(_dir, "link.md");
        try { File.CreateSymbolicLink(link, real); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }   // Windows without developer mode
        var e = new EditorView(); e.Load(link);
        e.Lines[0] = "new"; e.Save();
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("new\n", File.ReadAllText(real));
    }

    [Fact]
    public void Save_keeps_the_unix_permissions()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Write("run.sh", "echo hi\n");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead;
        File.SetUnixFileMode(path, mode);
        var e = new EditorView(); e.Load(path);
        e.Lines[0] = "echo bye"; e.Save();
        Assert.Equal(mode, File.GetUnixFileMode(path));
    }

    [Fact]
    public void Live_preview_relayout_of_two_lines_matches_a_full_layout()
    {
        // lines whose markup makes them wrap differently when shown (caret line) and hidden
        var words = string.Join(" ", Enumerable.Repeat("**bold** `code` [link](http://example.com/a/b)", 3));
        var text = string.Join("\n", Enumerable.Range(0, 30).Select(i => i % 3 == 0 ? "# Heading " + i : i % 3 == 1 ? words : "plain " + i)) + "\n";
        var e = Open("live.md", text);
        e.SetArea(new Tui.Rect(0, 0, 40, 12));
        e.Draw(new Tui.Screen(40, 12));
        var down = new Tui.KeyInput(Tui.Key.Down, '\0', Tui.Mods.None);
        var up = new Tui.KeyInput(Tui.Key.Up, '\0', Tui.Mods.None);
        for (int step = 0; step < 60; step++)
        {
            e.HandleKey(step % 7 == 6 ? up : down);
            var incremental = e.LayoutSignature();
            e.DropLayout();
            Assert.Equal(e.LayoutSignature(), incremental);
        }
    }

    // ---------- editing ----------
    [Fact]
    public void Replace_all_is_case_insensitive_and_counts_matches()
    {
        var e = Open("a.txt", "Line one\nsecond LINE\nthird\n");
        Assert.Equal(2, e.ReplaceAll("line", "ROW"));
        Assert.Equal(new[] { "ROW one", "second ROW", "third", "" }, e.Lines);
    }

    [Fact]
    public void Replace_all_can_be_undone_in_one_step()
    {
        var e = Open("a.txt", "x x x\nx\n");
        e.ReplaceAll("x", "y");
        e.Undo();
        Assert.Equal(new[] { "x x x", "x", "" }, e.Lines);
        e.Redo();
        Assert.Equal(new[] { "y y y", "y", "" }, e.Lines);
    }

    [Fact]
    public void Delete_and_duplicate_line()
    {
        var e = Open("a.txt", "one\ntwo\nthree\n");
        e.Row = 1;
        e.DuplicateLinePublic();
        Assert.Equal(new[] { "one", "two", "two", "three", "" }, e.Lines);
        e.DeleteLinePublic();
        Assert.Equal(new[] { "one", "two", "three", "" }, e.Lines);
    }

    [Fact]
    public void Go_to_line_clamps_to_the_document()
    {
        var e = Open("a.txt", "a\nb\nc\n");
        e.GoToLine(2); Assert.Equal(1, e.Row);
        e.GoToLine(999); Assert.Equal(3, e.Row);
        e.GoToLine(-5); Assert.Equal(0, e.Row);
    }

    [Fact]
    public void Find_wraps_around_and_is_case_insensitive()
    {
        var e = Open("a.txt", "alpha\nBeta\nalpha\n");
        e.Row = 1; e.Col = 4;
        Assert.True(e.Find("ALPHA"));
        Assert.Equal(2, e.Row);
        Assert.True(e.Find());                  // next one wraps to the first line
        Assert.Equal(0, e.Row);
        Assert.False(e.Find("zzz"));
    }

    // ---------- Markdown formatting ----------
    [Fact]
    public void Bold_wraps_the_word_under_the_caret_and_toggles_back()
    {
        var e = Open("a.md", "plain line\n");
        e.Col = 2;
        e.Bold();
        Assert.Equal("**plain** line", e.Lines[0]);
        e.Bold();
        Assert.Equal("plain line", e.Lines[0]);
    }

    [Fact]
    public void Heading_level_cycles_one_to_six_then_off()
    {
        var e = Open("a.md", "title\n");
        var seen = new List<string>();
        for (int i = 0; i < 7; i++) { e.CycleHeading(); seen.Add(e.Lines[0]); }
        Assert.Equal(new[] { "# title", "## title", "### title", "#### title", "##### title", "###### title", "title" }, seen);
    }

    [Fact]
    public void Bullet_list_toggles_on_and_off()
    {
        var e = Open("a.md", "one\ntwo\n");
        e.BulletList();
        Assert.Equal("- one", e.Lines[0]);
        e.BulletList();
        Assert.Equal("one", e.Lines[0]);
    }

    [Fact]
    public void Checkbox_is_added_then_flipped()
    {
        var e = Open("a.md", "task\n");
        e.ToggleCheckbox(); Assert.Equal("- [ ] task", e.Lines[0]);
        e.ToggleCheckbox(); Assert.Equal("- [x] task", e.Lines[0]);
        e.ToggleCheckbox(); Assert.Equal("- [ ] task", e.Lines[0]);
    }

    [Fact]
    public void Link_inserts_empty_brackets_with_the_caret_inside()
    {
        var e = Open("a.md", "text\n");
        e.Col = 4;
        e.InsertLink();
        Assert.Equal("text[]()", e.Lines[0]);
        Assert.Equal(5, e.Col);
    }

    [Fact]
    public void Outline_lists_headings_but_skips_code_fences()
    {
        var e = Open("a.md", "# One\ntext\n```\n# not a heading\n```\n## Two\n");
        var heads = e.Outline();
        Assert.Equal(new[] { "One", "Two" }, heads.Select(h => h.Title));
        Assert.Equal(new[] { 1, 2 }, heads.Select(h => h.Level));
    }

    [Fact]
    public void Classic_clipboard_keys_copy_cut_and_paste()
    {
        var e = Open("a.txt", "one two\n");
        e.HandleKey(new KeyInput(Key.End, Mods: Mods.Shift));                 // select "one two"
        e.HandleKey(new KeyInput(Key.Insert, Mods: Mods.Ctrl));               // Ctrl+Insert: copy
        e.HandleKey(new KeyInput(Key.End));
        e.HandleKey(new KeyInput(Key.Insert, Mods: Mods.Shift));              // Shift+Insert: paste
        Assert.Equal("one twoone two", e.Lines[0]);
        e.HandleKey(new KeyInput(Key.Home, Mods: Mods.Shift));                // select the line
        e.HandleKey(new KeyInput(Key.Delete, Mods: Mods.Shift));              // Shift+Delete: cut
        Assert.Equal("", e.Lines[0]);
        e.HandleKey(new KeyInput(Key.Insert, Mods: Mods.Shift));
        Assert.Equal("one twoone two", e.Lines[0]);
        e.HandleKey(new KeyInput(Key.Home));
        e.HandleKey(new KeyInput(Key.Delete, Mods: Mods.Shift));              // no selection: deletes a character
        Assert.Equal("ne twoone two", e.Lines[0]);
    }

    // ---------- read-only (-r) ----------
    [Fact]
    public void Read_only_blocks_every_edit()
    {
        var e = Open("a.md", "keep\n");
        e.ReadOnlyFile = true;
        e.Bold(); e.CycleHeading(); e.BulletList(); e.DeleteLinePublic(); e.DuplicateLinePublic(); e.Paste(); e.Cut();
        Assert.Equal(0, e.ReplaceAll("keep", "lost"));
        Assert.Equal(new[] { "keep", "" }, e.Lines);
        Assert.False(e.Modified);
    }
}
