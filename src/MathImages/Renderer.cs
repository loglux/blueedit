using System.Text.RegularExpressions;
using CSharpMath.SkiaSharp;
using SkiaSharp;

namespace BlueEdit.MathImages;

/// <summary>
/// The module's one entry point, found by name by the editor (FormulaImages.cs), which runs it on a thread of its own.
/// Only types of the runtime cross between the two, so the editor needs no reference to this assembly.
/// </summary>
public static class Renderer
{
    /// <summary>The shape of <see cref="Render"/>; the editor refuses a module with another.</summary>
    public const int Api = 1;

    /// <summary>
    /// A formula typeset at <paramref name="fontSize"/> pixels as a coverage mask (0–255, one byte a pixel, rows of
    /// <c>Width</c>), cut to what is drawn; or null and CSharpMath's message when it cannot read the formula.
    /// </summary>
    public static (byte[]? Mask, int Width, int Height, string? Error) Render(string latex, float fontSize)
    {
        var painter = new MathPainter { LaTeX = Normalize(latex), FontSize = fontSize, TextColor = SKColors.White, AntiAlias = true };
        var box = painter.Measure();
        if (painter.ErrorMessage is { } error) return (null, 0, 0, error);
        // drawn with room around it, then cut to its pixels: CSharpMath's measure is short for some (the width of
        // aligned and cases, a script on a \right bracket above the ascent)
        int pad = (int)Math.Ceiling(fontSize * 2);
        int w = (int)Math.Ceiling(box.Width * 1.5f) + 2 * pad, h = (int)Math.Ceiling(box.Height) + 2 * pad;
        if (box.Width <= 0 || box.Height <= 0 || w > 12000 || h > 4000) return (null, 0, 0, "empty or too large");
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Alpha8, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            painter.Draw(canvas, pad, pad - box.Y);               // the baseline's place: box.Y is minus the ascent
        }
        var pixels = bitmap.GetPixelSpan();
        int stride = bitmap.RowBytes;
        int x0 = w, x1 = -1, y0 = h, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (pixels[y * stride + x] != 0) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        if (x1 < 0) return (null, 0, 0, "nothing drawn");
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        var mask = new byte[cw * ch];
        for (int y = 0; y < ch; y++) pixels.Slice((y0 + y) * stride + x0, cw).CopyTo(mask.AsSpan(y * cw));
        return (mask, cw, ch, null);
    }

    // CSharpMath reads less LaTeX than MathJax and KaTeX; these rewrite what notes use most into what it reads. On the
    // author's maths notes (10 October 2026) they took it from 12,595 of 17,222 display formulas read to 17,119.
    static readonly (Regex Rx, string To)[] Rewrites =
    [
        (new(@"\\begin\{(?:equation|displaymath|math)\*?\}|\\end\{(?:equation|displaymath|math)\*?\}"), " "),
        (new(@"\\(begin|end)\{(?:align|alignat|flalign|eqnarray|split|alignedat)\*?\}(?:\{\d+\})?"), @"\$1{aligned}"),
        (new(@"\\(begin|end)\{(?:gather|gathered|multline)\*?\}"), @"\$1{gather}"),
        (new(@"\\tag\*?\{([^{}]*)\}"), @"\qquad ($1)"),
        (new(@"\\(?:notag|nonumber|hline|displaystyle|textstyle|scriptstyle|limits|nolimits|[Bb]igg?[lrm]?)(?![a-zA-Z])"), " "),
        (new(@"\\prime(?![a-zA-Z])"), "'"),
        (new(@"\\(?:boldsymbol|bm)(?![a-zA-Z])"), @"\mathbf"),
        (new(@"\\mathscr(?![a-zA-Z])"), @"\mathcal"),
        (new(@"\\overrightarrow(?![a-zA-Z])"), @"\vec"),
        (new(@"\\[dtc]frac(?![a-zA-Z])"), @"\frac"),
        (new(@"\\boxed(?![a-zA-Z])"), ""),
        (new(@"(\\[a-zA-Z]+)="), "$1 ="),                       // CSharpMath takes "\theta=" for one command
    ];

    internal static string Normalize(string latex)
    {
        foreach (var (rx, to) in Rewrites) latex = rx.Replace(latex, to);
        return latex;
    }
}
