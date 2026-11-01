namespace Aspose.Pdf.Text;

internal sealed partial class Type1GlyphSource
{
    /// <summary>What the header's font matrix does to an outline beyond scaling it (an oblique
    /// face written as the upright outlines under a sheared matrix), or null.</summary>
    private (double A, double B, double C, double D)? _shape;

    private const int MatrixShapeNumbers = 4;

    private static int ResolveUnitsPerEm(string header)
    {
        // /FontMatrix [a b c d e f] readonly def — a usually 0.001 → UnitsPerEm 1000.
        var m = SafeRegex.Match(header,
            @"/FontMatrix\s*\[\s*([0-9.+\-eE]+)\b");
        if (!m.Success) return 1000;
        if (!double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var a)) return 1000;
        if (a <= 0) return 1000;
        return (int)Math.Round(1.0 / a);
    }

    /// <summary>The shape of /FontMatrix [a b c d e f], relative to the horizontal scale the
    /// em is taken from; null for the usual plain scale or an unreadable matrix.</summary>
    private static (double A, double B, double C, double D)? ResolveShape(string header)
    {
        var m = SafeRegex.Match(header,
            @"/FontMatrix\s*\[\s*([0-9.+\-eE]+)\s+([0-9.+\-eE]+)\s+([0-9.+\-eE]+)\s+([0-9.+\-eE]+)\b");
        if (!m.Success) return null;
        var v = new double[MatrixShapeNumbers];
        for (var i = 0; i < v.Length; i++)
        {
            if (!double.TryParse(m.Groups[i + 1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v[i])) return null;
        }
        return v[0] > 0 ? CffGlyphSource.ShapeOfMatrix(v[0], v[1], v[2], v[3], emIsHorizontal: true) : null;
    }
}
