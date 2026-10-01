namespace Aspose.Pdf.Devices;

/// <summary>
/// How a PDF pen lands on the page, shared by both rasterisers so the GDI+ renderer and
/// the software renderer draw a stroke to the same laws: how wide the pen comes out under
/// a transform, when a transform is too lopsided to carry the pen through it, and the
/// dash pattern the device actually draws.
/// </summary>
internal static class PenGeometry
{
    /// <summary>Beyond this ratio of stretches the pen is not carried through the
    /// transform: it stays round on the page, as wide as the transform makes a
    /// horizontal unit.</summary>
    internal const double AnisotropyLimit = 1.5;

    /// <summary>The ratio a degenerate matrix reports: flatter than any real one.</summary>
    private const double DegenerateAnisotropy = 1e9;

    /// <summary>Below this the smaller stretch counts as none at all.</summary>
    private const double NegligibleStretch = 1e-9;

    /// <summary>The shortest dash element the device draws, in pen widths.</summary>
    private const float MinimumDashElement = 1f;

    /// <summary>The cap that rounds off both ends of every dash.</summary>
    internal const int RoundCap = 1;

    /// <summary>The cap that squares off the ends of a line past its end points.</summary>
    internal const int SquareCap = 2;

    /// <summary>The join that rounds a corner.</summary>
    internal const int RoundJoin = 1;

    /// <summary>The join that cuts a corner straight across.</summary>
    internal const int BevelJoin = 2;

    /// <summary>
    /// Ratio of the CTM's singular values (max/min stretch). 1 for uniform scale and
    /// rotation; grows as the matrix squashes one axis relative to the other.
    /// Degenerate matrices report a huge ratio (callers treat them like "very anisotropic",
    /// where the widen-and-fill path still produces the right geometry).
    /// </summary>
    internal static double CtmAnisotropy(double[] m)
    {
        double sMax = CtmMaxScale(m);
        if (sMax <= 0) return 1;
        double det = Math.Abs(m[0] * m[3] - m[1] * m[2]);
        double sMin = det / sMax;
        return sMin > NegligibleStretch ? sMax / sMin : DegenerateAnisotropy;
    }

    /// <summary>The largest factor the matrix stretches any direction by.</summary>
    internal static double CtmMaxScale(double[] m)
    {
        double a = m[0], b = m[1], c = m[2], d = m[3];
        double e = a * a + b * b + c * c + d * d;
        double det = Math.Abs(a * d - b * c);
        // σmax² = (E + √(E²−4·det²))/2
        double disc = Math.Sqrt(Math.Max(0, e * e - 4 * det * det));
        return Math.Sqrt((e + disc) / 2);
    }

    /// <summary>How wide a nominal pen comes out on the page: the length the matrix
    /// gives a horizontal unit. Measured against the reference renderer — a square
    /// stroked under `4 0 0 1 0 0 cm` gets a frame four times its nominal width all
    /// the way round, while an ellipse under `1 0 0 2 0 0 cm` keeps its nominal width
    /// on every side. The pen stays round on the page either way.</summary>
    internal static double CtmPenScale(double[] m) => Math.Sqrt(m[0] * m[0] + m[1] * m[1]);

    /// <summary>
    /// The dash pattern the device draws, in PEN WIDTHS, or null for a solid line (no
    /// array, or one whose every element is zero). Each element is at least one pen
    /// width — measured on the expected render over nine (pattern, width) combinations:
    /// the rendered period is max(on,w) + max(off,w) every time, and the duty cycle
    /// agrees ([3 2] at w3 draws as [3 3], [3 2] at w5 as [5 5], while [6 4] at w3 is
    /// already above the floor and renders nominally). A dash of NO length gives the
    /// stretch back out of the gap that follows it, and a ROUND cap has its own pattern
    /// (<see cref="RoundCapDashPattern"/>).
    /// </summary>
    internal static float[]? DashPatternInPenWidths(double[] dashArray, double lineWidth, int lineCap)
    {
        if (dashArray.Length == 0) return null;
        var w = (float)lineWidth;
        if (w <= 0) w = 1;
        var pattern = new float[dashArray.Length];
        var allZero = true;
        for (int i = 0; i < pattern.Length; i++)
        {
            var nominal = dashArray[i] / w;
            if (nominal > 0) allZero = false;
            pattern[i] = (float)Math.Max(nominal, 1.0);
        }

        KeepDashPeriod(dashArray, pattern);
        if (lineCap == RoundCap) pattern = RoundCapDashPattern(dashArray, w);
        return allZero ? null : pattern;
    }

    /// <summary>Give back, out of the gap that follows it, the length a dash of NO
    /// length was stretched by. A dot laid out as `[0 36] 0 d` is drawn by the cap and
    /// takes a pen width whatever the array says, but the period the program asked for
    /// — one dot every 36 points, not every 40 — is what must survive.</summary>
    private static void KeepDashPeriod(double[] nominal, float[] pattern)
    {
        for (var i = 0; i + 1 < pattern.Length; i += 2)
        {
            if (nominal[i] > 0) continue;
            pattern[i + 1] = Math.Max(pattern[i + 1] - MinimumDashElement, MinimumDashElement);
        }
    }

    /// <summary>The dash pattern a ROUND cap draws, in pen widths. The cap puts half a
    /// pen on each end of every dash, so the ink is the element plus a WHOLE pen and the
    /// gap that follows gives that pen back — never below one pen width, the shortest
    /// element the device draws. Measured on the expected renders: [54 54] at width 36
    /// draws 90 on and 36 off, [36 54] at width 18 draws 54 on and 36 off, and a dot laid
    /// out as [0 54] at width 18 keeps its one-pen dot every 54. This is the general form
    /// of what <see cref="KeepDashPeriod"/> does for a dash of no length.</summary>
    private static float[] RoundCapDashPattern(double[] nominal, float width)
    {
        var pattern = new float[nominal.Length];
        for (var i = 0; i < pattern.Length; i++)
        {
            var element = nominal[i] / width;
            pattern[i] = (float)(i % 2 == 0
                ? element + MinimumDashElement
                : Math.Max(element - MinimumDashElement, MinimumDashElement));
        }

        return pattern;
    }
}
