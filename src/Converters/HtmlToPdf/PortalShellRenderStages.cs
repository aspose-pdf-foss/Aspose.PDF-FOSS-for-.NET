using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the portal-shell render: the pixel measure, the CSS colour read, the rectangle and sunken-box fills, and the panel sections.
    private static double PortalPx(PortalShellRenderState po, string s) => double.Parse(s, po.inv) * 0.75;

    // Canvas + wrapper colours, straight from their rules.
    private static (double R, double G, double B) PortalCssColor(PortalShellRenderState po, string selector, (double, double, double) fallback)
    {
        var m = Regex.Match(po.html, Regex.Escape(selector)
            + @"\s*\{[^}]*background(?:-color)?\s*:\s*#(?<h>[0-9a-fA-F]{3,6})",
            RegexOptions.IgnoreCase);
        if (!m.Success) return fallback;
        var hx = m.Groups["h"].Value;
        if (hx.Length == 3) hx = $"{hx[0]}{hx[0]}{hx[1]}{hx[1]}{hx[2]}{hx[2]}";
        return (System.Convert.ToInt32(hx[..2], 16) / 255.0,
                System.Convert.ToInt32(hx[2..4], 16) / 255.0,
                System.Convert.ToInt32(hx[4..6], 16) / 255.0);
    }

    private static void PortalRect(PortalShellRenderState po, double x, double yTop, double w, double h, (double R, double G, double B) c)
        => po.sb.Append(Compat.Format(po.inv,
            $"q {c.R:0.###} {c.G:0.###} {c.B:0.###} rg {x:F2} {po.pageH - yTop - h:F2} {w:F2} {h:F2} re f Q\n"));

    private static void PortalSunkenBox(PortalShellRenderState po, double x, double yTop, double w, double h)
    {
        PortalRect(po, x, yTop, w, h, (0.25, 0.25, 0.25));
        PortalRect(po, x + 1.0, yTop + 1.0, w - 2.0, h - 2.0, (1, 1, 1));
    }

    /// <summary></summary>
    private static void RenderPortalPanels(PortalShellRenderState po)
    {
        if (po.welcomeM.Success)
            foreach (Match li in Regex.Matches(po.welcomeM.Groups["b"].Value,
                         @"<li\b[^>]*>(?<t>[\s\S]*?)</li\s*>", RegexOptions.IgnoreCase))
                po.items.Add(Regex.Replace(DecodeEntities(
                    Regex.Replace(li.Groups["t"].Value, "<[^>]+>", "")), @"\s+", " ").Trim());

        po.sb.Append(Compat.Format(po.inv, $"q {PsInk:0.###} {PsInk:0.###} {PsInk:0.###} rg\n"));
        po.page.AddContentStream(Encoding.ASCII.GetBytes(po.sb.ToString()));
        po.sb.Clear();
        po.textX = po.left + PsListIndentPt;
        for (var i = 0; i < po.items.Count; i++)
        {
            var glyphTop = po.bannerTop + PsListFirstGlyphDropPt + i * PsListPitchPt;
            var baseline = po.pageH - (glyphTop + PsCapHeight * po.fs);
            EmitGridsterText(po.page, po.resByFace, po.fs, po.textX, baseline, po.items[i], "Arial");
            var cy = po.pageH - (glyphTop + PsBulletDropPt);
            var r = PsBulletRadiusPt;
            po.sb.Append(Compat.Format(po.inv,
                $"{po.textX - PsBulletLeftOfTextPt - r:F2} {cy - r:F2} {2 * r:F2} {2 * r:F2} re f\n"));
        }
        po.page.AddContentStream(Encoding.ASCII.GetBytes(po.sb.ToString() + "Q\n"));
        po.sb.Clear();

        // The #col2 input row at the content edge, and the Go bevel.
        PortalSunkenBox(po, po.left, PsCol2InputTopPt, PsInputWPt, PsCol2InputHPt);
        po.goX = po.left + PsInputWPt + PsGoGapPt;
        PortalRect(po, po.goX, PsCol2InputTopPt, PsGoWPt, PsGoHPt, (0.25, 0.25, 0.25));
        PortalRect(po, po.goX + 0.5, PsCol2InputTopPt + 0.5, PsGoWPt - 1.0, PsGoHPt - 1.0, (0.83, 0.83, 0.83));
        PortalRect(po, po.goX + 2.0, PsCol2InputTopPt + 2.0, PsGoWPt - 4.0, PsGoHPt - 4.0, (0.94, 0.94, 0.94));
        po.goValM = Regex.Match(po.html, @"<input\b[^>]*value=""(?<v>[^""]+)""[^>]*type=""submit""[^>]*>|<input\b[^>]*type=""submit""[^>]*value=""(?<v>[^""]+)""[^>]*>",
            RegexOptions.IgnoreCase);
        po.goLabel = po.goValM.Success ? po.goValM.Groups["v"].Value : "Go";
        po.goLabelW = MeasureFaceText("Arial", po.goLabel, PsGoLabelPt);
        EmitGridsterText(po.page, po.resByFace, PsGoLabelPt,
            po.goX + (PsGoWPt - po.goLabelW) / 2,
            po.pageH - (PsGoGlyphTopPt + PsCapHeight * PsGoLabelPt),
            po.goLabel, "Arial");
    }
}
