using System.Text;
using Xunit;
using Item = Recovery.Item;

/// <summary>Copies of unsaved documents (Recovery.cs): written, skipped, found after an editor ended.</summary>
public sealed class RecoveryTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "blueedit-recovery-" + Guid.NewGuid().ToString("N"));

    public RecoveryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    void Store(int pid, params Item[] items) => new Recovery(_dir, pid, DateTime.UtcNow).StoreNow(items);

    // A process id no BlueEdit has (Windows' Idle process; none on Linux).
    static int Gone() => 0;

    [Fact]
    public void Copies_of_an_editor_that_ended_are_found_with_their_text_and_encoding()
    {
        var file = Path.Combine(_dir, "note.md");
        File.WriteAllText(file, "saved\n");
        Store(Gone(), new Item(file, "saved\nand more", "cp1251", true), new Item(null, "never saved", "UTF-8", false));
        var s = Assert.Single(Recovery.Left(_dir));
        Assert.Equal(2, s.Items.Count);
        Assert.Equal(("saved\nand more", "cp1251", true), (s.Items[0].Text, s.Items[0].Encoding, s.Items[0].CrLf));
        Assert.Null(s.Items[1].Path);
        Assert.Equal("windows-1251", FileEncodings.FromShortName(s.Items[0].Encoding)!.WebName);
    }

    [Fact]
    public void The_running_editors_own_copies_are_not_offered()
    {
        Recovery.ForThisProcess(_dir).StoreNow(new[] { new Item(null, "mine", "UTF-8", false) });
        Assert.Empty(Recovery.Left(_dir));
    }

    [Fact]
    public void Copies_of_an_ended_editor_whose_id_went_to_a_running_one_are_offered_and_kept_apart()
    {
        // the system reused the id: same process id, another start time
        new Recovery(_dir, Environment.ProcessId, DateTime.UtcNow.AddHours(-1)).StoreNow(new[] { new Item(null, "lost", "UTF-8", false) });
        var mine = Recovery.ForThisProcess(_dir);
        mine.StoreNow(new[] { new Item(null, "mine", "UTF-8", false) });
        Assert.Equal(2, Directory.GetFiles(_dir, "session-*.json").Length);      // not written over
        Assert.Equal("lost", Assert.Single(Assert.Single(Recovery.Left(_dir)).Items).Text);
        mine.Clear();
        Assert.Single(Directory.GetFiles(_dir, "session-*.json"));                // its own only
    }

    [Fact]
    public void A_copy_that_says_what_the_file_says_or_is_older_than_the_file_has_nothing_to_recover()
    {
        var same = Path.Combine(_dir, "same.md");
        File.WriteAllText(same, "one\r\ntwo");
        var later = Path.Combine(_dir, "later.md");
        File.WriteAllText(later, "old");
        Store(Gone(), new Item(same, "one\ntwo", "UTF-8", true), new Item(later, "edited", "UTF-8", false));
        File.SetLastWriteTimeUtc(later, DateTime.UtcNow.AddMinutes(1));          // saved after the copy
        Assert.Empty(Assert.Single(Recovery.Left(_dir)).Items);
    }

    [Fact]
    public void No_items_removes_the_copy_and_the_same_items_are_not_written_twice()
    {
        var r = new Recovery(_dir, Gone(), DateTime.UtcNow);
        r.StoreNow(new[] { new Item(null, "a", "UTF-8", false) });
        var file = Assert.Single(Directory.GetFiles(_dir, "session-*.json"));
        var first = File.GetLastWriteTimeUtc(file);
        File.SetLastWriteTimeUtc(file, first.AddMinutes(-5));
        r.StoreNow(new[] { new Item(null, "a", "UTF-8", false) });
        Assert.Equal(first.AddMinutes(-5), File.GetLastWriteTimeUtc(file));
        r.Clear();
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void A_damaged_copy_is_passed_over()
    {
        File.WriteAllText(Path.Combine(_dir, "session-12345.json"), "{ not json");
        Assert.Empty(Recovery.Left(_dir));
    }

    [Fact]
    public void Recovered_text_opens_modified_with_its_encoding_and_line_ends()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var file = Path.Combine(_dir, "r.txt");
        File.WriteAllText(file, "old");
        var e = new EditorView();
        e.Recover(file, "Привет\nмир", FileEncodings.FromShortName("cp1251")!, true);
        Assert.True(e.Modified);
        Assert.Equal(new[] { "Привет", "мир" }, e.Lines);
        Assert.Equal("cp1251 CRLF", e.FileFormat);
        Assert.Equal(EditorView.DiskChange.None, e.CheckDisk());
        e.Save();
        Assert.Equal(Encoding.GetEncoding(1251).GetBytes("Привет\r\nмир"), File.ReadAllBytes(file));
    }

    [Fact]
    public void Every_encoding_name_comes_back()
    {
        foreach (var c in FileEncodings.All)
            Assert.Equal(c.Name, FileEncodings.ShortName(FileEncodings.FromShortName(FileEncodings.ShortName(c.Create()))!));
        Assert.Null(FileEncodings.FromShortName("nonsense"));
    }
}
