using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Metrics homepage: the CTA pill with its clipped two-colour gradient.</summary>
    private static void DrawMetricsCta(MetricsHomepageState mh)
    {
        // the CTA pill: rounded ends (radius = half height), the 129° two-colour
        // gradient painted as vertical strips clipped to the pill path
        {
            var r = MhCtaH / 2;
            var x0 = MhCtaX; var x1 = MhCtaX + MhCtaW;
            var yb = MhPageH - MhCtaTop - MhCtaH; var yt = MhPageH - MhCtaTop;
            var k = 0.5523 * r;
            var path = Compat.Format(mh.inv,
                $"{x0 + r:F2} {yb:F2} m {x1 - r:F2} {yb:F2} l "
                + $"{x1 - r + k:F2} {yb:F2} {x1:F2} {yb + r - k:F2} {x1:F2} {yb + r:F2} c "
                + $"{x1:F2} {yb + r + k:F2} {x1 - r + k:F2} {yt:F2} {x1 - r:F2} {yt:F2} c "
                + $"{x0 + r:F2} {yt:F2} l "
                + $"{x0 + r - k:F2} {yt:F2} {x0:F2} {yb + r + k:F2} {x0:F2} {yb + r:F2} c "
                + $"{x0:F2} {yb + r - k:F2} {x0 + r - k:F2} {yb:F2} {x0 + r:F2} {yb:F2} c h");
            mh.shapes.AppendLine("q " + path + " W n");
            const int strips = 48;
            for (var s = 0; s < strips; s++)
            {
                var f = (s + 0.5) / strips;
                var cr = (MhGrad0.R + (MhGrad1.R - MhGrad0.R) * f) / 255.0;
                var cg = (MhGrad0.G + (MhGrad1.G - MhGrad0.G) * f) / 255.0;
                var cb = (MhGrad0.B + (MhGrad1.B - MhGrad0.B) * f) / 255.0;
                var sx = x0 + MhCtaW * s / (double)strips;
                mh.shapes.AppendLine(Compat.Format(mh.inv,
                    $"{cr:0.###} {cg:0.###} {cb:0.###} rg {sx:F2} {yb:F2} {MhCtaW / strips + 0.1:F2} {MhCtaH:F2} re f"));
            }
            mh.shapes.AppendLine("Q");
            var lw2 = M(mh, mh.arialBold, "ArialBold", mh.ctaText, 15);
            var lx = MhCtaX + (MhCtaW - lw2) / 2;
            Run(mh, mh.arialBold, "ArialBold", 15, lx, MhCtaBase, mh.ctaText, "1 1 1");
            HLine(mh, lx, lx + lw2, MhCtaBase + 1.5, 1.5, "1 1 1");
        }
    }

    /// <summary>Metrics homepage: the hero page - white ground and the SVG illustration.</summary>
    private static void DrawMetricsHero(MetricsHomepageState mh)
    {
        // ── page 1: hero ─────────────────────────────────────────────────────
        // The hero heading/paragraph paint white on white — no visible ink, so
        // they are skipped outright. The SVG illustration renders through the
        // rasteriser at its declared 400px (300 pt) width on the column's right.
        EnsureFonts(mh.page);
        WhiteGround(mh);   // the ground goes UNDER the hero image
        if (mh.options?.BasePath is { Length: > 0 } basePath)
        {
            // callers commonly hand the HTML FILE path as the "base path"
            if (System.IO.File.Exists(basePath))
                basePath = System.IO.Path.GetDirectoryName(basePath) ?? basePath;
            var svgM = Regex.Match(mh.html, @"<img\b[^>]*src\s*=\s*""([^""]*headergraphics\.svg)""",
                RegexOptions.IgnoreCase);
            if (svgM.Success)
            {
                try
                {
                    var svgPath = System.IO.Path.Combine(basePath,
                        svgM.Groups[1].Value.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(svgPath))
                    {
                        var svgData = System.IO.File.ReadAllBytes(svgPath);
                        double svgH = MhSvgW;
                        var vb = Regex.Match(Encoding.UTF8.GetString(svgData, 0, Math.Min(2048, svgData.Length)),
                            @"viewBox\s*=\s*""[\d.\s-]*?([\d.]+)\s+([\d.]+)""");
                        if (vb.Success
                            && double.TryParse(vb.Groups[1].Value, System.Globalization.NumberStyles.Float, mh.inv, out var vw)
                            && double.TryParse(vb.Groups[2].Value, System.Globalization.NumberStyles.Float, mh.inv, out var vh)
                            && vw > 0)
                            svgH = MhSvgW * vh / vw;
                        // rasterise at 2× for a supersampled placement
                        var raster = ImageRasterizer.RasterizeSvgSized(svgData, MhSvgW * 2, svgH * 2);
                        if (raster is not null)
                            mh.page.AddImage(raster, new Rectangle(
                                MhX1 - MhSvgW, MhPageH - MhSvgTop - svgH, MhX1, MhPageH - MhSvgTop));
                    }
                }
                catch { /* a missing or unrenderable illustration just leaves the box empty */ }
            }
        }
    }

    /// <summary>Metrics homepage: the table containers read into rows and cells.</summary>
    private static void ParseMetricsTables(MetricsHomepageState mh)
    {
        mh.tables = new List<List<List<MhCell>>>();   // table → rows → cells
        foreach (Match tc in Regex.Matches(mh.html,
            @"<div class=""table-container"">([\s\S]*?)</div>", RegexOptions.IgnoreCase))
        {
            var rows = new List<List<MhCell>>();
            foreach (Match ol in Regex.Matches(tc.Groups[1].Value,
                @"<ol class=""flex-row""[^>]*>([\s\S]*?)</ol>", RegexOptions.IgnoreCase))
            {
                var cells = new List<MhCell>();
                foreach (Match li in Regex.Matches(ol.Groups[1].Value,
                    @"<li\b([^>]*)>([\s\S]*?)</li>", RegexOptions.IgnoreCase))
                {
                    var attrs = li.Groups[1].Value;
                    var flexM = Regex.Match(attrs, @"flex:\s*([\d.]+)%");
                    var cell = new MhCell
                    {
                        Flex = flexM.Success ? double.Parse(flexM.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) / 100.0 : 0,
                        Text = MhFlat(li.Groups[2].Value),
                        IsLink = li.Groups[2].Value.Contains("<a ", StringComparison.OrdinalIgnoreCase)
                            || li.Groups[2].Value.Contains("<a\t", StringComparison.OrdinalIgnoreCase)
                            || Regex.IsMatch(li.Groups[2].Value, @"<a\b", RegexOptions.IgnoreCase),
                    };
                    cell.IsHeading = cell.Flex >= 0.99;
                    cell.IsLabel = !cell.IsHeading && cell.Flex >= 0.15;
                    cells.Add(cell);
                }
                if (cells.Count > 0) rows.Add(cells);
            }
            if (rows.Count > 0) mh.tables.Add(rows);
        }
    }
}
