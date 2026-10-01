using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>An image element: its box under the current (axis-aligned) transform, the bytes loaded from the href (absolute or beside the SVG), placed through the page under the current effect state.</summary>
    private static void ReplaySvgImage(SvgReplayState sr, string attrs)
    {
        var href = Regex.Match(attrs, @"(?:xlink:)?href=""(?<v>[^""]+)""");
        if (!href.Success) return;
        double ix = 0, iy = 0, iw = 0, ih = 0;
        var xm = Regex.Match(attrs, @"(?<![\w-])x=""(?<v>-?[\d.]+)""");
        var ym = Regex.Match(attrs, @"(?<![\w-])y=""(?<v>-?[\d.]+)""");
        var wm = Regex.Match(attrs, @"width=""(?<v>[\d.]+)""");
        var hm = Regex.Match(attrs, @"height=""(?<v>[\d.]+)""");
        if (xm.Success) ix = Num(sr, xm.Groups["v"].Value);
        if (ym.Success) iy = Num(sr, ym.Groups["v"].Value);
        if (wm.Success) iw = Num(sr, wm.Groups["v"].Value);
        if (hm.Success) ih = Num(sr, hm.Groups["v"].Value);
        if (iw <= 0 || ih <= 0) return;
        var url = DecodeEntities(href.Groups["v"].Value);
        var bytes = LoadConverterImage(url, sr.options)
                    ?? (sr.svgDir.Length > 0 ? LoadConverterImage(sr.svgDir + url, sr.options) : null);
        if (bytes is null) return;
        // The image box in local coords maps through the total matrix; only
        // axis-aligned results can go through AddImage — rotation is rare in
        // this generator's output and is skipped rather than mis-drawn.
        if (Math.Abs(sr.total[1]) > 1e-6 || Math.Abs(sr.total[2]) > 1e-6) return;
        var x0 = ix * sr.total[0] + sr.total[4];
        var y0 = iy * sr.total[3] + sr.total[5];
        var x1 = (ix + iw) * sr.total[0] + sr.total[4];
        var y1 = (iy + ih) * sr.total[3] + sr.total[5];
        FlushPaths(sr);
        try
        {
            // The active <g opacity>/<g mask> rides an ExtGState around
            // the image draw (AddImage's own q…Q pairs inside it).
            var imgGs = CurrentGs(sr);
            if (imgGs is not null)
                sr.page.AddContentStream(Encoding.ASCII.GetBytes($"q /{imgGs} gs\n"));
            sr.page.AddImage(bytes, new Aspose.Pdf.Rectangle(
                Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1)));
            if (imgGs is not null)
                sr.page.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
        }
        catch { /* undecodable image — skip */ }
    }

    /// <summary>A path element: fill and stroke colours, its own transform on top of the current one, the PDF operators of its d attribute, painted under the current effect state.</summary>
    private static void ReplaySvgPath(SvgReplayState sr, string attrs)
    {
        // The boundary keeps this from matching the tail of `id="…"`
        // (the inline dialect's paths carry an id attribute).
        var d = Regex.Match(attrs, @"(?<![\w-])d=""(?<d>[^""]*)""");
        if (!d.Success) return;
        var fillM = Regex.Match(attrs, @"fill=""(?<v>[^""]*)""");
        var strokeM = Regex.Match(attrs, @"stroke=""(?<v>[^""]*)""");
        var widthM = Regex.Match(attrs, @"stroke-width=""(?<v>[-\d.]+)""");
        var fill = ParseSvgRgb(fillM.Success ? fillM.Groups["v"].Value : "rgb(0,0,0)");
        var stroke = ParseSvgRgb(strokeM.Success ? strokeM.Groups["v"].Value : "none");
        if (fill is null && stroke is null) return;
        var ops = SvgPathToPdfOps(d.Groups["d"].Value);
        if (ops is null) return;

        // The inline dialect leaves the y flip to each path's own
        // transform matrix rather than the wrapper's.
        var pathTotal = sr.total;
        var ptm = Regex.Match(attrs, @"transform=""matrix\((?<m>[-\d.eE ]+)\)""");
        if (ptm.Success)
        {
            var pparts = ptm.Groups["m"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pparts.Length == 6)
            {
                var pm = new double[6];
                for (var k = 0; k < 6; k++) pm[k] = Num(sr, pparts[k]);
                pathTotal = MulM(pm, sr.total);
            }
        }

        sr.sb.Append("q ");
        if (CurrentGs(sr) is { } pathGs) sr.sb.Append($"/{pathGs} gs ");
        sr.sb.Append(string.Join(" ", pathTotal.Select(v => v.ToString("F5", sr.inv))));
        sr.sb.Append(" cm ");
        if (fill is not null)
            sr.sb.Append($"{fill.Value.r.ToString("F3", sr.inv)} {fill.Value.g.ToString("F3", sr.inv)} {fill.Value.b.ToString("F3", sr.inv)} rg ");
        if (stroke is not null)
        {
            sr.sb.Append($"{stroke.Value.r.ToString("F3", sr.inv)} {stroke.Value.g.ToString("F3", sr.inv)} {stroke.Value.b.ToString("F3", sr.inv)} RG ");
            var wRaw = widthM.Success ? Num(sr, widthM.Groups["v"].Value) : 1.0;
            sr.sb.Append($"{Math.Abs(wRaw).ToString("F3", sr.inv)} w ");
        }
        sr.sb.Append(ops);
        sr.sb.AppendLine(fill is not null && stroke is not null ? "B" : fill is not null ? "f" : "S");
        sr.sb.AppendLine("Q");
    }

    /// <summary>A group open: pushes the effect state, composes a matrix transform, multiplies an opacity, records a mask; a self-closing group pops at once.</summary>
    private static void ReplaySvgGroup(SvgReplayState sr, string attrs)
    {
        sr.stack.Push((sr.total, sr.alpha, sr.maskId));
        var tm = Regex.Match(attrs,
            @"transform=""matrix\((?<m>[-\d.eE ]+)\)""");
        if (tm.Success)
        {
            var parts = tm.Groups["m"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 6)
            {
                var g = new double[6];
                for (var k = 0; k < 6; k++) g[k] = Num(sr, parts[k]);
                sr.total = MulM(g, sr.total);
            }
        }
        var om = Regex.Match(attrs, @"opacity=""(?<v>[\d.]+)""");
        if (om.Success) sr.alpha *= Num(sr, om.Groups["v"].Value);
        var km = Regex.Match(attrs, @"mask=""url\(#(?<v>[^)]+)\)""");
        if (km.Success) sr.maskId = km.Groups["v"].Value;
        if (attrs.TrimEnd().EndsWith("/", StringComparison.Ordinal)) (sr.total, sr.alpha, sr.maskId) = sr.stack.Pop();
    }
}
