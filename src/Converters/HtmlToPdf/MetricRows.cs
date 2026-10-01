using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static void RenderMetricRows(MetricParseState mps, List<List<MetricCell>> rows, double[] colW, int nCols,
        double availW, double s, double lineH, string face, string boldFace, double hheaSum, (double asc, double sum) fm,
        double p, double pageWidth, double pageHeight, double marginTop, double marginBottom,
        double tableWpt, double tablePct, double baseFontSize, bool paragraphCells, string tableHtml,
        double symInsetPt, IReadOnlyDictionary<string, Dictionary<string, string>> css, Document doc,
        Core.PdfDictionary docFontDict, HtmlLoadOptions? loadOptions, System.Globalization.CultureInfo invc,
        Dictionary<string, string> flatRes, bool reportCells, bool serifReportCells, bool stdSerif, bool wrapperStacks,
        double collapseBoxW, double[] rowSpanExtra, bool tableHasText, bool tableRuleFace, double tableX,
        double marginLeft, double contentWidth, List<CssSiblingCellRule>? siblingCellRules, FlowPosition cursor)
    {
        var mr = new MetricRowsState();
        mr.mps = mps;
        mr.rows = rows;
        mr.colW = colW;
        mr.nCols = nCols;
        mr.availW = availW;
        mr.s = s;
        mr.lineH = lineH;
        mr.face = face;
        mr.boldFace = boldFace;
        mr.hheaSum = hheaSum;
        mr.fm = fm;
        mr.p = p;
        mr.pageWidth = pageWidth;
        mr.pageHeight = pageHeight;
        mr.marginTop = marginTop;
        mr.marginBottom = marginBottom;
        mr.tableWpt = tableWpt;
        mr.tablePct = tablePct;
        mr.baseFontSize = baseFontSize;
        mr.paragraphCells = paragraphCells;
        mr.tableHtml = tableHtml;
        mr.symInsetPt = symInsetPt;
        mr.css = css;
        mr.doc = doc;
        mr.docFontDict = docFontDict;
        mr.loadOptions = loadOptions;
        mr.invc = invc;
        mr.flatRes = flatRes;
        mr.reportCells = reportCells;
        mr.serifReportCells = serifReportCells;
        mr.stdSerif = stdSerif;
        mr.wrapperStacks = wrapperStacks;
        mr.collapseBoxW = collapseBoxW;
        mr.rowSpanExtra = rowSpanExtra;
        mr.tableHasText = tableHasText;
        mr.tableRuleFace = tableRuleFace;
        mr.tableX = tableX;
        mr.marginLeft = marginLeft;
        mr.contentWidth = contentWidth;
        mr.siblingCellRules = siblingCellRules;
        mr.cursor = cursor;
        var frameTopY = cursor.y;
        var framePage = cursor.page;
        var frameX0 = tableX;
        var frameStreamMark = cursor.page.ContentStreamCount;
        // the rows stand inside the table's own CSS frame
        var ownSides = mps.sideFrames is not null && !mps.bordered ? mps.sideFrames : null;
        if (ownSides is not null)
        {
            cursor.y -= ownSides[0].W;
            mr.tableX = tableX + ownSides[3].W;
            mr.availW = availW - ownSides[3].W - ownSides[1].W;
        }
    for (var ri = 0; ri < mr.rows.Count; ri++)
    {
        RenderMetricRow(mr, ri);
    }
        // A CSS-framed table (no border attribute) strokes its declared sides around the
        // box it laid out - a natural box hugs its columns, a declared one fills its width.
        var naturalW = (nCols + 1) * s;
        foreach (var w in colW) naturalW += w + 2 * p;
        var boxW = tableWpt > 0 ? tableWpt : tablePct > 0 ? availW : Math.Min(mr.availW, naturalW);
        // (a UA form grid declared narrower than its columns grows to them - measured on the
        // test request: the 506 px container draws 400 wide around its 506 px comment table)
        if (mps.uaFormCells && tableWpt > 0 && naturalW > boxW) boxW = naturalW;
        // (the pt form grid's box is its columns, spacings and table padding, whatever share it declared -
        //  probed: the 98 % title table's frame is 533.717 wide, its column 527.717 + 6 of chrome)
        if (mps.ptFormCells) boxW = naturalW + 2 * mps.tablePadLeftPt;
        if (ownSides is not null) { cursor.y -= ownSides[2].W; boxW += ownSides[3].W + ownSides[1].W; }
        cursor.lastTableX0 = frameX0;
        cursor.lastTableX1 = frameX0 + boxW;
        if (mps.uaFormCells && (ownSides is not null || mps.tableBg is not null))
            PaintUaFormTableChrome(mr, ownSides, frameX0, boxW, framePage, frameStreamMark, frameTopY);
        else if (ownSides is not null && ReferenceEquals(cursor.page, framePage))
            EmitCssSideFrames(cursor.page, ownSides, frameX0, frameX0 + boxW, frameTopY, cursor.y, invc);
    }

    /// <summary>A UA form grid's class chrome, page by page: the background fills the box it laid
    /// out on each page - under everything the rows drew there - and its border sides frame it,
    /// the top side on the first page and the bottom on the last (measured on the test request:
    /// the whitesmoke fill 93.75..770 on page 1, 72..770 on page 2 and 72..174 on page 3, the
    /// #94a6b5 side rules alongside, the bottom rule at 173.62).</summary>
    private static void PaintUaFormTableChrome(MetricRowsState mr, CssBorderSide[]? sides, double x0, double boxW,
        Page page0, int streamMark, double topY0)
    {
        var pageN = mr.cursor.page;
        for (var n = page0.Number; n <= pageN.Number; n++)
        {
            var page = mr.doc.Pages[n];
            var top = n == page0.Number ? topY0 : mr.pageHeight - mr.marginTop;
            // (the box closes under the last row's trailing spacing, which the caller spends)
            var bot = n == pageN.Number ? mr.cursor.y - mr.s : mr.marginBottom;
            if (mr.mps.tableBg is { } bg)
                page.InsertContentStreamAt(n == page0.Number ? streamMark : 0, Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                    $"q {bg.R / 255.0:0.###} {bg.G / 255.0:0.###} {bg.B / 255.0:0.###} rg " +
                    $"{x0:F2} {bot:F2} {boxW:F2} {top - bot:F2} re f Q\n")));
            if (sides is null) continue;
            var pageSides = (CssBorderSide[])sides.Clone();
            if (n != page0.Number) pageSides[0].W = 0;
            if (n != pageN.Number) pageSides[2].W = 0;
            EmitCssSideFrames(page, pageSides, x0, x0 + boxW, top, bot, mr.invc);
        }
    }
}
