using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Write the collected rows, fills, rules and items to their sheets in layer order.</summary>
    private static void EmitAuditReport(AuditReportState ar)
    {
        ar.ops = new List<(int Sheet, int Layer, int Seq, string Text)>();
        ar.seq = 0;

        foreach (var (rs, rt) in ar.rows)
        {
            PageAt(ar, rs);
            var rowL = ar.left + CtContentWPt * CtRowIndentFrac;
            var rowW = CtContentWPt * CtRowWidthFrac;
            var labW = rowW * (CtRowLabelFrac + CtRowPadLeftFrac);
            var h = 2 * CtRowPadPt + CtLineH(14);
            ar.ops.Add((rs, CtLayerFill, ar.seq++, Compat.Format(ar.invc,
                $"q {Rgb(ar, CtRowBg, "rg")}{rowL:0.##} {CtSheetHPt - rt - h:0.##} "
                + $"{labW:0.##} {h:0.##} re f Q")));
            ar.ops.Add((rs, CtLayerFill, ar.seq++, Compat.Format(ar.invc,
                $"q {Rgb(ar, CtBlue, "rg")}{rowL + labW:0.##} {CtSheetHPt - rt - h:0.##} "
                + $"{rowW * CtRowValueFrac:0.##} {h:0.##} re f Q")));
        }

        foreach (var (fs, fx, ft, fw, fh, fc) in ar.fills)
        {
            PageAt(ar, fs);
            ar.ops.Add((fs, CtLayerFill, ar.seq++, Compat.Format(ar.invc,
                $"q {Rgb(ar, fc, "rg")}{fx:0.##} {CtSheetHPt - ft - fh:0.##} "
                + $"{fw:0.##} {fh:0.##} re f Q")));
        }
        foreach (var (rs, x0, x1, ry, rc) in ar.rules)
        {
            PageAt(ar, rs);
            ar.ops.Add((rs, CtLayerRule, ar.seq++, Compat.Format(ar.invc,
                $"q {Rgb(ar, rc, "RG")}{CtRulePt:0.##} w {x0:0.##} {CtSheetHPt - ry:0.##} m "
                + $"{x1:0.##} {CtSheetHPt - ry:0.##} l S Q")));
        }

        foreach (var it in ar.items)
        {
            var pg = PageAt(ar, it.Sheet);
            if (pg.Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) continue;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ar.reg, "RobotoRegular", it.Text,
                stripSpacesInBaseFont: true);
            var baseline = it.Y + it.Size * CtAscEm;
            ar.ops.Add((it.Sheet, CtLayerText, ar.seq++, Compat.Format(ar.invc,
                $"BT {Rgb(ar, it.Ink, "rg")}/{rn} {it.Size:0.##} Tf 1 0 0 1 {it.X:0.##} "
                + $"{CtSheetHPt - baseline:0.##} Tm ")
                + "<" + Compat.ToHexString(hex) + "> Tj ET"));
        }

        foreach (var g in ar.ops.GroupBy(o => o.Sheet))
        {
            var sb = new StringBuilder();
            foreach (var o in g.OrderBy(o => o.Layer).ThenBy(o => o.Seq))
                sb.Append(o.Text).Append('\n');
            ar.pages[g.Key].AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        }
    }

    /// <summary>Lay out one block of the audit body by its class: titles, headings, paragraphs, key/value rows, tables and images, each advancing the column.</summary>
    private static bool LayoutAuditBlock(AuditReportState ar, string cls, string inner, string meta)
    {
        switch (cls)
        {
            case "auditReportTitleMain": case "auditReportTitleSub": case "auditReportHeading": case "auditReportHeadingMain": case "auditReportSubHeading": case "auditReportSubSubHeading":
                LayoutAuditHeadings(ar, inner, meta, cls);
                break;
            case "auditReportText":
                ar.y += CtTextPadTopPt;
                Lines(ar, CtSegments(inner).SelectMany(t => Wrap(ar, t, TextWrap(ar), 12)),
                    TextX(ar), 12, CtInk);
                ar.y += CtTextPadBotPt;
                break;
            case "auditReportSubText":
                Lines(ar, CtSegments(inner).SelectMany(t => Wrap(ar, t, ar.colWidth, 12)),
                    ar.colLeft + CtSubTextIndentPt, 12, CtInk);
                ar.y += CtSubTextPadBotPt;
                break;
            case "auditReportTableDiv":
            {
                LayoutAuditTable(ar, inner);
                break;
            }
            case "col": case "costbox":
                LayoutAuditColumns(ar, inner, cls);
                break;
            case "pagebreak": case "chartpair": case "chart": case "auditReportTwoColumnDiv":
                LayoutAuditFigures(ar, inner, cls);
                break;
        }
        return true;
    }
}
