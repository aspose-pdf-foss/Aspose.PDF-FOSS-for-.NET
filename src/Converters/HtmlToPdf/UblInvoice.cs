using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The UBL invoice-frame dialect (`#invoice-frame` + `.invoice-viewer` + the UBL
// class namespace): a Danish e-invoice export whose PRINT media rules put the
// tables in Verdana at 14px while the frame's own stack resolves to Arial for
// bare divs (the VARER headline). It lays on SYMMETRIC 96 pt side
// margins one UA body margin below the 72 pt top, paces table cells on the
// sheet's 20px line-height with the cell content inset one collapsed-border
// step (0.75) below the row top, sizes the item table's columns at each
// heading's width plus the 10px column padding (the 40% description column
// declared), floats the totals table right, and collapses the supplier block's
// 50px margin-top with the spacer's 20px bottom margin. Constants not read from
// the stylesheet are measured on the expected render.
internal static partial class HtmlToPdfConverter
{
    private const double UblMarginX = 96.0;
    private const double UblTop = 78.0;            // 72 pt margin + the UA 6 pt body margin
    private const double UblCellInset = 0.75;      // cell content sits one collapsed border below the row top
    private const double UblRefLinePt = 14.25;     // the floated references table's 19px line (measured)

    /// <summary>Render a UBL invoice-frame export, or null when the page does
    /// not carry the dialect's fingerprint.</summary>
    private static Document? TryRenderUblInvoice(string html,
        double pageWidth, double pageHeight)
    {
        if (!Regex.IsMatch(html, @"id\s*=\s*[""']invoice-frame[""']", RegexOptions.IgnoreCase)
            || html.IndexOf("invoice-viewer", StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("UBLInvoiceLine", StringComparison.OrdinalIgnoreCase) < 0)
            return null;
        if (WinMetricsFor("Verdana") is not { } vm || WinMetricsFor("Arial") is not { } am)
            return null;

        // the sheet's print-media table font (14px Verdana) and 20px line
        var cellFs = 10.5;
        var lineH = 15.0;
        var cellDrop = MetricBaselineDrop(cellFs, lineH, vm);

        // tags drop without a stand-in space (the sheet's spans butt together:
        // DK-8900), and NBSP survives the collapse (the
        // nbsp-spaced runs at their two-space width)
        string Flat(string frag) => Regex.Replace(DecodeEntities(
            Regex.Replace(frag, @"<[^>]+>", "")), @"[ \t\r\n]+", " ").Trim();

        // ── parse ──
        // header table: the address cell and the logo/references cell
        var custM = Regex.Match(html,
            @"class\s*=\s*[""']customerAddress[""'][^>]*>(?<body>[\s\S]*?)<br\s*/?>",
            RegexOptions.IgnoreCase);
        if (!custM.Success) return null;
        var addrLines = new List<string>();
        // address lines are the text runs of the nested leaf divs, in source order
        {
            var t = custM.Groups["body"].Value;
            // each leaf div contributes one line; EAN trails outside a div
            foreach (Match lm in Regex.Matches(t,
                @"<div\b[^>]*>(?<c>(?:(?!<div)[\s\S])*?)</div>", RegexOptions.IgnoreCase))
            {
                var line = Flat(lm.Groups["c"].Value);
                if (line.Length > 0) addrLines.Add(line);
            }
            // a text run directly after a closing div (the EAN line) is its own line
            foreach (Match tm in Regex.Matches(t, @"</div>\s*(?<txt>[^<>]+?)\s*<",
                RegexOptions.IgnoreCase))
            {
                var line = Flat(tm.Groups["txt"].Value);
                if (line.Length > 0) addrLines.Add(line);
            }
        }
        var h2M = Regex.Match(html, @"<h2\b[^>]*>([\s\S]*?)</h2>", RegexOptions.IgnoreCase);
        var h2Text = h2M.Success ? Flat(h2M.Groups[1].Value) : "";
        var logoM = Regex.Match(html, @"class\s*=\s*[""']logo[""'][^>]*>([\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        var logoText = logoM.Success ? Flat(logoM.Groups[1].Value).ToUpperInvariant() : "";
        var refRows = new List<(string Label, string Value)>();
        var refM = Regex.Match(html, @"class\s*=\s*[""']references[""'][^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (refM.Success)
            foreach (Match rm in Regex.Matches(refM.Groups[1].Value,
                @"<tr[^>]*>\s*<td[^>]*>([\s\S]*?)</td>\s*<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
                refRows.Add((Flat(rm.Groups[1].Value).ToUpperInvariant(), Flat(rm.Groups[2].Value)));
        var headlineM = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*[""']headline[""'][^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
        var headline = headlineM.Success ? Flat(headlineM.Groups[1].Value).ToUpperInvariant() : "";
        // item table: header row + line rows
        var itemThs = new List<(string Text, bool Right)>();
        var lineTds = new List<(string Text, bool Right)>();
        var linesM = Regex.Match(html,
            @"class\s*=\s*[""']invoice-viewer invoice-lines[""'][^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (!linesM.Success) return null;
        foreach (Match th in Regex.Matches(linesM.Groups[1].Value,
            @"<th\b(?<a>[^>]*)>(?<c>[\s\S]*?)</th>", RegexOptions.IgnoreCase))
            itemThs.Add((Flat(th.Groups["c"].Value).ToUpperInvariant(),
                th.Groups["a"].Value.Contains("right", StringComparison.OrdinalIgnoreCase)));
        var lineRowM = Regex.Match(linesM.Groups[1].Value,
            @"class\s*=\s*[""']UBLInvoiceLine[""'][\s\S]*?</tr>", RegexOptions.IgnoreCase);
        if (lineRowM.Success)
            foreach (Match td in Regex.Matches(lineRowM.Value,
                @"<td\b(?<a>[^>]*)>(?<c>[\s\S]*?)</td>", RegexOptions.IgnoreCase))
                lineTds.Add((Flat(td.Groups["c"].Value),
                    td.Groups["a"].Value.Contains("right", StringComparison.OrdinalIgnoreCase)));
        // totals table rows: label + right-aligned value (bold on the total row)
        var totRows = new List<(string Label, string Value, bool Bold, double PadTop)>();
        var totM = Regex.Match(html,
            @"class\s*=\s*[""']invoice-viewer invoice-lines-totals[""'][^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (totM.Success)
            foreach (Match rm in Regex.Matches(totM.Groups[1].Value,
                @"<tr[^>]*>\s*<td(?<a1>[^>]*)>(?<l>[\s\S]*?)</td>\s*<td(?<a2>[^>]*)>(?<v>[\s\S]*?)</td>",
                RegexOptions.IgnoreCase))
            {
                var label = Flat(rm.Groups["l"].Value);
                var value = Flat(rm.Groups["v"].Value);
                var bold = rm.Groups["v"].Value.Contains("<b>", StringComparison.OrdinalIgnoreCase)
                    || rm.Value.Contains("<b>", StringComparison.OrdinalIgnoreCase);
                var padTop = rm.Value.Contains("padding-top", StringComparison.OrdinalIgnoreCase) ? 7.5 : 0;
                var headlineLabel = rm.Groups["l"].Value.Contains("headline", StringComparison.OrdinalIgnoreCase);
                if (headlineLabel) label = label.ToUpperInvariant();
                totRows.Add((label, value, bold || headlineLabel, padTop));
            }
        // supplier footer lines (the <br> splits them)
        var supLines = new List<string>();
        var supM = Regex.Match(html,
            @"class\s*=\s*[""']invoice-viewer supplierAddress[""'][^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (supM.Success)
        {
            var cell = Regex.Match(supM.Groups[1].Value, @"<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
            if (cell.Success)
                foreach (var part in Regex.Split(cell.Groups[1].Value, @"<br\s*/?>", RegexOptions.IgnoreCase))
                {
                    var line = Flat(part);
                    if (line.Length > 0) supLines.Add(line);
                }
        }
        if (addrLines.Count == 0 || itemThs.Count == 0) return null;

        // ── layout ──
        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        EnsureFont(page, "Verdana", "FV");
        EnsureFont(page, "Verdana-Bold", "FW");
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        var contentW = pageWidth - 2 * UblMarginX;
        var rightEdge = UblMarginX + contentW;

        void Emit(string res, double fs, double x, double yTd, string text)
            => EmitPositionedRun(page, res, fs, x, pageHeight - yTd, text);
        void EmitRight(string res, string face, double fs, double x1, double yTd, string text)
            => Emit(res, fs, x1 - MeasureFaceText(face, text, fs), yTd, text);
        void FillW(double x, double yTd, double w, double h)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q 1 1 1 rg {x:F2} {pageHeight - yTd - h:F2} {w:F2} {h:F2} re f Q\n")));
        void Rule(double x0, double x1, double yTd, double w)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q 0.533 0.533 0.533 RG {w:0.##} w {x0:F2} {pageHeight - yTd:F2} m {x1:F2} {pageHeight - yTd:F2} l S Q\n")));

        // ── header table: white box, address column, logo + references ──
        var headerH = 2 * UblCellInset + Math.Max(
            (addrLines.Count + 1) * lineH + WrapH2Lines(h2Text).Count * lineH,
            2 * 27.0 + 2 * lineH + refRows.Count * UblRefLinePt);
        FillW(UblMarginX, UblTop, contentW, headerH);
        var yAddr = UblTop + UblCellInset + cellDrop;
        foreach (var line in addrLines)
        {
            Emit("FV", cellFs, UblMarginX + UblCellInset, yAddr, line);
            yAddr += lineH;
        }
        yAddr += lineH;                            // the <br> between address and FAKTURA
        // the inline h2: 1.5em type on the inherited 20px line (negative leading)
        var h2Fs = cellFs * 1.5;
        var h2Drop = MetricBaselineDrop(h2Fs, lineH, vm);
        foreach (var line in WrapH2Lines(h2Text))
        {
            Emit("FW", h2Fs, UblMarginX + UblCellInset, yAddr - cellDrop + h2Drop, line);
            yAddr += lineH;
        }
        // logo: 36px bold uppercase, right-aligned, wrapped per word on the 36px line
        var logoFs = 27.0;
        var logoDrop = MetricBaselineDrop(logoFs, logoFs, vm);
        var yLogo = UblTop + UblCellInset + logoDrop;
        foreach (var word in logoText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            EmitRight("FW", "Verdana-Bold", logoFs, rightEdge - UblCellInset, yLogo, word);
            yLogo += logoFs;
        }
        // two <br> lines, then the floated references table: both columns share
        // fixed edges — values right-aligned two borders inside the frame, labels
        // right-aligned one 20px padding left of the widest value
        var refDrop = MetricBaselineDrop(cellFs, UblRefLinePt, vm);
        var yRef = yLogo - logoDrop + 2 * lineH + refDrop;
        double refValueW = 0;
        foreach (var (_, value) in refRows)
            refValueW = Math.Max(refValueW, MeasureFaceText("Verdana", value, cellFs));
        var refValueRight = rightEdge - 2 * UblCellInset;
        var refLabelRight = refValueRight - refValueW - 15.0;
        foreach (var (label, value) in refRows)
        {
            EmitRight("FV", "Verdana", cellFs, refValueRight, yRef, value);
            EmitRight("FV", "Verdana", cellFs, refLabelRight, yRef, label);
            yRef += UblRefLinePt;
        }

        // ── the VARER headline: the frame's Arial on its hhea 16px line ──
        var yTd = UblTop + headerH + 45.0;         // #headerTable margin-bottom 60px
        var headlineFs = 10.5;                     // .headline 14px
        var headlineLineH = MetricLineHeight(headlineFs, HheaLineSumFor("Arial") ?? 1.15);
        var headlineDrop = MetricBaselineDrop(headlineFs, headlineLineH, am);
        Emit("F2", headlineFs, UblMarginX, yTd + headlineDrop, headline);
        yTd += headlineLineH;

        // ── the item table: top/bottom hairlines, headings at their widths ──
        var tableTop = yTd;
        // columns: each heading's width plus the following column's 10px padding;
        // the description column takes its declared 40%
        var colW = new double[itemThs.Count];
        for (var c = 0; c < itemThs.Count; c++)
            colW[c] = c == 1 ? 0.40 * contentW
                : MeasureFaceText("Verdana-Bold", itemThs[c].Text, cellFs) + 7.5;
        var colX = new double[itemThs.Count + 1];
        colX[0] = UblMarginX;
        for (var c = 0; c < itemThs.Count; c++) colX[c + 1] = colX[c] + colW[c];
        var rowH = UblCellInset + 11.25 + lineH + 7.5;   // th padding-top 15px / bottom 10px
        var lineRowH = UblCellInset + lineH + 7.5;        // item row + its 10px bottom padding
        const double UblEmptyRowPt = 2.65;                // the trailing all-empty row's collapsed band (measured)
        var tableH = rowH + lineRowH + UblEmptyRowPt;
        FillW(UblMarginX, tableTop, contentW, tableH);
        Rule(UblMarginX, rightEdge, tableTop + 0.38, 0.75);
        var thBase = tableTop + UblCellInset + 11.25 + cellDrop;
        for (var c = 0; c < itemThs.Count; c++)
        {
            var (text, right) = itemThs[c];
            if (right) EmitRight("FW", "Verdana-Bold", cellFs, colX[c + 1] - 0.75, thBase, text);
            else Emit("FW", cellFs, colX[c] + (c == 0 ? 0 : 7.5), thBase, text);
        }
        var rowBase = thBase + lineH + 7.5 + UblCellInset;
        for (var c = 0; c < lineTds.Count && c < itemThs.Count; c++)
        {
            var (text, right) = lineTds[c];
            if (text.Length == 0) continue;
            if (right) EmitRight("FV", "Verdana", cellFs, colX[c + 1] - 0.75, rowBase, text);
            else Emit("FV", cellFs, colX[c] + (c == 0 ? 0 : 7.5), rowBase, text);
        }
        var tableBottom = tableTop + tableH;
        Rule(UblMarginX, rightEdge, tableBottom - 0.75, 0.75);
        yTd = tableBottom + 7.5;                   // .invoice-lines margin-bottom 10px

        // ── the totals table, floated right ──
        double labelW = 0, valueW = 0;
        foreach (var (label, value, bold, _) in totRows)
        {
            labelW = Math.Max(labelW, MeasureFaceText(bold ? "Verdana-Bold" : "Verdana", label, cellFs));
            valueW = Math.Max(valueW, MeasureFaceText(bold ? "Verdana-Bold" : "Verdana", value, cellFs));
        }
        var totW = labelW + 45.0 + valueW + UblCellInset;   // td padding-left 60px between
        var totX = rightEdge - totW;
        var totRowH = lineH + 1.5;
        var yTot = yTd;
        foreach (var (label, value, bold, padTop) in totRows)
        {
            FillW(totX, yTot, totW, totRowH + padTop);
            var res = bold ? "FW" : "FV";
            var face = bold ? "Verdana-Bold" : "Verdana";
            Emit(res, cellFs, totX, yTot + padTop + UblCellInset + cellDrop, label);
            EmitRight(res, face, cellFs, rightEdge - UblCellInset,
                yTot + padTop + UblCellInset + cellDrop, value);
            yTot += totRowH + padTop;
        }
        Rule(totX, rightEdge, yTot - 0.75, 0.75);
        yTot += 0.75;

        // ── the empty spacer tables, then the supplier footer ──
        // each empty .invoice-viewer is a 1.5 pt white sliver with its 20px
        // bottom margin; the one carrying two <br>s holds their line boxes
        var emptyHeights = new[] { 1.5, 1.5, 2 * lineH + 1.5, 1.5 };
        var ySp = Math.Max(yTot, yTd) + 15.0;      // the floated totals' own 20px margin-bottom
        foreach (var eh in emptyHeights)
        {
            FillW(UblMarginX, ySp, contentW, eh);
            ySp += eh + 15.0;
        }
        // .supplierAddress margin-top 50px collapses with the spacer's 20px
        ySp += 37.5 - 15.0;
        var supH = supLines.Count * lineH + 1.5;
        FillW(UblMarginX, ySp, contentW, supH);
        var ySup = ySp + UblCellInset + cellDrop;
        foreach (var line in supLines)
        {
            var w = MeasureFaceText("Verdana", line, cellFs);
            Emit("FV", cellFs, UblMarginX + (contentW - w) / 2, ySup, line);
            ySup += lineH;
        }
        return doc;
    }

    /// <summary>The inline FAKTURA h2 wraps at the address cell's width — the
    /// sheet's runs break after the heading's first word (measured: two lines).</summary>
    private static List<string> WrapH2Lines(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1) return new List<string> { text };
        return new List<string> { words[0] + " ", string.Join(" ", words[1..]) };
    }
}
