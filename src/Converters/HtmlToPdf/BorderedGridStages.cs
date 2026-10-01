using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Bordered-grid drawing helpers: a border line, a border box and a cell's font resource.
    private static void BLine(BorderedGridState bg, double pageHeight, System.Globalization.CultureInfo invc, double x0, double y0d, double x1, double y1d)
        => bg.sbB.Append(Compat.Format(invc,
            $"{x0:F2} {pageHeight - y0d:F2} m {x1:F2} {pageHeight - y1d:F2} l S "));

    /// <summary>Strokes the four rules of a bordered cell box, each inset by half the border width.</summary>
    /// <param name="bg">The grid state whose border stream receives the strokes.</param>
    /// <param name="pageHeight">Page height in points, used to flip the top-down y values.</param>
    /// <param name="invc">The invariant culture used to format the numbers.</param>
    /// <param name="bw">Border width in points.</param>
    /// <param name="x0">Left edge of the box.</param>
    /// <param name="y0d">Top edge of the box, measured down from the page top.</param>
    /// <param name="x1">Right edge of the box.</param>
    /// <param name="y1d">Bottom edge of the box, measured down from the page top.</param>
    /// <param name="top">Stroke the top rule (false under a row-spanning cell: the box continues).</param>
    /// <param name="bottom">Stroke the bottom rule (false for a row-spanning cell: its box goes on below).</param>
    private static void BBox(BorderedGridState bg, double pageHeight, System.Globalization.CultureInfo invc, double bw, double x0, double y0d, double x1, double y1d, bool top = true, bool bottom = true)
    {
        if (top) BLine(bg, pageHeight, invc, x0, y0d + bw / 2, x1, y0d + bw / 2);
        if (bottom) BLine(bg, pageHeight, invc, x0, y1d - bw / 2, x1, y1d - bw / 2);
        BLine(bg, pageHeight, invc, x0 + bw / 2, y0d, x0 + bw / 2, y1d);
        BLine(bg, pageHeight, invc, x1 - bw / 2, y0d, x1 - bw / 2, y1d);
    }

    private static string ResOf(BorderedGridState bg, string face, string boldFace, bool stdSerif, MetricCell mc)
    {
        // (a UA grid under a face other than the serif draws in that face, as the unbordered rows do)
        if (mc.Face is null && !(stdSerif && !face.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase)))
            return mc.Bold ? (stdSerif ? "F6" : "F2")
                : mc.Italic ? (stdSerif ? "F7" : "F3")
                : (stdSerif ? "F5" : "F1");
        var fn = CellFaceName(face, boldFace, mc);
        if (!bg.extraRes.TryGetValue(fn, out var rn))
        {
            // Skip names the flow's Type0 embeds already claimed in the
            // shared /Font dictionary — landing on one would show the
            // cells' WinAnsi strings through an Identity-H font (every
            // byte pair a glyph id → notdef boxes).
            var fd = (bg.borderPage.Dict.Get("Resources") as Core.PdfDictionary)?
                .Get("Font") as Core.PdfDictionary;
            var idx = 8 + bg.extraRes.Count;
            while (fd?.Get("F" + idx) is { } takenObj
                   && (takenObj is not Core.PdfDictionary taken
                       || taken.GetName("BaseFont") != fn.Replace(" ", "")))
                idx++;
            rn = "F" + idx;
            bg.extraRes[fn] = rn;
        }
        EnsureFont(bg.borderPage, fn.Replace(" ", ""), rn);
        return rn;
    }

    /// <summary>Render one grid row: its content height and declared height, then each cell's box, background, borders and text at the walked column.</summary>
    private static bool RenderGridRow(BorderedGridState bg, MetricParseState mps, double[] colW, int nCols, double s, double bw, double lineH, string face, string boldFace, double hheaSum, (double asc, double sum) fm, double p, double pageWidth, double pageHeight, double marginTop, double marginBottom, double tableWpt, double baseFontSize, double symInsetPt, IReadOnlyDictionary<string, Dictionary<string, string>> css, Document doc, Core.PdfDictionary docFontDict, HtmlLoadOptions? loadOptions, System.Globalization.CultureInfo invc, bool stdSerif, bool wrapperStacks, Color? rmtAnchorColor, List<MetricCell> r)
    {
        bg.rowIdx++;
        bg.rowContentB = mps.borderHugs ? 0 : lineH;
        // a row-spanning cell hangs from its row top over the rows below: its
        // content does not band its own row
        foreach (var mc in r) if (mc.RowSpan <= 1) bg.rowContentB = Math.Max(bg.rowContentB, mc.ContentH);
        if (bg.rowContentB <= 0) bg.rowContentB = lineH;
        bg.cellBoxH = bg.rowContentB
            + 2 * (mps.wtInlineGrid && mps.wtPadV >= 0 ? mps.wtPadV : p)
            + (mps.attrCollapse ? 0 : 2 * bw);
        bg.bandExtra = 0.0;
        if ((mps.tableStyleHPt > 0 || (bg.rowIdx < mps.rowHeightAttr.Count && mps.rowHeightAttr[bg.rowIdx]))
            && bg.rowIdx < mps.rowHeights.Count && mps.rowHeights[bg.rowIdx] > bg.cellBoxH)
        {
            bg.bandExtra = mps.rowHeights[bg.rowIdx] - bg.cellBoxH;
            bg.cellBoxH = mps.rowHeights[bg.rowIdx];
        }
        bg.rowTopHalf = 0.0;
        if (bg.rowDeclH is not null)
        {
            bg.cellBoxH = Math.Max(bg.rowContentB, bg.rowDeclH[bg.rowIdx])
                + Math.Max(0, mps.wtPadV) + mps.wtPadB;
            bg.rowTopHalf = (bg.rowIdx == 0 ? bw : bg.rowBotW![bg.rowIdx - 1]) / 2;
        }
        // Pagination: a row whose box would cross the bottom margin moves whole to a
        // fresh page (as a separated row does); the frame closes on the page it
        // leaves and the continuation resumes at the raw content top.
        if (bg.rowIdx > 0 && bg.rowDeclH is null
            && bg.rowTopTd + bg.cellBoxH > pageHeight - marginBottom)
            BreakGridPage(bg, mps, doc, docFontDict, pageWidth, pageHeight, marginTop, invc, bw, s);
        // the table's own left padding is box space between its frame and its first column
        bg.colXB = bg.tableX + bw + FrameExtra(mps, bw) + s + mps.tablePadLeftPt;
        bg.spanSkip = 0;
        bg.rowSubBotTd = 0;
        bg.rowEdgeStrokes = new StringBuilder();
        for (var c = 0; c < nCols; c++)
        {
            if (!RenderGridCell(bg, mps, colW, nCols, s, bw, face, boldFace, hheaSum, fm, p, pageWidth, pageHeight, marginTop, marginBottom, tableWpt, baseFontSize, symInsetPt, css, doc, docFontDict, loadOptions, invc, stdSerif, wrapperStacks, rmtAnchorColor, r, c)) break;
        }
        if (bg.rowEdgeStrokes.Length > 0)
            bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes(bg.rowEdgeStrokes.ToString()));
        bg.rowTopTd += Math.Max(bg.cellBoxH, bg.rowSubBotTd - bg.rowTopTd) + s
            // Excel-fragment grid: each row boundary advances by its
            // one shared declared border. Declared-height rows advance
            // boundary-center to boundary-center: half the border
            // above, the box, half the row's OWN bottom border.
            // A collapsed grid's row boundary carries its one shared border too
            // (probed: 10.5 pt rows pitch 14.25 = the 13.5 box + the 1px rule).
            + (bg.rowDeclH is not null
                ? bg.rowTopHalf + bg.rowBotW![bg.rowIdx] / 2
                : mps.wtInlineGrid ? (mps.wtBw > 0 ? mps.wtBw : bw)
                : mps.attrCollapse ? bw : 0);
        return true;
    }

    /// <summary>The grid's accumulated border strokes drawn on the page the rows are on.</summary>
    /// <summary>What a thick attribute frame adds beyond the cell stroke width - the space it takes from the grid.</summary>
    private static double FrameExtra(MetricParseState mps, double bw) => Math.Max(0, mps.frameW - bw);

    private static void EmitGridStrokes(BorderedGridState bg, MetricParseState mps, System.Globalization.CultureInfo invc, double bw, string dashOps = "")
    {
        if (bg.sbB.Length == 0) return;
        bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
            $"q {mps.borderColor.R / 255.0:0.###} {mps.borderColor.G / 255.0:0.###} {mps.borderColor.B / 255.0:0.###} RG {bw:0.##} w {dashOps}{bg.sbB}Q\n")));
        bg.sbB.Clear();
    }

    /// <summary>The grid closes its frame on the current page and continues on a fresh one at the raw content top.</summary>
    private static void BreakGridPage(BorderedGridState bg, MetricParseState mps, Document doc, Core.PdfDictionary docFontDict, double pageWidth, double pageHeight, double marginTop, System.Globalization.CultureInfo invc, double bw, double s)
    {
        EmitGridFrame(bg, pageHeight, mps, invc, bw, bg.tableX, bg.tableTopTd, bg.outerR, bg.rowTopTd + bw);
        bg.borderPage = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(bg.borderPage, docFontDict);
        // the cell font resources were registered on the page left behind
        bg.extraRes.Clear();
        bg.tableTopTd = marginTop;
        bg.rowTopTd = bg.tableTopTd + bw + FrameExtra(mps, bw) + s;
    }

    /// <summary>Declared row heights, bottom widths and stroke colours for an inline grid, and the table-style band that seats the first row.</summary>
    private static void SizeGridRows(BorderedGridState bg, double pageHeight, MetricParseState mps, List<List<MetricCell>> rows, double bw, double tableWpt, string tableHtml, System.Globalization.CultureInfo invc, double contentWidth)
    {
        if (mps.wtInlineGrid && mps.wtPMarginDefaulted)
        {
            bg.rowDeclH = new double[rows.Count];
            bg.rowBotW = new double[rows.Count];
            bg.rowStrokeCol = new Color?[rows.Count];
            var trBlocks = Regex.Matches(tableHtml,
                @"<tr\b[\s\S]*?(?=<tr\b|</table)", RegexOptions.IgnoreCase);
            for (var tri = 0; tri < rows.Count; tri++)
            {
                bg.rowBotW[tri] = mps.wtBw > 0 ? mps.wtBw : bw;
                if (tri >= trBlocks.Count) continue;
                var trBlock = trBlocks[tri].Value;
                var hm = Regex.Match(trBlock, @"height\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase);
                if (hm.Success)
                    bg.rowDeclH[tri] = double.Parse(hm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                var tdSt = Regex.Match(trBlock,
                    @"<td\b[^>]*style\s*=\s*[""']([^""']*)", RegexOptions.IgnoreCase);
                if (!tdSt.Success) continue;
                var st = tdSt.Groups[1].Value;
                var bwm = Regex.Match(st, @"border-width\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase);
                if (bwm.Success)
                {
                    var toks = bwm.Groups[1].Value.Trim()
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var bm = Regex.Match(
                        toks[Math.Min(toks.Length >= 3 ? 2 : 0, toks.Length - 1)],
                        @"([\d.]+)\s*pt");
                    if (bm.Success)
                        bg.rowBotW[tri] = double.Parse(bm.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture);
                }
                var bcm = Regex.Match(st, @"border-color\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase);
                if (bcm.Success
                    && !bcm.Groups[1].Value.Contains("-moz-",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var firstCol = Regex.Match(bcm.Groups[1].Value.Trim(),
                        @"rgb\([^)]*\)|#\w+|[a-zA-Z]+").Value;
                    if (firstCol.Length > 0 && ParseCssColor(firstCol) is { } rcCol)
                        bg.rowStrokeCol[tri] = rcCol;
                }
            }
            // rowTopTd runs on BOUNDARY CENTERS for this grid: the top
            // border's center is half a width under the table top.
            bg.rowTopTd = bg.tableTopTd + bw / 2;
        }
        // The inline-style band: its background fills the declared width × height
        // rectangle before any cell ink (probed: 96 118.5 361.5 101.25 re on the
        // reference band, one uniform fill — the cells' own fills ride on top).
        if (mps.tableStyleBg is { } tsBand && mps.tableStyleHPt > 0)
        {
            var bandW = tableWpt > 0 ? tableWpt : contentWidth;
            bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
                $"q {tsBand.R / 255.0:0.###} {tsBand.G / 255.0:0.###} {tsBand.B / 255.0:0.###} rg " +
                $"{bg.tableX:F2} {pageHeight - bg.tableTopTd - mps.tableStyleHPt:F2} {bandW:F2} {mps.tableStyleHPt:F2} re f Q\n")));
        }
    }

    /// <returns>The table edge, shifted when a hugging grid centres itself.</returns>
    /// <summary>A border-hugging table pins its left edge and width to the percent-sized box before the borders are drawn.</summary>
    private static double HugGridBorders(MetricParseState mps, List<List<MetricCell>> rows, double[] colW, int nCols, double availW, double s, double bw, string face, string boldFace, double p, double pageWidth, double tablePct, bool stdSerif, double marginLeft, double tableX)
    {
        if (mps.borderHugs && tablePct > 0 && stdSerif && nCols > 0)
        {
            double hugW0 = 2 * (bw + FrameExtra(mps, bw)) + (nCols + 1) * s;
            foreach (var w in colW) hugW0 += w + 2 * p + 2 * bw;
            var targetBox = availW * tablePct / 100.0;
            double sumW0 = 0; foreach (var w in colW) sumW0 += w;
            var chrome = hugW0 - sumW0;
            if (hugW0 < targetBox && sumW0 > 0)
            {
                var pctBoxScale = (targetBox - chrome) / sumW0;
                for (var c = 0; c < nCols; c++) colW[c] *= pctBoxScale;
            }
            // Wider than its share: the columns give the excess back in
            // proportion to their slack above min-content and the cells
            // wrap at the solved width (the percent is a cap as much as a
            // fill - W = max(pct x base, sum of mins), the banked rule).
            else if (hugW0 > targetBox && sumW0 > 0)
            {
                var minP = new double[nCols];
                for (var c = 0; c < nCols; c++)
                    foreach (var r in rows)
                        if (c < r.Count && r[c].Text.Length > 0 && r[c].ColSpan <= 1)
                            foreach (var seg1 in r[c].Text.Split(''))
                            foreach (var word in seg1.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                                minP[c] = Math.Max(minP[c], MeasureFaceText(
                                    r[c].Bold ? boldFace : face, word,
                                    r[c].FontSize ?? mps.fontSize));
                double slackSum = 0;
                for (var c = 0; c < nCols; c++) slackSum += Math.Max(0, colW[c] - minP[c]);
                var deficit = hugW0 - targetBox;
                if (slackSum > 0)
                    for (var c = 0; c < nCols; c++)
                        colW[c] -= Math.Min(
                            deficit * Math.Max(0, colW[c] - minP[c]) / slackSum,
                            Math.Max(0, colW[c] - minP[c]));
            }
        }
        // Attribute grid: the outer box hugs the column grid; align=center
        // centres it on the page (the symmetric UA content frame's middle).
        if (mps.borderHugs)
        {
            var hugW = 2 * bw + (nCols + 1) * s;
            foreach (var w in colW) hugW += w + 2 * p + 2 * bw;
            if (mps.centerTable)
                tableX = Math.Max(marginLeft, (pageWidth - hugW) / 2);
        }
        return tableX;
    }

    /// <summary>Render one cell of a grid row: its span, box, background band, borders and the text lines seated inside it.</summary>
    private static bool RenderGridCell(BorderedGridState bg, MetricParseState mps, double[] colW, int nCols, double s, double bw, string face, string boldFace, double hheaSum, (double asc, double sum) fm, double p, double pageWidth, double pageHeight, double marginTop, double marginBottom, double tableWpt, double baseFontSize, double symInsetPt, IReadOnlyDictionary<string, Dictionary<string, string>> css, Document doc, Core.PdfDictionary docFontDict, HtmlLoadOptions? loadOptions, System.Globalization.CultureInfo invc, bool stdSerif, bool wrapperStacks, Color? rmtAnchorColor, List<MetricCell> r, int c)
    {
        bg.boxW = colW[c] + 2 * p + 2 * bw;
        if (bg.spanSkip > 0)
        {
            // a phantom slot under a spanning cell: no box of its own,
            // no advance — the spanning cell already covered it.
            bg.spanSkip--;
            return true;
        }
        if (c < r.Count && r[c].ColSpan > 1)
            for (var k = 1; k < r[c].ColSpan && c + k < nCols; k++)
            {
                bg.boxW += s + colW[c + k] + 2 * p + 2 * bw;
                bg.spanSkip++;
            }
        bg.fillH = bg.rowDeclH is not null
            ? bg.rowTopHalf + bg.cellBoxH + bg.rowBotW![bg.rowIdx] / 2
            : bg.cellBoxH;
        bg.fillW = bg.rowDeclH is not null && tableWpt > 0
            ? Math.Min(bg.boxW, bg.tableX + tableWpt - bg.colXB)
            : bg.boxW;
        if (c < r.Count && r[c].Bg is { } cbg)
            bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
                $"q {cbg.R / 255.0:0.###} {cbg.G / 255.0:0.###} {cbg.B / 255.0:0.###} rg " +
                $"{bg.colXB:F2} {pageHeight - bg.rowTopTd - bg.fillH:F2} {bg.fillW:F2} {bg.fillH:F2} re f Q\n")));
        if (bg.rowDeclH is not null)
        {
            // ONE line per boundary in the row's own ink: the top
            // border on the first row, the row's bottom under it,
            // verticals AT the box edges (measured: the
            // left vertical centers half a width inside the table
            // edge, the boundary at the shared edge) — the grid
            // clips at the declared table width.
            var rsCol = bg.rowStrokeCol?[bg.rowIdx] ?? Color.FromArgb(0, 0, 0);
            var rbW = bg.rowBotW![bg.rowIdx];
            var clipR = tableWpt > 0 ? bg.tableX + tableWpt : double.MaxValue;
            var vTop = bg.rowTopTd - bg.rowTopHalf;
            var vBot = bg.rowTopTd + bg.rowTopHalf + bg.cellBoxH + rbW;
            var vLx = bg.colXB - bw / 2;
            var vRx = Math.Min(bg.colXB + bg.boxW, clipR) - bw / 2;
            var hRx = Math.Min(bg.colXB + bg.boxW + bw / 2, clipR);
            void RowLine(double w2, double sx0, double sy0, double sx1, double sy1)
                => bg.rowEdgeStrokes.Append(Compat.Format(invc,
                    $"q {rsCol.R / 255.0:0.###} {rsCol.G / 255.0:0.###} {rsCol.B / 255.0:0.###} RG " +
                    $"{w2:0.##} w {sx0:F2} {pageHeight - sy0:F2} m {sx1:F2} {pageHeight - sy1:F2} l S Q\n"));
            if (bg.rowIdx == 0)
                RowLine(bw, bg.colXB - bw, bg.rowTopTd, hRx, bg.rowTopTd);
            RowLine(rbW, bg.colXB - bw, bg.rowTopTd + bg.rowTopHalf + bg.cellBoxH + rbW / 2,
                hRx, bg.rowTopTd + bg.rowTopHalf + bg.cellBoxH + rbW / 2);
            RowLine(bw, vLx, vTop, vLx, vBot);
            RowLine(bw, vRx, vTop, vRx, vBot);
        }
        else
        {
            // a row-spanning cell's box continues below its row, and the slots it
            // covers continue it: neither rules the boundary between them
            var spanTop = !(c < r.Count && r[c].RowSpanCovered);
            var spanBottom = !(c < r.Count && r[c].RowSpan > 1);
            if (mps.attrCollapse)
                // the collapsed grid's box reaches its shared boundaries: the rule is centred on
                // the boundary, so the rows above and below stroke the SAME line
                BBox(bg, pageHeight, invc, bw, bg.colXB, bg.rowTopTd - bw, bg.colXB + bg.boxW, bg.rowTopTd + bg.cellBoxH + bw, spanTop, spanBottom);
            else
                BBox(bg, pageHeight, invc, bw, bg.colXB, bg.rowTopTd, bg.colXB + bg.boxW, bg.rowTopTd + bg.cellBoxH, spanTop, spanBottom);
        }
        // a style border-right strokes that one edge in its own colour
        // over the shared grid (the separator-column idiom); emitted
        // after the row's fills so a neighbour's fill can't bury it
        if (c < r.Count && r[c].BorderRightW > 0)
        {
            var brc = r[c].BorderRightCol;
            bg.rowEdgeStrokes.Append(Compat.Format(invc,
                $"q {brc.R / 255.0:0.###} {brc.G / 255.0:0.###} {brc.B / 255.0:0.###} RG " +
                $"{r[c].BorderRightW:0.##} w {bg.colXB + bg.boxW:F2} {pageHeight - bg.rowTopTd:F2} m " +
                $"{bg.colXB + bg.boxW:F2} {pageHeight - bg.rowTopTd - bg.cellBoxH:F2} l S Q\n"));
        }
        if (c < r.Count && (r[c].Lines.Length > 0 || r[c].SubTables is { Count: > 0 }
            || r[c].DivSegs is { Count: > 0 }))
        {
            RenderGridCellContent(bg, mps, bw, face, boldFace, hheaSum, fm, p, pageWidth, pageHeight, marginTop, marginBottom, baseFontSize, symInsetPt, css, doc, docFontDict, loadOptions, invc, stdSerif, wrapperStacks, rmtAnchorColor, r, c);
        }
        // (a collapsed grid's neighbours share one rule: the next box starts a border
        // width inside this one, so their vertical strokes coincide)
        bg.colXB += bg.boxW + s - (mps.attrCollapse ? bw : 0);
        return true;
    }

    /// <summary>The cell's text lines and nested sub-tables seated inside its box, with the paragraph or list geometry the cell declares.</summary>
    private static void RenderGridCellContent(BorderedGridState bg, MetricParseState mps, double bw, string face, string boldFace, double hheaSum, (double asc, double sum) fm, double p, double pageWidth, double pageHeight, double marginTop, double marginBottom, double baseFontSize, double symInsetPt, IReadOnlyDictionary<string, Dictionary<string, string>> css, Document doc, Core.PdfDictionary docFontDict, HtmlLoadOptions? loadOptions, System.Globalization.CultureInfo invc, bool stdSerif, bool wrapperStacks, Color? rmtAnchorColor, List<MetricCell> r, int c)
    {
        bg.mc = r[c];
        bg.cellFs = bg.mc.FontSize ?? mps.fontSize;
        bg.cFm = CellFm(fm, bg.mc);
        bg.cellLineH = CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, bg.mc, bg.cellFs);
        bg.mFace = CellFaceName(face, boldFace, bg.mc);
        bg.fontRes = ResOf(bg, face, boldFace, stdSerif, bg.mc);
        bg.lineTopTd = bg.rowTopTd
            // shared borders: content opens half the boundary
            // below the row's border center
            + (mps.attrCollapse ? (mps.wtInlineGrid ? bw / 2 : 0) : bw)
            + (mps.wtInlineGrid && mps.wtPadV >= 0 ? mps.wtPadV : p)
            // a cell's own top padding (a padding-top style, a paragraph's block margin)
            // seats its lines below it - the same band its content height already holds
            + bg.mc.PadTopPt
            // (a row-spanning cell seats at its row top, over the rows below)
            + (bg.mc.VAlignTop || bg.mc.RowSpan > 1 ? 0 : (bg.rowContentB + bg.bandExtra - bg.mc.ContentH) / 2);
        // Declared-height rows: a valign=top cell seats at the
        // content top (pad under the border); an attr-less cell
        // CENTRES its lines in the declared box (probed: the
        // body rows' text centers on the 25.25 pt content box).
        if (bg.rowDeclH is not null)
            bg.lineTopTd = bg.rowTopTd + bg.rowTopHalf + Math.Max(0, mps.wtPadV)
                + (bg.mc.VAlignTop ? 0
                    : (bg.cellBoxH - Math.Max(0, mps.wtPadV) - mps.wtPadB
                       - bg.mc.Lines.Length * bg.cellLineH) / 2);
        bg.uaLink = bg.mc.LinkUrl is not null && bg.mc.Fore is null
            && rmtAnchorColor is null;
        bg.cellInk = bg.mc.Fore ?? (bg.uaLink ? Color.FromArgb(0, 0, 255) : null);
        if (bg.cellInk is { } fc)
            bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
                $"{fc.R / 255.0:0.###} {fc.G / 255.0:0.###} {fc.B / 255.0:0.###} rg")));
        bg.cellAsc = (CellFm(fm, bg.mc).asc) * bg.cellFs;
        foreach (var ln in bg.mc.Lines)
        {
            if (!RenderGridCellLine(bg, mps, bw, fm, p, pageHeight, invc, stdSerif, ln)) break;
        }
        // A cell whose content is stacked <div>/<p> boxes draws them band by
        // band, each in its own typography — the row already measured their
        // height (MeasureMetricCell), and without this draw the bordered grid
        // sized the cell for content it never painted (a bordered td holding a
        // div came out an empty box).
        if (bg.mc.DivSegs is { Count: > 0 } dsegs)
            RenderGridCellBands(bg, mps, bw, face, boldFace, fm, p, pageHeight, invc, stdSerif, dsegs);
        if (bg.cellInk is not null)
            bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
        // nested grids render inside the cell, stacked below its
        // own lines — the row then covers their real drawn extent
        if (bg.mc.SubTables is { Count: > 0 })
        {
            var subInset = mps.attrCollapse ? 0 : bw + p;
            // The nested grids flow on the border page from below the cell's lines,
            // at a position of their own: they share the page (a grid that paginates
            // moves the border page on) but not the row's y.
            var sub = new FlowPosition { page = bg.borderPage, y = pageHeight - bg.lineTopTd };
            foreach (var subTable in bg.mc.SubTables)
                RenderMetricTable(doc, sub, subTable, css,
                    bg.colXB + subInset, bg.boxW - 2 * bw - 2 * p, pageWidth,
                    pageHeight, marginTop, marginBottom, face, fm,
                    docFontDict, stdSerif, baseFontSize,
                    wrapperStacks: true, symInsetPt: 0,
                    loadOptions: loadOptions, hostCellPadPt: p);
            bg.borderPage = sub.page;
            bg.rowSubBotTd = Math.Max(bg.rowSubBotTd, pageHeight - sub.y);
        }
    }

    /// <summary>The stacked div/paragraph bands of a grid cell, each in its own
    /// typography and colour, seated from the cell's content top downward. Mirrors
    /// the plain metric painter's band draw in the bordered grid's coordinates.</summary>
    private static void RenderGridCellBands(BorderedGridState bg, MetricParseState mps, double bw, string face, string boldFace, (double asc, double sum) fm, double p, double pageHeight, System.Globalization.CultureInfo invc, bool stdSerif, List<MetricDivSeg> dsegs)
    {
        var inset = mps.attrCollapse ? 0 : bw;
        var contentLeft = bg.colXB + inset + p + CellPadLeftExtra(bg.mc, p);
        var contentW = bg.boxW - 2 * inset - 2 * p - CellPadLeftExtra(bg.mc, p) - CellPadRightExtra(bg.mc, p);
        var segTopY = pageHeight - bg.lineTopTd;
        var prevMb = 0.0;
        foreach (var sg in dsegs)
        {
            segTopY -= Math.Max(sg.MarginTopPt, prevMb);
            prevMb = sg.MarginBottomPt;
            var sgFs = sg.FontSize ?? mps.fontSize;
            var sgProbe = new MetricCell { Face = sg.Face, Bold = sg.Bold, FontSize = sg.FontSize };
            var sgFace = CellFaceName(face, boldFace, sgProbe);
            var sgRes = ResOf(bg, face, boldFace, stdSerif, sgProbe);
            var sgFmv = CellFm(fm, sgProbe);
            var sgSum0 = sgFmv.sum <= 1.0 ? 1.2 : sgFmv.sum;
            var sgLineH = MetricLineHeight(sgFs, sgSum0);
            var sgWrapW = contentW - sg.PadLeft - bg.mc.BorderLeftW;
            var sgLines = sg.Text.Length == 0 ? System.Array.Empty<string>()
                : MeasuredWordWrap(sg.Text, sgWrapW, sgFace, sgFs);
            if (sg.Bg is { } sgBgC)
            {
                var sgBandH = Math.Max(sg.LineBoxPt, sgLines.Length * sgLineH);
                if (sgBandH > 0)
                    bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
                        $"q {sgBgC.R / 255.0:0.###} {sgBgC.G / 255.0:0.###} {sgBgC.B / 255.0:0.###} rg " +
                        $"{contentLeft:F2} {segTopY - sgBandH:F2} {contentW:F2} {sgBandH:F2} re f Q\n")));
            }
            if (sg.Fore is { } sgc)
                bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
                    $"{sgc.R / 255.0:0.###} {sgc.G / 255.0:0.###} {sgc.B / 255.0:0.###} rg")));
            var segLy = segTopY;
            foreach (var ln in sgLines)
            {
                var sgDrop = MetricBaselineDrop(sgFs, sgLineH, sgFmv);
                var sgLw = MeasureFaceText(sgFace, ln, sgFs);
                var sgLx = bg.mc.Align switch
                {
                    HorizontalAlignment.Right => contentLeft + contentW - sgLw,
                    HorizontalAlignment.Center => contentLeft + (contentW - sgLw) / 2,
                    _ => contentLeft + sg.PadLeft + bg.mc.BorderLeftW,
                };
                if (ln.Length > 0)
                    EmitCellLineRuns(bg.borderPage, sgRes, sgFs, sgLx, segLy - sgDrop, ln, sgFace);
                segLy -= sgLineH;
            }
            if (sg.Fore is not null)
                bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
            var bandH = Math.Max(sg.LineBoxPt, sgLines.Length * sgLineH);
            if (sg.BorderBottom)
                bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invc,
                    $"q 0 0 0 RG 0.75 w {contentLeft:F2} {segTopY - bandH:F2} m {contentLeft + contentW:F2} {segTopY - bandH:F2} l S Q\n")));
            segTopY -= bandH + sg.BorderBottomPt;
        }
    }

    /// <summary>One text line of a grid cell: seated at the cell's baseline, aligned as the cell declares, with its link colour and underline.</summary>
    private static bool RenderGridCellLine(BorderedGridState bg, MetricParseState mps, double bw, (double asc, double sum) fm, double p, double pageHeight, System.Globalization.CultureInfo invc, bool stdSerif, string ln)
    {
        var drop = CellDropOf(mps, stdSerif, fm, bg.mc, bg.cellFs, bg.cellLineH);
        var lw = MeasureFaceText(bg.mFace, ln, bg.cellFs);
        // (the cell's own side padding insets the line beyond the table's)
        var lx = bg.mc.Align switch
        {
            HorizontalAlignment.Right => bg.colXB + bg.boxW - (mps.attrCollapse ? 0 : bw) - p - CellPadRightExtra(bg.mc, p) - lw,
            HorizontalAlignment.Center => bg.colXB + (bg.boxW - lw) / 2,
            _ => bg.colXB + (mps.attrCollapse ? 0 : bw) + p + CellPadLeftExtra(bg.mc, p),
        };
        if (ln.Length > 0)
        {
            // Mixed-size inline spans on the single cell
            // line draw per-segment at their own sizes,
            // sharing the dominant line's baseline seat.
            if (bg.mc.SizedRuns is { Count: > 1 } szr
                && bg.mc.Lines.Length == 1)
            {
                var sx = lx;
                foreach (var (st2, sfs2) in szr)
                {
                    var szFs = sfs2 > 0 ? sfs2 : bg.cellFs;
                    if (st2.AsSpan().Trim().Length > 0)
                        EmitCellLineRuns(bg.borderPage, bg.fontRes, szFs, sx,
                            pageHeight - bg.lineTopTd - drop, st2, bg.mFace);
                    sx += MeasureStyledFaceRun(bg.mFace, st2, szFs);
                }
            }
            else
            EmitCellLineRuns(bg.borderPage, bg.fontRes, bg.cellFs, lx,
                pageHeight - bg.lineTopTd - drop, ln, bg.mFace);
            if (bg.mc.LinkUrl is { } lurl)
            {
                var lBase = bg.lineTopTd + drop;
                if (bg.uaLink)
                    bg.borderPage.AddContentStream(Encoding.ASCII.GetBytes(
                        Compat.Format(invc,
                            $"q 0 0 1 RG {0.1 * bg.cellFs:0.##} w " +
                            $"{lx:F2} {pageHeight - lBase - 0.1 * bg.cellFs:F2} m " +
                            $"{lx + lw:F2} {pageHeight - lBase - 0.1 * bg.cellFs:F2} l S Q\n")));
                bg.borderPage.Annotations.AddLinkAnnotation(
                    new Rectangle(lx, pageHeight - lBase - 0.25 * bg.cellFs,
                        lx + lw, pageHeight - lBase + bg.cellAsc), lurl);
                bg.mc.LinkUrl = null;   // one annotation per cell
            }
        }
        bg.lineTopTd += bg.cellLineH;
        return true;
    }

    /// <summary>The grid's outer frame: at the cell stroke's width it joins the cell strokes; a thick or dashed
    /// frame (border=5, border-style:dashed) is stroked on its own at its width and dash (dashes of two widths,
    /// gaps of one; dots of one and one).</summary>
    private static void EmitGridFrame(BorderedGridState bg, double pageHeight, MetricParseState mps, System.Globalization.CultureInfo invc,
        double bw, double x0, double y0d, double x1, double y1d)
    {
        var fw = mps.frameW > 0 ? mps.frameW : bw;
        if (Math.Abs(fw - bw) < 0.01 && mps.frameDash is null)
        {
            BBox(bg, pageHeight, invc, bw, x0, y0d, x1, y1d);
            EmitGridStrokes(bg, mps, invc, bw);
            return;
        }
        EmitGridStrokes(bg, mps, invc, bw);
        BBox(bg, pageHeight, invc, fw, x0, y0d, x1, y1d);
        var dashOps = mps.frameDash switch
        {
            "dashed" => Compat.Format(invc, $"[{fw * DashedDashWidths:0.##} {fw * DashedGapWidths:0.##}] 0 d "),
            "dotted" => Compat.Format(invc, $"[{fw * DottedDashWidths:0.##} {fw * DottedGapWidths:0.##}] 0 d "),
            _ => "",
        };
        EmitGridStrokes(bg, mps, invc, fw, dashOps);
    }
}
