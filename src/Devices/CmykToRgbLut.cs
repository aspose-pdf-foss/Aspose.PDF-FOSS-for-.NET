using System.Reflection;

namespace Aspose.Pdf.Devices;

/// <summary>
/// CMYK→sRGB conversion via a baked-in 9⁴ ICC-based lookup table.
///
/// Background: PDF 32000 §6.2.4.2's algebraic CMYK→RGB formula
/// <c>R = (1−C)(1−K)</c> is *spec-correct* but produces results that
/// differ markedly from what GDI+ / Acrobat / Aspose.Pdf
/// emit when rasterising CMYK fills. Those products run CMYK through
/// an ICC profile (typically a SWOP-style transform) — non-linear,
/// per-channel, with explicit dot-gain compensation. The result is
/// usually 20-30% brighter per channel than the naïve subtractive
/// formula, which is what the GDI+-rendered visual-test templates
/// have baked in.
///
/// Implementation: at build time we sample 9 levels per CMYK channel
/// (6561 samples) through macOS's <c>Generic CMYK Profile.icc</c>
/// chained to <c>sRGB Profile.icc</c> (via ImageMagick) and store the
/// resulting RGB triples (~19 KB) as an <c>EmbeddedResource</c>. At
/// runtime we quadrilinear-interpolate between the 16 nearest grid
/// corners.
///
/// On the canonical regression target — <c>/IC [0.2 0.3 0.7 0.6]</c>
/// — this gives sRGB (91, 81, 50) versus the
/// GDI+ template's (102, 102, 51). All three channels land within
/// the visual-comparison tolerance (colorDelta=30), where the naïve
/// formula's (82, 71, 31) was off by 31 on G.
/// </summary>
internal static class CmykToRgbLut
{
    private const int Grid = 9; // 9 samples per channel — matches the bake script
    private const int Stride = Grid * Grid * Grid; // K-stride: K varies fastest in cmyk-grid layout
    private static byte[]? _table;
    private static readonly object _lock = new();

    private static byte[] Table
    {
        get
        {
            if (_table is not null) return _table;
            lock (_lock)
            {
                if (_table is not null) return _table;
                var asm = typeof(CmykToRgbLut).Assembly;
                // Default resource name: <RootNamespace>.<relative-path-with-dots>
                using var stream = asm.GetManifestResourceStream("Aspose.Pdf.Devices.CmykToRgbLut.bin")
                    ?? throw new InvalidOperationException(
                        "Embedded CMYK LUT resource not found. Check csproj <EmbeddedResource> entry.");
                var data = new byte[stream.Length];
                stream.ReadExactly(data);
                _table = data;
                return _table;
            }
        }
    }

    /// <summary>
    /// Convert CMYK (each channel in 0..1) to sRGB bytes via quadrilinear
    /// interpolation. Inputs are clamped to [0,1] so callers don't have
    /// to defensively guard.
    /// </summary>
    public static (byte r, byte g, byte b) Convert(double c, double m, double y, double k)
    {
        // Clamp to [0, 1].
        if (c < 0) c = 0; else if (c > 1) c = 1;
        if (m < 0) m = 0; else if (m > 1) m = 1;
        if (y < 0) y = 0; else if (y > 1) y = 1;
        if (k < 0) k = 0; else if (k > 1) k = 1;

        var lut = Table;

        // Map [0,1] to [0, Grid-1] grid coords.
        var cf = c * (Grid - 1);
        var mf = m * (Grid - 1);
        var yf = y * (Grid - 1);
        var kf = k * (Grid - 1);

        var c0 = (int)cf; if (c0 >= Grid - 1) c0 = Grid - 2;
        var m0 = (int)mf; if (m0 >= Grid - 1) m0 = Grid - 2;
        var y0 = (int)yf; if (y0 >= Grid - 1) y0 = Grid - 2;
        var k0 = (int)kf; if (k0 >= Grid - 1) k0 = Grid - 2;

        var dc = cf - c0;
        var dm = mf - m0;
        var dy = yf - y0;
        var dk = kf - k0;

        // Quadrilinear blend across 16 corners. Bake-script layout:
        // index = ((c × Grid + m) × Grid + y) × Grid + k → triple at idx*3.
        double r = 0, g = 0, b = 0;
        for (var dci = 0; dci <= 1; dci++)
        for (var dmi = 0; dmi <= 1; dmi++)
        for (var dyi = 0; dyi <= 1; dyi++)
        for (var dki = 0; dki <= 1; dki++)
        {
            var ci = c0 + dci;
            var mi = m0 + dmi;
            var yi = y0 + dyi;
            var ki = k0 + dki;
            var idx = (((ci * Grid) + mi) * Grid + yi) * Grid + ki;
            var p = idx * 3;
            var w = (dci == 1 ? dc : 1 - dc)
                  * (dmi == 1 ? dm : 1 - dm)
                  * (dyi == 1 ? dy : 1 - dy)
                  * (dki == 1 ? dk : 1 - dk);
            r += lut[p]     * w;
            g += lut[p + 1] * w;
            b += lut[p + 2] * w;
        }

        return ((byte)Math.Clamp(r + 0.5, 0, 255),
                (byte)Math.Clamp(g + 0.5, 0, 255),
                (byte)Math.Clamp(b + 0.5, 0, 255));
    }
}
