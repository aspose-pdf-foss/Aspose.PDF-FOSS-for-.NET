using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static void FitBorderedColumns(MetricParseState mps, List<List<MetricCell>> rows, double[] colW,
        bool[] colFixed, double[] colPct, double[] colPx, bool[] colPxStyle, int nCols, double availW, double bw,
        double p, double pageWidth, double pageHeight, double marginTop, double marginBottom,
        double tableWpt, double tablePct, double baseFontSize, bool paragraphCells, string tableHtml,
        double s, string face, string boldFace, double symInsetPt, bool tableFills, bool stdSerif = false, MetricTableState? host = null)
    {
        var fb = new FitBorderedState();
        fb.mps = mps;
        fb.host = host;
        fb.stdSerif = stdSerif;
        fb.rows = rows;
        fb.colW = colW;
        fb.colFixed = colFixed;
        fb.colPct = colPct;
        fb.colPx = colPx;
        fb.colPxStyle = colPxStyle;
        fb.nCols = nCols;
        fb.availW = availW;
        fb.bw = bw;
        fb.p = p;
        fb.pageWidth = pageWidth;
        fb.pageHeight = pageHeight;
        fb.marginTop = marginTop;
        fb.marginBottom = marginBottom;
        fb.tableWpt = tableWpt;
        fb.tablePct = tablePct;
        fb.baseFontSize = baseFontSize;
        fb.paragraphCells = paragraphCells;
        fb.tableHtml = tableHtml;
        fb.s = s;
        fb.face = face;
        fb.boldFace = boldFace;
        fb.symInsetPt = symInsetPt;
        fb.tableFills = tableFills;
        fb.innerW = fb.availW - 2 * fb.bw;
        if (fb.mps.attrCollapse)
        {
            FitCollapsedColumns(fb);
        }
        else if (fb.mps.layoutFixed)
        {
            for (var c = 0; c < fb.nCols; c++)
                fb.colW[c] = Math.Max(fb.mps.fontSize, fb.colPct[c] / 100.0 * fb.innerW);
        }
        else
        {
            FitSeparatedColumns(fb);
        }
    }

    private static void RenderBorderedGrid(MetricParseState mps, List<List<MetricCell>> rows, double[] colW, int nCols,
        double availW, double s, double bw, double lineH, string face, string boldFace, double hheaSum, (double asc, double sum) fm,
        double p, double pageWidth, double pageHeight, double marginTop, double marginBottom,
        double tableWpt, double tablePct, double baseFontSize, bool paragraphCells, string tableHtml,
        double symInsetPt, bool tableFills, IReadOnlyDictionary<string, Dictionary<string, string>> css, Document doc,
        Core.PdfDictionary docFontDict, HtmlLoadOptions? loadOptions, System.Globalization.CultureInfo invc,
        bool stdSerif, bool wrapperStacks, Color? rmtAnchorColor,
        double marginLeft, double contentWidth, double tableX, FlowPosition cursor)
    {
        // A bordered percent-width attribute grid FILLS its declared share of
        // the content box: the columns scale up proportionally so the outer
        // box lands on pct x avail (probed: width=80% align=center draws its
        // border box at 0.8 of the content width, centred; a grid whose
        // natural box already exceeds the share keeps its hug).
        var bg = new BorderedGridState();
        // The grid's own left edge: the caller's table edge, hugged to the borders.
        bg.tableX = HugGridBorders(mps, rows, colW, nCols, availW, s, bw, face, boldFace, p, pageWidth, tablePct, stdSerif, marginLeft, tableX);
        bg.sbB = new StringBuilder();
        bg.extraRes = new Dictionary<string, string>(StringComparer.Ordinal);
        bg.borderPage = cursor.page;
        bg.tableTopTd = pageHeight - cursor.y;
        // a thick attribute frame stands outside the cell grid: the rows and the first
        // column start its extra width in from the box (probed: border=5 cells open 3.75 + spacing in)
        bg.rowTopTd = bg.tableTopTd + bw + FrameExtra(mps, bw) + s;
        bg.rowDeclH = null;
        bg.rowBotW = null;
        bg.rowStrokeCol = (Color?[]?)null;
        SizeGridRows(bg, pageHeight, mps, rows, bw, tableWpt, tableHtml, invc, contentWidth);
        bg.outerW = 2 * (bw + FrameExtra(mps, bw)) + (nCols + 1) * s;
        foreach (var w in colW) bg.outerW += w + 2 * p + 2 * bw;
        // (a collapsed grid's neighbours share one rule: the frame ends on the last one)
        if (mps.attrCollapse) bg.outerW -= nCols * bw;
        bg.outerR = bg.tableX + (mps.borderHugs ? bg.outerW
            : tableFills && !mps.layoutFixed ? availW : Math.Max(availW, bg.outerW));
        bg.rowIdx = -1;
        foreach (var r in rows)
        {
            if (!RenderGridRow(bg, mps, colW, nCols, s, bw, lineH, face, boldFace, hheaSum, fm, p, pageWidth, pageHeight, marginTop, marginBottom, tableWpt, baseFontSize, symInsetPt, css, doc, docFontDict, loadOptions, invc, stdSerif, wrapperStacks, rmtAnchorColor, r)) break;
        }
        bg.tableBottomTd = bg.rowDeclH is not null
            ? bg.rowTopTd + (bg.rowBotW![^1] > 0 ? bg.rowBotW[^1] : bw) / 2
            : bg.rowTopTd + bw + FrameExtra(mps, bw);
        if (bg.rowDeclH is null)
            EmitGridFrame(bg, pageHeight, mps, invc, bw, bg.tableX, bg.tableTopTd, bg.outerR, bg.tableBottomTd);
        EmitGridStrokes(bg, mps, invc, bw);
        // the rows may have paginated: the flow continues on the page the last row landed on
        cursor.page = bg.borderPage;
        cursor.y = pageHeight - bg.tableBottomTd;
        cursor.lastTableX0 = bg.tableX;
        cursor.lastTableX1 = bg.outerR;
        return;
    }

}
