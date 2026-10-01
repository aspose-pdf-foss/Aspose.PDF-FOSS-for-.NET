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
// below are measured against the expected output for this idiom.
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
    // The idiom lays out on the UA sheet: 90 pt side margins,
    // 72 pt top and bottom (measured: bands and canvas span (90,72)-(x,770)).
    private const double PpMarginLeftPt = 90.0;
    private const double PpMarginRightPt = 90.0;
    private const double PpMarginTopPt = 72.0;
    private const double PpMarginBottomPt = 72.0;

    /// <summary>Render the fixed-band print-page document, or null when the page
    /// does not carry the idiom's selectors.</summary>
    private static Document? TryRenderPrintPage(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageHeight)
    {
        var pp = new PrintPageState();
        pp.html = html;
        pp.css = css;
        pp.pageHeight = pageHeight;
        if (!pp.css.TryGetValue(".page-header", out var hdrRule)
            || !hdrRule.TryGetValue("position", out var hdrPos)
            || !hdrPos.Contains("fixed", StringComparison.OrdinalIgnoreCase)
            || !pp.css.TryGetValue(".page-footer", out var ftrRule)
            || !ftrRule.TryGetValue("position", out var ftrPos)
            || !ftrPos.Contains("fixed", StringComparison.OrdinalIgnoreCase)
            || !pp.css.TryGetValue(".page", out var pageRule)
            || !pageRule.TryGetValue("page-break-after", out var pba)
            || !pba.Contains("always", StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(pp.html, @"class\s*=\s*['""]page-header-space['""]",
                RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Times New Roman") is not { } fm) return null;
        pp.fm = fm;

        // the sheet: margins + the @media body width
        pp.bodyW = pp.css.TryGetValue("body", out var bodyRule)
            && bodyRule.TryGetValue("width", out var bwV)
            && TryParseLength(bwV) is { } bwPt ? bwPt : 595.28;
        pp.pageWidth = PpMarginLeftPt + pp.bodyW + PpMarginRightPt;
        pp.contentL = PpMarginLeftPt;
        pp.contentR = PpMarginLeftPt + pp.bodyW;
        pp.canvasBot = pp.pageHeight - PpMarginBottomPt;

        // band geometry: the declared heights plus the demo border (1px)
        pp.hdrH = hdrRule.TryGetValue("height", out var hhV)
            && TryParseLength(hhV) is { } hhPt ? hhPt
            : pp.css.TryGetValue(".page-header-space", out var hsRule)
              && hsRule.TryGetValue("height", out var hsV)
              && TryParseLength(hsV) is { } hsPt ? hsPt : 75.0;
        pp.ftrH = ftrRule.TryGetValue("height", out var fhV)
            && TryParseLength(fhV) is { } fhPt ? fhPt : 37.5;
        pp.hdrBorder = hdrRule.ContainsKey("border-bottom") ? 0.75 : 0;
        pp.ftrBorder = ftrRule.ContainsKey("border-top") ? 0.75 : 0;
        pp.hdrBg = hdrRule.TryGetValue("background", out var hbgV)
            ? ParseCssColor(hbgV) : null;
        pp.ftrBg = ftrRule.TryGetValue("background", out var fbgV)
            ? ParseCssColor(fbgV) : null;
        pp.canvasBg = bodyRule is not null
            && bodyRule.TryGetValue("background", out var cbgV)
            ? ParseCssColor(cbgV) : null;

        // ── parse the bands and the content pages ──
        pp.headerText = PrintBandText(pp, "page-header");
        pp.footerText = PrintBandText(pp, "page-footer");
        pp.headerCentered = Regex.IsMatch(pp.html,
            @"<div\b[^>]*class\s*=\s*['""]page-header['""][^>]*text-align\s*:\s*center",
            RegexOptions.IgnoreCase);

        pp.pages = new List<(string text, double lineBox)>();
        ReadPrintPageDivs(pp);
        if (pp.pages.Count == 0) return null;

        pp.doc = new Document();
        pp.invc = System.Globalization.CultureInfo.InvariantCulture;
        pp.contentTop = PpMarginTopPt + pp.hdrH + pp.hdrBorder + PpTheadChromePt;
        pp.ftrBandTop = pp.canvasBot - pp.ftrH - pp.ftrBorder;
        pp.drop12 = MetricBaselineDrop(PpFontPt, PpLineBoxPt, pp.fm);

        pp.page = NewPrintSheet(pp);
        pp.y = pp.contentTop;                        // top of the next content row
        pp.cellL = pp.contentL + PpCellInsetPt;
        pp.cellR = pp.contentR - PpCellInsetPt;
        LayoutPrintPages(pp);
        return pp.doc;
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
    // tables of flattened widget text. All constants are measured values.
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
        var sw = new StepRowsState();
        sw.html = html;
        sw.css = css;
        sw.pageHeight = pageHeight;
        if (!sw.css.TryGetValue(".step-row .sr-content", out var srcRule)
            || !srcRule.TryGetValue("width", out var srcW) || !srcW.Contains("490")
            || !sw.css.TryGetValue(".sr-bullet", out var srbRule)
            || !srbRule.ContainsKey("width")
            || !Regex.IsMatch(sw.html, @"class\s*=\s*['""]step-row",
                RegexOptions.IgnoreCase)
            || !Regex.IsMatch(sw.html, @"class\s*=\s*['""]swdt-table['""]",
                RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Arial") is null) return null;
        sw.invc = System.Globalization.CultureInfo.InvariantCulture;

        sw.contentTop = 78.0;            // 72 + the UA body margin
        sw.bulletX = 96.0 + SrBulletPadPt;
        sw.contentX = 96.0 + SrBulletWPt;
        sw.limit = sw.pageHeight - 72.0;
        sw.pageWidth = 96.0 + SrBulletWPt + SrContentWPt + SrContentMrPt + SrAckWPt + 90.0;

        sw.doc = new Document();
        sw.page = sw.doc.Pages.Add(sw.pageWidth, sw.pageHeight);
        EnsureFonts(sw.page);
        RegisterProcedureFonts(sw.page);
        sw.sb = new StringBuilder();
        sw.yTd = sw.contentTop;
        RenderStepRowPreamble(sw);

        foreach (Match srM in Regex.Matches(sw.html,
            @"<div\b[^>]*class\s*=\s*['""]step-row[^'""]*['""][^>]*>", RegexOptions.IgnoreCase))
        {
            if (!RenderStepRow(sw, srM)) break;
        }
        FlushOps(sw);
        return sw.doc;
    }

    /// <summary>The centred fixed-layout detable: whole-table page avoidance,
    /// row splits without header repetition, per-cell widget layout.</summary>
    private static double RenderDetable(StepRowsState sw, string tableHtml, double yTd, double limit)
    {
        var de = new DetableRenderState();
        de.sw = sw;
        de.tableHtml = tableHtml;
        de.yTd = yTd;
        de.limit = limit;
        de.contentX = 96.0 + SrBulletWPt;
        de.contentTop = 78.0;
        de.tableX = de.contentX + (SrContentWPt - SrDetableWPt) / 2;

        de.colW = new List<double>();
        de.thTexts = new List<string>();
        foreach (Match thM in Regex.Matches(de.tableHtml,
            @"<th\b(?<a>[^>]*)>(?<t>[\s\S]*?)</th>", RegexOptions.IgnoreCase))
        {
            var wM = Regex.Match(thM.Groups["a"].Value, @"width\s*=\s*['""]?([\d.]+)");
            de.colW.Add(wM.Success ? double.Parse(wM.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75 : 75);
            de.thTexts.Add(Regex.Replace(DecodeEntities(Regex.Replace(
                thM.Groups["t"].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim());
        }
        if (de.colW.Count == 0) return de.yTd;

        de.rows = new List<List<(bool el, string lbl)>>();
        foreach (Match trM in Regex.Matches(de.tableHtml, @"<tr\b[^>]*>(?<r>[\s\S]*?)</tr>",
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
            if (row.Count > 0) de.rows.Add(row);
        }

        de.tableH = SrThRowHPt + de.rows.Count * SrDataRowHPt;
        if (de.yTd + de.tableH > de.limit && de.yTd > de.contentTop + 0.1)
        {
            NewPage(de.sw);
            de.yTd = de.contentTop + SrTableFreshTopPt - SrTableTopPadPt;
        }

        de.top = de.yTd + SrTableTopPadPt;
        de.gridTop = de.top;
        // header row (first page of the table only): labels centre BOTH ways —
        // the cells are vertical-align:middle (template: a one-line label's
        // glyph opens 8.6 under the row top, a two-line one 2.4 under)
        HLine(de.sw, de.tableX, de.tableX + SrDetableWPt, de.top, 0.75);
        for (var ci = 0; ci < de.thTexts.Count; ci++)
        {
            var cx = de.tableX; for (var k = 0; k < ci; k++) cx += de.colW[k];
            var lines = MeasuredWordWrap(de.thTexts[ci], de.colW[ci] - 6, "Arial-Bold", SrCellFsPt);
            var seat = lines.Length > 1 ? 2.4 : 8.6;
            for (var li = 0; li < lines.Length; li++)
            {
                var lw = MeasureFaceText("Arial-Bold", lines[li], SrCellFsPt);
                Run(de.sw, "FB", SrCellFsPt, cx + (de.colW[ci] - lw) / 2,
                    de.top + seat + li * SrThLinePt, lines[li]);
            }
        }
        de.y = de.top + SrThRowHPt;
        HLine(de.sw, de.tableX, de.tableX + SrDetableWPt, de.y, 0.75);

        foreach (var row in de.rows)
        {
            if (!RenderDetableRow(de, row)) break;
        }
        CloseDetableGrid(de, de.y);
        return de.y;
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
