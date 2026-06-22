namespace Aspose.Pdf;

/// <summary>
/// Converts CIE L*a*b* colour (as used by a PDF /Lab colour space and by /Separation
/// or /DeviceN spaces whose alternate space is /Lab) to sRGB. PDF /Lab spaces use the
/// D50 white point, so a fixed D50 reference white is assumed; this is an approximation
/// good enough for the spot-colour / Pantone fills these spaces carry in practice.
/// </summary>
internal static class LabColor
{
    /// <summary>L in [0,100], a/b roughly [-128,127] → r,g,b in [0,1].</summary>
    public static void ToRgb(double l, double a, double bb, out double r, out double g, out double b)
    {
        // L*a*b* → CIE XYZ (D50).
        var fy = (l + 16.0) / 116.0;
        var fx = fy + a / 500.0;
        var fz = fy - bb / 200.0;
        const double xn = 0.9642, yn = 1.0, zn = 0.8249; // D50 reference white
        var x = xn * Finv(fx);
        var y = yn * Finv(fy);
        var z = zn * Finv(fz);

        // XYZ (D50) → linear sRGB (Bradford-adapted D50→sRGB matrix).
        var rl = 3.1338561 * x - 1.6168667 * y - 0.4906146 * z;
        var gl = -0.9787684 * x + 1.9161415 * y + 0.0334540 * z;
        var bl = 0.0719453 * x - 0.2289914 * y + 1.4052427 * z;

        r = Gamma(rl);
        g = Gamma(gl);
        b = Gamma(bl);
    }

    private static double Finv(double t)
        => t > 6.0 / 29.0 ? t * t * t : 3.0 * (6.0 / 29.0) * (6.0 / 29.0) * (t - 4.0 / 29.0);

    private static double Gamma(double c)
    {
        c = c < 0 ? 0 : (c > 1 ? 1 : c);
        return c <= 0.0031308 ? 12.92 * c : 1.055 * System.Math.Pow(c, 1.0 / 2.4) - 0.055;
    }
}
