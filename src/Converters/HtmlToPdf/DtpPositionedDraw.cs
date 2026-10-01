using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Positioned DTP drawing helpers: per-page ops, text, strokes and images.
    private static List<object> OpsFor(DtpPositionedState dp, int p)
    {
        while (dp.pageOps.Count <= p) dp.pageOps.Add(new List<object>());
        return dp.pageOps[p];
    }

    private static void DrawText(DtpPositionedState dp, int page, double x, double baselineInPage, DtpRun run, string text)
    {
        if (text.Length == 0) return;
        var faceName = DtpFaceName(run);
        var face = PosFace(faceName);
        var drawn = text;
        if (face.ttf is null)
        {
            // The face is unavailable (e.g. Symbol on a bare rig): draw the
            // run in Arial — the advance model below still measured it in
            // the declared face where possible, so following runs hold.
            faceName = run.Bold ? "Arial Bold" : "Arial";
            face = PosFace(faceName);
            if (face.ttf is null) return;
        }
        else if (faceName == "Symbol")
            drawn = DtpToSymbolPua(face.parser, text);
        var (rn, hex) = Text.Type0FontEmbedder.Embed(dp.fontDict, face.ttf, faceName, drawn,
            stripSpacesInBaseFont: true);
        OpsFor(dp, page).Add(Compat.Format(dp.inv,
            $"BT {run.Color.R:F3} {run.Color.G:F3} {run.Color.B:F3} rg /{rn} {run.SizePt:F2} Tf 1 0 0 1 {x:F2} {dp.pageH - baselineInPage:F2} Tm <{Compat.ToHexString(hex)}> Tj ET\n"));
    }

    private static void StrokeLine(DtpPositionedState dp, int page, double x0, double x1, double yInPage, (double R, double G, double B) col)
        => OpsFor(dp, page).Add(Compat.Format(dp.inv,
            $"q {col.R:F3} {col.G:F3} {col.B:F3} RG 1 w {x0:F2} {dp.pageH - yInPage:F2} m {x1:F2} {dp.pageH - yInPage:F2} l S Q\n"));

    // A positioned image: a boundary-crossing box draws on EVERY page whose
    // band it intersects, shifted by the band height — unclipped, so the
    // halves join seamlessly (measured on the fixture's p6/p7). A NESTED
    // positioned image offsets by its positioned ancestor's (left, top).
    private static void DrawDtpImage(DtpPositionedState dp, string imgTag, DtpIdRule rule, double offL, double offT)
    {
        var src = DtpAttr(imgTag, "src");
        if (src is null || !src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
        var comma = src.IndexOf(',');
        if (comma < 0 || !rule.HasH) return;
        byte[] bytes;
        try { bytes = System.Convert.FromBase64String(src[(comma + 1)..]); }
        catch { return; }
        var top = offT + rule.T;
        var p0 = (int)Math.Floor(top / dp.band);
        var p1 = (int)Math.Floor((top + rule.H - 1e-6) / dp.band);
        for (var p = Math.Max(p0, 0); p <= p1; p++)
        {
            var yTop = top - p * dp.band + DtpVertMarginPt;
            OpsFor(dp, p).Add((bytes, DtpSideMarginPt + offL + rule.L, yTop, rule.W, rule.H));
        }
    }
}
