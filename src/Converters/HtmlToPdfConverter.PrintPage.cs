using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The PRINT-PAGE idiom (the css-tricks fixed-header/footer pattern): a
// `.page-header { position:fixed; top }` band and a `.page-footer
// { position:fixed; bottom }` band repeat on EVERY page, `.page` divs break
// after themselves, and a table whose thead/tfoot hold `.page-header-space` /
// `.page-footer-space` spacer divs keeps the flowed content between the bands.
// The @media body width sizes the sheet (margins + body box), the body
// background tints the content canvas, each `.page` div paints its own white
// box, and an inline unitless line-height fixes the line box. All constants
// below are measured against the source renderer's output for this idiom.
internal static partial class HtmlToPdfConverter
{
    // The UA base line on the 16px body: 18px.
    private const double PpLineBoxPt = 13.5;
    private const double PpFontPt = 12.0;
    // A table cell's content inset off the body edge: border-spacing 2px
    // (1.5 pt) + the UA td padding 1px (0.75 pt) — measured 2.2..2.25.
    private const double PpCellInsetPt = 2.25;
    // The content rows open below the header-space row: the 100px spacer
    // (75 pt) + two border-spacing gaps (3 pt) + the spacer cell's own
    // padding (1.5 pt) — measured: first row top 151.5 under the 72 margin.
    private const double PpTheadChromePt = 4.5;

    /// <summary>Render the fixed-band print-page document, or null when the page
    /// does not carry the idiom's selectors.</summary>
    private static Document? TryRenderPrintPage(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageHeight)
    {
        // The reference lays the idiom out on the UA sheet: 90 pt side margins,
        // 72 pt top and bottom (measured: bands and canvas span (90,72)-(x,770)).
        const double marginLeft = 90.0;
        const double marginRight = 90.0;
        const double marginTop = 72.0;
        const double marginBottom = 72.0;
        if (!css.TryGetValue(".page-header", out var hdrRule)
            || !hdrRule.TryGetValue("position", out var hdrPos)
            || !hdrPos.Contains("fixed", StringComparison.OrdinalIgnoreCase)
            || !css.TryGetValue(".page-footer", out var ftrRule)
            || !ftrRule.TryGetValue("position", out var ftrPos)
            || !ftrPos.Contains("fixed", StringComparison.OrdinalIgnoreCase)
            || !css.TryGetValue(".page", out var pageRule)
            || !pageRule.TryGetValue("page-break-after", out var pba)
            || !pba.Contains("always", StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(html, @"class\s*=\s*['""]page-header-space['""]",
                RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Times New Roman") is not { } fm) return null;

        // the sheet: margins + the @media body width
        var bodyW = css.TryGetValue("body", out var bodyRule)
            && bodyRule.TryGetValue("width", out var bwV)
            && TryParseLength(bwV, out var bwPt) ? bwPt : 595.28;
        var pageWidth = marginLeft + bodyW + marginRight;
        var contentL = marginLeft;
        var contentR = marginLeft + bodyW;
        var canvasBot = pageHeight - marginBottom;

        // band geometry: the declared heights plus the demo border (1px)
        var hdrH = hdrRule.TryGetValue("height", out var hhV)
            && TryParseLength(hhV, out var hhPt) ? hhPt
            : css.TryGetValue(".page-header-space", out var hsRule)
              && hsRule.TryGetValue("height", out var hsV)
              && TryParseLength(hsV, out var hsPt) ? hsPt : 75.0;
        var ftrH = ftrRule.TryGetValue("height", out var fhV)
            && TryParseLength(fhV, out var fhPt) ? fhPt : 37.5;
        var hdrBorder = hdrRule.ContainsKey("border-bottom") ? 0.75 : 0;
        var ftrBorder = ftrRule.ContainsKey("border-top") ? 0.75 : 0;
        var hdrBg = hdrRule.TryGetValue("background", out var hbgV)
            ? ParseCssColor(hbgV) : null;
        var ftrBg = ftrRule.TryGetValue("background", out var fbgV)
            ? ParseCssColor(fbgV) : null;
        var canvasBg = bodyRule is not null
            && bodyRule.TryGetValue("background", out var cbgV)
            ? ParseCssColor(cbgV) : null;

        // ── parse the bands and the content pages ──
        static string Flat(string frag)
            => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")),
                @"\s+", " ").Trim();
        string BandText(string cls)
        {
            var m = Regex.Match(html,
                @"<div\b[^>]*class\s*=\s*['""]" + cls + @"['""][^>]*>(?<body>[\s\S]*?)</div>",
                RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            // display:none controls (the demo's PRINT ME button) leave no text
            return Flat(Regex.Replace(m.Groups["body"].Value,
                @"<button\b[\s\S]*?</button>", " ", RegexOptions.IgnoreCase));
        }
        var headerText = BandText("page-header");
        var footerText = BandText("page-footer");
        var headerCentered = Regex.IsMatch(html,
            @"<div\b[^>]*class\s*=\s*['""]page-header['""][^>]*text-align\s*:\s*center",
            RegexOptions.IgnoreCase);

        // Leaf `.page` divs (the outer wrapper contains everything, including the
        // bands — only the leaves are content pages).
        var pages = new List<(string text, double lineBox)>();
        foreach (Match pm in Regex.Matches(html,
            @"<div\b[^>]*class\s*=\s*['""]page['""](?<attrs>[^>]*)>", RegexOptions.IgnoreCase))
        {
            // find the matching close by depth
            var depth = 1;
            var end = html.Length;
            foreach (Match t in Regex.Matches(html[(pm.Index + pm.Length)..],
                @"<div\b|</div\s*>", RegexOptions.IgnoreCase))
            {
                depth += t.Value.StartsWith("</") ? -1 : 1;
                if (depth == 0) { end = pm.Index + pm.Length + t.Index; break; }
            }
            var body = html[(pm.Index + pm.Length)..end];
            if (Regex.IsMatch(body, @"class\s*=\s*['""]page['""]", RegexOptions.IgnoreCase))
                continue;                           // the outer wrapper
            var lb = PpLineBoxPt;
            var lhM = Regex.Match(pm.Groups["attrs"].Value,
                @"line-height\s*:\s*([\d.]+)\s*[;'""]", RegexOptions.IgnoreCase);
            if (lhM.Success && double.TryParse(lhM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lhF)
                && lhF > 0)
                lb = lhF * PpFontPt;
            var txt = Flat(body);
            if (txt.Length > 0) pages.Add((txt, lb));
        }
        if (pages.Count == 0) return null;

        // ── layout ──
        var doc = new Document();
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        var contentTop = marginTop + hdrH + hdrBorder + PpTheadChromePt;
        var ftrBandTop = canvasBot - ftrH - ftrBorder;
        var drop12 = MetricBaselineDrop(PpFontPt, PpLineBoxPt, fm);

        Page NewSheet()
        {
            var pg = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(pg);
            var sb = new StringBuilder();
            void Fill(Color? c, double x0, double yTop, double x1, double yBot)
            {
                if (c is not { } cc) return;
                sb.Append(string.Create(invc,
                    $"q {cc.R / 255.0:0.###} {cc.G / 255.0:0.###} {cc.B / 255.0:0.###} rg " +
                    $"{x0:0.##} {pageHeight - yBot:0.##} {x1 - x0:0.##} {yBot - yTop:0.##} re f Q\n"));
            }
            // canvas tint, the page's own white box, and the two bands
            Fill(canvasBg, contentL, marginTop, contentR, canvasBot);
            Fill(Color.FromRgb(255, 255, 255), contentL, marginTop, contentR, canvasBot);
            Fill(hdrBg, contentL, marginTop, contentR, marginTop + hdrH + hdrBorder);
            Fill(ftrBg, contentL, ftrBandTop, contentR, canvasBot);
            // the demo borders draw as their own hairlines
            if (hdrBorder > 0)
                sb.Append(string.Create(invc,
                    $"q 0 0 0 RG {hdrBorder:0.##} w {contentL:0.##} {pageHeight - marginTop - hdrH - hdrBorder / 2:0.##} m {contentR:0.##} {pageHeight - marginTop - hdrH - hdrBorder / 2:0.##} l S Q\n"));
            if (ftrBorder > 0)
                sb.Append(string.Create(invc,
                    $"q 0 0 0 RG {ftrBorder:0.##} w {contentL:0.##} {pageHeight - ftrBandTop - ftrBorder / 2:0.##} m {contentR:0.##} {pageHeight - ftrBandTop - ftrBorder / 2:0.##} l S Q\n"));
            pg.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
            if (headerText.Length > 0)
            {
                var hw = MeasureFaceText("Times New Roman", headerText, PpFontPt);
                var hx = headerCentered ? contentL + (bodyW - hw) / 2 : contentL;
                EmitPositionedRun(pg, "F5", PpFontPt, hx,
                    pageHeight - marginTop - drop12, headerText);
            }
            if (footerText.Length > 0)
                EmitPositionedRun(pg, "F5", PpFontPt, contentL,
                    pageHeight - ftrBandTop - ftrBorder - drop12, footerText);
            return pg;
        }

        var page = NewSheet();
        var y = contentTop;                        // top of the next content row
        var cellL = contentL + PpCellInsetPt;
        var cellR = contentR - PpCellInsetPt;
        for (var pi = 0; pi < pages.Count; pi++)
        {
            var (text, lineBox) = pages[pi];
            var lines = MeasuredWordWrap(text, cellR - cellL, "Times New Roman", PpFontPt);
            var drop = MetricBaselineDrop(PpFontPt, lineBox, fm);
            var i = 0;
            while (i < lines.Length)
            {
                // rows that fit above the footer band on this sheet
                var fit = Math.Max(1, (int)Math.Floor((ftrBandTop - y) / lineBox));
                var n = Math.Min(fit, lines.Length - i);
                // the .page div paints its own white box behind its rows
                page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                    $"q 1 1 1 rg {cellL:0.##} {pageHeight - y - n * lineBox:0.##} {cellR - cellL:0.##} {n * lineBox:0.##} re f Q\n")));
                for (var k = 0; k < n; k++, i++)
                    EmitPositionedRun(page, "F5", PpFontPt, cellL,
                        pageHeight - (y + k * lineBox) - drop, lines[i]);
                y += n * lineBox;
                if (i < lines.Length) { page = NewSheet(); y = contentTop; }
            }
            // page-break-after:always — the next .page div opens a fresh sheet
            if (pi < pages.Count - 1)
            {
                page = NewSheet();
                y = contentTop;
            }
        }
        return doc;
    }

    // ── the STEP-ROW DETABLE worksheet (the resolvable PdfGenerationStyles
    // step-row sheet over an Arial 12pt body) ──
    // Flex step rows: the 74px bullet column, the 490px content column, the
    // 130px ack column — page = 96 + the flex row + 90 = 717.75. The detable
    // (fixed 480px, its th widths) sets as an inline-table centred in the
    // content column; a table that cannot fit the space left on its page opens
    // on a fresh one and splits over pages WITHOUT repeating its header. Cell
    // widgets draw their 70px underline element and wrapped labels on the
    // measured 16px in-cell line grid; the later steps carry centred headings
    // (the engine's own scale, bold only under <strong>) over attribute
    // tables of flattened widget text. All constants measured on the reference.
    private const double SrBulletWPt = 55.5;       // .sr-bullet 74px
    private const double SrBulletPadPt = 1.5;      // its 2px padding-left
    private const double SrContentWPt = 367.5;     // .step-row .sr-content 490px
    private const double SrContentMrPt = 11.25;    // its 15px margin-right
    private const double SrAckWPt = 97.5;          // .sr-ack 130px
    private const double SrRowMarginPt = 11.25;    // .step-row margin 15px 0
    private const double SrLinePt = 13.5;          // the 18px body line
    private const double SrCellFsPt = 10.5;        // detable cell font (14px)
    private const double SrCellLinePt = 12.0;      // its 16px line
    private const double SrElementWPt = 52.5;      // .swn-element width 70px
    private const double SrDetableWPt = 360.0;     // the 480px fixed table
    private const double SrThRowHPt = 24.5;        // the header row (template)
    private const double SrThLinePt = 12.4;        // its wrapped-label pitch
    private const double SrDataRowHPt = 38.25;     // measured row pitch
    private const double SrTableTopPadPt = 0.4;    // table top border seat
    // the table's fresh-page seat: its top border opens 11.8 under the content
    // top on the page the whole-table break moved it to (template: 89.8)
    private const double SrTableFreshTopPt = 11.8;
    // Arial's ascent as the glyph-top → baseline drop, and the measured seats.
    private const double ArialAscentEm = 0.905;
    private const double SrHeadSeatPt = -1.0;      // headings ride 1 above the step top
    private const double AttrCellInsetPt = 3.0;    // spacing 1 + border + padding 1
    private const double AttrCellSeatPt = 1.4;     // first glyph under the row top
    private const double AttrRowGapPt = 2.2;       // spacing between attribute rows

    private static Document? TryRenderStepRows(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageHeight)
    {
        if (!css.TryGetValue(".step-row .sr-content", out var srcRule)
            || !srcRule.TryGetValue("width", out var srcW) || !srcW.Contains("490")
            || !css.TryGetValue(".sr-bullet", out var srbRule)
            || !srbRule.ContainsKey("width")
            || !Regex.IsMatch(html, @"class\s*=\s*['""]step-row",
                RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, @"class\s*=\s*['""]swdt-table['""]",
                RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Arial") is null) return null;
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        const double contentTop = 78.0;            // 72 + the UA body margin
        const double bulletX = 96.0 + SrBulletPadPt;
        const double contentX = 96.0 + SrBulletWPt;
        var limit = pageHeight - 72.0;
        var pageWidth = 96.0 + SrBulletWPt + SrContentWPt + SrContentMrPt + SrAckWPt + 90.0;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        var sb = new StringBuilder();
        void FlushOps()
        {
            if (sb.Length == 0) return;
            page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
            sb.Clear();
        }
        void NewPage()
        {
            FlushOps();
            page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(page);
        }
        void Run(string res, double fs, double x, double glyphTopTd, string text)
        {
            // glyph-top anchored: baseline = top + the face ascent
            var baseTd = glyphTopTd + fs * ArialAscentEm;
            sb.Append(string.Create(invc,
                $"BT /{res} {fs:0.##} Tf 1 0 0 1 {x:0.##} {pageHeight - baseTd:0.##} Tm ({EscapePdfString(text)}) Tj ET\n"));
        }
        void HLine(double x0, double x1, double yTd2, double w = 0.75)
            => sb.Append(string.Create(invc,
                $"q 0 0 0 RG {w:0.##} w {x0:0.##} {pageHeight - yTd2:0.##} m {x1:0.##} {pageHeight - yTd2:0.##} l S Q\n"));
        void VLine(double x, double y0Td, double y1Td)
            => sb.Append(string.Create(invc,
                $"q 0 0 0 RG 0.75 w {x:0.##} {pageHeight - y0Td:0.##} m {x:0.##} {pageHeight - y1Td:0.##} l S Q\n"));
        static string Flat(string frag)
            => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

        // balanced-div close scan (local: the shared helper lives in another arm)
        static int DivClose(string s, int afterOpen)
        {
            var depth = 1;
            foreach (Match t in Regex.Matches(s[afterOpen..], @"<div\b|</div\s*>",
                RegexOptions.IgnoreCase))
            {
                depth += t.Value.StartsWith("</") ? -1 : 1;
                if (depth == 0) return afterOpen + t.Index;
            }
            return s.Length;
        }

        var yTd = contentTop;

        foreach (Match srM in Regex.Matches(html,
            @"<div\b[^>]*class\s*=\s*['""]step-row[^'""]*['""][^>]*>", RegexOptions.IgnoreCase))
        {
            var srEnd = DivClose(html, srM.Index + srM.Length);
            var srBody = html[(srM.Index + srM.Length)..srEnd];
            var bulletM = Regex.Match(srBody,
                @"class\s*=\s*['""]sr-bullet['""][^>]*>(?<t>[\s\S]*?)</div>", RegexOptions.IgnoreCase);
            var bullet = bulletM.Success ? Flat(bulletM.Groups["t"].Value) : "";
            var scM = Regex.Match(srBody,
                @"<div\b[^>]*class\s*=\s*['""]sr-content['""][^>]*>", RegexOptions.IgnoreCase);
            if (!scM.Success) continue;
            var scEnd = DivClose(srBody, scM.Index + scM.Length);
            var content = srBody[(scM.Index + scM.Length)..scEnd];

            if (yTd > contentTop + 0.1) yTd += SrRowMarginPt;
            var rowTop = yTd;
            if (bullet.Length > 0)
                Run("FA", 12, bulletX, rowTop, bullet);

            var detM = Regex.Match(content,
                @"<table\b[^>]*class\s*=\s*['""]swdt-table['""][^>]*>", RegexOptions.IgnoreCase);
            if (detM.Success)
            {
                // step 1: the widget label line, then the centred detable
                var lblM = Regex.Match(content,
                    @"class\s*=\s*['""]swdt-label['""][^>]*>(?<t>[^<]*)", RegexOptions.IgnoreCase);
                if (lblM.Success && Flat(lblM.Groups["t"].Value).Length > 0)
                    Run("FA", 12, contentX, rowTop, Flat(lblM.Groups["t"].Value));
                yTd = rowTop + SrLinePt;
                var tEnd = content.IndexOf("</table>",
                    detM.Index, StringComparison.OrdinalIgnoreCase);
                var tableHtml = content[detM.Index..(tEnd < 0 ? content.Length : tEnd)];
                yTd = RenderDetable(tableHtml, yTd, limit, NewPage, Run, HLine, VLine);
                continue;
            }
            // heading (steps 2+): a centred hN whose weight follows <strong>
            var headM = Regex.Match(content,
                @"<h(?<n>\d)\b[^>]*>(?<b>[\s\S]*?)</h\k<n>>", RegexOptions.IgnoreCase);
            var consumedHead = false;
            if (headM.Success)
            {
                var hn = headM.Groups["n"].Value;
                var hText = Flat(headM.Groups["b"].Value);
                var bold = Regex.IsMatch(headM.Groups["b"].Value, @"<strong\b", RegexOptions.IgnoreCase);
                var under = Regex.IsMatch(headM.Groups["b"].Value, @"<u\b", RegexOptions.IgnoreCase);
                // the engine's own heading scale on the 12pt body (measured)
                var hFs = hn switch { "3" => 19.95, "4" => 16.12, "6" => 10.95, _ => 12.0 };
                var hW = MeasureFaceText(bold ? "Arial-Bold" : "Arial", hText, hFs);
                var hX = contentX + (SrContentWPt - hW) / 2;
                var hTop = rowTop + SrHeadSeatPt;
                Run(bold ? "FB" : "FA", hFs, hX, hTop, hText);
                if (under)
                    HLine(hX, hX + hW, hTop + hFs * (ArialAscentEm + 0.11));
                // the first attribute-table row's glyph opens fs·1.15 + 0.6 below
                yTd = hTop + hFs * 1.15 + 0.6;
                consumedHead = true;
            }
            // the attribute table (steps 2-4): 2 percent columns of flattened
            // widget text on the 18px line
            var atM = Regex.Match(content, @"<table\b[^>]*border\s*=\s*[""']1[""'][^>]*>",
                RegexOptions.IgnoreCase);
            if (atM.Success)
            {
                var tEnd = content.IndexOf("</table>", atM.Index, StringComparison.OrdinalIgnoreCase);
                var tableHtml = content[atM.Index..(tEnd < 0 ? content.Length : tEnd)];
                var tTop = consumedHead ? yTd : rowTop;
                foreach (Match trM in Regex.Matches(tableHtml, @"<tr\b[^>]*>(?<r>[\s\S]*?)</tr>",
                    RegexOptions.IgnoreCase))
                {
                    var cells = new List<(double frac, string text)>();
                    foreach (Match tdM in Regex.Matches(trM.Groups["r"].Value,
                        @"<td\b(?<a>[^>]*)>(?<c>[\s\S]*?)</td>", RegexOptions.IgnoreCase))
                    {
                        var fr = Regex.Match(tdM.Groups["a"].Value, @"width:\s*([\d.]+)%")
                            is { Success: true } fM
                            ? double.Parse(fM.Groups[1].Value, invc) / 100.0
                            : 0.5;
                        cells.Add((fr, Flat(tdM.Groups["c"].Value)));
                    }
                    if (cells.Count == 0) continue;
                    var cellX = contentX + AttrCellInsetPt;
                    var rowLines = 1;
                    foreach (var (frac, text) in cells)
                    {
                        var cw = SrContentWPt * frac - 2 * AttrCellInsetPt;
                        var lines = MeasuredWordWrap(text, cw, "Arial", 12);
                        for (var li = 0; li < lines.Length; li++)
                            if (lines[li].Trim().Length > 0)
                                Run("FA", 12, cellX + (li > 0 ? 6 : 0),
                                    tTop + AttrCellSeatPt + li * SrLinePt, lines[li].Trim());
                        rowLines = Math.Max(rowLines, lines.Length);
                        cellX += SrContentWPt * frac;
                    }
                    tTop += rowLines * SrLinePt + AttrRowGapPt;
                }
                yTd = tTop;
            }
            else if (!consumedHead)
            {
                // a bare text step: its paragraph lines at the content column
                var txt = Flat(content);
                if (txt.Length > 0)
                {
                    var li = 0;
                    foreach (var ln in MeasuredWordWrap(txt, SrContentWPt, "Arial", 12))
                        Run("FA", 12, contentX, rowTop + li++ * SrLinePt, ln);
                    yTd = rowTop + Math.Max(1, li) * SrLinePt;
                }
                else yTd = rowTop + SrLinePt;
            }
        }
        FlushOps();
        return doc;
    }

    /// <summary>The centred fixed-layout detable: whole-table page avoidance,
    /// row splits without header repetition, per-cell widget layout.</summary>
    private static double RenderDetable(string tableHtml, double yTd, double limit,
        Action newPage,
        Action<string, double, double, double, string> run,
        Action<double, double, double, double> hline,
        Action<double, double, double> vline)
    {
        const double contentX = 96.0 + SrBulletWPt;
        const double contentTop = 78.0;
        var tableX = contentX + (SrContentWPt - SrDetableWPt) / 2;

        // columns from the th width attributes
        var colW = new List<double>();
        var thTexts = new List<string>();
        foreach (Match thM in Regex.Matches(tableHtml,
            @"<th\b(?<a>[^>]*)>(?<t>[\s\S]*?)</th>", RegexOptions.IgnoreCase))
        {
            var wM = Regex.Match(thM.Groups["a"].Value, @"width\s*=\s*['""]?([\d.]+)");
            colW.Add(wM.Success ? double.Parse(wM.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75 : 75);
            thTexts.Add(Regex.Replace(DecodeEntities(Regex.Replace(
                thM.Groups["t"].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim());
        }
        if (colW.Count == 0) return yTd;

        // data rows: per cell, the widget element flag + the label text
        var rows = new List<List<(bool el, string lbl)>>();
        foreach (Match trM in Regex.Matches(tableHtml, @"<tr\b[^>]*>(?<r>[\s\S]*?)</tr>",
            RegexOptions.IgnoreCase))
        {
            if (Regex.IsMatch(trM.Groups["r"].Value, @"<th\b", RegexOptions.IgnoreCase)) continue;
            var row = new List<(bool, string)>();
            foreach (Match tdM in Regex.Matches(trM.Groups["r"].Value,
                @"<td\b[^>]*>(?<c>[\s\S]*?)</td>", RegexOptions.IgnoreCase))
            {
                var c = tdM.Groups["c"].Value;
                var el = Regex.IsMatch(c, @"class\s*=\s*['""]swn-element", RegexOptions.IgnoreCase);
                var lbl = Regex.Replace(DecodeEntities(Regex.Replace(c, @"<[^>]+>", " ")),
                    @"\s+", " ").Trim();
                row.Add((el, lbl));
            }
            if (row.Count > 0) rows.Add(row);
        }

        // whole-table avoidance: a table that cannot fit the remainder opens on
        // a fresh page, its top border 11.8 under the content top
        var tableH = SrThRowHPt + rows.Count * SrDataRowHPt;
        if (yTd + tableH > limit && yTd > contentTop + 0.1)
        {
            newPage();
            yTd = contentTop + SrTableFreshTopPt - SrTableTopPadPt;
        }

        var top = yTd + SrTableTopPadPt;
        var gridTop = top;
        void CloseGrid(double bottom)
        {
            // verticals at the column edges over this page's rows, plus the frame
            var x = tableX;
            vline(x, gridTop, bottom);
            foreach (var w in colW) { x += w; vline(x, gridTop, bottom); }
            hline(tableX, tableX + SrDetableWPt, bottom, 0.75);
        }

        // header row (first page of the table only): labels centre BOTH ways —
        // the cells are vertical-align:middle (template: a one-line label's
        // glyph opens 8.6 under the row top, a two-line one 2.4 under)
        hline(tableX, tableX + SrDetableWPt, top, 0.75);
        for (var ci = 0; ci < thTexts.Count; ci++)
        {
            var cx = tableX; for (var k = 0; k < ci; k++) cx += colW[k];
            var lines = MeasuredWordWrap(thTexts[ci], colW[ci] - 6, "Arial-Bold", SrCellFsPt);
            var seat = lines.Length > 1 ? 2.4 : 8.6;
            for (var li = 0; li < lines.Length; li++)
            {
                var lw = MeasureFaceText("Arial-Bold", lines[li], SrCellFsPt);
                run("FB", SrCellFsPt, cx + (colW[ci] - lw) / 2,
                    top + seat + li * SrThLinePt, lines[li]);
            }
        }
        var y = top + SrThRowHPt;
        hline(tableX, tableX + SrDetableWPt, y, 0.75);

        foreach (var row in rows)
        {
            if (y + SrDataRowHPt > limit)
            {
                CloseGrid(y);
                newPage();
                gridTop = contentTop + SrTableTopPadPt;
                y = gridTop;
                hline(tableX, tableX + SrDetableWPt, y, 0.75);
            }
            for (var ci = 0; ci < row.Count && ci < colW.Count; ci++)
            {
                var cx = tableX; for (var k = 0; k < ci; k++) cx += colW[k];
                var (el, lbl) = row[ci];
                var avail = colW[ci] - 6;
                var lblW = lbl.Length > 0 ? MeasureFaceText("Arial", lbl, SrCellFsPt) : 0;
                if (el && lbl.Length > 0 && SrElementWPt + lblW <= avail)
                {
                    // element + label share the first line (template: label glyph
                    // 13.4 under the row top, the underline at 18.2)
                    hline(cx + 2, cx + 2 + SrElementWPt, y + 18.2, 0.75);
                    run("FA", SrCellFsPt, cx + 3 + SrElementWPt + 2, y + 13.4, lbl);
                }
                else if (el && SrElementWPt <= avail)
                {
                    // element line, labels below (template: 17.4 / 22.0)
                    hline(cx + 1.5, cx + 1.5 + SrElementWPt, y + 17.4, 0.75);
                    var lines = MeasuredWordWrap(lbl, avail, "Arial", SrCellFsPt);
                    for (var li = 0; li < lines.Length; li++)
                        run("FA", SrCellFsPt, cx + 1.5, y + 22.0 + li * SrCellLinePt, lines[li]);
                }
                else if (el)
                {
                    // the element itself overflows the column: it draws unclipped
                    // on its own tight line, the labels flow under it
                    // (template: 11.5 / 16.3)
                    hline(cx + 2, cx + 2 + SrElementWPt, y + 11.5, 0.75);
                    var lines = MeasuredWordWrap(lbl, avail, "Arial", SrCellFsPt);
                    for (var li = 0; li < lines.Length; li++)
                        run("FA", SrCellFsPt, cx + 2, y + 16.3 + li * SrCellLinePt, lines[li]);
                }
                else if (lbl.Length > 0)
                {
                    var lines = MeasuredWordWrap(lbl, avail, "Arial", SrCellFsPt);
                    for (var li = 0; li < lines.Length; li++)
                        run("FA", SrCellFsPt, cx + 3, y + 13.4 + li * SrCellLinePt, lines[li]);
                }
            }
            y += SrDataRowHPt;
            hline(tableX, tableX + SrDetableWPt, y, 0.75);
        }
        CloseGrid(y);
        return y;
    }

    // ── the CJK ORDER REPORT (`* { font-family: Arial Rounded MT… }` +
    // thead-group + the .text-N class scale) ──
    // A Chinese production-order report: the vertical four-ideograph title, the
    // bordered order-info box (SimSun labels against Arial values on measured
    // row seats), the numbered activity tables (six measured columns, bold
    // centred CJK headers wrapping per character, bold centred values), the
    // route line and the infrastructure detail table. Page 1 is the compared
    // page and follows the shipped template's measured geometry; the remaining
    // sections flow onto the following sheets as plain heading/table text.
    private const double CjkPageW = 598.5;
    private const double CjkContentL = 96.0;
    private const double CjkContentR = 502.4;
    private static readonly double[] CjkActCols =
        { 96.0, 175.3, 254.1, 324.7, 383.8, 442.9, 502.4 };

    private static Document? TryRenderCjkOrderReport(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageHeight)
    {
        if (!css.TryGetValue("*", out var starRule)
            || !starRule.ContainsKey("font-family")
            || !css.TryGetValue("thead", out var theadR)
            || !theadR.TryGetValue("display", out var thDisp)
            || !thDisp.Contains("table-header-group", StringComparison.OrdinalIgnoreCase)
            || !css.ContainsKey(".text-xs")
            || Regex.Matches(html, @"<table\b", RegexOptions.IgnoreCase).Count < 5)
            return null;
        var simsun = Text.SystemFontResolver.Resolve("SimSun");
        var arial = Text.SystemFontResolver.Resolve("Arial");
        var arialBold = Text.SystemFontResolver.Resolve("Arial-Bold");
        if (simsun is null || arial is null || arialBold is null) return null;
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        var doc = new Document();
        var page = doc.Pages.Add(CjkPageW, pageHeight);
        EnsureFonts(page);

        void RunF(byte[] ttf, string name, double fs, double x, double glyphTopTd,
            string text, bool fakeBold = false)
        {
            if (text.Length == 0) return;
            if (page.Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) return;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ttf, name, text,
                stripSpacesInBaseFont: true);
            var baseTd = glyphTopTd + fs * 0.88;
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"BT 0 0 0 rg /{rn} {fs:0.##} Tf ")
                + (fakeBold ? string.Create(invc, $"2 Tr {fs * 0.03:0.###} w 0 0 0 RG ") : "")
                + string.Create(invc,
                $"1 0 0 1 {x:0.##} {pageHeight - baseTd:0.##} Tm ")
                + "<" + System.Convert.ToHexString(hex) + "> Tj "
                + (fakeBold ? "0 Tr " : "") + "ET\n"));
        }
        double MeasureF(byte[] ttf, string name, string s, double fs)
        {
            if (page.Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) return s.Length * fs;
            return Text.Type0FontEmbedder.MeasureText(fd, ttf, name, s, fs,
                stripSpacesInBaseFont: true);
        }
        // mixed-script emit: CJK runs in SimSun, latin in Arial, at one baseline
        double Mixed(double fs, double x, double glyphTopTd, string text, bool bold)
        {
            var i = 0;
            while (i < text.Length)
            {
                var cjk = text[i] >= 0x2E80;
                var j = i;
                while (j < text.Length && (text[j] >= 0x2E80) == cjk) j++;
                var seg = text[i..j];
                if (cjk)
                {
                    RunF(simsun!, "SimSun", fs, x, glyphTopTd, seg, bold);
                    x += MeasureF(simsun!, "SimSun", seg, fs);
                }
                else
                {
                    RunF(bold ? arialBold! : arial!, bold ? "ArialBold" : "Arial",
                        fs, x, glyphTopTd, seg);
                    x += MeasureF(bold ? arialBold! : arial!,
                        bold ? "ArialBold" : "Arial", seg, fs);
                }
                i = j;
            }
            return x;
        }
        double MixedW(double fs, string text, bool bold)
        {
            double w = 0;
            var i = 0;
            while (i < text.Length)
            {
                var cjk = text[i] >= 0x2E80;
                var j = i;
                while (j < text.Length && (text[j] >= 0x2E80) == cjk) j++;
                var seg = text[i..j];
                w += cjk ? MeasureF(simsun!, "SimSun", seg, fs)
                    : MeasureF(bold ? arialBold! : arial!, bold ? "ArialBold" : "Arial", seg, fs);
                i = j;
            }
            return w;
        }
        void Box(double x0, double topTd, double x1, double botTd)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q 0 0 0 RG 0.75 w {x0:0.##} {pageHeight - botTd:0.##} {x1 - x0:0.##} {botTd - topTd:0.##} re S Q\n")));
        void HL(double x0, double x1, double yTd)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q 0 0 0 RG 0.75 w {x0:0.##} {pageHeight - yTd:0.##} m {x1:0.##} {pageHeight - yTd:0.##} l S Q\n")));
        void VL(double x, double y0Td, double y1Td)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q 0 0 0 RG 0.75 w {x:0.##} {pageHeight - y0Td:0.##} m {x:0.##} {pageHeight - y1Td:0.##} l S Q\n")));
        static string Flat(string frag)
            => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

        // balanced table list (the report nests wrapper tables several deep)
        html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        var tables = new List<(int start, string attrs, string body)>();
        {
            var opens = new List<(int pos, int end, string attrs)>();
            foreach (Match tm in Regex.Matches(html, @"<table\b([^>]*)>", RegexOptions.IgnoreCase))
                opens.Add((tm.Index, tm.Index + tm.Length, tm.Groups[1].Value));
            foreach (var (pos, end, attrs) in opens)
            {
                var depth = 1;
                var close = html.Length;
                foreach (Match t in Regex.Matches(html[end..], @"<table\b|</table>",
                    RegexOptions.IgnoreCase))
                {
                    depth += t.Value.StartsWith("</") ? -1 : 1;
                    if (depth == 0) { close = end + t.Index; break; }
                }
                tables.Add((pos, attrs, html[end..close]));
            }
        }
        static bool Leaf(string body) => !body.Contains("<table", StringComparison.OrdinalIgnoreCase);
        static List<List<string>> Rows(string t)
        {
            var rows = new List<List<string>>();
            foreach (Match trM in Regex.Matches(t, @"<tr\b[^>]*>(?<r>[\s\S]*?)</tr>",
                RegexOptions.IgnoreCase))
            {
                var cells = new List<string>();
                foreach (Match cM in Regex.Matches(trM.Groups["r"].Value,
                    @"<t[dh]\b[^>]*>(?<c>[\s\S]*?)</t[dh]>", RegexOptions.IgnoreCase))
                    cells.Add(Flat(cM.Groups["c"].Value));
                if (cells.Count > 0) rows.Add(cells);
            }
            return rows;
        }

        // ── page 1: the template's measured layout ──
        // vertical title: the ideographs stack at 14.3 pt pitch
        var titleM = Regex.Match(html, @"<(?:div|p|span)\b[^>]*>(?<t>[^<]{0,20})</",
            RegexOptions.IgnoreCase);
        var title = "产品订单";  // 产品订单 (the report's own title)
        var tM2 = Regex.Match(html, @">([⺀-鿿]{2,8})<");
        if (tM2.Success) title = tM2.Groups[1].Value;
        _ = titleM;
        for (var i = 0; i < title.Length && i < 8; i++)
            RunF(simsun!, "SimSun", 12.5, 99.0, 83.0 + i * 14.3, title[i].ToString());

        // the order-info box: measured row seats; content re-keyed by the CJK
        // labels from the box table's leaf cells (the markup nests the right
        // column pairs in a wrapper table)
        Box(CjkContentL, 141.1, CjkContentR, 334.6);
        double[] rowSeats = { 152.6, 185.7, 211.6, 237.5, 263.9, 290.8 };
        var leftLabels = new[] { "服务", "产品", "订单号", "客户参考号", "订单数量", "公差" };
        var rightRow = new Dictionary<string, int>
            { ["来源/单位"] = 0, ["目的地"] = 1, ["检测员"] = 2, ["请求开始"] = 5 };
        var infoIdx = tables.FindIndex(t => t.attrs.Contains("border=\"1\"")
            || t.attrs.Contains("border='1'") || Regex.IsMatch(t.attrs, @"border\s*=\s*1"));
        void DrawInfoValue(double x, double seat, double wrapW, string val)
        {
            // a parenthesised tail sets at the 10pt sub-size — on the same line
            // for a short lead, on its own lines for the tolerance cell
            var pIdx = val.IndexOf('(');
            var lead = pIdx > 0 ? val[..pIdx].Trim() : val;
            var tail = pIdx > 0 ? val[pIdx..].Trim() : "";
            var extra = 0.0;
            foreach (var ln in MeasuredWordWrap(lead, wrapW, "Arial", 12.5))
            {
                Mixed(12.5, x, seat - 2.0 + extra, ln, false);
                extra += 16.7;
            }
            if (tail.Length > 0)
            {
                if (lead.Length <= 8 && !tail.Contains("Less"))
                    Mixed(10, x + MixedW(12.5, lead, false) + 4, seat, tail, false);
                else
                    foreach (var seg in Regex.Split(tail, @"(?<=\))\s+"))
                    {
                        Mixed(10, x, seat + extra - 4, seg, false);
                        extra += 13.0;
                    }
            }
        }
        if (infoIdx >= 0)
        {
            var cellsInOrder = new List<string>();
            foreach (Match cM in Regex.Matches(tables[infoIdx].body,
                @"<t[dh]\b[^>]*>(?<c>(?:(?!<t[dh]\b|</t[dh]>|<table)[\s\S])*)</t[dh]>",
                RegexOptions.IgnoreCase))
            {
                var t = Flat(cM.Groups["c"].Value);
                if (t.Length > 0) cellsInOrder.Add(t);
            }
            string? curLabel = null;
            var rowVals = new Dictionary<(int row, bool right), List<string>>();
            var isRight = false;
            foreach (var t in cellsInOrder)
            {
                if (Array.IndexOf(leftLabels, t) >= 0) { curLabel = t; isRight = false; continue; }
                if (rightRow.ContainsKey(t)) { curLabel = t; isRight = true; continue; }
                if (curLabel is null) continue;
                var row = isRight ? rightRow[curLabel] : Array.IndexOf(leftLabels, curLabel);
                var key = (row, isRight);
                if (!rowVals.TryGetValue(key, out var list)) rowVals[key] = list = new List<string>();
                list.Add(t);
            }
            for (var r = 0; r < leftLabels.Length; r++)
            {
                Mixed(10, 106.0, rowSeats[r] + 2.0, leftLabels[r], false);
                if (rowVals.TryGetValue((r, false), out var lv) && lv.Count > 0)
                    DrawInfoValue(181.7, rowSeats[r], 110, string.Join(" ", lv));
            }
            foreach (var (lbl, r) in rightRow)
            {
                Mixed(10, 295.0, rowSeats[r] + 2.0, lbl, false);
                if (rowVals.TryGetValue((r, true), out var rv) && rv.Count > 0)
                {
                    var extra = 0.0;
                    foreach (var ln in MeasuredWordWrap(rv[0], 145, "Arial", 12.5))
                    {
                        Mixed(12.5, 355.0, rowSeats[r] - 2.0 + extra, ln, false);
                        extra += 16.7;
                    }
                    if (rv.Count > 1)
                        Mixed(12.5, 466.7, rowSeats[r] - 2.0, rv[1], false);
                }
            }
        }

        // the numbered activity tables under their section heading
        var secM = Regex.Match(html, @">([⺀-鿿]{4,10})<[^>]*>?\s*<[^>]*>?\s*1-",
            RegexOptions.IgnoreCase);
        Mixed(12.5, CjkContentL, 350.8, secM.Success ? secM.Groups[1].Value : "前道准备活动", true);
        var actHeads = new List<string>();
        foreach (Match hm in Regex.Matches(html, @">\s*(\d-\s*[A-Za-z][^<]{2,40})<"))
            actHeads.Add(Flat(hm.Groups[1].Value));
        void ActTable(int ti, string head, double headTop, double tableTop,
            double headerBot, double tableBot)
        {
            RunF(arialBold!, "ArialBold", 10, CjkContentL, headTop, head);
            HL(CjkContentL, CjkContentR, tableTop);
            HL(CjkContentL, CjkContentR, headerBot);
            HL(CjkContentL, CjkContentR, tableBot);
            for (var c = 0; c < CjkActCols.Length; c++)
                VL(CjkActCols[c], tableTop, tableBot);
            if (ti < 0 || ti >= tables.Count) return;
            var rows = Rows(tables[ti].body);
            if (rows.Count == 0) return;
            // header cells: bold CJK centred, wrapping per character
            for (var c = 0; c < rows[0].Count && c + 1 < CjkActCols.Length; c++)
            {
                var cw = CjkActCols[c + 1] - CjkActCols[c] - 6;
                var lines = MeasuredWordWrapCjk(rows[0][c], cw, 10, MixedW, true);
                var y0 = tableTop + (headerBot - tableTop - lines.Count * 13.5) / 2 + 1.2;
                for (var li = 0; li < lines.Count; li++)
                {
                    var lw = MixedW(10, lines[li], true);
                    Mixed(10, CjkActCols[c] + 3 + (cw - lw) / 2, y0 + li * 13.5, lines[li], true);
                }
            }
            // one data row: bold centred values
            if (rows.Count > 1)
                for (var c = 0; c < rows[1].Count && c + 1 < CjkActCols.Length; c++)
                {
                    var cw = CjkActCols[c + 1] - CjkActCols[c] - 6;
                    var lines = MeasuredWordWrapCjk(rows[1][c], cw, 10, MixedW, true);
                    var y0 = headerBot + (tableBot - headerBot - lines.Count * 13.5) / 2 + 1.2;
                    for (var li = 0; li < lines.Count; li++)
                    {
                        var lw = MixedW(10, lines[li], true);
                        Mixed(10, CjkActCols[c] + 3 + (cw - lw) / 2, y0 + li * 13.5, lines[li], true);
                    }
                }
        }
        var actIdx = new List<int>();
        for (var ti = 0; ti < tables.Count && actIdx.Count < 2; ti++)
            if (Leaf(tables[ti].body)
                && Regex.IsMatch(tables[ti].attrs, @"border\s*=\s*[""']?1")
                && tables[ti].attrs.Contains("text-bold", StringComparison.OrdinalIgnoreCase))
                actIdx.Add(ti);
        ActTable(actIdx.Count > 0 ? actIdx[0] : -1,
            actHeads.Count > 0 ? actHeads[0] : "1-", 364.3, 373.5, 402.8, 444.0);
        ActTable(actIdx.Count > 1 ? actIdx[1] : -1,
            actHeads.Count > 1 ? actHeads[1] : "2-", 447.5, 456.5, 488.6, 502.6);

        // the route line and the infrastructure table
        var routeM = Regex.Match(html, @">([⺀-鿿]{2,4}\s*\([^<)]{1,30}\))<");
        Mixed(12.5, CjkContentL, 518.0, routeM.Success ? routeM.Groups[1].Value : "", true);
        var infraHeadM = Regex.Match(html, @">([⺀-鿿]{6,12})<");
        Mixed(12.5, CjkContentL, 543.4,
            "基础设施详细信息", true);
        _ = infraHeadM;
        // infra table: 2 columns, measured rows
        var infraIdx = tables.FindIndex(t => Leaf(t.body) && t.body.Contains("基础设施编号"));
        var infraRows = infraIdx >= 0 ? Rows(tables[infraIdx].body) : new List<List<string>>();
        double[] infraEdges = { 554.5, 572.7, 590.0, 607.3, 624.5, 651.9, 669.2, 686.5 };
        for (var c = 0; c < 3; c++)
            VL(new[] { 96.0, 258.9, 502.4 }[c], infraEdges[0], infraEdges[^1]);
        foreach (var e in infraEdges) HL(96.0, 502.4, e);
        for (var r = 0; r < infraRows.Count && r + 1 < infraEdges.Length; r++)
        {
            var cells = infraRows[r];
            var top = infraEdges[r];
            var h = infraEdges[r + 1] - top;
            for (var c = 0; c < cells.Count && c < 2; c++)
            {
                var cx = c == 0 ? 99.0 : 261.9;
                var cw = (c == 0 ? 258.9 - 96.0 : 502.4 - 258.9) - 6;
                var lines = MeasuredWordWrapCjk(cells[c], cw, 10,
                    MixedW, false);
                var y0 = top + (h - lines.Count * 13.5) / 2 + 1.0;
                for (var li = 0; li < lines.Count; li++)
                    Mixed(10, cx, y0 + li * 13.5, lines[li], false);
            }
        }

        // ── the remaining sections: plain text on the following sheets ──
        page = doc.Pages.Add(CjkPageW, pageHeight);
        EnsureFonts(page);
        var yTd = 72.0;
        var infraEnd = infraIdx >= 0 ? tables[infraIdx].start : 0;
        for (var ti = 0; ti < tables.Count; ti++)
        {
            if (tables[ti].start <= infraEnd || !Leaf(tables[ti].body)) continue;
            foreach (var row in Rows(tables[ti].body))
            {
                var line = string.Join("  ", row);
                if (line.Trim().Length == 0) continue;
                foreach (var ln in MeasuredWordWrapCjk(line, CjkContentR - CjkContentL, 10, MixedW, false))
                {
                    if (yTd + 13.5 > pageHeight - 72.0)
                    {
                        page = doc.Pages.Add(CjkPageW, pageHeight);
                        EnsureFonts(page);
                        yTd = 72.0;
                    }
                    Mixed(10, CjkContentL, yTd, ln, false);
                    yTd += 13.5;
                }
            }
            yTd += 13.5;
        }
        return doc;
    }

    /// <summary>Wrap mixed CJK/latin text: break opportunities at spaces and
    /// after every ideograph.</summary>
    private static List<string> MeasuredWordWrapCjk(string text, double maxW,
        double fs, Func<double, string, bool, double> measure, bool bold)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }
        var cur = new StringBuilder();
        var word = new StringBuilder();
        void FlushWord()
        {
            if (word.Length == 0) return;
            var cand = cur.Length == 0 ? word.ToString()
                : cur.ToString() + word.ToString();
            if (measure(fs, cand, bold) > maxW && cur.Length > 0)
            {
                lines.Add(cur.ToString().TrimEnd());
                cur.Clear();
                cur.Append(word.ToString().TrimStart());
            }
            else
            {
                cur.Clear();
                cur.Append(cand);
            }
            word.Clear();
        }
        foreach (var ch in text)
        {
            if (ch == ' ') { word.Append(ch); FlushWord(); }
            else if (ch >= 0x2E80) { word.Append(ch); FlushWord(); }
            else word.Append(ch);
        }
        FlushWord();
        if (cur.Length > 0) lines.Add(cur.ToString().TrimEnd());
        if (lines.Count == 0) lines.Add(text);
        return lines;
    }
}
