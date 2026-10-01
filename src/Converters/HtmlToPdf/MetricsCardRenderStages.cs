using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the metrics-card render: the text run, the row read and the card draw.
    private static void McRun(MetricsCardRenderState mc, string res, double fs, double x, double yTd, string text)
        => mc.sb.AppendLine(Compat.Format(mc.inv,
            $"BT /{res} {fs:0.##} Tf 1 0 0 1 {x:F2} {mc.pageHeight - yTd:F2} Tm ({EscapePdfString(text)}) Tj ET"));

    /// <summary></summary>
    private static void DrawMetricsCardRows(MetricsCardRenderState mc)
    {
        foreach (var (label, valueMain, valueSup) in mc.rows)
        {
            var lines = MeasuredWordWrap(label, mc.labelBoxW, mc.face + " Bold", mc.bodyFs);
            var lb = mc.yBase;
            foreach (var line in lines)
            {
                McRun(mc, "F9", mc.bodyFs, McLabelXPt, lb, line);
                lb += McCellLinePt;
            }
            if (valueMain.Length > 0)
            {
                var vBase = valueSup.Length > 0 ? mc.yBase + McSupValueDropPt : mc.yBase;
                McRun(mc, "F8", mc.bodyFs, McValueXPt, vBase, valueMain);
                if (valueSup.Length > 0)
                    McRun(mc, "F8", mc.bodyFs * McSupSizeFactor,
                        McValueXPt + MeasureFaceText(mc.face!, valueMain, mc.bodyFs),
                        vBase - McSupRaisePt, valueSup);
            }
            mc.yBase += McRowPitchPt + (lines.Length - 1) * McCellLinePt;
        }
    }

    /// <summary></summary>
    private static void DrawMetricsCardFrame(MetricsCardRenderState mc)
    {
        mc.doc = Document.Create();
        mc.docFontDict = new Core.PdfDictionary();
        mc.page = mc.doc.Pages.Add(McPageWidthPt, mc.pageHeight);
        EnsureFonts(mc.page, mc.docFontDict);
        mc.faceRes = mc.face!.Replace(" ", "");
        EnsureFont(mc.page, mc.faceRes, "F8");
        EnsureFont(mc.page, mc.faceRes + "Bold", "F9");

        mc.sb = new StringBuilder();
        mc.inv = System.Globalization.CultureInfo.InvariantCulture;
        mc.fillL = McCardLeftPt + McFramePt / 2;
        mc.fillR = McCardRightPt - McFramePt / 2;
        mc.sb.AppendLine(Compat.Format(mc.inv,
            $"q {mc.headBg.R / 255.0:0.###} {mc.headBg.G / 255.0:0.###} {mc.headBg.B / 255.0:0.###} rg " +
            $"{mc.fillL:F2} {mc.pageHeight - McHeadBandBotPt:F2} {mc.fillR - mc.fillL:F2} {McHeadBandBotPt - McHeadBandTopPt:F2} re f Q"));
        mc.sb.AppendLine(Compat.Format(mc.inv,
            $"q 0 0 0 RG {McFramePt:0.##} w " +
            $"{mc.fillL:F2} {mc.pageHeight - McHeadBandBotPt:F2} {mc.fillR - mc.fillL:F2} {McHeadBandBotPt - McHeadBandTopPt:F2} re S " +
            $"{mc.fillL:F2} {mc.pageHeight - McCardBottomPt:F2} {mc.fillR - mc.fillL:F2} {McCardBottomPt - McHeadBandBotPt:F2} re S Q"));

        // Heading text on the band.
        if (mc.headText.Length > 0)
            McRun(mc, "F9", mc.headFs, mc.fillL + McFramePt / 2, McHeadBandTopPt + McHeadBaseDropPt, mc.headText);
    }

    /// <summary></summary>
    private static void ReadMetricsCardRows(MetricsCardRenderState mc)
    {
        foreach (Match rm in Regex.Matches(mc.innerTblM.Groups[1].Value,
                     @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
        {
            var cells = Regex.Matches(rm.Groups[1].Value,
                @"<td\b[^>]*>([\s\S]*?)</td\s*>", RegexOptions.IgnoreCase);
            if (cells.Count < 2) continue;
            var label = CollapseWs(DecodeEntities(
                Regex.Replace(cells[0].Groups[1].Value, "<[^>]+>", " "))).Trim();
            var valRaw = cells[1].Groups[1].Value;
            var supM = Regex.Match(valRaw, @"<sup\b[^>]*>([\s\S]*?)</sup\s*>",
                RegexOptions.IgnoreCase);
            var sup = supM.Success
                ? CollapseWs(DecodeEntities(supM.Groups[1].Value)).Trim() : "";
            var main = CollapseWs(DecodeEntities(Regex.Replace(
                supM.Success ? valRaw[..supM.Index] : valRaw, "<[^>]+>", " "))).Trim();
            mc.rows.Add((label, main, sup));
        }
    }
}
