using Xunit;
using static EditorView;

public sealed class DiskChangeTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-disk-" + Guid.NewGuid().ToString("N"));
    readonly string _file;
    public DiskChangeTests() { Directory.CreateDirectory(_dir); _file = Path.Combine(_dir, "a.txt"); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string text)
    {
        File.WriteAllText(_file, text);
        var e = new EditorView(); e.Load(_file);
        return e;
    }

    // another program writes the file; the time is moved on so the change is visible even on coarse clocks
    void External(string text)
    {
        File.WriteAllText(_file, text);
        File.SetLastWriteTimeUtc(_file, DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void Nothing_is_reported_while_the_file_is_untouched()
    {
        var e = Open("one\n");
        Assert.Equal(DiskChange.None, e.CheckDisk());
        Assert.Equal(DiskChange.None, e.CheckDisk());
    }

    [Fact]
    public void A_change_is_reported_once_it_has_settled()
    {
        var e = Open("one\n");
        External("two\n");
        Assert.Equal(DiskChange.None, e.CheckDisk());             // first sighting: maybe still being written
        Assert.Equal(DiskChange.Changed, e.CheckDisk());
    }

    [Fact]
    public void A_file_still_being_written_is_not_reported_until_it_stops_changing()
    {
        var e = Open("one\n");
        External("tw");
        Assert.Equal(DiskChange.None, e.CheckDisk());
        File.WriteAllText(_file, "two\n"); File.SetLastWriteTimeUtc(_file, DateTime.UtcNow.AddSeconds(9));
        Assert.Equal(DiskChange.None, e.CheckDisk());
        Assert.Equal(DiskChange.Changed, e.CheckDisk());
    }

    [Fact]
    public void Own_save_is_not_an_external_change()
    {
        var e = Open("one\n");
        e.Lines[0] = "mine"; e.Save();
        Assert.Equal(DiskChange.None, e.CheckDisk());
        Assert.Equal(DiskChange.None, e.CheckDisk());
        Assert.False(e.DiskDiffers);
    }

    [Fact]
    public void Reload_takes_the_new_text_keeps_the_cursor_and_can_be_undone()
    {
        var e = Open("line 1\nline 2\nline 3\n");
        e.GoToLine(2, 4);
        External("line 1\nline 2 changed\nline 3\nline 4\n");
        e.ReloadFromDisk();
        Assert.Equal(("line 2 changed", 1, 3, false), (e.Lines[1], e.Row, e.Col, e.Modified));
        Assert.Equal(DiskChange.None, e.CheckDisk());
        e.Undo();
        Assert.Equal("line 2", e.Lines[1]);
        Assert.True(e.Modified);                                   // the buffer no longer matches the file
    }

    [Fact]
    public void Reload_over_unsaved_edits_keeps_them_one_undo_away()
    {
        var e = Open("base\n");
        e.HandleKey(new Tui.KeyInput(Tui.Key.Character, 'X', Tui.Mods.None));
        External("theirs\n");
        e.ReloadFromDisk();
        Assert.Equal("theirs", e.Lines[0]);
        e.Undo();
        Assert.Equal("Xbase", e.Lines[0]);
    }

    [Fact]
    public void Keeping_your_version_stops_the_reports_and_leaves_the_buffer_unsaved()
    {
        var e = Open("one\n");
        External("two\n");
        e.CheckDisk(); e.CheckDisk();
        e.AcceptDisk();
        Assert.True(e.Modified);
        Assert.Equal(DiskChange.None, e.CheckDisk());
        Assert.Equal(DiskChange.None, e.CheckDisk());
        Assert.False(e.DiskDiffers);
    }

    [Fact]
    public void Deletion_is_reported()
    {
        var e = Open("one\n");
        File.Delete(_file);
        Assert.Equal(DiskChange.None, e.CheckDisk());
        Assert.Equal(DiskChange.Deleted, e.CheckDisk());
    }

    [Fact]
    public void Replace_by_delete_and_rename_is_a_change_not_a_deletion()
    {
        var e = Open("one\n");
        File.Delete(_file);
        Assert.Equal(DiskChange.None, e.CheckDisk());             // gone for a moment ...
        External("two\n");                                        // ... and back with new text
        Assert.Equal(DiskChange.None, e.CheckDisk());
        Assert.Equal(DiskChange.Changed, e.CheckDisk());
    }

    [Fact]
    public void Disk_state_follows_each_document()
    {
        var a = Open("a\n");
        var docA = a.CaptureDoc();
        var other = Path.Combine(_dir, "b.txt"); File.WriteAllText(other, "b\n");
        a.Load(other);
        External("a changed\n");
        a.ApplyDoc(docA);
        a.CheckDisk();
        Assert.Equal(DiskChange.Changed, a.CheckDisk());
    }

    [Theory]
    [InlineData("notes.md:12", "notes.md", 12, 0)]
    [InlineData("src/Program.cs:40:7", "src/Program.cs", 40, 7)]
    [InlineData(@"C:\work\a.cs:3", @"C:\work\a.cs", 3, 0)]
    [InlineData(@"C:\work\a.cs:3:9:", @"C:\work\a.cs", 3, 9)]                  // grep -n style trailing colon
    [InlineData("plain.txt", "plain.txt", 0, 0)]
    [InlineData("C:5", "C:5", 0, 0)]                                           // a drive, not a file
    [InlineData("a.txt:99999999999", "a.txt", int.MaxValue, 0)]
    public void Locations_on_the_command_line(string arg, string path, int line, int col) =>
        Assert.Equal((path, line, col), Location.Parse(arg));

    [Fact]
    public void Go_to_line_and_column_clamps()
    {
        var e = Open("abc\ndefgh\n");
        e.GoToLine(2, 4);
        Assert.Equal((1, 3), (e.Row, e.Col));
        e.GoToLine(99, 99);
        Assert.Equal((2, 0), (e.Row, e.Col));
    }
}
