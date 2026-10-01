using System.Reflection;

namespace Aspose.Pdf.Operators;

/// <summary>
/// The RGB a CMYK colour operator reports through <c>getColor()</c>: an 11-level grid per
/// channel (0.0, 0.1, ... 1.0) sampled from the reference's own answers (controlled inputs
/// in, its RGB out - a SWOP-like profile transform: pure K reads (35, 31, 32), pure C
/// (0, 173, 239), (0.1, 0.2, 0.2, 1) reads (31, 23, 21)), quadrilinearly interpolated
/// between grid points. Grid points reproduce the reference exactly.
/// </summary>
internal static class CmykOperatorColors
{
    private const int Grid = 11;
    private const string ResourceName = "Aspose.Pdf.Operators.CmykOperatorColors.bin";
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
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(ResourceName)
                    ?? throw new InvalidOperationException("Missing embedded resource " + ResourceName);
                var data = new byte[stream.Length];
                var read = 0;
                while (read < data.Length)
                {
                    var n = stream.Read(data, read, data.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                _table = data;
                return _table;
            }
        }
    }

    /// <summary>The reference's RGB for the operator's CMYK components, each clamped to [0, 1].</summary>
    public static (int r, int g, int b) Convert(double c, double m, double y, double k)
    {
        var lut = Table;
        var (c0, dc) = Cell(c);
        var (m0, dm) = Cell(m);
        var (y0, dy) = Cell(y);
        var (k0, dk) = Cell(k);
        double r = 0, g = 0, b = 0;
        for (var ci = 0; ci <= 1; ci++)
        for (var mi = 0; mi <= 1; mi++)
        for (var yi = 0; yi <= 1; yi++)
        for (var ki = 0; ki <= 1; ki++)
        {
            var w = (ci == 1 ? dc : 1 - dc) * (mi == 1 ? dm : 1 - dm) * (yi == 1 ? dy : 1 - dy) * (ki == 1 ? dk : 1 - dk);
            if (w == 0) continue;
            var p = ((((c0 + ci) * Grid + m0 + mi) * Grid + y0 + yi) * Grid + k0 + ki) * 3;
            r += w * lut[p];
            g += w * lut[p + 1];
            b += w * lut[p + 2];
        }
        return ((int)Math.Round(r), (int)Math.Round(g), (int)Math.Round(b));
    }

    /// <summary>The grid cell a component falls in and its fraction across it; a value on a
    /// grid line has a zero fraction, so the line's own sample answers.</summary>
    private static (int index, double fraction) Cell(double v)
    {
        if (v < 0) v = 0; else if (v > 1) v = 1;
        var f = v * (Grid - 1);
        var i = (int)Math.Floor(f + 1e-9);
        if (i >= Grid - 1) return (Grid - 2, 1);
        var frac = f - i;
        if (frac < 1e-9) frac = 0;
        return (i, frac);
    }
}
