using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static void FitBorderedColumns(MetricParseState mps, List<List<MetricCell>> rows, double[] colW,
        bool[] colFixed, double[] colPct, double[] colPx, bool[] colPxStyle, int nCols, double availW, double bw,
        double p, double pageWidth, double pageHeight, double marginTop, double marginBottom,
        double tableWpt, double tablePct, double baseFontSize, bool paragraphCells, string tableHtml,
        double s, string face, string boldFace, double symInsetPt, bool tableFills)
    {
        var innerW = availW - 2 * bw;
        if (mps.attrCollapse)
        {
            // Shared borders: a pixel column's box = its content plus the two
            // half-borders it absorbs; the percent columns split what the
            // table box leaves beside those (each taking its declared share
            // and losing its own shared borders). No symmetric inset and no
            // over-full shrink — the declared table keeps its width.
            double pxBoxes = 0;
            for (var c = 0; c < nCols; c++)
                if (colPx[c] > 0)
                {
                    colW[c] = colPx[c];
                    colFixed[c] = true;
                    pxBoxes += colPx[c] + 2 * bw;
                }
            var pctBase = availW - 2 * bw - pxBoxes;
            for (var c = 0; c < nCols; c++)
            {
                if (colFixed[c]) continue;
                if (colPct[c] > 0)
                    colW[c] = colPct[c] / 100.0 * pctBase - 2 * p - 2 * bw;
                else
                    foreach (var r in rows)
                        if (c < r.Count && r[c].Text.Length > 0)
                            colW[c] = Math.Max(colW[c], MeasureFaceText(
                                CellFaceName(face, boldFace, r[c]), r[c].Text.Replace('\u0001', ' '),
                                r[c].FontSize ?? mps.fontSize));
                colFixed[c] = true;
            }
            // Excel-fragment grid with a declared table box: row-1 cells split
            // the declared width ∝ 1/colspan (probed: the colspan-2 lead cell
            // takes one third beside its plain sibling). Style widths are CSS
            // content boxes (+ padding + one shared border); attribute widths
            // are border boxes; the LAST spanned column of each row-1 cell
            // takes the cell's remainder.
            if (mps.wtInlineGrid && tableWpt > 0 && rows.Count > 0 && rows[0].Count > 0)
            {
                double DeclBox(int dc) => colPx[dc]
                    + (colPxStyle[dc] ? 2 * p + (mps.wtBw > 0 ? mps.wtBw : bw) : 0);
                // Weigh only the cells that fit the column count — CloseRow
                // pads short rows with empty slots a spanning cell already
                // covers, and those carry no width share.
                double wSum = 0;
                var covered = 0;
                foreach (var mc0 in rows[0])
                {
                    if (covered >= nCols) break;
                    wSum += 1.0 / Math.Max(1, mc0.ColSpan);
                    covered += Math.Max(1, mc0.ColSpan);
                }
                // When EVERY column declares a width, the declared boxes
                // SCALE proportionally to fill the declared table (the
                // email grid's 118.15 + 429.6 pt columns fill the 708 pt
                // box); otherwise row-1 cells split ∝ 1/colspan and the
                // last spanned column takes each cell's remainder.
                var allDecl = true;
                double declSum = 0;
                for (var dc0 = 0; dc0 < nCols; dc0++)
                {
                    if (colPx[dc0] <= 0) { allDecl = false; break; }
                    declSum += DeclBox(dc0);
                }
                if (allDecl && declSum > 0 && nCols > 1
                    && Math.Abs(declSum - tableWpt) > 1.0)
                {
                    var declScale = tableWpt / declSum;
                    for (var dc0 = 0; dc0 < nCols; dc0++)
                        colW[dc0] = Math.Max(1, DeclBox(dc0) * declScale - 2 * p - 2 * bw);
                }
                else
                {
                    var col0 = 0;
                    foreach (var mc0 in rows[0])
                    {
                        var span0 = Math.Max(1, mc0.ColSpan);
                        if (col0 >= nCols) break;
                        var cellBox = tableWpt * (1.0 / span0) / wSum;
                        var rem = cellBox;
                        for (var k = 0; k < span0 - 1 && col0 + k < nCols; k++)
                        {
                            var b = colPx[col0 + k] > 0 ? DeclBox(col0 + k) : 0;
                            if (b > 0)
                            {
                                colW[col0 + k] = Math.Max(1, b - 2 * p - 2 * bw);
                                rem -= b;
                            }
                        }
                        var last0 = Math.Min(col0 + span0 - 1, nCols - 1);
                        var lastB = colPx[last0] > 0 ? DeclBox(last0) : Math.Max(1, rem);
                        colW[last0] = Math.Max(1, lastB - 2 * p - 2 * bw);
                        col0 += span0;
                    }
                }
            }
        }
        else if (mps.layoutFixed)
        {
            for (var c = 0; c < nCols; c++)
                colW[c] = Math.Max(mps.fontSize, colPct[c] / 100.0 * innerW);
        }
        else
        {
            var chromeB = 2 * bw + (nCols + 1) * s + nCols * (2 * p + 2 * bw);
            var naturalB = new double[nCols];
            for (var c = 0; c < nCols; c++)
            {
                foreach (var r in rows)
                    if (c < r.Count && r[c].Text.Length > 0)
                        naturalB[c] = Math.Max(naturalB[c],
                            MeasureFaceText(r[c].Bold ? boldFace : face, r[c].Text,
                                r[c].FontSize ?? mps.fontSize));
                var share = colPct[c] > 0 ? colPct[c] / 100.0 * innerW - 2 * p - 2 * bw : 0;
                // A pixel width attribute fixes the column (its text wraps at
                // that width instead of widening it) — but a larger declared
                // SHARE still wins (measured: a 50% column beats its 366px
                // content cells).
                if (colPx[c] > 0)
                {
                    // Excel-fragment grid: the width attribute is the BORDER
                    // BOX (probed: 80/95/72 px land as 60/71.25/54 pt boxes,
                    // shared borders inside) — the generic attribute grid
                    // keeps its content-box reading.
                    colW[c] = mps.wtInlineGrid
                        ? Math.Max(1, colPx[c] - 2 * p - 2 * bw)
                        : Math.Max(colPx[c] - 2 * p, share);
                    continue;
                }
                colW[c] = Math.Max(naturalB[c], share);
            }
            // Attribute grid: undeclared columns take what the grid box leaves
            // beside the pixel-fixed ones, floored at their min-content (the
            // widest unbreakable chunk) — an over-long word overflows the box
            // rather than shrinking below it. The grid box is the SYMMETRIC
            // content frame (one UA body margin inside the right content edge
            // too), and the outer border straddles OUTSIDE it — measured: the
            // grid spans 96..499 with its outer border edge at 500.5.
            var availB = availW - symInsetPt;
            var gridChrome = chromeB - 2 * bw;
            if (mps.borderHugs)
            {
                double sumH = 0; foreach (var w in colW) sumH += w;
                if (sumH + gridChrome > availB)
                    for (var c = nCols - 1; c >= 0; c--)
                        if (colPx[c] <= 0 && colW[c] > 0)
                        {
                            double others = 0;
                            for (var o = 0; o < nCols; o++) if (o != c) others += colW[o];
                            double minC = 0;
                            foreach (var r in rows)
                                if (c < r.Count && r[c].Text.Length > 0)
                                    foreach (var seg in DashSegments(r[c].Text.Replace('\u0001', ' ')))
                                    foreach (var seg2 in CjkWordSegments(seg))
                                        minC = Math.Max(minC, MeasureFaceText(
                                            r[c].Bold ? boldFace : face, seg2, r[c].FontSize ?? mps.fontSize));
                            colW[c] = Math.Max(minC, availB - gridChrome - others);
                            break;
                        }
            }
            // Declared shares apply only when they FIT beside the min-contents;
            // an over-constrained set falls back to min-content columns (the
            // 15%-column's long word forces its share past 15, so the 85%
            // partner cannot keep 85 — measured: it takes the REMAINDER).
            // Attribute grids resolved their overflow above — pixel-fixed
            // columns must not fall back to natural widths.
            double sumB = 0; foreach (var w in colW) sumB += w;
            if (sumB + chromeB > availW && !mps.borderHugs)
            {
                Array.Copy(naturalB, colW, nCols);
                sumB = 0; foreach (var w in colW) sumB += w;
            }
            // Natural columns that STILL over-fill the box give the deficit
            // back ∝ their slack (max-content − min-content), floored at
            // min-content, and their text wraps at the solved width — the
            // banked auto-width rule (measured on the aggregate grid:
            // 62.1/79.2/64.1/66.2/58.4/52.1, reproduced within 0.3 pt).
            // An attribute grid reaches here only when its own last-column
            // shrink bottomed out at min-content and the grid still spills.
            var bankAvail = mps.borderHugs ? availB - gridChrome + chromeB : availW;
            sumB = 0; foreach (var w in colW) sumB += w;
            if (sumB + chromeB > bankAvail)
            {
                var minB = new double[nCols];
                for (var c = 0; c < nCols; c++)
                    foreach (var r in rows)
                        if (c < r.Count && r[c].Text.Length > 0 && r[c].ColSpan <= 1)
                            foreach (var wSeg in r[c].Text.Replace('\u0001', ' ')
                                         .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            foreach (var wSeg2 in CjkWordSegments(wSeg))
                                minB[c] = Math.Max(minB[c], MeasureFaceText(
                                    r[c].Bold ? boldFace : face, wSeg2,
                                    r[c].FontSize ?? mps.fontSize));
                double slackSum = 0;
                for (var c = 0; c < nCols; c++) slackSum += Math.Max(0, colW[c] - minB[c]);
                var deficitB = sumB + chromeB - bankAvail;
                if (slackSum > 0)
                    for (var c = 0; c < nCols; c++)
                        colW[c] -= Math.Min(
                            deficitB * Math.Max(0, colW[c] - minB[c]) / slackSum,
                            Math.Max(0, colW[c] - minB[c]));
            }
            if (tableFills)
            {
                var leftoverB = availW - chromeB - sumB;
                if (leftoverB > 0 && nCols > 0) colW[nCols - 1] += leftoverB;
            }
        }
    }

    private static void RenderBorderedGrid(MetricParseState mps, List<List<MetricCell>> rows, double[] colW, int nCols,
        double availW, double s, double bw, double lineH, string face, string boldFace, double hheaSum, (double asc, double sum) fm,
        double p, double pageWidth, double pageHeight, double marginTop, double marginBottom,
        double tableWpt, double tablePct, double baseFontSize, bool paragraphCells, string tableHtml,
        double symInsetPt, bool tableFills, IReadOnlyDictionary<string, Dictionary<string, string>> css, Document doc,
        Core.PdfDictionary docFontDict, HtmlLoadOptions? loadOptions, System.Globalization.CultureInfo invc,
        bool stdSerif, bool wrapperStacks, Color? rmtAnchorColor,
        double marginLeft, double contentWidth, ref double tableX, ref Page page, ref double y)
    {
        // A bordered percent-width attribute grid FILLS its declared share of
        // the content box: the columns scale up proportionally so the outer
        // box lands on pct x avail (probed: width=80% align=center draws its
        // border box at 0.8 of the content width, centred; a grid whose
        // natural box already exceeds the share keeps its hug).
        if (mps.borderHugs && tablePct > 0 && stdSerif && nCols > 0)
        {
            double hugW0 = 2 * bw + (nCols + 1) * s;
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
        var sbB = new StringBuilder();
        void BLine(double x0, double y0d, double x1, double y1d)
            => sbB.Append(string.Create(invc,
                $"{x0:F2} {pageHeight - y0d:F2} m {x1:F2} {pageHeight - y1d:F2} l S "));
        void BBox(double x0, double y0d, double x1, double y1d)
        {
            BLine(x0, y0d + bw / 2, x1, y0d + bw / 2);
            BLine(x0, y1d - bw / 2, x1, y1d - bw / 2);
            BLine(x0 + bw / 2, y0d, x0 + bw / 2, y1d);
            BLine(x1 - bw / 2, y0d, x1 - bw / 2, y1d);
        }
        // WinAnsi Type1 resources for <font face> cells (the Markdown pattern),
        // allocated from F8 up and registered on the page lazily. The bordered
        // branch never paginates, so the page snapshot stays valid throughout.
        var extraRes = new Dictionary<string, string>(StringComparer.Ordinal);
        var borderPage = page;
        string ResOf(MetricCell mc)
        {
            if (mc.Face is null)
                return mc.Bold ? (stdSerif ? "F6" : "F2")
                    : mc.Italic ? (stdSerif ? "F7" : "F3")
                    : (stdSerif ? "F5" : "F1");
            var fn = CellFaceName(face, boldFace, mc);
            if (!extraRes.TryGetValue(fn, out var rn))
            {
                // Skip names the flow's Type0 embeds already claimed in the
                // shared /Font dictionary — landing on one would show the
                // cells' WinAnsi strings through an Identity-H font (every
                // byte pair a glyph id → notdef boxes).
                var fd = (borderPage.Dict.Get("Resources") as Core.PdfDictionary)?
                    .Get("Font") as Core.PdfDictionary;
                var idx = 8 + extraRes.Count;
                while (fd?.Get("F" + idx) is { } takenObj
                       && (takenObj is not Core.PdfDictionary taken
                           || taken.GetName("BaseFont") != fn.Replace(" ", "")))
                    idx++;
                rn = "F" + idx;
                extraRes[fn] = rn;
            }
            EnsureFont(borderPage, fn.Replace(" ", ""), rn);
            return rn;
        }
        var tableTopTd = pageHeight - y;
        var rowTopTd = tableTopTd + bw + s;
        // Margin-free email grid: per-row truth from each row's FIRST cell.
        //   * height:Npt — the row box is the DECLARED height plus the top
        //     pad (bottom pad 0), not the content extent.
        //   * border-width TRB — the row's bottom value is the boundary it
        //     closes with (the header's 2.25 triplet, the body rows' 1pt).
        //   * border-color — a value list carrying the -moz-use-text-color
        //     debris is DEAD (the row strokes black); a clean value
        //     colours the row's lines (the header's orange).
        // Measured: boundary centers 446.22 → 467.645
        // → 495.27 → 522.27 = 1pt top + (19.05+0.75) + 2.25 + (25.25+0.75)
        // + 1 + (25.25+0.75) + 1.
        double[]? rowDeclH = null;
        double[]? rowBotW = null;
        Color?[]? rowStrokeCol = null;
        if (mps.wtInlineGrid && mps.wtPMarginDefaulted)
        {
            rowDeclH = new double[rows.Count];
            rowBotW = new double[rows.Count];
            rowStrokeCol = new Color?[rows.Count];
            var trBlocks = Regex.Matches(tableHtml,
                @"<tr\b[\s\S]*?(?=<tr\b|</table)", RegexOptions.IgnoreCase);
            for (var tri = 0; tri < rows.Count; tri++)
            {
                rowBotW[tri] = mps.wtBw > 0 ? mps.wtBw : bw;
                if (tri >= trBlocks.Count) continue;
                var trBlock = trBlocks[tri].Value;
                var hm = Regex.Match(trBlock, @"height\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase);
                if (hm.Success)
                    rowDeclH[tri] = double.Parse(hm.Groups[1].Value,
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
                        rowBotW[tri] = double.Parse(bm.Groups[1].Value,
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
                        rowStrokeCol[tri] = rcCol;
                }
            }
            // rowTopTd runs on BOUNDARY CENTERS for this grid: the top
            // border's center is half a width under the table top.
            rowTopTd = tableTopTd + bw / 2;
        }
        // The inline-style band: its background fills the declared width × height
        // rectangle before any cell ink (probed: 96 118.5 361.5 101.25 re on the
        // reference band, one uniform fill — the cells' own fills ride on top).
        if (mps.tableStyleBg is { } tsBand && mps.tableStyleHPt > 0)
        {
            var bandW = tableWpt > 0 ? tableWpt : contentWidth;
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {tsBand.R / 255.0:0.###} {tsBand.G / 255.0:0.###} {tsBand.B / 255.0:0.###} rg " +
                $"{tableX:F2} {pageHeight - tableTopTd - mps.tableStyleHPt:F2} {bandW:F2} {mps.tableStyleHPt:F2} re f Q\n")));
        }
        var rowIdx = -1;
        foreach (var r in rows)
        {
            rowIdx++;
            // The attribute grid's row box hugs its tallest cell (a size=1
            // header row is a 9px band); the css-bordered mode keeps its
            // calibrated one-line floor.
            double rowContentB = mps.borderHugs ? 0 : lineH;
            foreach (var mc in r) rowContentB = Math.Max(rowContentB, mc.ContentH);
            if (rowContentB <= 0) rowContentB = lineH;
            // collapse shares the borders across the boundary: the row pitch
            // carries no border of its own (measured: 13.5 exactly per strut row)
            var cellBoxH = rowContentB
                + 2 * (mps.wtInlineGrid && mps.wtPadV >= 0 ? mps.wtPadV : p)
                + (mps.attrCollapse ? 0 : 2 * bw);
            // A band row (the table's inline-style height shared to the rows)
            // floors the box; content centres in the grown box below.
            var bandExtra = 0.0;
            if (mps.tableStyleHPt > 0 && rowIdx < mps.rowHeights.Count && mps.rowHeights[rowIdx] > cellBoxH)
            {
                bandExtra = mps.rowHeights[rowIdx] - cellBoxH;
                cellBoxH = mps.rowHeights[rowIdx];
            }
            // Declared-height rows (the email grid): box = the tr's height
            // plus the top pad (bottom pad 0), floored by the content.
            var rowTopHalf = 0.0;
            if (rowDeclH is not null)
            {
                cellBoxH = Math.Max(rowContentB, rowDeclH[rowIdx])
                    + Math.Max(0, mps.wtPadV) + mps.wtPadB;
                rowTopHalf = (rowIdx == 0 ? bw : rowBotW![rowIdx - 1]) / 2;
            }
            var colXB = tableX + bw + s;
            var spanSkip = 0;
            double rowSubBotTd = 0;
            var rowEdgeStrokes = new StringBuilder();
            for (var c = 0; c < nCols; c++)
            {
                var boxW = colW[c] + 2 * p + 2 * bw;
                if (spanSkip > 0)
                {
                    // a phantom slot under a spanning cell: no box of its own,
                    // no advance — the spanning cell already covered it.
                    spanSkip--;
                    continue;
                }
                if (c < r.Count && r[c].ColSpan > 1)
                    for (var k = 1; k < r[c].ColSpan && c + k < nCols; k++)
                    {
                        boxW += s + colW[c + k] + 2 * p + 2 * bw;
                        spanSkip++;
                    }
                // bgcolor cell fill inside the cell border box. The
                // declared-height grid's fill spans boundary center to
                // boundary center (measured: the header band 446.2..467.65,
                // under its own border lines).
                var fillH = rowDeclH is not null
                    ? rowTopHalf + cellBoxH + rowBotW![rowIdx] / 2
                    : cellBoxH;
                var fillW = rowDeclH is not null && tableWpt > 0
                    ? Math.Min(boxW, tableX + tableWpt - colXB)
                    : boxW;
                if (c < r.Count && r[c].Bg is { } cbg)
                    page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                        $"q {cbg.R / 255.0:0.###} {cbg.G / 255.0:0.###} {cbg.B / 255.0:0.###} rg " +
                        $"{colXB:F2} {pageHeight - rowTopTd - fillH:F2} {fillW:F2} {fillH:F2} re f Q\n")));
                if (rowDeclH is not null)
                {
                    // ONE line per boundary in the row's own ink: the top
                    // border on the first row, the row's bottom under it,
                    // verticals AT the box edges (measured: the
                    // left vertical centers half a width inside the table
                    // edge, the boundary at the shared edge) — the grid
                    // clips at the declared table width.
                    var rsCol = rowStrokeCol?[rowIdx] ?? Color.FromArgb(0, 0, 0);
                    var rbW = rowBotW![rowIdx];
                    var clipR = tableWpt > 0 ? tableX + tableWpt : double.MaxValue;
                    var vTop = rowTopTd - rowTopHalf;
                    var vBot = rowTopTd + rowTopHalf + cellBoxH + rbW;
                    var vLx = colXB - bw / 2;
                    var vRx = Math.Min(colXB + boxW, clipR) - bw / 2;
                    var hRx = Math.Min(colXB + boxW + bw / 2, clipR);
                    void RowLine(double w2, double sx0, double sy0, double sx1, double sy1)
                        => rowEdgeStrokes.Append(string.Create(invc,
                            $"q {rsCol.R / 255.0:0.###} {rsCol.G / 255.0:0.###} {rsCol.B / 255.0:0.###} RG " +
                            $"{w2:0.##} w {sx0:F2} {pageHeight - sy0:F2} m {sx1:F2} {pageHeight - sy1:F2} l S Q\n"));
                    if (rowIdx == 0)
                        RowLine(bw, colXB - bw, rowTopTd, hRx, rowTopTd);
                    RowLine(rbW, colXB - bw, rowTopTd + rowTopHalf + cellBoxH + rbW / 2,
                        hRx, rowTopTd + rowTopHalf + cellBoxH + rbW / 2);
                    RowLine(bw, vLx, vTop, vLx, vBot);
                    RowLine(bw, vRx, vTop, vRx, vBot);
                }
                else
                BBox(colXB, rowTopTd, colXB + boxW, rowTopTd + cellBoxH);
                // a style border-right strokes that one edge in its own colour
                // over the shared grid (the separator-column idiom); emitted
                // after the row's fills so a neighbour's fill can't bury it
                if (c < r.Count && r[c].BorderRightW > 0)
                {
                    var brc = r[c].BorderRightCol;
                    rowEdgeStrokes.Append(string.Create(invc,
                        $"q {brc.R / 255.0:0.###} {brc.G / 255.0:0.###} {brc.B / 255.0:0.###} RG " +
                        $"{r[c].BorderRightW:0.##} w {colXB + boxW:F2} {pageHeight - rowTopTd:F2} m " +
                        $"{colXB + boxW:F2} {pageHeight - rowTopTd - cellBoxH:F2} l S Q\n"));
                }
                if (c < r.Count && (r[c].Lines.Length > 0 || r[c].SubTables is { Count: > 0 }))
                {
                    var mc = r[c];
                    var cellFs = mc.FontSize ?? mps.fontSize;
                    var cFm = CellFm(fm, mc);
                    var cellLineH = CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, mc, cellFs);
                    var mFace = CellFaceName(face, boldFace, mc);
                    var fontRes = ResOf(mc);
                    // Middle vertical alignment (the HTML cell default);
                    // a valign='top' cell seats its first line at the row top.
                    var lineTopTd = rowTopTd
                        // shared borders: content opens half the boundary
                        // below the row's border center
                        + (mps.attrCollapse ? (mps.wtInlineGrid ? bw / 2 : 0) : bw)
                        + (mps.wtInlineGrid && mps.wtPadV >= 0 ? mps.wtPadV : p)
                        + (mc.VAlignTop ? 0 : (rowContentB + bandExtra - mc.ContentH) / 2);
                    // Declared-height rows: a valign=top cell seats at the
                    // content top (pad under the border); an attr-less cell
                    // CENTRES its lines in the declared box (probed: the
                    // body rows' text centers on the 25.25 pt content box).
                    if (rowDeclH is not null)
                        lineTopTd = rowTopTd + rowTopHalf + Math.Max(0, mps.wtPadV)
                            + (mc.VAlignTop ? 0
                                : (cellBoxH - Math.Max(0, mps.wtPadV) - mps.wtPadB
                                   - mc.Lines.Length * cellLineH) / 2);
                    // A link cell with no sheet anchor rule draws the UA link
                    // ink: #0000FF text with an underline one tenth of an em
                    // below each baseline (measured on the grid).
                    var uaLink = mc.LinkUrl is not null && mc.Fore is null
                        && rmtAnchorColor is null;
                    var cellInk = mc.Fore ?? (uaLink ? Color.FromArgb(0, 0, 255) : null);
                    if (cellInk is { } fc)
                        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                            $"{fc.R / 255.0:0.###} {fc.G / 255.0:0.###} {fc.B / 255.0:0.###} rg")));
                    var cellAsc = (CellFm(fm, mc).asc) * cellFs;
                    foreach (var ln in mc.Lines)
                    {
                        var drop = CellDropOf(mps, stdSerif, fm, mc, cellFs, cellLineH);
                        var lw = MeasureFaceText(mFace, ln, cellFs);
                        var lx = mc.Align switch
                        {
                            HorizontalAlignment.Right => colXB + boxW - (mps.attrCollapse ? 0 : bw) - p - lw,
                            HorizontalAlignment.Center => colXB + (boxW - lw) / 2,
                            _ => colXB + (mps.attrCollapse ? 0 : bw) + p,
                        };
                        if (ln.Length > 0)
                        {
                            // Mixed-size inline spans on the single cell
                            // line draw per-segment at their own sizes,
                            // sharing the dominant line's baseline seat.
                            if (mc.SizedRuns is { Count: > 1 } szr
                                && mc.Lines.Length == 1)
                            {
                                var sx = lx;
                                foreach (var (st2, sfs2) in szr)
                                {
                                    var szFs = sfs2 > 0 ? sfs2 : cellFs;
                                    if (st2.AsSpan().Trim().Length > 0)
                                        EmitCellLineRuns(page, fontRes, szFs, sx,
                                            pageHeight - lineTopTd - drop, st2, mFace);
                                    sx += MeasureStyledFaceRun(mFace, st2, szFs);
                                }
                            }
                            else
                            EmitCellLineRuns(page, fontRes, cellFs, lx,
                                pageHeight - lineTopTd - drop, ln, mFace);
                            if (mc.LinkUrl is { } lurl)
                            {
                                var lBase = lineTopTd + drop;
                                if (uaLink)
                                    page.AddContentStream(Encoding.ASCII.GetBytes(
                                        string.Create(invc,
                                            $"q 0 0 1 RG {0.1 * cellFs:0.##} w " +
                                            $"{lx:F2} {pageHeight - lBase - 0.1 * cellFs:F2} m " +
                                            $"{lx + lw:F2} {pageHeight - lBase - 0.1 * cellFs:F2} l S Q\n")));
                                page.Annotations.AddLinkAnnotation(
                                    new Rectangle(lx, pageHeight - lBase - 0.25 * cellFs,
                                        lx + lw, pageHeight - lBase + cellAsc), lurl);
                                mc.LinkUrl = null;   // one annotation per cell
                            }
                        }
                        lineTopTd += cellLineH;
                    }
                    if (cellInk is not null)
                        page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
                    // nested grids render inside the cell, stacked below its
                    // own lines — the row then covers their real drawn extent
                    if (mc.SubTables is { Count: > 0 })
                    {
                        var subInset = mps.attrCollapse ? 0 : bw + p;
                        var subY = pageHeight - lineTopTd;
                        foreach (var sub in mc.SubTables)
                            RenderMetricTable(doc, ref page, ref subY, sub, css,
                                colXB + subInset, boxW - 2 * bw - 2 * p, pageWidth,
                                pageHeight, marginTop, marginBottom, face, fm,
                                docFontDict, stdSerif, baseFontSize,
                                wrapperStacks: true, symInsetPt: 0,
                                loadOptions: loadOptions);
                        rowSubBotTd = Math.Max(rowSubBotTd, pageHeight - subY);
                    }
                }
                colXB += boxW + s;
            }
            if (rowEdgeStrokes.Length > 0)
                page.AddContentStream(Encoding.ASCII.GetBytes(rowEdgeStrokes.ToString()));
            rowTopTd += Math.Max(cellBoxH, rowSubBotTd - rowTopTd) + s
                // Excel-fragment grid: each row boundary advances by its
                // one shared declared border. Declared-height rows advance
                // boundary-center to boundary-center: half the border
                // above, the box, half the row's OWN bottom border.
                + (rowDeclH is not null
                    ? rowTopHalf + rowBotW![rowIdx] / 2
                    : mps.wtInlineGrid ? (mps.wtBw > 0 ? mps.wtBw : bw) : 0);
        }
        // Declared-height grid: rowTopTd ended on the LAST boundary's
        // center — the table ends at that border's bottom edge, and its
        // rows already stroked every edge (no outer frame).
        var tableBottomTd = rowDeclH is not null
            ? rowTopTd + (rowBotW![^1] > 0 ? rowBotW[^1] : bw) / 2
            : rowTopTd + bw;
        // The outer box spans the availW under width:100%; a FIXED grid's
        // chrome pushes its right edge past it.
        var outerW = 2 * bw + (nCols + 1) * s;
        foreach (var w in colW) outerW += w + 2 * p + 2 * bw;
        var outerR = tableX + (mps.borderHugs ? outerW
            : tableFills && !mps.layoutFixed ? availW : Math.Max(availW, outerW));
        if (rowDeclH is null)
            BBox(tableX, tableTopTd, outerR, tableBottomTd);
        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
            $"q {mps.borderColor.R / 255.0:0.###} {mps.borderColor.G / 255.0:0.###} {mps.borderColor.B / 255.0:0.###} RG {bw:0.##} w {sbB}Q\n")));
        y = pageHeight - tableBottomTd;
        return;
    }

}
