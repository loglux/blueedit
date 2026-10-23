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

    /// <summary>Coverage (0–255) of a pixel.</summary>
    public byte At(int x, int y) => _mask[y * Cols * CellW + x];

    /// <summary>Rows <paramref name="first"/>.. of the picture as one Sixel image (kept: a picture is sent again on every
    /// scroll), <paramref name="fg"/> over <paramref name="bg"/> as 0xRRGGBB (-1: the terminal's default).</summary>
    public string Sixel(int first, int count, int fg, int bg)
    {
        var key = (first, count, fg, bg);
        if (_sixel.TryGetValue(key, out var hit)) return hit;
        if (_sixel.Count > 16) _sixel.Clear();
        return _sixel[key] = Tui.Sixel.Encode(_mask, Cols * CellW, first * CellH, count * CellH, fg < 0 ? 0xCCCCCC : fg, bg < 0 ? 0x000000 : bg);
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
