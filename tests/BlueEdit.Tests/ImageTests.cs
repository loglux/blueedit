using SkiaSharp;
using Tui;
using Xunit;

/// <summary>Images in Markdown as pictures: the module reads the file, the screen sends it in colour (Sixel), the
/// editor finds the file and gives the picture its rows.</summary>
[Collection("formula pictures")]
public sealed class ImageTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-images-" + Guid.NewGuid().ToString("N"));

    public ImageTests()
    {
        Directory.CreateDirectory(_dir);
        FormulaImages.Reset();
    }

    public void Dispose()
    {
        FormulaImages.SetTerminal(false, null);
        FormulaImages.Decoder = null;
        FormulaImages.Reset();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // a PNG of solid blocks, made with Skia
    string Png(string name, int w, int h, Action<SKCanvas> draw)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bmp = new SKBitmap(w, h);
        using (var c = new SKCanvas(bmp)) { c.Clear(SKColors.Transparent); draw(c); }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    static void Fill(SKCanvas c, SKColor colour, float x, float y, float w, float h)
    {
        using var p = new SKPaint { Color = colour };
        c.DrawRect(x, y, w, h, p);
    }

    // ---- the module ----

    [Fact]
    public void The_module_reads_a_png_shrunk_to_fit_and_keeps_its_colours_and_transparency()
    {
        var path = Png("a.png", 200, 100, c => { Fill(c, SKColors.Red, 0, 0, 100, 100); Fill(c, SKColors.Blue, 100, 0, 50, 100); });
        var (pixels, w, h, error) = BlueEdit.MathImages.Renderer.Decode(path, 100, 100);
        Assert.Null(error);
        Assert.Equal((100, 50), (w, h));                                    // half: the width limits it, the shape kept
        Assert.Equal(unchecked((int)0xFFFF0000), pixels![10 * w + 10]);
        Assert.Equal(unchecked((int)0xFF0000FF), pixels[10 * w + 60]);
        Assert.Equal(0u, (uint)pixels[10 * w + 90] >> 24);                 // transparent stays so
        var (_, w2, h2, _) = BlueEdit.MathImages.Renderer.Decode(path, 1000, 1000);
        Assert.Equal((200, 100), (w2, h2));                                 // never enlarged
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void A_photo_is_turned_the_way_its_exif_says(int orientation)
    {
        // each file is stored turned some way and says so: all show 40 × 20, red left, blue right, green top left
        var (pixels, w, h, error) = BlueEdit.MathImages.Renderer.Decode(Path.Combine(AppContext.BaseDirectory, "images", $"exif-{orientation}.jpg"), 400, 400);
        Assert.Null(error);
        Assert.Equal((40, 20), (w, h));
        static (int R, int G, int B) Rgb(int p) => ((p >> 16) & 255, (p >> 8) & 255, p & 255);
        Assert.True(Rgb(pixels![2 * w + 2]) is { G: > 150, R: < 80 }, $"green at the top left: {Rgb(pixels[2 * w + 2])}");
        Assert.True(Rgb(pixels[10 * w + 12]) is { R: > 150, B: < 80 }, "red on the left");
        Assert.True(Rgb(pixels[10 * w + 30]) is { B: > 150, R: < 80 }, "blue on the right");
    }

    [Fact]
    public void A_file_that_is_no_picture_is_refused_with_a_reason()
    {
        var path = Path.Combine(_dir, "not.png");
        File.WriteAllText(path, "hello");
        var (pixels, _, _, error) = BlueEdit.MathImages.Renderer.Decode(path, 100, 100);
        Assert.Null(pixels);
        Assert.NotNull(error);
    }

    // ---- colour Sixel ----

    [Fact]
    public void A_picture_of_few_colours_keeps_them_exactly_and_transparent_pixels_are_the_background()
    {
        int[] argb = [unchecked((int)0xFF102030), unchecked((int)0xFF405060), 0, unchecked((int)0xFF102030)];
        var (index, palette) = Sixel.Quantize(argb, 2);
        Assert.Equal(0x102030, palette[index[0]]);
        Assert.Equal(0x405060, palette[index[1]]);
        Assert.Equal(Sixel.Background, index[2]);
        Assert.Equal(index[0], index[3]);
    }

    [Fact]
    public void A_photo_of_many_colours_gets_a_palette_of_its_own_and_its_average_colour_stays()
    {
        // a gradient of 600 colours: more than 255, so a palette made for it, with the error spread
        int w = 600, h = 6;
        var argb = new int[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) argb[y * w + x] = unchecked((int)0xFF000000) | (x * 255 / (w - 1) << 16) | 0x4080;
        var (index, palette) = Sixel.Quantize(argb, w);
        Assert.InRange(palette.Length, 2, 255);
        // dithering keeps the local average: the red of 50 pixels around x = 300 is about 128
        double red = Enumerable.Range(275, 50).Average(x => (palette[index[2 * w + x]] >> 16) & 255);
        Assert.InRange(red, 115, 141);
    }

    [Fact]
    public void Colour_sixel_decodes_back_to_the_picture()
    {
        int w = 7, h = 13;                                                 // not whole bands: the last has one row
        var argb = new int[w * h];
        var rnd = new Random(5);
        int[] colours = [0xFF0000, 0x00FF00, 0x0000FF, 0x808080];
        for (int i = 0; i < argb.Length; i++) argb[i] = rnd.Next(5) == 0 ? 0 : unchecked((int)0xFF000000) | colours[rnd.Next(4)];
        var pic = new CellPicture(argb, 1, 1, w, h);
        Assert.True(pic.Colour);
        var decoded = DecodeSixel(pic.Sixel(0, 1, -1, 0x333333), out int dw, out int dh);
        Assert.Equal((w, h), (dw, dh));
        for (int i = 0; i < argb.Length; i++)
            Assert.Equal(argb[i] == 0 ? 0x333333 : argb[i] & 0xFFFFFF, decoded[i]);
    }

    // A Sixel reader for the tests: registers (#n;2;r;g;b in percent), bands, runs (!n), $ and -
    static int[] DecodeSixel(string s, out int width, out int height)
    {
        int i = s.IndexOf('"', StringComparison.Ordinal) + 1;
        int Num() { int n = 0; while (char.IsDigit(s[i])) n = n * 10 + (s[i++] - '0'); return n; }
        Num(); i++; Num(); i++;                                             // "1;1: square pixels
        int w = Num(); i++;
        int h = Num();
        (width, height) = (w, h);
        var px = Enumerable.Repeat(-1, w * h).ToArray();
        var regs = new Dictionary<int, int>();
        int x = 0, band = 0, colour = 0;
        while (i < s.Length && s[i] != '\u001b')
        {
            char c = s[i];
            if (c == '#')
            {
                i++; int n = Num();
                if (s[i] == ';')
                {
                    i++; Num(); i++; int r = Num(); i++; int g = Num(); i++; int b = Num();
                    static int Byte(int pct) => (pct * 255 + 50) / 100;
                    regs[n] = (Byte(r) << 16) | (Byte(g) << 8) | Byte(b);
                }
                colour = n;
            }
            else if (c == '$') { x = 0; i++; }
            else if (c == '-') { x = 0; band++; i++; }
            else
            {
                int count = 1;
                if (c == '!') { i++; count = Num(); c = s[i]; }
                int bits = c - 63;
                for (int k = 0; k < count; k++, x++)
                    for (int b = 0; b < 6; b++)
                        if ((bits & (1 << b)) != 0 && band * 6 + b < h) px[(band * 6 + b) * w + x] = regs[colour];
                i++;
            }
        }
        return px;
    }

    // ---- finding the file ----

    [Theory]
    [InlineData("![a graph](img/graph.png)", "img/graph.png", false, null)]
    [InlineData("  ![](<my pics/a b.png> \"title\")  ", "my pics/a b.png", false, null)]
    [InlineData("![x](a%20b.JPG)", "a b.JPG", false, null)]
    [InlineData("![[graph.png]]", "graph.png", true, null)]
    [InlineData("![[graph.png|300]]", "graph.png", true, 300)]
    [InlineData("![[sub/graph.webp|alt text]]", "sub/graph.webp", true, null)]
    public void A_line_that_is_only_an_image_is_found(string line, string target, bool wiki, int? width) =>
        Assert.Equal((target, wiki, width), MarkdownImages.Parse(line));

    [Theory]
    [InlineData("Text before ![a](a.png)")]
    [InlineData("![a](https://example.com/a.png)")]
    [InlineData("![a](notes.md)")]
    [InlineData("![[Another note]]")]
    [InlineData("[a](a.png)")]
    public void Anything_else_is_not_an_image_line(string line) => Assert.Null(MarkdownImages.Parse(line));

    [Fact]
    public void An_obsidian_image_is_found_in_the_attachment_folder_its_settings_name()
    {
        var vault = Path.Combine(_dir, "vault");
        Directory.CreateDirectory(Path.Combine(vault, ".obsidian"));
        File.WriteAllText(Path.Combine(vault, ".obsidian", "app.json"), "{ \"attachmentFolderPath\": \"Attachments\" }");
        var pic = Path.GetFullPath(Png("vault/Attachments/g.png", 4, 4, c => Fill(c, SKColors.Red, 0, 0, 4, 4)));
        var note = Path.Combine(vault, "course", "week1.md");
        Assert.Equal(pic, MarkdownImages.Resolve(note, "g.png", true)?.Path);
        Assert.Null(MarkdownImages.Resolve(note, "g.png", false));            // a plain link is relative to the note
        Assert.Equal(pic, MarkdownImages.Resolve(note, "../Attachments/g.png", false)?.Path);
    }

    [Theory]
    [InlineData("![[photo.jpg]]", "![[photo.jpg]]")]
    [InlineData("See [[Week 3 notes]] too", "[[Week 3 notes]]")]
    [InlineData("![a graph](img/graph.png)", "](img/graph.png)")]
    public void File_and_note_names_in_links_are_not_spell_checked(string line, string skipped) =>
        Assert.Contains(EditorView.NotProseRx.Matches(line), m => m.Value == skipped);

    // ---- in the editor ----

    [Fact]
    public void An_image_line_shows_its_picture_in_the_reading_view_and_the_text_moves_below_it()
    {
        Png("pic.png", 4, 4, c => Fill(c, SKColors.Red, 0, 0, 4, 4));
        // the fake module: 85 × 45 pixels, 9 cells by 3 rows of 10 × 20
        FormulaImages.Decoder = (path, maxW, maxH) => (Enumerable.Repeat(unchecked((int)0xFFFF0000), 85 * 45).ToArray(), 85, 45, null);
        FormulaImages.SetTerminal(true, (10, 20));
        var md = Path.Combine(_dir, "n.md");
        File.WriteAllText(md, "Before\n![a picture](pic.png)\nAfter");
        var e = new EditorView { WrapProse = true };
        e.Load(md);
        e.SetView(MdView.Reading);
        e.SetArea(new Rect(0, 0, 40, 12));
        e.ApplyTheme(Themes.Get("dark"));
        var s = new Screen(40, 12);
        e.Draw(s);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!FormulaImages.Idle) { Assert.True(DateTime.UtcNow < until); Thread.Sleep(10); }
        FormulaImages.Taken();
        e.FormulaPicturesChanged();
        e.Draw(s);
        var at = s.PictureAt(Disp.PictureIndent, 1);
        Assert.NotNull(at);
        Assert.True(at.Value.Picture.Colour);
        Assert.Equal((9, 3), (at.Value.Picture.Cols, at.Value.Picture.Rows));
        Assert.Equal(2, s.PictureAt(Disp.PictureIndent, 3)?.Row);
        Assert.Equal("After", new string(Enumerable.Range(0, 40).Select(x => s.CharAt(x, 4)).ToArray()).TrimEnd());
    }

    [Fact]
    public void With_images_off_or_no_sixel_the_line_stays_text()
    {
        Png("pic.png", 4, 4, c => Fill(c, SKColors.Red, 0, 0, 4, 4));
        FormulaImages.Decoder = (path, maxW, maxH) => (new int[100], 10, 10, null);
        FormulaImages.SetTerminal(false, null);
        Assert.False(FormulaImages.ImagesActive);
        FormulaImages.SetTerminal(true, (10, 20));
        FormulaImages.ImagesWanted = false;
        try { Assert.False(FormulaImages.ImagesActive); }
        finally { FormulaImages.ImagesWanted = true; }
        Assert.True(FormulaImages.ImagesActive);
    }
}
