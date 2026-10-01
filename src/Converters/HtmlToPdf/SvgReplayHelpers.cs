using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The SVG replay helpers: number parsing, the mask-definition test, the pending-path flush and the current graphics-state resource.
    private static double Num(SvgReplayState sr, string v) => double.Parse(v, System.Globalization.NumberStyles.Float, sr.inv);

    private static bool InsideMaskDef(SvgReplayState sr, int idx)
    {
        foreach (var (s0, e0) in sr.maskSpans)
            if (idx >= s0 && idx < e0) return true;
        return false;
    }

    private static void FlushPaths(SvgReplayState sr)
    {
        if (sr.sb.Length == 0) return;
        sr.page.AddContentStream(Encoding.ASCII.GetBytes(sr.sb.ToString()));
        sr.sb.Clear();
    }

    // ExtGState for the current alpha/mask, registered once per distinct pair.
    private static string? CurrentGs(SvgReplayState sr)
    {
        if (sr.alpha >= 1.0 - 1e-9 && sr.maskId is null) return null;
        var key = $"{sr.alpha:F6}|{sr.maskId}";
        if (sr.gsNames.TryGetValue(key, out var cached)) return cached;
        var name = RegisterSvgEffectGState(sr.page, sr.alpha,
            sr.maskId is not null && sr.maskDefs.TryGetValue(sr.maskId, out var def) ? def : null, sr.rootTotal);
        if (name is null) return null;
        sr.gsNames[key] = name;
        return name;
    }
}
