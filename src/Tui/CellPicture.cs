using System.Globalization;
using System.Text;

namespace Tui;

/// <summary>
/// A picture that covers whole cells (a typeset formula), for terminals that show Sixel graphics. It is a coverage
/// mask, not colours: <see cref="Sixel"/> paints it in the cell style's foreground over its background, so it follows
/// the theme. The mask is <see cref="Cols"/> × <see cref="CellW"/> pixels wide and <see cref="Rows"/> ×
/// <see cref="CellH"/> tall, the cell size the terminal reported.
/// </summary>
public sealed class CellPicture
{
    readonly byte[] _mask;
    readonly int[]? _palette;                     // a picture in colour (an image): _mask holds palette indexes
    readonly Dictionary<(int, int, int, int), string> _sixel = new();

    public int Cols { get; }
    public int Rows { get; }
    public int CellW { get; }
    public int CellH { get; }

    public CellPicture(byte[] mask, int cols, int rows, int cellW, int cellH)
    {
        if (mask.Length != cols * cellW * rows * cellH) throw new ArgumentException("the mask is not cols × rows cells", nameof(mask));
        _mask = mask; Cols = cols; Rows = rows; CellW = cellW; CellH = cellH;
    }

    /// <summary>A picture in colour: <paramref name="argb"/> is 0xAARRGGBB pixels, <paramref name="cols"/> ×
    /// <paramref name="cellW"/> wide; what is mostly transparent shows the cell's background. Its colours are brought
    /// to at most 255 here, once (<see cref="Tui.Sixel.Quantize"/>).</summary>
    public CellPicture(int[] argb, int cols, int rows, int cellW, int cellH)
    {
        if (argb.Length != cols * cellW * rows * cellH) throw new ArgumentException("the pixels are not cols × rows cells", nameof(argb));
        (_mask, _palette) = Tui.Sixel.Quantize(argb, cols * cellW);
        Cols = cols; Rows = rows; CellW = cellW; CellH = cellH;
    }

    /// <summary>In colour (an image) rather than a coverage mask painted in the text's colour.</summary>
    public bool Colour => _palette != null;

    /// <summary>Coverage (0–255) of a pixel; for a picture in colour, its palette index (255: background).</summary>
    public byte At(int x, int y) => _mask[y * Cols * CellW + x];

    /// <summary>Rows <paramref name="first"/>.. of the picture as one Sixel image (kept: a picture is sent again on every
    /// scroll), <paramref name="fg"/> over <paramref name="bg"/> as 0xRRGGBB (-1: the terminal's default); a picture in
    /// colour takes only the background, for what is transparent.</summary>
    public string Sixel(int first, int count, int fg, int bg)
    {
        var key = (first, count, fg, bg);
        if (_sixel.TryGetValue(key, out var hit)) return hit;
        if (_sixel.Count > 16) _sixel.Clear();
        return _sixel[key] = _palette != null
            ? Tui.Sixel.EncodeIndexed(_mask, _palette, Cols * CellW, first * CellH, count * CellH, bg < 0 ? 0x000000 : bg)
            : Tui.Sixel.Encode(_mask, Cols * CellW, first * CellH, count * CellH, fg < 0 ? 0xCCCCCC : fg, bg < 0 ? 0x000000 : bg);
    }
}

/// <summary>Sixel output: six rows of pixels per character, one pass per colour (DEC VT340, as xterm and Windows
/// Terminal 1.22+ read it).</summary>
public static class Sixel
{
    /// <summary>Shades between background and foreground: antialiased edges without a large palette.</summary>
    public const int Levels = 16;

    /// <summary>Rows <paramref name="top"/>..+<paramref name="height"/> of a coverage mask <paramref name="width"/>
    /// pixels wide, every pixel painted (background included, so whatever was under it is covered).</summary>
    public static string Encode(byte[] mask, int width, int top, int height, int fg, int bg)
    {
        var sb = new StringBuilder(4096);
        // P2 = 1: pixels not painted stay as they are; "1;1 = square pixels, then the size
        sb.Append("\u001bP0;1;0q\"1;1;").Append(width.ToString(CultureInfo.InvariantCulture)).Append(';').Append(height.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < Levels; i++)
        {
            int c = Mix(bg, fg, i);
            sb.Append('#').Append(i.ToString(CultureInfo.InvariantCulture)).Append(";2;")
              .Append(Pct(c >> 16)).Append(';').Append(Pct((c >> 8) & 255)).Append(';').Append(Pct(c & 255));
        }
        var level = new byte[width * 6];
        var used = new bool[Levels];
        for (int band = 0; band < height; band += 6)
        {
            Array.Clear(used);
            int rows = Math.Min(6, height - band);
            for (int k = 0; k < 6; k++)
                for (int x = 0; x < width; x++)
                {
                    byte l = k < rows ? (byte)((mask[(top + band + k) * width + x] * (Levels - 1) + 127) / 255) : byte.MaxValue;
                    level[k * width + x] = l;
                    if (l != byte.MaxValue) used[l] = true;
                }
            bool firstColour = true;
            for (int i = 0; i < Levels; i++)
            {
                if (!used[i]) continue;
                if (!firstColour) sb.Append('$');                     // back to the band's start for the next colour
                firstColour = false;
                sb.Append('#').Append(i.ToString(CultureInfo.InvariantCulture));
                char run = '\0'; int n = 0;
                for (int x = 0; x < width; x++)
                {
                    int bits = 0;
                    for (int k = 0; k < rows; k++) if (level[k * width + x] == i) bits |= 1 << k;
                    char ch = (char)(63 + bits);
                    if (ch == run) { n++; continue; }
                    Run(sb, run, n);
                    run = ch; n = 1;
                }
                if (run != '?') Run(sb, run, n);                       // nothing painted at the end: left out
            }
            if (band + 6 < height) sb.Append('-');
        }
        return sb.Append("\u001b\\").ToString();
    }

    /// <summary>The palette index of the background in <see cref="Quantize"/>'s output.</summary>
    public const byte Background = 255;

    /// <summary>
    /// Pixels (0xAARRGGBB) brought to at most 255 colours, as Sixel's registers hold: a picture with that few (a diagram,
    /// a chart, a screenshot) keeps them exactly; one with more (a photo) goes to a fixed 6 × 7 × 6 cube, with the error
    /// spread to the next pixels (Floyd–Steinberg), which keeps gradients from banding. A pixel under half opaque is the
    /// background (<see cref="Background"/>); a half-transparent edge is taken as opaque.
    /// </summary>
    public static (byte[] Index, int[] Palette) Quantize(int[] argb, int width)
    {
        var index = new byte[argb.Length];
        var exact = new Dictionary<int, byte>();
        bool few = true;
        foreach (var p in argb)
        {
            if ((uint)p >> 24 < 128) continue;
            int rgb = p & 0xFFFFFF;
            if (exact.ContainsKey(rgb)) continue;
            if (exact.Count == 255) { few = false; break; }
            exact[rgb] = (byte)exact.Count;
        }
        if (few)
        {
            for (int i = 0; i < argb.Length; i++) index[i] = (uint)argb[i] >> 24 < 128 ? Background : exact[argb[i] & 0xFFFFFF];
            var palette = new int[exact.Count];
            foreach (var (rgb, i) in exact) palette[i] = rgb;
            return (index, palette);
        }
        var (cube, nearest) = MedianCut(argb);
        int height = argb.Length / width;
        // the error carried to this row and the next, per channel
        var err = new int[2, width + 2, 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int p = argb[y * width + x];
                if ((uint)p >> 24 < 128) { index[y * width + x] = Background; continue; }
                int r = Math.Clamp(((p >> 16) & 255) + err[0, x + 1, 0] / 16, 0, 255);
                int g = Math.Clamp(((p >> 8) & 255) + err[0, x + 1, 1] / 16, 0, 255);
                int b = Math.Clamp((p & 255) + err[0, x + 1, 2] / 16, 0, 255);
                int c = nearest((r >> 3 << 10) | (g >> 3 << 5) | (b >> 3));
                index[y * width + x] = (byte)c;
                Spread(r - (cube[c] >> 16), 0); Spread(g - ((cube[c] >> 8) & 255), 1); Spread(b - (cube[c] & 255), 2);

                void Spread(int e, int ch)
                {
                    err[0, x + 2, ch] += e * 7;
                    err[1, x, ch] += e * 3;
                    err[1, x + 1, ch] += e * 5;
                    err[1, x + 2, ch] += e;
                }
            }
            for (int x = 0; x < width + 2; x++)
                for (int ch = 0; ch < 3; ch++) { err[0, x, ch] = err[1, x, ch]; err[1, x, ch] = 0; }
        }
        return (index, cube);
    }

    /// <summary>
    /// A palette of 255 made for the picture (median cut): the colours, 5 bits a channel, are split into boxes, the box
    /// with the most pixels across the widest spread halved at its median each time; a box's colour is the mean of its
    /// pixels, so a colour that fills a box alone (a chart's bar) is kept exactly. A fixed cube of colours wasted most of
    /// its entries on colours the picture has not and covered a chart's flat colours with dither.
    /// </summary>
    static (int[] Palette, Func<int, int> Nearest) MedianCut(int[] argb)
    {
        var count = new int[32768];
        var sum = new long[32768 * 3];
        foreach (var p in argb)
        {
            if ((uint)p >> 24 < 128) continue;
            int r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;
            int bin = (r >> 3 << 10) | (g >> 3 << 5) | (b >> 3);
            count[bin]++; sum[bin * 3] += r; sum[bin * 3 + 1] += g; sum[bin * 3 + 2] += b;
        }
        var boxes = new List<int[]> { Enumerable.Range(0, 32768).Where(i => count[i] > 0).ToArray() };
        static int Channel(int bin, int ch) => (bin >> (10 - 5 * ch)) & 31;
        (int Axis, int Spread) Widest(int[] box)
        {
            int best = 0, spread = -1;
            for (int ch = 0; ch < 3; ch++)
            {
                int lo = 31, hi = 0;
                foreach (var bin in box) { int v = Channel(bin, ch); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
                if (hi - lo > spread) (best, spread) = (ch, hi - lo);
            }
            return (best, spread);
        }
        while (boxes.Count < 255)
        {
            int pick = -1; long score = 0; int axis = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Length < 2) continue;
                var (a, spread) = Widest(boxes[i]);
                long pixels = 0;
                foreach (var bin in boxes[i]) pixels += count[bin];
                long s = pixels * (spread + 1);
                if (s > score) (pick, score, axis) = (i, s, a);
            }
            if (pick < 0) break;
            var sorted = boxes[pick].OrderBy(bin => Channel(bin, axis)).ToArray();
            long half = sorted.Sum(bin => (long)count[bin]) / 2, run = 0;
            int cut = 1;
            for (; cut < sorted.Length - 1; cut++) { run += count[sorted[cut - 1]]; if (run >= half) break; }
            boxes[pick] = sorted[..cut];
            boxes.Add(sorted[cut..]);
        }
        var palette = boxes.Select(box =>
        {
            long n = 0, r = 0, g = 0, b = 0;
            foreach (var bin in box) { n += count[bin]; r += sum[bin * 3]; g += sum[bin * 3 + 1]; b += sum[bin * 3 + 2]; }
            return (int)((r / n) << 16 | (g / n) << 8 | (b / n));
        }).ToArray();
        // the nearest palette colour of a bin, worked out when first needed (dithering reaches bins the picture has not)
        var table = new short[32768];
        Array.Fill(table, (short)-1);
        int Nearest(int bin)
        {
            if (table[bin] >= 0) return table[bin];
            int r = (Channel(bin, 0) << 3) + 4, g = (Channel(bin, 1) << 3) + 4, b = (Channel(bin, 2) << 3) + 4;
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                int dr = r - (palette[i] >> 16), dg = g - ((palette[i] >> 8) & 255), db = b - (palette[i] & 255);
                int d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;          // the eye: green most, then blue, then red
                if (d < bestD) (best, bestD) = (i, d);
            }
            return table[bin] = (short)best;
        }
        return (palette, Nearest);
    }

    /// <summary>Rows <paramref name="top"/>..+<paramref name="height"/> of a picture in palette indexes, every pixel
    /// painted (<see cref="Background"/> in <paramref name="bg"/>). One pass per colour a band uses, the band's pixels
    /// sorted to their colours first, so a photo's 200 colours cost no more than its width per colour.</summary>
    public static string EncodeIndexed(byte[] index, int[] palette, int width, int top, int height, int bg)
    {
        var sb = new StringBuilder(16384);
        sb.Append("\u001bP0;1;0q\"1;1;").Append(width.ToString(CultureInfo.InvariantCulture)).Append(';').Append(height.ToString(CultureInfo.InvariantCulture));
        var defined = new bool[256];
        for (int i = top * width; i < (top + height) * width; i++) defined[index[i]] = true;
        for (int c = 0; c < 256; c++)
        {
            if (!defined[c]) continue;
            int rgb = c == Background ? bg : palette[c];
            sb.Append('#').Append(c.ToString(CultureInfo.InvariantCulture)).Append(";2;")
              .Append(Pct(rgb >> 16)).Append(';').Append(Pct((rgb >> 8) & 255)).Append(';').Append(Pct(rgb & 255));
        }
        var bits = new byte[256 * width];
        var used = new List<int>(256);
        var seen = new bool[256];
        for (int band = 0; band < height; band += 6)
        {
            int rows = Math.Min(6, height - band);
            for (int k = 0; k < rows; k++)
            {
                int row = (top + band + k) * width;
                for (int x = 0; x < width; x++)
                {
                    int c = index[row + x];
                    bits[c * width + x] |= (byte)(1 << k);
                    if (!seen[c]) { seen[c] = true; used.Add(c); }
                }
            }
            for (int u = 0; u < used.Count; u++)
            {
                int c = used[u];
                if (u > 0) sb.Append('$');
                sb.Append('#').Append(c.ToString(CultureInfo.InvariantCulture));
                char run = '\0'; int n = 0;
                for (int x = 0; x < width; x++)
                {
                    char ch = (char)(63 + bits[c * width + x]);
                    bits[c * width + x] = 0;
                    if (ch == run) { n++; continue; }
                    Run(sb, run, n);
                    run = ch; n = 1;
                }
                if (run != '?') Run(sb, run, n);
                seen[c] = false;
            }
            used.Clear();
            if (band + 6 < height) sb.Append('-');
        }
        return sb.Append("\u001b\\").ToString();
    }

    static void Run(StringBuilder sb, char ch, int n)
    {
        if (n <= 0) return;
        if (n > 3) sb.Append('!').Append(n.ToString(CultureInfo.InvariantCulture)).Append(ch);
        else sb.Append(ch, n);
    }

    static int Mix(int bg, int fg, int level)
    {
        int Channel(int shift) => (((bg >> shift) & 255) * (Levels - 1 - level) + ((fg >> shift) & 255) * level + (Levels - 1) / 2) / (Levels - 1);
        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    static string Pct(int v) => ((v * 100 + 127) / 255).ToString(CultureInfo.InvariantCulture);
}
