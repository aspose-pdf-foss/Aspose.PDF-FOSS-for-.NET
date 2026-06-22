namespace Aspose.Pdf.Devices.Rasterizer;

/// <summary>
/// PDF 32000 §11.3.5 blend modes. The eleven separable modes (Normal, Multiply,
/// Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight,
/// Difference, Exclusion) operate per-channel via <see cref="BlendModes.BlendChannel"/>.
/// The four non-separable modes (Hue, Saturation, Color, Luminosity) require the full
/// RGB triple and use <see cref="BlendModes.BlendTriple"/>. Callers should prefer
/// <see cref="BlendModes.Blend"/>, which dispatches to the right path.
/// </summary>
internal enum BlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity,
}

internal static class BlendModes
{
    public static BlendMode Parse(string? name) => name switch
    {
        "Multiply" => BlendMode.Multiply,
        "Screen" => BlendMode.Screen,
        "Overlay" => BlendMode.Overlay,
        "Darken" => BlendMode.Darken,
        "Lighten" => BlendMode.Lighten,
        "ColorDodge" => BlendMode.ColorDodge,
        "ColorBurn" => BlendMode.ColorBurn,
        "HardLight" => BlendMode.HardLight,
        "SoftLight" => BlendMode.SoftLight,
        "Difference" => BlendMode.Difference,
        "Exclusion" => BlendMode.Exclusion,
        "Hue" => BlendMode.Hue,
        "Saturation" => BlendMode.Saturation,
        "Color" => BlendMode.Color,
        "Luminosity" => BlendMode.Luminosity,
        _ => BlendMode.Normal,
    };

    private static bool IsNonSeparable(BlendMode m) =>
        m >= BlendMode.Hue;

    /// <summary>
    /// Apply the blend formula for any mode and return the blended source RGB.
    /// Routes to per-channel <see cref="BlendChannel"/> for separable modes and to
    /// <see cref="BlendTriple"/> for the four non-separable HSL modes. Normal mode
    /// is the identity (returns src unchanged). Inputs and outputs are 0..255 ints.
    /// </summary>
    public static void Blend(BlendMode mode, int dr, int dg, int db, int sr, int sg, int sb,
                             out int br, out int bg, out int bb)
    {
        if (mode == BlendMode.Normal)
        {
            br = sr; bg = sg; bb = sb; return;
        }
        if (IsNonSeparable(mode))
        {
            BlendTriple(mode, dr, dg, db, sr, sg, sb, out br, out bg, out bb);
            return;
        }
        br = BlendChannel(mode, dr, sr);
        bg = BlendChannel(mode, dg, sg);
        bb = BlendChannel(mode, db, sb);
    }

    /// <summary>
    /// Apply a separable blend formula B(Cb, Cs) on a single 0..255 channel.
    /// <paramref name="dst"/> is the backdrop colour (Cb), <paramref name="src"/>
    /// is the source colour (Cs). The result is the colour the source contributes
    /// to the compositor BEFORE alpha-blending; callers still alpha-blend the
    /// returned value with the destination at the source's effective alpha.
    /// PDF 32000 §11.3.5.4.
    /// </summary>
    public static int BlendChannel(BlendMode mode, int dst, int src)
    {
        switch (mode)
        {
            case BlendMode.Multiply:
                return (dst * src) / 255;
            case BlendMode.Screen:
                return dst + src - (dst * src) / 255;
            case BlendMode.Overlay:
                // PDF 32000: Overlay(Cb, Cs) = HardLight(Cs, Cb) — args swapped.
                return HardLightChannel(src, dst);
            case BlendMode.Darken:
                return dst < src ? dst : src;
            case BlendMode.Lighten:
                return dst > src ? dst : src;
            case BlendMode.ColorDodge:
                if (src >= 255) return 255;
                {
                    var v = (dst * 255) / (255 - src);
                    return v > 255 ? 255 : v;
                }
            case BlendMode.ColorBurn:
                if (src <= 0) return 0;
                {
                    var v = ((255 - dst) * 255) / src;
                    if (v > 255) v = 255;
                    return 255 - v;
                }
            case BlendMode.HardLight:
                return HardLightChannel(dst, src);
            case BlendMode.SoftLight:
                return SoftLightChannel(dst, src);
            case BlendMode.Difference:
                return dst > src ? dst - src : src - dst;
            case BlendMode.Exclusion:
                return dst + src - 2 * (dst * src) / 255;
            default:
                return src;
        }
    }

    private static int HardLightChannel(int dst, int src)
    {
        // HardLight(Cb, Cs) = Cs ≤ 0.5 ? Multiply(Cb, 2·Cs) : Screen(Cb, 2·Cs − 1).
        if (src <= 127)
            return (dst * src * 2) / 255;
        var s2 = src * 2 - 255;
        return dst + s2 - (dst * s2) / 255;
    }

    /// <summary>
    /// Apply a non-separable HSL blend (Hue / Saturation / Color / Luminosity) to
    /// the full RGB triple per PDF 32000 §11.3.5.4. Operates in [0,1] doubles for
    /// the HSL math, then quantises back to 0..255 ints. Out-of-range channels
    /// after SetLum are clipped via <see cref="ClipColor"/> with the spec's
    /// luminance-preserving formula. Mode must be one of the four HSL modes.
    /// </summary>
    public static void BlendTriple(BlendMode mode, int dr, int dg, int db, int sr, int sg, int sb,
                                   out int br, out int bg, out int bb)
    {
        var cbR = dr / 255.0; var cbG = dg / 255.0; var cbB = db / 255.0;
        var csR = sr / 255.0; var csG = sg / 255.0; var csB = sb / 255.0;
        double rR, rG, rB;
        switch (mode)
        {
            case BlendMode.Hue:
                // SetLum(SetSat(Cs, Sat(Cb)), Lum(Cb))
                SetSat(csR, csG, csB, Sat(cbR, cbG, cbB), out var hR, out var hG, out var hB);
                SetLum(hR, hG, hB, Lum(cbR, cbG, cbB), out rR, out rG, out rB);
                break;
            case BlendMode.Saturation:
                // SetLum(SetSat(Cb, Sat(Cs)), Lum(Cb))
                SetSat(cbR, cbG, cbB, Sat(csR, csG, csB), out var sR, out var sG, out var sB);
                SetLum(sR, sG, sB, Lum(cbR, cbG, cbB), out rR, out rG, out rB);
                break;
            case BlendMode.Color:
                // SetLum(Cs, Lum(Cb))
                SetLum(csR, csG, csB, Lum(cbR, cbG, cbB), out rR, out rG, out rB);
                break;
            case BlendMode.Luminosity:
                // SetLum(Cb, Lum(Cs))
                SetLum(cbR, cbG, cbB, Lum(csR, csG, csB), out rR, out rG, out rB);
                break;
            default:
                rR = csR; rG = csG; rB = csB;
                break;
        }
        br = QuantiseChannel(rR);
        bg = QuantiseChannel(rG);
        bb = QuantiseChannel(rB);
    }

    private static int QuantiseChannel(double v)
    {
        if (v < 0.0) v = 0.0;
        else if (v > 1.0) v = 1.0;
        return (int)(v * 255.0 + 0.5);
    }

    private static double Lum(double r, double g, double b) =>
        0.3 * r + 0.59 * g + 0.11 * b;

    private static double Sat(double r, double g, double b)
    {
        var max = r > g ? (r > b ? r : b) : (g > b ? g : b);
        var min = r < g ? (r < b ? r : b) : (g < b ? g : b);
        return max - min;
    }

    /// <summary>
    /// PDF 32000 §11.3.5.4 ClipColor: when applying SetLum pushes any channel
    /// outside [0, 1], pull all channels back toward the luminance to preserve
    /// the colour's hue/saturation, instead of just hard-clamping each channel
    /// independently (which would shift the hue at extremes).
    /// </summary>
    private static void ClipColor(ref double r, ref double g, ref double b)
    {
        var l = Lum(r, g, b);
        var n = r < g ? (r < b ? r : b) : (g < b ? g : b);
        var x = r > g ? (r > b ? r : b) : (g > b ? g : b);
        if (n < 0.0)
        {
            var denom = l - n;
            if (denom > 0.0)
            {
                r = l + (r - l) * l / denom;
                g = l + (g - l) * l / denom;
                b = l + (b - l) * l / denom;
            }
        }
        if (x > 1.0)
        {
            var denom = x - l;
            if (denom > 0.0)
            {
                r = l + (r - l) * (1.0 - l) / denom;
                g = l + (g - l) * (1.0 - l) / denom;
                b = l + (b - l) * (1.0 - l) / denom;
            }
        }
    }

    private static void SetLum(double r, double g, double b, double l,
                               out double or, out double og, out double ob)
    {
        var d = l - Lum(r, g, b);
        or = r + d; og = g + d; ob = b + d;
        ClipColor(ref or, ref og, ref ob);
    }

    /// <summary>
    /// PDF 32000 §11.3.5.4 SetSat: rebuild the colour so its saturation
    /// (max − min) equals <paramref name="s"/>, preserving the relative ranks
    /// of the three channels. The middle-rank channel is interpolated; the
    /// max-rank channel becomes <paramref name="s"/>; the min-rank channel
    /// becomes 0. When all three are equal, saturation is undefined and the
    /// result is 0 across the board.
    /// </summary>
    private static void SetSat(double r, double g, double b, double s,
                               out double or, out double og, out double ob)
    {
        // Place each channel into max/mid/min slots by rank, scale the mid by s,
        // then write back to its original position. Avoids any heap allocation
        // — relevant because BlendTriple is on the per-pixel hot path.
        double range, newMid;
        if (r >= g)
        {
            if (g >= b)
            {
                // r ≥ g ≥ b
                range = r - b;
                if (range <= 0.0) { or = og = ob = 0.0; return; }
                newMid = (g - b) * s / range;
                or = s; og = newMid; ob = 0.0;
            }
            else if (r >= b)
            {
                // r ≥ b > g
                range = r - g;
                if (range <= 0.0) { or = og = ob = 0.0; return; }
                newMid = (b - g) * s / range;
                or = s; og = 0.0; ob = newMid;
            }
            else
            {
                // b > r ≥ g
                range = b - g;
                if (range <= 0.0) { or = og = ob = 0.0; return; }
                newMid = (r - g) * s / range;
                or = newMid; og = 0.0; ob = s;
            }
        }
        else // g > r
        {
            if (r >= b)
            {
                // g > r ≥ b
                range = g - b;
                if (range <= 0.0) { or = og = ob = 0.0; return; }
                newMid = (r - b) * s / range;
                or = newMid; og = s; ob = 0.0;
            }
            else if (g >= b)
            {
                // g ≥ b > r
                range = g - r;
                if (range <= 0.0) { or = og = ob = 0.0; return; }
                newMid = (b - r) * s / range;
                or = 0.0; og = s; ob = newMid;
            }
            else
            {
                // b > g > r
                range = b - r;
                if (range <= 0.0) { or = og = ob = 0.0; return; }
                newMid = (g - r) * s / range;
                or = 0.0; og = newMid; ob = s;
            }
        }
    }

    private static int SoftLightChannel(int dst, int src)
    {
        // PDF 32000 §11.3.5.4 SoftLight (separable):
        //   if Cs ≤ 0.5:  Cb − (1 − 2·Cs)·Cb·(1 − Cb)
        //   else:          Cb + (2·Cs − 1)·(D(Cb) − Cb), where
        //   D(Cb) = Cb ≤ 0.25 ? ((16·Cb − 12)·Cb + 4)·Cb : √Cb
        // Done in floating point: SoftLight is rare and the rounded-integer form
        // diverges visibly from the spec at mid greys.
        var cb = dst / 255.0;
        var cs = src / 255.0;
        double r;
        if (cs <= 0.5)
        {
            r = cb - (1.0 - 2.0 * cs) * cb * (1.0 - cb);
        }
        else
        {
            var d = cb <= 0.25
                ? ((16.0 * cb - 12.0) * cb + 4.0) * cb
                : System.Math.Sqrt(cb);
            r = cb + (2.0 * cs - 1.0) * (d - cb);
        }
        if (r < 0.0) r = 0.0;
        else if (r > 1.0) r = 1.0;
        return (int)(r * 255.0 + 0.5);
    }
}
