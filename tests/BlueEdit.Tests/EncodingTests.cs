using System.Text;
using Xunit;

public sealed class EncodingTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bp-enc-" + Guid.NewGuid().ToString("N"));
    public EncodingTests() { Directory.CreateDirectory(_dir); Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static Encoding Cp(int n) => Encoding.GetEncoding(n);
    string Write(string name, byte[] bytes) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, bytes); return p; }

    [Theory]
    [InlineData("Привет, мир! Это обычный текст.")]
    [InlineData("ПРОГРАММА ЗАВЕРШЕНА")]                    // all capitals
    [InlineData("да")]
    public void Cp1251_text_is_recognised(string text)
    {
        var (s, enc) = FileEncodings.Detect(Cp(1251).GetBytes(text));
        Assert.Equal((text, "cp1251"), (s, FileEncodings.ShortName(enc)));
    }

    [Theory]
    [InlineData("Привет, мир! Это обычный текст из DOS.")]
    [InlineData("ПРОГРАММА ЗАВЕРШЕНА")]
    [InlineData("╔════════╗\r\n║ Меню   ║\r\n╚════════╝")]   // pseudographics
    [InlineData("════════════")]                          // pseudographics only: "НННН" in cp1251
    public void Cp866_text_is_recognised(string text)
    {
        var (s, enc) = FileEncodings.Detect(Cp(866).GetBytes(text));
        Assert.Equal((text, "cp866"), (s, FileEncodings.ShortName(enc)));
    }

    [Fact]
    public void Utf8_wins_over_code_pages_and_ascii_is_utf8()
    {
        Assert.Equal("UTF-8", FileEncodings.ShortName(FileEncodings.Detect(Encoding.UTF8.GetBytes("Привет")).Enc));
        Assert.Equal("UTF-8", FileEncodings.ShortName(FileEncodings.Detect(Encoding.ASCII.GetBytes("plain")).Enc));
        Assert.Equal("UTF-8", FileEncodings.ShortName(FileEncodings.Detect(Array.Empty<byte>()).Enc));
    }

    [Theory]
    [InlineData(866)]
    [InlineData(20866)]
    [InlineData(1252)]
    public void File_in_a_code_page_round_trips_after_detection_or_reopen(int cp)
    {
        var text = cp == 1252 ? "Café crème\r\nnaïve\r\n" : "Привет\r\nмир ╔═╗\r\n";
        if (cp == 20866) text = "Привет\r\nмир\r\n";
        var bytes = Cp(cp).GetBytes(text);
        var path = Write("f.txt", bytes);
        var e = new EditorView(); e.Load(path);
        e.Reload(Cp(cp));
        Assert.Equal(text.Split("\r\n")[0], e.Lines[0]);
        e.Save();
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Reopen_keeps_the_cursor_and_clears_unsaved_state()
    {
        var path = Write("a.txt", Cp(866).GetBytes("строка 1\nстрока 2\nстрока 3\n"));
        var e = new EditorView(); e.Load(path);
        e.Reload(Cp(1251));                                  // wrong on purpose
        e.Row = 2; e.Col = 3; e.HandleKey(new Tui.KeyInput(Tui.Key.Character, 'x', Tui.Mods.None));
        Assert.True(e.Modified);
        Assert.Equal(0, e.Reload(Cp(866)));
        Assert.Equal(("строка 3", 2, 4, false), (e.Lines[2], e.Row, e.Col, e.Modified));
    }

    [Fact]
    public void Reopen_as_utf8_counts_bytes_that_do_not_decode()
    {
        var path = Write("a.txt", Cp(1251).GetBytes("абв"));
        var e = new EditorView(); e.Load(path);
        Assert.Equal(3, e.Reload(new UTF8Encoding(false)));
        Assert.Equal("UTF-8", e.EncodingName);
    }

    [Fact]
    public void Reopen_as_utf8_keeps_a_bom_out_of_the_text()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("ok")).ToArray();
        var path = Write("a.txt", bytes);
        var e = new EditorView(); e.Load(path);
        e.Reload(new UTF8Encoding(false));
        Assert.Equal(("ok", "UTF-8 BOM"), (e.Lines[0], e.EncodingName));
        e.Save();
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-8 BOM")]
    [InlineData("UTF-16 LE")]
    [InlineData("UTF-16 BE")]
    [InlineData("cp1251")]
    [InlineData("cp866")]
    public void Convert_writes_the_new_encoding(string name)
    {
        var path = Write("a.txt", Cp(866).GetBytes("Привет\r\n"));
        var e = new EditorView(); e.Load(path);
        var enc = FileEncodings.All.Single(c => c.Name == name).Create();
        e.ConvertTo(enc);
        Assert.True(e.Modified || name == "cp866");
        e.Save();
        Assert.Equal(enc.GetPreamble().Concat(enc.GetBytes("Привет\r\n")), File.ReadAllBytes(path));
        var again = new EditorView(); again.Load(path);
        Assert.Equal(("Привет", name), (again.Lines[0], again.EncodingName));
    }

    [Fact]
    public void Characters_a_code_page_cannot_store_are_found()
    {
        var lines = new[] { "plain", "Привет — “quotes” ✓", "😀" };
        var lost = FileEncodings.Unencodable(lines, Cp(866));
        Assert.Equal(new[] { (1, 7), (1, 9), (1, 16), (1, 18), (2, 0) }, lost);
        Assert.Empty(FileEncodings.Unencodable(lines, new UTF8Encoding(false)));
        Assert.Empty(FileEncodings.Unencodable(new[] { "Привет" }, Cp(1251)));
    }

    [Fact]
    public void Line_endings_can_be_switched()
    {
        var path = Write("a.txt", Encoding.UTF8.GetBytes("a\r\nb\r\n"));
        var e = new EditorView(); e.Load(path);
        Assert.True(e.CrLf);
        e.SetLineEndings(false);
        Assert.True(e.Modified);
        e.Save();
        Assert.Equal("a\nb\n", File.ReadAllText(path));
    }

    [Fact]
    public void Every_choice_has_a_matching_short_name() =>
        Assert.Equal(FileEncodings.All.Select(c => c.Name), FileEncodings.All.Select(c => FileEncodings.ShortName(c.Create())));
}
