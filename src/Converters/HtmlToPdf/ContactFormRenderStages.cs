using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the contact-form render: the em measure, one body token and one block.
    private static double CfEm(double em) => em * CfEmPx * 0.75;

    /// <summary></summary>
    private static bool RenderContactFormBlock(ContactFormRenderState cf, string kind, string text, List<Aspose.Pdf.Converters.HtmlToPdfConverter.CfField> fields)
    {
        if (kind is "h2" or "h2row")
        {
            if (kind == "h2row") cf.y += CfInRowHeadingLeadPt;
            EmitGridsterText(cf.page, cf.resByFace, CfSectionPt, cf.colX,
                cf.pageHeight - (cf.y + CfSectionPt + CfSectionSeatPt), text, "Arial");
            cf.y += CfHeadingToFirstTitlePt;
            cf.pendingHeading = true;
            return true;
        }

        var x = cf.colX;
        double rowH = 0;
        foreach (var f in fields)
        {
            var fw = CfEm(f.WidthEm) + CfEm(CfBorderPx / CfEmPx);
            var fh = f.IsRadioGroup ? CfRadioRowHeightPt : CfEm(f.HeightEm);
            rowH = System.Math.Max(rowH, fh);
            var boxTop = cf.y + CfTitleAboveBoxPt;

            if (f.Title.Length > 0)
                EmitGridsterText(cf.page, cf.resByFace, CfTitlePt, x,
                    cf.pageHeight - (cf.y + CfTitlePt), f.Title, "Arial,Bold");

            if (f.IsRadioGroup)
            {
                // Option circles on the value line, each followed by its label
                // (probed: circles at +5.76 on a 43.02 pt pitch, 18.98 under the
                // title's glyph top; labels 14.5 to the right of each circle).
                var rx = x + CfRadioFirstPt;
                foreach (var (lab, on) in f.Radios)
                {
                    var cy = cf.pageHeight - (cf.y + CfRadioTopPt + CfRadioRPt);
                    var r = CfRadioRPt;
                    cf.sb.Append(Compat.Format(cf.inv,
                        $"q 0 0 0 RG 1 w {rx + r:F2} {cy:F2} m " +
                        $"{rx + r:F2} {cy + r:F2} {rx - r:F2} {cy + r:F2} {rx - r:F2} {cy:F2} c " +
                        $"{rx - r:F2} {cy - r:F2} {rx + r:F2} {cy - r:F2} {rx + r:F2} {cy:F2} c S Q\n"));
                    if (on)
                        cf.sb.Append(Compat.Format(cf.inv,
                            $"q 0 0 0 rg {rx - r / 2:F2} {cy - r / 2:F2} {r:F2} {r:F2} re f Q\n"));
                    EmitGridsterText(cf.page, cf.resByFace, CfValuePt, rx + CfRadioLabelPt,
                        cy - CfValuePt * 0.30, lab, "Arial,Bold");
                    rx += CfRadioPitchPt;
                }
            }
            else if (f.HasControl)
            {
                // The 2 px-bordered control box, stroked on its inset centre line.
                cf.sb.Append(Compat.Format(cf.inv,
                    $"q 0 0 0 RG 1 w {x + 0.5:F2} {cf.pageHeight - boxTop - 0.5:F2} " +
                    $"{fw - CfEm(CfBorderPx / CfEmPx) - 1:F2} {-(fh):F2} re S Q\n"));
                if (f.Value.Length > 0)
                    EmitGridsterText(cf.page, cf.resByFace, CfValuePt, x + CfValueInsetPt,
                        cf.pageHeight - (boxTop + CfValueBelowBoxPt), f.Value, "Times New Roman");
            }
            else if (f.Value.Length > 0)
            {
                EmitGridsterText(cf.page, cf.resByFace, CfValuePt, x,
                    cf.pageHeight - (boxTop + CfValueBelowBoxPt), f.Value, "Arial,Bold");
            }

            x += fw + CfEm(CfFieldGapEm);
        }
        _ = cf.pendingHeading;
        cf.y += rowH + CfEm(CfRowGapEm);
        return true;
    }

    /// <summary></summary>
    private static bool ScanContactFormToken(ContactFormRenderState cf)
    {
        var m = cf.tokRx.Match(cf.body2, cf.scanPos);
        if (!m.Success) return false;
        if (m.Groups["h2"].Success)
        {
            var t = Regex.Replace(m.Groups["h2"].Value, "<[^>]+>", "");
            cf.blocks.Add(("h2", Regex.Replace(DecodeEntities(t), @"\s+", " ").Trim(), new List<CfField>()));
            cf.scanPos = m.Index + m.Length;
            return true;
        }
        // The row runs to its matching </div>.
        var depth = 1;
        var end = -1;
        for (var s = cf.divRx.Match(cf.body2, m.Index + m.Length); s.Success;
             s = cf.divRx.Match(cf.body2, s.Index + s.Length))
        {
            depth += s.Groups["c"].Length > 0 ? -1 : 1;
            if (depth == 0) { end = s.Index; break; }
        }
        if (end < 0) return false;
        var rowHtml = cf.body2[(m.Index + m.Length)..end];
        var rowH2 = Regex.Match(rowHtml, @"<h2\b[^>]*>(?<t>[\s\S]*?)</h2\s*>", RegexOptions.IgnoreCase);
        if (rowH2.Success)
        {
            var rt = Regex.Replace(rowH2.Groups["t"].Value, "<[^>]+>", "");
            cf.blocks.Add(("h2row", Regex.Replace(DecodeEntities(rt), @"\s+", " ").Trim(), new List<CfField>()));
        }
        var fields = ParseContactFields(rowHtml, cf.clsW);
        if (fields.Count > 0) cf.blocks.Add(("row", "", fields));
        cf.scanPos = end;
        return true;
    }
}
