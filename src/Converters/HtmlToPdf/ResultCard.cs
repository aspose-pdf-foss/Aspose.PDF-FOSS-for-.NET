using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The px-width body RESULT CARD ───────────────────────────────────────────
    // A letter-style results page: a px-width body, stacked styled divs (Arial px
    // fonts), px-height spacer divs, unfetchable images rendering their ALT text,
    // and a padded background-color box holding a two-column label table whose
    // label class declares nowrap + paddings and whose rows draw 1px (white)
    // bottom borders. The whole ladder is formula-driven from the win metrics;
    // the only measured constants are the broken-image line box and its baseline.
    //
    // Geometry (all measured):
    //   page = 96 + bodyPx + 90 wide; content x = 96.75 (one cell padding);
    //   broken-img alt line box 20.63 with its baseline 17.55 below the top;
    //   the box div's padding insets its table; label rows pitch at
    //   padTop + line + padBottom with the border stroke on the row bottom.

    private const double RcBrokenImgLineBoxPt = 20.63;   // measured: alt line box
    private const double RcBrokenImgBaselinePt = 17.55;  // measured: alt baseline drop

    private static Document? TryRenderResultCard(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageHeight)
    {
        // Gate: a px-width body style, a nowrap label class with paddings, and a
        // px-width padded background box div — the results-card shape.
        var bodyM = Regex.Match(html,
            @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?width\s*:\s*(\d+(?:\.\d+)?)\s*px[^'""]*\1",
            RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        var bodyPx = double.Parse(bodyM.Groups[2].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        Dictionary<string, string>? labelCls = null;
        string? labelClsName = null;
        foreach (var (sel, props) in css)
            if (sel.StartsWith('.') && props.TryGetValue("white-space", out var wsv)
                && wsv.Contains("nowrap", StringComparison.OrdinalIgnoreCase)
                && props.ContainsKey("padding-top"))
            { labelCls = props; labelClsName = sel[1..]; break; }
        if (labelCls is null || labelClsName is null) return null;
        var boxM = Regex.Match(html,
            @"<div\b[^>]*style\s*=\s*(['""])(?=[^'""]*width\s*:\s*(\d+(?:\.\d+)?)px)(?=[^'""]*background-color\s*:\s*(#[0-9a-fA-F]{3,6}))(?=[^'""]*padding\s*:\s*(\d+(?:\.\d+)?)px)[^'""]*\1[^>]*>",
            RegexOptions.IgnoreCase);
        if (!boxM.Success) return null;

        const double PxPt = 0.75;
        var pageWidth = 96.0 + bodyPx * PxPt + 90.0;
        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page, docFontDict);
        EnsureFont(page, "Arial", "F8");
        EnsureFont(page, "ArialBold", "F9");

        var contentX = 96.0 + 0.75;                   // one cell padding inside the margin
        var contentW = bodyPx * PxPt;
        var arial = WinMetricsFor("Arial") ?? (0.905, 1.117);
        var serif = WinMetricsFor("Times New Roman") ?? (0.891, 1.107);

        double ArialLine(double fs) => Math.Round(fs / PxPt * arial.sum, MidpointRounding.AwayFromZero) * PxPt;
        double Drop(double fs, double box, (double asc, double sum) fm)
            => (box - fs * fm.sum) / 2 + fs * fm.asc;

        var sb = new StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        void Run(string res, double fs, double x, double yTd, string text, Color? col = null)
        {
            sb.Append("BT ");
            if (col is { } c)
                sb.Append(string.Create(inv, $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg "));
            sb.Append(string.Create(inv,
                $"/{res} {fs:0.##} Tf 1 0 0 1 {x:F2} {pageHeight - yTd:F2} Tm ({EscapePdfString(text)}) Tj "));
            if (col is not null) sb.Append("0 g ");
            sb.AppendLine("ET");
        }
        void HLine(double x0, double x1, double yTd, Color c)
            => sb.AppendLine(string.Create(inv,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG 0.75 w " +
                $"{x0:F2} {pageHeight - yTd:F2} m {x1:F2} {pageHeight - yTd:F2} l S Q"));

        var y = 72.0;                                 // body margin-top:0 → raw content top

        // Walk the body content in source order: imgs (alt text), spacer divs,
        // styled text divs, and the padded box (its inner label table).
        var body = Regex.Match(html, @"<body\b[^>]*>([\s\S]*)</body>", RegexOptions.IgnoreCase) is
            { Success: true } bm ? bm.Groups[1].Value : html;
        var boxStart = body.IndexOf(boxM.Value, StringComparison.Ordinal);
        if (boxStart < 0) return null;

        // 1. leading img alt line (the logo). alt='' images draw nothing.
        foreach (Match im in Regex.Matches(body[..boxStart], @"<img\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var alt = Regex.Match(im.Value, @"alt\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (alt.Success && alt.Groups[1].Value.Trim().Length > 0)
            {
                Run("F5", 12, contentX, y + RcBrokenImgBaselinePt, alt.Groups[1].Value.Trim());
                break;                                 // the logo line holds one alt run
            }
        }
        y += RcBrokenImgLineBoxPt;

        // 2. the stacked divs before the box: px-height spacers advance; styled
        // text divs draw at their declared Arial size (bold spans bold).
        foreach (Match dm in Regex.Matches(body[..boxStart],
            @"<div\b([^>]*)>([\s\S]*?)</div>", RegexOptions.IgnoreCase))
        {
            var attrs = dm.Groups[1].Value;
            var inner = dm.Groups[2].Value;
            var st = Regex.Match(attrs, @"style\s*=\s*(['""])([^'""]*)\1").Groups[2].Value;
            var hm = Regex.Match(st, @"height\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
            if (hm.Success && Regex.Replace(inner, @"<[^>]+>", "").Trim().Length == 0)
            {
                y += double.Parse(hm.Groups[1].Value, inv) * PxPt;
                continue;
            }
            if (Regex.Replace(inner, @"<[^>]+>", "").Trim().Length == 0) continue;
            // line-height:N% pitches the div's lines; each span carries its size/weight
            var lhPct = Regex.Match(st, @"line-height\s*:\s*(\d+)\s*%", RegexOptions.IgnoreCase) is
                { Success: true } lm ? double.Parse(lm.Groups[1].Value, inv) / 100.0 : 0;
            var divFsM = Regex.Match(st, @"font-size\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
            var divFs = divFsM.Success ? double.Parse(divFsM.Groups[1].Value, inv) * PxPt : 12.0;
            var divBold = st.Contains("font-weight:bold", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(st, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase);
            var divCol = Regex.Match(st, @"(?<![-\w])color\s*:\s*(#[0-9a-fA-F]{3,6})") is
                { Success: true } cm ? ParseCssColor(cm.Groups[1].Value) : null;
            // segment the div into lines at <br> boundaries; spans style their line
            foreach (var seg in Regex.Split(inner, @"<br\s*/?>", RegexOptions.IgnoreCase))
            {
                var segFs = divFs; var segBold = divBold;
                var spanFsM = Regex.Match(seg, @"font-size\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
                if (spanFsM.Success) segFs = double.Parse(spanFsM.Groups[1].Value, inv) * PxPt;
                if (Regex.IsMatch(seg, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase)) segBold = true;
                var textSeg = CollapseWs(DecodeEntities(Regex.Replace(seg, @"<[^>]+>", " ")));
                if (textSeg.Length == 0) continue;
                var box = lhPct > 0 ? segFs * lhPct : ArialLine(segFs);
                var drop = Drop(segFs, box, arial);
                Run(segBold ? "F9" : "F8", segFs, contentX, y + drop, textSeg, divCol);
                y += box;
            }
        }

        // 3. the padded background box with its label table.
        var boxPx = double.Parse(boxM.Groups[2].Value, inv) * PxPt;
        var boxCol = ParseCssColor(boxM.Groups[3].Value) ?? Color.FromArgb(226, 232, 237);
        var boxPad = double.Parse(boxM.Groups[4].Value, inv) * PxPt;
        var padTop = labelCls.TryGetValue("padding-top", out var ptv) && TryParseLength(ptv, out var ptPt) ? ptPt : 7.5;
        var padBottom = labelCls.TryGetValue("padding-bottom", out var pbv) && TryParseLength(pbv, out var pbPt) ? pbPt : 7.5;
        var padRight = labelCls.TryGetValue("padding-right", out var prv) && TryParseLength(prv, out var prPt) ? prPt : 11.25;
        var labelFs = labelCls.TryGetValue("font-size", out var lfv) && TryParseCssFontSize(lfv, out var lfPt) ? lfPt : 11.25;

        // rows: label td (class) + value td, from the box's inner table
        var boxHtml = body[boxStart..];
        var rows = new List<(string label, string value)>();
        foreach (Match rm in Regex.Matches(boxHtml, @"<tr>([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
        {
            var cells = Regex.Matches(rm.Groups[1].Value, @"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
            if (cells.Count < 2) continue;
            string CellText(Match c) => CollapseWs(DecodeEntities(Regex.Replace(c.Groups[1].Value, @"<[^>]+>", " ")));
            rows.Add((CellText(cells[0]), CellText(cells[1])));
        }
        if (rows.Count == 0) return null;

        var rowLine = ArialLine(labelFs);
        var rowPitch = padTop + padBottom + rowLine;
        var boxH = rows.Count * rowPitch + 2 * boxPad;
        sb.AppendLine(string.Create(inv,
            $"q {boxCol.R / 255.0:0.###} {boxCol.G / 255.0:0.###} {boxCol.B / 255.0:0.###} rg " +
            $"{contentX:F2} {pageHeight - y - boxH:F2} {boxPx + 2 * boxPad:F2} {boxH:F2} re f Q"));

        var tableX = contentX + boxPad;
        double labelW = 0;
        foreach (var (label, _) in rows)
            labelW = Math.Max(labelW, MeasureFaceText("Arial Bold", label, labelFs));
        // measured: the label column box = widest label + its padding-right +
        // one default-font em of slack (130.13 on the card's reference)
        var labelBoxW = labelW + padRight + 12.0;
        var valueX = tableX + labelBoxW + 0.75;
        var rowTop = y + boxPad;
        var innerW = boxPx - 2 * 0.75;
        foreach (var (label, value) in rows)
        {
            var baseTd = rowTop + padTop + Drop(labelFs, rowLine, arial);
            Run("F9", labelFs, tableX + 0.75, baseTd, label);
            if (value.Length > 0) Run("F8", labelFs, valueX, baseTd, value);
            var rowBot = rowTop + rowPitch;
            HLine(tableX, tableX + labelBoxW, rowBot, Color.FromArgb(255, 255, 255));
            HLine(tableX + labelBoxW, tableX + innerW, rowBot, Color.FromArgb(255, 255, 255));
            rowTop = rowBot;
        }
        y += boxH;

        // 4. trailing spacers and the footer img alt with the outer cell's own
        // white bottom border across the content box.
        foreach (Match dm in Regex.Matches(boxHtml, @"<div\b[^>]*style\s*=\s*(['""])[^'""]*height\s*:\s*(\d+(?:\.\d+)?)px[^'""]*\1[^>]*>\s*</div>", RegexOptions.IgnoreCase))
            y += double.Parse(dm.Groups[2].Value, inv) * PxPt;
        var footerAlt = Regex.Match(boxHtml, @"<img\b[^>]*alt\s*=\s*[""']([^""']+)[""'][^>]*>(?![\s\S]*<img)", RegexOptions.IgnoreCase);
        if (footerAlt.Success)
        {
            Run("F5", 12, contentX, y + RcBrokenImgBaselinePt, footerAlt.Groups[1].Value.Trim());
            HLine(96.0, 96.0 + contentW, y + RcBrokenImgBaselinePt + 3.83, Color.FromArgb(255, 255, 255));
        }

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
    }
}
