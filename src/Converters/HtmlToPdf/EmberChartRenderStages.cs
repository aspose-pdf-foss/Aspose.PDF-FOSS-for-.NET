using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the ember-chart render: the pixel read, the card collection, one card and one series.
    private static double EmberPx(EmberChartRenderState ec, string st, string key, double dflt = 0)
            => Regex.Match(st, key + @"\s*:\s*(-?[\d.]+)px", RegexOptions.IgnoreCase)
                is { Success: true } m
                ? double.Parse(m.Groups[1].Value, ec.invc) : dflt;

    /// <summary></summary>
    private static bool RenderEmberCard(EmberChartRenderState ec, double cx, double cy, double cw, double ch, List<string> fields)
    {
        // white box + the double border: two hairlines per side at the
        // measured insets
        ec.sb.Append(Compat.Format(ec.invc,
            $"q 1 1 1 rg {cx:0.##} {ec.pageHeight - cy - ch:0.##} {cw:0.##} {ch:0.##} re f Q\n"));
        foreach (var inset in new[] { 0.2, 0.8 })
        {
            var l = cx + inset; var r = cx + cw - inset;
            var t = cy + inset; var b = cy + ch - inset;
            ec.sb.Append("q 0 0 0 RG 0.33 w ");
            ec.sb.Append(Compat.Format(ec.invc,
                $"{cx:0.##} {ec.pageHeight - t:0.##} m {cx + cw:0.##} {ec.pageHeight - t:0.##} l S "));
            ec.sb.Append(Compat.Format(ec.invc,
                $"{cx:0.##} {ec.pageHeight - b:0.##} m {cx + cw:0.##} {ec.pageHeight - b:0.##} l S "));
            ec.sb.Append(Compat.Format(ec.invc,
                $"{l:0.##} {ec.pageHeight - cy:0.##} m {l:0.##} {ec.pageHeight - cy - ch:0.##} l S "));
            ec.sb.Append(Compat.Format(ec.invc,
                $"{r:0.##} {ec.pageHeight - cy:0.##} m {r:0.##} {ec.pageHeight - cy - ch:0.##} l S Q\n"));
        }
        // the fields wrap in the 90% inner box; overflow:hidden clips at the
        // card's content height
        var wrapW = (cw - 2 * EmCardBorderPt) * EmInnerWidthFrac;
        var pen = cy + EmCardBorderPt;
        var clipBottom = cy + ch - EmCardBorderPt;
        foreach (var f in fields)
        {
            foreach (var ln in MeasuredWordWrap(f, wrapW, "Times New Roman", EmCardFsPt))
            {
                if (pen + EmCardLineHPt > clipBottom) break;
                ec.sb.Append(Compat.Format(ec.invc,
                    $"BT /F5 {EmCardFsPt:0.##} Tf 1 0 0 1 {cx + EmCardPadPt:0.##} {ec.pageHeight - pen - ec.drop:0.##} Tm ({EscapePdfString(ln)}) Tj ET\n"));
                pen += EmCardLineHPt;
            }
            if (pen + EmCardLineHPt > clipBottom) break;
        }
        return true;
    }

    /// <summary></summary>
    private static void CollectEmberCards(EmberChartRenderState ec)
    {
        foreach (Match nm in Regex.Matches(ec.html,
            @"<div\b[^>]*class\s*=\s*[""']ember-view node[^""']*[""'][^>]*style\s*=\s*[""'](?<st>[^""']*)[""'][^>]*>",
            RegexOptions.IgnoreCase))
        {
            var st = nm.Groups["st"].Value;
            var end = FindDivClose(ec.html, nm.Index + nm.Length);
            var body = ec.html[(nm.Index + nm.Length)..end];
            var fields = new List<string>();
            foreach (Match fmM in Regex.Matches(body,
                @"<div\b[^>]*class\s*=\s*[""']ember-view[""'][^>]*>(?<t>[\s\S]*?)</div>",
                RegexOptions.IgnoreCase))
            {
                var t = Regex.Replace(DecodeEntities(Regex.Replace(
                    fmM.Groups["t"].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim();
                if (t.Length > 0) fields.Add(t);
            }
            var cx = ec.originX + EmberPx(ec, st, "left") * 0.75;
            var cy = ec.originY + EmberPx(ec, st, "top") * 0.75;
            var cw = EmberPx(ec, st, "width") * 0.75 + 2 * EmCardBorderPt;
            var ch = EmberPx(ec, st, "height") * 0.75 + 2 * EmCardBorderPt;
            if (cw <= 2 || ch <= 2) continue;
            ec.cards.Add((cx, cy, cw, ch, fields));
            ec.maxRight = Math.Max(ec.maxRight, cx + cw);
        }
    }
}
