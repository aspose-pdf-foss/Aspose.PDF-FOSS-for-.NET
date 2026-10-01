using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>UBL invoice stages: the html parse and the header, items and footer draws.</summary>
    private static bool ParseUblInvoice(UblInvoiceState ub)
    {
        ub.custM = Regex.Match(ub.html,
            @"class\s*=\s*[""']customerAddress[""'][^>]*>(?<body>[\s\S]*?)<br\s*/?>",
            RegexOptions.IgnoreCase);
        if (!ub.custM.Success) return false;
        ub.addrLines = new List<string>();
        // address lines are the text runs of the nested leaf divs, in source order
        {
            var t = ub.custM.Groups["body"].Value;
            // each leaf div contributes one line; EAN trails outside a div
            foreach (Match lm in Regex.Matches(t,
                @"<div\b[^>]*>(?<c>(?:(?!<div)[\s\S])*?)</div>", RegexOptions.IgnoreCase))
            {
                var line = Flat(ub, lm.Groups["c"].Value);
                if (line.Length > 0) ub.addrLines.Add(line);
            }
            // a text run directly after a closing div (the EAN line) is its own line
            foreach (Match tm in Regex.Matches(t, @"</div>\s*(?<txt>[^<>]+?)\s*<",
                RegexOptions.IgnoreCase))
            {
                var line = Flat(ub, tm.Groups["txt"].Value);
                if (line.Length > 0) ub.addrLines.Add(line);
            }
        }
        ub.h2M = Regex.Match(ub.html, @"<h2\b[^>]*>([\s\S]*?)</h2>", RegexOptions.IgnoreCase);
        ub.h2Text = ub.h2M.Success ? Flat(ub, ub.h2M.Groups[1].Value) : "";
        ub.logoM = Regex.Match(ub.html, @"class\s*=\s*[""']logo[""'][^>]*>([\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        ub.logoText = ub.logoM.Success ? Flat(ub, ub.logoM.Groups[1].Value).ToUpperInvariant() : "";
        ub.refRows = new List<(string Label, string Value)>();
        ub.refM = Regex.Match(ub.html, @"class\s*=\s*[""']references[""'][^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (ub.refM.Success)
            foreach (Match rm in Regex.Matches(ub.refM.Groups[1].Value,
                @"<tr[^>]*>\s*<td[^>]*>([\s\S]*?)</td>\s*<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
                ub.refRows.Add((Flat(ub, rm.Groups[1].Value).ToUpperInvariant(), Flat(ub, rm.Groups[2].Value)));
        ub.headlineM = Regex.Match(ub.html,
            @"<div\b[^>]*class\s*=\s*[""']headline[""'][^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
        ub.headline = ub.headlineM.Success ? Flat(ub, ub.headlineM.Groups[1].Value).ToUpperInvariant() : "";
        ub.itemThs = new List<(string Text, bool Right)>();
        ub.lineTds = new List<(string Text, bool Right)>();
        ub.linesM = Regex.Match(ub.html,
            @"class\s*=\s*[""']invoice-viewer invoice-lines[""'][^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (!ub.linesM.Success) return false;
        foreach (Match th in Regex.Matches(ub.linesM.Groups[1].Value,
            @"<th\b(?<a>[^>]*)>(?<c>[\s\S]*?)</th>", RegexOptions.IgnoreCase))
            ub.itemThs.Add((Flat(ub, th.Groups["c"].Value).ToUpperInvariant(),
                th.Groups["a"].Value.Contains("right", StringComparison.OrdinalIgnoreCase)));
        ub.lineRowM = Regex.Match(ub.linesM.Groups[1].Value,
            @"class\s*=\s*[""']UBLInvoiceLine[""'][\s\S]*?</tr>", RegexOptions.IgnoreCase);
        if (ub.lineRowM.Success)
            foreach (Match td in Regex.Matches(ub.lineRowM.Value,
                @"<td\b(?<a>[^>]*)>(?<c>[\s\S]*?)</td>", RegexOptions.IgnoreCase))
                ub.lineTds.Add((Flat(ub, td.Groups["c"].Value),
                    td.Groups["a"].Value.Contains("right", StringComparison.OrdinalIgnoreCase)));
        ub.totRows = new List<(string Label, string Value, bool Bold, double PadTop)>();
        ub.totM = Regex.Match(ub.html,
            @"class\s*=\s*[""']invoice-viewer invoice-lines-totals[""'][^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (ub.totM.Success)
            foreach (Match rm in Regex.Matches(ub.totM.Groups[1].Value,
                @"<tr[^>]*>\s*<td(?<a1>[^>]*)>(?<l>[\s\S]*?)</td>\s*<td(?<a2>[^>]*)>(?<v>[\s\S]*?)</td>",
                RegexOptions.IgnoreCase))
            {
                var label = Flat(ub, rm.Groups["l"].Value);
                var value = Flat(ub, rm.Groups["v"].Value);
                var bold = rm.Groups["v"].Value.Contains("<b>", StringComparison.OrdinalIgnoreCase)
                    || rm.Value.Contains("<b>", StringComparison.OrdinalIgnoreCase);
                var padTop = rm.Value.Contains("padding-top", StringComparison.OrdinalIgnoreCase) ? 7.5 : 0;
                var headlineLabel = rm.Groups["l"].Value.Contains("headline", StringComparison.OrdinalIgnoreCase);
                if (headlineLabel) label = label.ToUpperInvariant();
                ub.totRows.Add((label, value, bold || headlineLabel, padTop));
            }
        ub.supLines = new List<string>();
        ub.supM = Regex.Match(ub.html,
            @"class\s*=\s*[""']invoice-viewer supplierAddress[""'][^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (ub.supM.Success)
        {
            var cell = Regex.Match(ub.supM.Groups[1].Value, @"<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
            if (cell.Success)
                foreach (var part in Regex.Split(cell.Groups[1].Value, @"<br\s*/?>", RegexOptions.IgnoreCase))
                {
                    var line = Flat(ub, part);
                    if (line.Length > 0) ub.supLines.Add(line);
                }
        }
        if (ub.addrLines.Count == 0 || ub.itemThs.Count == 0) return false;
        return true;
    }

    /// <summary></summary>
    private static void DrawUblFooter(UblInvoiceState ub)
    {
        ub.emptyHeights = new[] { 1.5, 1.5, 2 * ub.lineH + 1.5, 1.5 };
        ub.ySp = Math.Max(ub.yTot, ub.yTd) + 15.0;
        foreach (var eh in ub.emptyHeights)
        {
            FillW(ub, UblMarginX, ub.ySp, ub.contentW, eh);
            ub.ySp += eh + 15.0;
        }
        // .supplierAddress margin-top 50px collapses with the spacer's 20px
        ub.ySp += 37.5 - 15.0;
        ub.supH = ub.supLines.Count * ub.lineH + 1.5;
        FillW(ub, UblMarginX, ub.ySp, ub.contentW, ub.supH);
        ub.ySup = ub.ySp + UblCellInset + ub.cellDrop;
        foreach (var line in ub.supLines)
        {
            var w = MeasureFaceText("Verdana", line, ub.cellFs);
            Emit(ub, "FV", ub.cellFs, UblMarginX + (ub.contentW - w) / 2, ub.ySup, line);
            ub.ySup += ub.lineH;
        }
    }

    /// <summary></summary>
    private static void DrawUblItems(UblInvoiceState ub)
    {
        ub.yTd = UblTop + ub.headerH + 45.0;
        ub.headlineFs = 10.5;
        ub.headlineLineH = MetricLineHeight(ub.headlineFs, HheaLineSumFor("Arial") ?? 1.15);
        ub.headlineDrop = MetricBaselineDrop(ub.headlineFs, ub.headlineLineH, ub.am);
        Emit(ub, "F2", ub.headlineFs, UblMarginX, ub.yTd + ub.headlineDrop, ub.headline);
        ub.yTd += ub.headlineLineH;

        ub.tableTop = ub.yTd;
        ub.colW = new double[ub.itemThs.Count];
        for (var c = 0; c < ub.itemThs.Count; c++)
            ub.colW[c] = c == 1 ? 0.40 * ub.contentW
                : MeasureFaceText("Verdana-Bold", ub.itemThs[c].Text, ub.cellFs) + 7.5;
        ub.colX = new double[ub.itemThs.Count + 1];
        ub.colX[0] = UblMarginX;
        for (var c = 0; c < ub.itemThs.Count; c++) ub.colX[c + 1] = ub.colX[c] + ub.colW[c];
        ub.rowH = UblCellInset + 11.25 + ub.lineH + 7.5;
        ub.lineRowH = UblCellInset + ub.lineH + 7.5;
        const double UblEmptyRowPt = 2.65;                // the trailing all-empty row's collapsed band (measured)
        ub.tableH = ub.rowH + ub.lineRowH + UblEmptyRowPt;
        FillW(ub, UblMarginX, ub.tableTop, ub.contentW, ub.tableH);
        Rule(ub, UblMarginX, ub.rightEdge, ub.tableTop + 0.38, 0.75);
        ub.thBase = ub.tableTop + UblCellInset + 11.25 + ub.cellDrop;
        for (var c = 0; c < ub.itemThs.Count; c++)
        {
            var (text, right) = ub.itemThs[c];
            if (right) EmitRight(ub, "FW", "Verdana-Bold", ub.cellFs, ub.colX[c + 1] - 0.75, ub.thBase, text);
            else Emit(ub, "FW", ub.cellFs, ub.colX[c] + (c == 0 ? 0 : 7.5), ub.thBase, text);
        }
        ub.rowBase = ub.thBase + ub.lineH + 7.5 + UblCellInset;
        for (var c = 0; c < ub.lineTds.Count && c < ub.itemThs.Count; c++)
        {
            var (text, right) = ub.lineTds[c];
            if (text.Length == 0) continue;
            if (right) EmitRight(ub, "FV", "Verdana", ub.cellFs, ub.colX[c + 1] - 0.75, ub.rowBase, text);
            else Emit(ub, "FV", ub.cellFs, ub.colX[c] + (c == 0 ? 0 : 7.5), ub.rowBase, text);
        }
        ub.tableBottom = ub.tableTop + ub.tableH;
        Rule(ub, UblMarginX, ub.rightEdge, ub.tableBottom - 0.75, 0.75);
        ub.yTd = ub.tableBottom + 7.5;                   // .invoice-lines margin-bottom 10px

        ub.labelW = 0;
        ub.valueW = 0;
        foreach (var (label, value, bold, _) in ub.totRows)
        {
            ub.labelW = Math.Max(ub.labelW, MeasureFaceText(bold ? "Verdana-Bold" : "Verdana", label, ub.cellFs));
            ub.valueW = Math.Max(ub.valueW, MeasureFaceText(bold ? "Verdana-Bold" : "Verdana", value, ub.cellFs));
        }
        ub.totW = ub.labelW + 45.0 + ub.valueW + UblCellInset;
        ub.totX = ub.rightEdge - ub.totW;
        ub.totRowH = ub.lineH + 1.5;
        ub.yTot = ub.yTd;
        foreach (var (label, value, bold, padTop) in ub.totRows)
        {
            FillW(ub, ub.totX, ub.yTot, ub.totW, ub.totRowH + padTop);
            var res = bold ? "FW" : "FV";
            var face = bold ? "Verdana-Bold" : "Verdana";
            Emit(ub, res, ub.cellFs, ub.totX, ub.yTot + padTop + UblCellInset + ub.cellDrop, label);
            EmitRight(ub, res, face, ub.cellFs, ub.rightEdge - UblCellInset,
                ub.yTot + padTop + UblCellInset + ub.cellDrop, value);
            ub.yTot += ub.totRowH + padTop;
        }
        Rule(ub, ub.totX, ub.rightEdge, ub.yTot - 0.75, 0.75);
        ub.yTot += 0.75;
    }

    /// <summary></summary>
    private static void DrawUblHeader(UblInvoiceState ub)
    {
        ub.headerH = 2 * UblCellInset + Math.Max(
            (ub.addrLines.Count + 1) * ub.lineH + WrapH2Lines(ub.h2Text).Count * ub.lineH,
            2 * 27.0 + 2 * ub.lineH + ub.refRows.Count * UblRefLinePt);
        FillW(ub, UblMarginX, UblTop, ub.contentW, ub.headerH);
        ub.yAddr = UblTop + UblCellInset + ub.cellDrop;
        foreach (var line in ub.addrLines)
        {
            Emit(ub, "FV", ub.cellFs, UblMarginX + UblCellInset, ub.yAddr, line);
            ub.yAddr += ub.lineH;
        }
        ub.yAddr += ub.lineH;                            // the <br> between address and FAKTURA
        ub.h2Fs = ub.cellFs * 1.5;
        ub.h2Drop = MetricBaselineDrop(ub.h2Fs, ub.lineH, ub.vm);
        foreach (var line in WrapH2Lines(ub.h2Text))
        {
            Emit(ub, "FW", ub.h2Fs, UblMarginX + UblCellInset, ub.yAddr - ub.cellDrop + ub.h2Drop, line);
            ub.yAddr += ub.lineH;
        }
        ub.logoFs = 27.0;
        ub.logoDrop = MetricBaselineDrop(ub.logoFs, ub.logoFs, ub.vm);
        ub.yLogo = UblTop + UblCellInset + ub.logoDrop;
        foreach (var word in ub.logoText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            EmitRight(ub, "FW", "Verdana-Bold", ub.logoFs, ub.rightEdge - UblCellInset, ub.yLogo, word);
            ub.yLogo += ub.logoFs;
        }
        ub.refDrop = MetricBaselineDrop(ub.cellFs, UblRefLinePt, ub.vm);
        ub.yRef = ub.yLogo - ub.logoDrop + 2 * ub.lineH + ub.refDrop;
        ub.refValueW = 0;
        foreach (var (_, value) in ub.refRows)
            ub.refValueW = Math.Max(ub.refValueW, MeasureFaceText("Verdana", value, ub.cellFs));
        ub.refValueRight = ub.rightEdge - 2 * UblCellInset;
        ub.refLabelRight = ub.refValueRight - ub.refValueW - 15.0;
        foreach (var (label, value) in ub.refRows)
        {
            EmitRight(ub, "FV", "Verdana", ub.cellFs, ub.refValueRight, ub.yRef, value);
            EmitRight(ub, "FV", "Verdana", ub.cellFs, ub.refLabelRight, ub.yRef, label);
            ub.yRef += UblRefLinePt;
        }
    }
}
