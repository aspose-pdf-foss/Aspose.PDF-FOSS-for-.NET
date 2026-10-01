using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Bootstrap rows: 
    private static void EmitRun(BootstrapRowsState bs, string res, double fs, double x, double yBaselineTd, string text, Color col)
        => bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
            $"BT {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
            $"/{res} {fs:F2} Tf 1 0 0 1 {x:F2} {bs.pageHeight - yBaselineTd:F2} Tm ({EscapePdfString(text)}) Tj ET\n")));

    private static void Fill(BootstrapRowsState bs, Color c, double x, double yTd, double w, double h)
        => bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg {x:F2} {bs.pageHeight - yTd - h:F2} {w:F2} {h:F2} re f Q\n")));

    private static void StrokeRect(BootstrapRowsState bs, Color c, double x, double yTd, double w, double h, double sw)
        => bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG {sw:0.##} w " +
            $"{x + sw / 2:F2} {bs.pageHeight - yTd - h + sw / 2:F2} {w - sw:F2} {h - sw:F2} re S Q\n")));

    private static string Flat(BootstrapRowsState bs, string frag)
        => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

    // ── the column set of one .row: [(pct, inner html)] ──
    private static List<(double frac, string body)> RowCols(BootstrapRowsState bs, string rowBody)
    {
        var cols = new List<(double, string)>();
        foreach (Match cm in Regex.Matches(rowBody,
            @"<div\b[^>]*class\s*=\s*['""]col-xs-(\d+)[^'""]*['""][^>]*>(?<b>[\s\S]*?)</div>",
            RegexOptions.IgnoreCase))
            cols.Add((int.Parse(cm.Groups[1].Value) / 12.0, cm.Groups["b"].Value));
        return cols;
    }

    // Render one row's columns from rowTop; returns the row's height.
    // draw:false only measures (the panel-heading fill needs the height first).
    private static double RenderRow(BootstrapRowsState bs, string rowBody, double rowTop, double boxL, double boxR,
        bool draw = true, bool headFg = false)
    {
        var rowL = boxL - BrRowExpand;
        var rowW = boxR + BrRowExpand - rowL;
        var cols = RowCols(bs, rowBody);
        var maxH = BrLineH;
        var colX = rowL;
        foreach (var (frac, colBody) in cols)
        {
            var colW = rowW * frac;
            var textX = colX + BrColPad;
            var wrapW = colW - 2 * BrColPad;
            var y = rowTop;
            // bold label segment, then the value after the <br>
            var boldM = Regex.Match(colBody, @"<b\b[^>]*>(?<t>[\s\S]*?)</b>", RegexOptions.IgnoreCase);
            var rest = boldM.Success
                ? colBody.Remove(boldM.Index, boldM.Length) : colBody;
            var linkM = Regex.Match(rest, @"<a\b[^>]*>(?<t>[\s\S]*?)</a>", RegexOptions.IgnoreCase);
            var isLink = false;
            string valueText;
            if (linkM.Success && Flat(bs, rest).Length == Flat(bs, linkM.Groups["t"].Value).Length)
            {
                valueText = Flat(bs, linkM.Groups["t"].Value);
                isLink = true;
            }
            else valueText = Flat(bs, rest);
            if (boldM.Success)
            {
                var lbl = Flat(bs, boldM.Groups["t"].Value);
                if (lbl.Length > 0)
                {
                    if (draw) EmitRun(bs, "FB", BrFontPt, textX, y + bs.drop, lbl,
                        headFg ? BrHeadFg : BrText);
                    y += BrLineH;
                }
            }
            if (valueText.Length > 0)
                foreach (var ln in MeasuredWordWrap(valueText, wrapW, bs.face, BrFontPt))
                {
                    if (ln.Length == 0) continue;
                    if (draw) EmitRun(bs, "FA", BrFontPt, textX, y + bs.drop, ln,
                        isLink ? BrLink : headFg ? BrHeadFg : BrText);
                    y += BrLineH;
                }
            maxH = Math.Max(maxH, y - rowTop);
            colX += colW;
        }
        return maxH;
    }

    private static void Advance(BootstrapRowsState bs, double marginTop)
    {
        bs.yTd += Math.Max(bs.pendingMb, marginTop);
        bs.pendingMb = 0;
    }
}
