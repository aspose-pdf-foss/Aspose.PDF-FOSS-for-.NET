using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Contract invoice card stages: the heading card and the line-item table.</summary>
    private static void DrawInvoiceTable(ContractInvoiceState cv, InvoiceCardState ic)
    {
        for (var i = 0; i < ic.ths.Count && i < 3; i++)
        {
            var cw = cv.colX[i + 1] - cv.colX[i];
            var words = MeasuredWordWrap(ic.ths[i], cw - 30.0, cv.regFace, 15);
            var bandMid = ic.thTop + CiTheadHPt / 2;
            var yLn = bandMid - words.Length * 9.0;
            foreach (var ln in words)
            {
                EmitRun(cv, ln, 15, cv.regFace, cv.colX[i] + (cw - W(cv, ln, cv.regFace, 15)) / 2,
                    yLn + Drop(cv, 15, 18), cv.white);
                yLn += 18.0;
            }
        }
        ic.rTop = ic.thTop + CiTheadHPt;
        for (var ri = 0; ri < ic.rows.Count; ri++)
        {
            var isLast = ri == ic.rows.Count - 1;
            var cells = ic.rows[ri];
            if (!isLast)
            {
                // wrap each cell; the row grows to the tallest and the
                // single-line cells centre in the taller band
                var wrapped = new string[Math.Min(cells.Count, 3)][];
                var maxLines = 1;
                for (var ci = 0; ci < wrapped.Length; ci++)
                {
                    wrapped[ci] = MeasuredWordWrap(cells[ci],
                        cv.colX[ci + 1] - cv.colX[ci] - 30.0, cv.lightFace, 13);
                    maxLines = Math.Max(maxLines, wrapped[ci].Length);
                }
                var rowH = CiRowHPt + (maxLines - 1) * 15.8;
                for (var ci = 0; ci < wrapped.Length; ci++)
                {
                    var cw = cv.colX[ci + 1] - cv.colX[ci];
                    var yC = ic.rTop + CiRowBasePt
                        + (maxLines - wrapped[ci].Length) * 15.8 / 2;
                    foreach (var ln in wrapped[ci])
                    {
                        EmitRun(cv, ln, 13, cv.lightFace,
                            cv.colX[ci] + (cw - W(cv, ln, cv.lightFace, 13)) / 2, yC, cv.ink);
                        yC += 15.8;
                    }
                }
                // the row's cell borders
                Line(cv, cv.tX, ic.rTop + rowH, cv.tX + cv.tW, ic.rTop + rowH, cv.orange, 0.75, false);
                for (var ci = 0; ci <= 3; ci++)
                    Line(cv, cv.colX[ci], ic.rTop, cv.colX[ci], ic.rTop + rowH, cv.orange, 0.75, false);
                ic.rTop += rowH;
            }
            else
            {
                var cw1 = cv.colX[2] - cv.colX[1];
                var totLines = MeasuredWordWrap(cells.Count > 1 ? cells[1] : "",
                    cw1 - 30.0, cv.regFace, 15);
                var yT = ic.rTop + CiRowBasePt + 1.9;
                foreach (var ln in totLines)
                {
                    EmitRun(cv, ln, 15, cv.regFace,
                        cv.colX[2] - 15.0 - W(cv, ln, cv.regFace, 15), yT + 0, cv.ink);
                    yT += 18.0;
                }
                var amt2 = cells.Count > 2 ? cells[2] : "";
                var midY = ic.rTop + CiRowBasePt + 1.9 + (totLines.Length - 1) * 9.0;
                EmitRun(cv, amt2, 15, cv.regFace,
                    cv.colX[2] + (cv.colX[3] - cv.colX[2] - W(cv, amt2, cv.regFace, 15)) / 2,
                    midY, cv.ink);
                ic.rTop += CiRowHPt + (totLines.Length - 1) * 18.0;
            }
        }
    }

    /// <summary></summary>
    private static bool TryRenderInvoiceHeading(ContractInvoiceState cv, InvoiceCardState ic, Match cM)
    {
        if (cM.Groups[1].Value.Equals("total", StringComparison.OrdinalIgnoreCase))
        {
            // the float-right total strip: 18 pt blue, borderless
            var tds = Regex.Matches(ic.inner, @"<td[^>]*>(.*?)</td>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (tds.Count >= 3)
            {
                // measured: the strip draws at 15 pt, its amount cell
                // right edge 45 in from the content right, the label a
                // 30 pt cell-pad gap before it
                var lbl = CollapseWs(DecodeEntities(tds[1].Groups[1].Value)).Trim();
                var amt = CollapseWs(DecodeEntities(tds[2].Groups[1].Value)).Trim();
                cv.y += 15.7;
                var xAmt = CiContentXPt + CiContentWPt - 45.0 - W(cv, amt, cv.regFace, 15);
                EmitRun(cv, amt, 15, cv.regFace, xAmt, cv.y + Drop(cv, 15, 18), cv.blue);
                EmitRun(cv, lbl, 15, cv.regFace, xAmt - 30.0 - W(cv, lbl, cv.regFace, 15),
                    cv.y + Drop(cv, 15, 18), cv.blue);
            }
            return true;
        }
        return false;
    }

    /// <summary></summary>
    private static void RenderInvoiceHeader(ContractInvoiceState cv)
    {

        cv.ink = ParseCssColor("#627881") ?? Color.FromArgb(98, 120, 129);
        cv.blue = ParseCssColor("#5d80ba") ?? Color.FromArgb(93, 128, 186);
        cv.orange = ParseCssColor("#f69a1b") ?? Color.FromArgb(246, 154, 27);
        cv.white = Color.FromArgb(255, 255, 255);

        cv.doc = Document.Create();
        cv.docFontDict = new Core.PdfDictionary();
        cv.page = cv.doc.Pages.Add(CiPageWPt, cv.pageHeight);
        EnsureFonts(cv.page, cv.docFontDict);
        cv.sb = new StringBuilder();
        cv.streams = new List<(Page pg, StringBuilder ops)> { (cv.page, cv.sb) };

        cv.yTop = 72.0 + 6.0;
        {
            var h1s = new List<string>();
            var infoM = Regex.Match(cv.html, @"class='info'[^>]*>(.*?)</div>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (infoM.Success)
                foreach (Match hM in Regex.Matches(infoM.Groups[1].Value, @"<h1[^>]*>(.*?)</h1>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    h1s.Add(CollapseWs(DecodeEntities(
                        Regex.Replace(hM.Groups[1].Value, "<[^>]+>", " "))).Trim());
            double boxW = 0;
            foreach (var t in h1s) boxW = Math.Max(boxW, W(cv, t, cv.lightFace, 16));
            boxW += 45.0;                             // 30 px side pads
            var boxH = 30.0 + h1s.Count * 19.5;       // 20 px pads + 16 pt lines
            FillRect(cv, CiContentXPt, cv.yTop, boxW, boxH, cv.blue);
            var yH1 = cv.yTop + 15.0;
            foreach (var t in h1s)
            {
                EmitRun(cv, t, 16, cv.lightFace, CiContentXPt + 22.5, yH1 + Drop(cv, 16, 19.5), cv.white);
                yH1 += 19.5;
            }
            var logoM = Regex.Match(cv.html, @"class='logo'[^>]*>\s*<img[^>]*src='(https?://[^']+)'",
                RegexOptions.IgnoreCase);
            if (logoM.Success && FetchRemoteImage(logoM.Groups[1].Value) is { } logoBytes
                && logoBytes.Length > 24)
            {
                double natW = 0, natH = 0;
                if (logoBytes[1] == 'P' && logoBytes.Length > 24)
                {
                    natW = (logoBytes[16] << 24) | (logoBytes[17] << 16) | (logoBytes[18] << 8) | logoBytes[19];
                    natH = (logoBytes[20] << 24) | (logoBytes[21] << 16) | (logoBytes[22] << 8) | logoBytes[23];
                }
                if (natW > 0 && natH > 0)
                {
                    var iw = natW * 0.75; var ih = natH * 0.75;
                    cv.page.AddImage(logoBytes, new Rectangle(
                        CiContentXPt + CiContentWPt - iw, cv.pageHeight - cv.yTop - ih,
                        CiContentXPt + CiContentWPt, cv.pageHeight - cv.yTop));
                }
            }
        }
        cv.y = cv.yTop + CiHeaderHPt;

        cv.colShare = new[] { 141.5 / 405.0, 177.3 / 405.0, 85.8 / 405.0 };
        cv.tX = CiContentXPt + 22.5;
        cv.tW = CiContentWPt - 45.0;
        cv.colX = new double[4];
        cv.colX[0] = cv.tX;
        for (var i = 0; i < 3; i++) cv.colX[i + 1] = cv.colX[i] + cv.colShare[i] * cv.tW;

        cv.first = true;
    }
}
