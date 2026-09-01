using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static (int nCols, double[] colW, double availW, double tableX, double hheaSum, System.Globalization.CultureInfo invc) SolveMetricColumns(
        MetricParseState mps, ref List<List<MetricCell>> rows, StringBuilder text, IReadOnlyDictionary<string, Dictionary<string, string>> css,
        string face, string boldFace, double fmSum, double lineH, double s, ref double p, double bw, double symInsetPt,
        bool tableFills, bool reportCells, bool stdSerif, bool wrapperStacks, bool paragraphCells, bool serifReportCells,
        double collapseBoxW, bool elemCollapseGrid, bool tableRuleFace, double indent, double tablePct, double tableWpt,
        double baseFontSize, double marginLeft, double marginTop, double marginBottom, double pageWidth, double pageHeight,
        double contentWidth, bool rtl, HtmlLoadOptions? loadOptions, string tableHtml, (double asc, double sum) fm)
    {
        var rowsIn = rows;

    // Reorder row groups: thead, then tbody, then tfoot — each group keeping
    // its source order (a tfoot authored before the tbody still closes the
    // table; rowHeights travels with its row).
    if (mps.rowSections.Contains(0) || mps.rowSections.Contains(2))
    {
        var order = Enumerable.Range(0, rows.Count)
            .OrderBy(i => mps.rowSections[i]).ToArray();
        rows = order.Select(i => rowsIn[i]).ToList();
        mps.rowHeights = order.Select(i => mps.rowHeights[i]).ToList();
        mps.rowHeightExact = order.Select(i => mps.rowHeightExact[i]).ToList();
    }

    // A table HEIGHT attribute scales the declared row heights up
    // proportionally to fill it (probed: 19/69/22 px rows in a height=147
    // table land at 25.39/92.21/29.40 px).
    if (mps.tableHeightPt > 0)
    {
        double declSum = 0;
        foreach (var rh in mps.rowHeights) declSum += rh;
        if (declSum > 0 && declSum < mps.tableHeightPt)
            for (var ri = 0; ri < mps.rowHeights.Count; ri++)
                mps.rowHeights[ri] += (mps.tableHeightPt - declSum) * mps.rowHeights[ri] / declSum;
    }

    // An inline-style table height with NO declared row heights: the rows share
    // the band equally, spacing between them kept (probed: the 135px single-row
    // band's cell box is 101.25 − 2 × 1.5 = 98.25 pt, content centred in it).
    if (mps.tableStyleHPt > 0 && rows.Count > 0)
    {
        double rhSum = 0;
        foreach (var rh in mps.rowHeights) rhSum += rh;
        if (rhSum <= 0)
        {
            var share = (mps.tableStyleHPt - (rows.Count + 1) * s) / rows.Count;
            if (share > 0)
            {
                while (mps.rowHeights.Count < rows.Count) { mps.rowHeights.Add(0); mps.rowHeightExact.Add(false); }
                for (var ri = 0; ri < rows.Count; ri++)
                    mps.rowHeights[ri] = Math.Max(mps.rowHeights[ri], share);
            }
        }
    }

    // colspan: a spanning cell keeps its own column and occupies phantom
    // empty slots after it, so per-column index arithmetic stays intact;
    // the wrap and draw passes extend the real cell's box over its phantoms.
    foreach (var r0 in rows)
        for (var i0 = 0; i0 < r0.Count; i0++)
            if (r0[i0].ColSpan > 1)
                for (var k0 = 1; k0 < r0[i0].ColSpan; k0++)
                    r0.Insert(i0 + k0, new MetricCell { Text = "", Phantom = true });
    var nCols = 0;
    foreach (var r in rows) nCols = Math.Max(nCols, r.Count);
    // RTL document: cells fill columns from the RIGHT — mirror every row
    // onto the LTR grid (pad the visual-left slots, reverse, and move each
    // spanning cell back AHEAD of its phantom slots so the LTR draw loop's
    // spanner-then-phantoms convention holds).
    if (rtl)
        foreach (var rr in rows)
        {
            while (rr.Count < nCols) rr.Add(new MetricCell { Text = "", Phantom = true });
            rr.Reverse();
            for (var i = 0; i < rr.Count; i++)
                if (rr[i].ColSpan > 1)
                {
                    var lead = i;
                    var k = rr[i].ColSpan - 1;
                    while (k-- > 0 && lead > 0 && rr[lead - 1].Phantom) lead--;
                    if (lead < i)
                    {
                        var spanner = rr[i];
                        rr.RemoveAt(i);
                        rr.Insert(lead, spanner);
                    }
                }
        }
    var tableX = marginLeft + indent;
    var availW = contentWidth - indent;

    // Column content widths: an inline-table span fixes the column; a width="%"
    // attribute takes its share of the table box; otherwise the widest measured
    // cell line. Over-wide natural columns are clamped from the right.
    // The Excel-fragment grid's cells style their own padding — it wins
    // over whatever the cellpadding attribute set during the parse.
    if (mps.wtInlineGrid && mps.wtPadH >= 0) p = mps.wtPadH;
    var colW = new double[nCols];
    var colPct = new double[nCols];
    var colPx = new double[nCols];
    var colPxStyle = new bool[nCols];
    var colFixed = new bool[nCols];
    foreach (var r in rows)
        for (var c = 0; c < r.Count; c++)
        {
            if (r[c].SpanW > 0 && r[c].SpanW > (colFixed[c] ? colW[c] : 0))
            { colW[c] = r[c].SpanW; colFixed[c] = true; }
            var cSpan = Math.Max(1, r[c].ColSpan);
            // RTL attribute grids and the pt-report mode: a SPANNING cell's
            // declared width never pins the slots it crosses — the
            // non-spanning cells' declared widths fix their columns and the
            // spanner rides over them (measured: the 600px colspan cell
            // lands at 561.75 − the 19/98/91 px columns; the report's
            // width=84% colspan=3 cell must not widen its middle columns).
            if ((rtl || (!stdSerif && wrapperStacks)) && cSpan > 1) continue;
            for (var k = 0; k < cSpan && c + k < nCols; k++)
            {
                if (r[c].WidthPct > 0)
                    colPct[c + k] = Math.Max(colPct[c + k], r[c].WidthPct / cSpan);
                if (r[c].WidthPx > 0)
                {
                    colPx[c + k] = Math.Max(colPx[c + k], r[c].WidthPx / cSpan);
                    if (r[c].WidthPxStyle) colPxStyle[c + k] = true;
                }
            }
        }
    // Bordered mode: CSS table column resolution against the availW box.
    // FIXED layout: each column takes its declared percent of the table's
    // inner width (inside the outer border) — content neither wraps nor
    // widens it, so a long word OVERFLOWS across the neighbour (and the
    // table's chrome pushes its box past the declared width). AUTO layout:
    // a column is max(declared share, min-content) and — under width:100% —
    // the leftover goes to the LAST column (all measured).
    if (mps.bordered) { FitBorderedColumns(mps, rows, colW, colFixed, colPct, colPx, colPxStyle, nCols, availW, bw, p, pageWidth, pageHeight, marginTop, marginBottom, tableWpt, tablePct, baseFontSize, paragraphCells, tableHtml, s, face, boldFace, symInsetPt, tableFills); }
    // Outer-frame collapse grid: every column box (content + 2·padding)
    // shares the symmetric grid box minus the two half-frames; an
    // over-declared set gives its deficit back ∝ slack (declared −
    // min-content), floored at min-content — the banked auto-width rule.
    if (collapseBoxW > 0 && nCols > 0)
    {
        var cbAvail = availW - symInsetPt - collapseBoxW;
        var cbDeclBox = new double[nCols];
        var cbMinBox = new double[nCols];
        for (var c = 0; c < nCols; c++)
        {
            double minC = 0;
            foreach (var r in rows)
                if (c < r.Count && r[c].ColSpan <= 1 && r[c].Text.Length > 0)
                    foreach (var word in r[c].Text.Replace('\u0001', ' ')
                                 .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        minC = Math.Max(minC, MeasureFaceText(
                            CellFaceName(face, boldFace, r[c]), word, r[c].FontSize ?? mps.fontSize));
            cbMinBox[c] = minC + 2 * p;
            cbDeclBox[c] = colPx[c] > 0 ? colPx[c] + 2 * p : cbMinBox[c];
        }
        double cbSumDecl = 0, cbSumSlack = 0;
        for (var c = 0; c < nCols; c++)
        {
            cbSumDecl += cbDeclBox[c];
            cbSumSlack += Math.Max(0, cbDeclBox[c] - cbMinBox[c]);
        }
        var cbDeficit = cbSumDecl - cbAvail;
        for (var c = 0; c < nCols; c++)
        {
            var box = cbDeclBox[c];
            if (cbDeficit > 0 && cbSumSlack > 0)
                box = Math.Max(cbMinBox[c], cbDeclBox[c]
                    - cbDeficit * Math.Max(0, cbDeclBox[c] - cbMinBox[c]) / cbSumSlack);
            colW[c] = box - 2 * p;
            colFixed[c] = true;
        }
    }
    var usableW = availW - (nCols + 1) * s;
    // stdSerif percent grid: browser auto layout, measured —
    // declared share of the SYMMETRIC usable box (one UA body margin inside
    // the right content edge too), w = max(declared, min-content); an
    // over-full set gives its deficit back proportionally to each column's
    // SLACK (w − min-content). Non-percent columns ride along at their
    // natural width with zero slack.
    var uaPctGrid = false;
    if (stdSerif && !mps.bordered)
        foreach (var pc in colPct) if (pc > 0) { uaPctGrid = true; break; }
    if (uaPctGrid)
    {
        var usableSym = availW - symInsetPt - (nCols + 1) * s;
        var minCol = new double[nCols];
        for (var c = 0; c < nCols; c++)
        {
            foreach (var r in rows)
            {
                if (c < r.Count && r[c].SubTables is { Count: > 0 } pctSubs)
                    foreach (var sub in pctSubs)
                        foreach (var seg in DashSegments(CollapseWs(DecodeEntities(
                            Regex.Replace(sub, "<[^>]+>", " ")))))
                            minCol[c] = Math.Max(minCol[c], MeasureFaceText(
                                CellFaceName(face, boldFace, r[c]), seg, r[c].FontSize ?? mps.fontSize) + 2 * p);
                if (c < r.Count && r[c].Text.Length > 0)
                {
                    // a NOWRAP cell's min-content is its WHOLE text
                    if (r[c].NoWrap)
                    {
                        minCol[c] = Math.Max(minCol[c], MeasureFaceText(
                            CellFaceName(face, boldFace, r[c]), r[c].Text.Replace('\u0001', ' '),
                            r[c].FontSize ?? mps.fontSize));
                        continue;
                    }
                    foreach (var seg in DashSegments(r[c].Text.Replace('\u0001', ' ')))
                        minCol[c] = Math.Max(minCol[c], MeasureFaceText(
                            CellFaceName(face, boldFace, r[c]), seg, r[c].FontSize ?? mps.fontSize));
                }
            }
            if (colFixed[c]) { minCol[c] = colW[c]; continue; }
            var decl = colPct[c] > 0 ? colPct[c] / 100.0 * usableSym - 2 * p : minCol[c];
            colW[c] = Math.Max(decl, minCol[c]);
            colFixed[c] = true;
        }
        double sumCol = nCols * 2 * p;
        foreach (var w in colW) sumCol += w;
        var deficit = sumCol - usableSym;
        if (deficit > 0)
        {
            double slackSum = 0;
            for (var c = 0; c < nCols; c++) slackSum += Math.Max(0, colW[c] - minCol[c]);
            if (slackSum > 0)
                for (var c = 0; c < nCols; c++)
                    colW[c] -= deficit * Math.Max(0, colW[c] - minCol[c]) / slackSum;
        }
        // …and a width:100% table's UNDECLARED columns absorb the surplus,
        // proportionally to their content (the label grid's value column
        // takes everything the 20% label leaves).
        else if (deficit < 0 && tablePct > 0)
        {
            var surplus = -deficit;
            for (var c = nCols - 1; c >= 0; c--)
                if (colPct[c] <= 0)
                {
                    colW[c] += surplus;
                    break;
                }
        }
    }
    for (var c = 0; c < nCols && !mps.bordered; c++)
    {
        if (colFixed[c]) continue;
        if (colPct[c] > 0) { colW[c] = colPct[c] / 100.0 * usableW - 2 * p; colFixed[c] = true; continue; }
        // Modern-nesting model: a width attribute or class fixes its column —
        // the nested grids and class-framework grids wrap at their declared
        // cols instead of their natural text extents.
        if (wrapperStacks && (tablePct == 0 && !tableFills || !stdSerif)
            && colPx[c] > 0)
        { colW[c] = colPx[c]; colFixed[c] = true; continue; }
        foreach (var r in rows)
        {
            // a SPANNING cell stretches over several columns and must
            // not pin its first one to its whole width; an alt-text cell
            // sizes to its image BOX, not to the alt's unwrapped advance
            if (c < r.Count && r[c].Text.Length > 0 && r[c].ColSpan <= 1
                && !(r[c].AltTextOnly && r[c].ImgWPt > 0))
                foreach (var brSeg in r[c].Text.Split('\u0001'))
                    colW[c] = Math.Max(colW[c], MeasureFaceText(r[c].Bold ? boldFace : face,
                        brSeg.Trim(), r[c].FontSize ?? mps.fontSize)
                        // a class padding-left is part of the cell's box —
                        // the wrap pass subtracts it back out
                        + (r[c].PadLeft > 0 ? r[c].PadLeft : 0));
            // a report cell's declared IMAGE box is content width too — the
            // logo column sizes to its 210px box, not to its alt text
            if (paragraphCells && c < r.Count && r[c].ColSpan <= 1 && r[c].ImgWPt > 0)
                colW[c] = Math.Max(colW[c], r[c].ImgWPt);
            // Div-stacked cell content sizes its column the same way — each
            // segment's unwrapped advance is the cell's max-content.
            if (c < r.Count && r[c].ColSpan <= 1 && r[c].DivSegs is { Count: > 0 } wSegs)
                foreach (var wSeg in wSegs)
                    if (wSeg.Text.Trim().Length > 0)
                        colW[c] = Math.Max(colW[c], MeasureFaceText(
                            wSeg.Bold || r[c].Bold ? boldFace : wSeg.Face ?? face,
                            wSeg.Text.Trim(), wSeg.FontSize ?? r[c].FontSize ?? mps.fontSize));
            // a cell whose content is a nested grid sizes for it: the
            // browser gives the container the sub-table's max-content,
            // capped by the available box (a width:100% sub then fills it)
            if (c < r.Count && r[c].ColSpan <= 1 && r[c].SubTables is { Count: > 0 } natSubs)
            {
                double subMax = 0;
                foreach (var sub in natSubs)
                {
                    var subText = CollapseWs(DecodeEntities(
                        Regex.Replace(sub, "<[^>]+>", " "))).Trim();
                    if (subText.Length > 0)
                        subMax = Math.Max(subMax, MeasureFaceText(face, subText,
                            r[c].FontSize ?? mps.fontSize));
                }
                if (subMax > 0)
                    colW[c] = Math.Max(colW[c], Math.Min(subMax, usableW - 2 * p));
            }
        }
    }
    // A pixel table width the grid fills exactly: the surplus over the natural
    // columns distributes proportionally to each column's content width
    // (auto-layout distribution — the measured 285/305.2 boxes).
    if (!mps.bordered && tableWpt > 0)
    {
        double natSum = 0;
        for (var c = 0; c < nCols; c++) if (!colFixed[c]) natSum += colW[c];
        var fixedSum = (nCols + 1) * s + nCols * 2 * p;
        for (var c = 0; c < nCols; c++) if (colFixed[c]) fixedSum += colW[c];
        var surplus = tableWpt - fixedSum - natSum;
        if (surplus > 0 && natSum > 0)
        {
            for (var c = 0; c < nCols; c++)
                if (!colFixed[c]) colW[c] += surplus * colW[c] / natSum;
        }
        // an all-declared grid splits the surplus EQUALLY (measured on the
        // boleto: 666px over five declared cols lands +5.25 pt on each)…
        else if (surplus > 0 && nCols > 0)
        {
            // …but the RTL attr grid gives the remainder to the SPANNING
            // cell's open slots — its declared px columns keep their widths
            // exactly (measured: 561.75 − 19/98/91px = the 405.75 span box).
            var rtlOpen = 0;
            if (rtl) for (var c = 0; c < nCols; c++) if (!colFixed[c]) rtlOpen++;
            if (rtl && rtlOpen > 0)
                for (var c = 0; c < nCols; c++)
                { if (!colFixed[c]) colW[c] += surplus / rtlOpen; }
            else
                for (var c = 0; c < nCols; c++) colW[c] += surplus / nCols;
        }
    }
    // A declared percent width scales the column grid UP to fill its share of
    // the content box — the extra width distributes proportionally to each
    // column's content width (browser auto-layout distribution). A BORDERED
    // grid already resolved its box against the avail (banked shrink /
    // hug) — re-inflating it here spills the border past the content edge.
    if ((stdSerif || wrapperStacks) && tablePct > 0 && !uaPctGrid && !mps.bordered)
    {
        var targetContent = availW * tablePct / 100.0 - (nCols + 1) * s - nCols * 2 * p;
        double sumW = 0; foreach (var w in colW) sumW += w;
        if (sumW > 0 && sumW < targetContent)
            for (var c = 0; c < nCols; c++) colW[c] *= targetContent / sumW;
    }
    // width:100% from the sheet's table rule: the column grid fills the
    // content box — the leftover joins the last column (a centered single
    // cell then centers across the sheet, as the corpus letter's title).
    if (!mps.bordered && tableFills)
    {
        // the element-rule collapse grid fills the SYMMETRIC frame, its
        // shared borders inside it (measured: cols sum to 400 in the 403
        // box — 96..499 on the 409 band)
        var tfAvail = elemCollapseGrid
            ? availW - symInsetPt - 2 * 0.75 : availW;
        double sumW0 = (nCols + 1) * s;
        foreach (var w in colW) sumW0 += w + 2 * p;
        if (sumW0 < tfAvail && nCols > 0)
        {
            // pt-report grids spread the width:100% surplus over the auto
            // columns ∝ their content width (probed: the monitoring row's
            // 86/96 pt columns land at ~186/207, the nbsp spacers at ~5);
            // the UA-serif letter sheets keep their last-column stretch.
            double ptNatS = 0;
            if ((!stdSerif && wrapperStacks) || (stdSerif && symInsetPt > 0))
                for (var c = 0; c < nCols; c++)
                    if (!colFixed[c]) ptNatS += colW[c];
            // …and the UA-flow report grids at the SYMMETRIC inset spread it
            // the same way (the order ticket's four label columns); only the
            // edge-to-edge letter sheets keep their calibrated last-column
            // stretch (title centering).
            if (ptNatS > 0
                && ((!stdSerif && wrapperStacks) || (stdSerif && symInsetPt > 0)))
            {
                var ptSur = tfAvail - sumW0;
                for (var c = 0; c < nCols; c++)
                    if (!colFixed[c]) colW[c] += ptSur * colW[c] / ptNatS;
            }
            else
            {
                colW[nCols - 1] += tfAvail - sumW0;
                colFixed[nCols - 1] = true;
            }
        }
    }

    // Clamp: shrink the right-most non-fixed column into the remaining width.
    var total = (nCols + 1) * s;
    foreach (var w in colW) total += w + 2 * p;
    if (total > availW && !mps.bordered)
    {
        // SEVERAL over-full auto columns distribute like the browser: each
        // keeps its min-content (longest word) and the remaining width goes
        // out proportionally to the max-content EXCESS over that floor
        // (probed on the three-paragraph grid: 207/155/44 of a 409 box).
        var autoCols = 0;
        for (var c = 0; c < nCols; c++)
            if (!colFixed[c] && colW[c] > 0) autoCols++;
        if (autoCols > 1)
        {
            var minW = new double[nCols];
            for (var c = 0; c < nCols; c++)
            {
                if (colFixed[c] || colW[c] <= 0) continue;
                foreach (var r in rows)
                {
                    if (c >= r.Count || r[c].ColSpan > 1) continue;
                    var mcFs = r[c].FontSize ?? mps.fontSize;
                    foreach (var word in r[c].Text.Split(
                        new[] { ' ', '\u0001' }, StringSplitOptions.RemoveEmptyEntries))
                        minW[c] = Math.Max(minW[c], MeasureFaceText(
                            r[c].Bold ? boldFace : face, word, mcFs)
                            + (r[c].PadLeft > 0 ? r[c].PadLeft : 0));
                    if (r[c].DivSegs is { Count: > 0 } mSegs)
                        foreach (var mSeg in mSegs)
                            foreach (var word in mSeg.Text.Split(' ',
                                StringSplitOptions.RemoveEmptyEntries))
                                minW[c] = Math.Max(minW[c], MeasureFaceText(
                                    mSeg.Bold || r[c].Bold ? boldFace : mSeg.Face ?? face,
                                    word, mSeg.FontSize ?? mcFs));
                }
            }
            // A class PERCENT column pins at max(its share, min-content) in
            // an over-constrained table (probed: the worksheet's 10% label
            // grid wraps one word per line while its 2-column sibling —
            // which FITS — keeps max-content untouched).
            var colClassPct = new double[nCols];
            foreach (var r in rows)
                for (var c = 0; c < Math.Min(r.Count, nCols); c++)
                    if (r[c].ColSpan <= 1 && r[c].ClassWidthPct > 0)
                        colClassPct[c] = Math.Max(colClassPct[c], r[c].ClassWidthPct);
            double fixedSumB = (nCols + 1) * s + nCols * 2 * p, minSum = 0, excessSum = 0;
            for (var c = 0; c < nCols; c++)
            {
                if (colFixed[c] || colW[c] <= 0)
                {
                    if (!colFixed[c] && colClassPct[c] > 0)
                    {
                        // an EMPTY percent column still takes its share
                        colW[c] = colClassPct[c] / 100.0 * availW;
                        colFixed[c] = true;
                    }
                    fixedSumB += colW[c];
                    continue;
                }
                if (colClassPct[c] > 0)
                {
                    colW[c] = Math.Max(colClassPct[c] / 100.0 * availW, minW[c]);
                    colFixed[c] = true;
                    fixedSumB += colW[c];
                    continue;
                }
                minSum += minW[c];
                excessSum += Math.Max(0, colW[c] - minW[c]);
            }
            var room = availW - fixedSumB - minSum;
            if (room > 0 && excessSum > 0)
            {
                for (var c = 0; c < nCols; c++)
                    if (!colFixed[c] && colW[c] > 0)
                        colW[c] = minW[c] + room * Math.Max(0, colW[c] - minW[c]) / excessSum;
            }
            else if (excessSum > 0)
            {
                for (var c = 0; c < nCols; c++)
                    if (!colFixed[c] && colW[c] > 0)
                        colW[c] = Math.Max(mps.fontSize, minW[c]);
            }
        }
        else
        for (var c = nCols - 1; c >= 0; c--)
            if (!colFixed[c])
            {
                var others = (nCols + 1) * s;
                for (var o = 0; o < nCols; o++) if (o != c) others += colW[o] + 2 * p;
                colW[c] = Math.Max(mps.fontSize, availW - others - 2 * p);
                break;
            }
        // still over-full: the declared percents over-fill the box (a
        // nested grid's 99% column beside its labels) — the right-most
        // percent column takes the remainder instead of overflowing
        total = (nCols + 1) * s;
        foreach (var w in colW) total += w + 2 * p;
        if (total > availW)
            for (var c = nCols - 1; c >= 0; c--)
                if (colPct[c] > 0)
                {
                    var others = (nCols + 1) * s;
                    for (var o = 0; o < nCols; o++) if (o != c) others += colW[o] + 2 * p;
                    colW[c] = Math.Max(mps.fontSize, availW - others - 2 * p);
                    break;
                }
    }

    // RTL grid: the (mirrored-LTR) table RIGHT-anchors one right inset
    // inside the page edge — the widest grid's left edge then sits on the
    // 90 pt page margin the RTL page-width model left for it.
    if (rtl)
    {
        var rtlTotal = (nCols + 1) * s;
        foreach (var w in colW) rtlTotal += w + 2 * p;
        tableX = Math.Max(0, pageWidth - RtlGridRightInsetPt - rtlTotal);
    }

    // Wrap cell text and size rows. An inline-table span grows the cell's first
    // line box by 3 pt (22px vs 18px line).
    var invc = System.Globalization.CultureInfo.InvariantCulture;
    // Per-cell face/metrics: a <font face> cell wraps, paces and seats with its
    // own family's win metrics (the flow face otherwise).
    // Font-tag-sized cells pace on the face's HHEA line (the quirks strut
    // model, measured: a size-4 cell's 18px font sits in a 21px line and
    // never under the table base font's own 18px strut); CSS-sized cells
    // keep the calibrated win-metric line.
    var hheaSum = stdSerif ? (HheaLineSumFor(face) ?? fmSum) : fmSum;
    // …and their baselines align on the shared line: the drop is whichever
    // is deeper — the table base font's strut baseline or the cell font's
    // own seat (measured: size-2 rows seat on the 12pt strut's 10.8, the
    // size-4 row on its own 12.43).
    foreach (var r in rows)
        for (var c = 0; c < r.Count; c++)
        {
            var mc = r[c];
            if (mc.Text.Length == 0 && mc.SubTables is not { Count: > 0 }
                && mc.DivSegs is not { Count: > 0 })
            {
                mc.Lines = [];
                mc.ContentH = mc.HrRule
                    ? Math.Max(mc.ImgHPt, CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, mc, mc.FontSize ?? mps.fontSize))
                    : mc.ImgHPt;
                continue;
            }
            if (mc.Text.Length == 0) mc.Lines = [];
            var cellFs = mc.FontSize ?? mps.fontSize;

            // FIXED layout never wraps — the content overflows its column.
            var effW = colW[c];
            for (var k = 1; k < mc.ColSpan && c + k < nCols; k++)
                effW += 2 * p + s + colW[c + k];
            // class padding/border-left eat into the wrap width
            if (mc.PadLeft > 0 || mc.BorderLeftW > 0)
                effW -= (mc.PadLeft > 0 ? mc.PadLeft : 0) + mc.BorderLeftW;
            // div-stacked content: each div is one styled band — its class
            // height floors the band, wrapped lines grow it
            if (mc.DivSegs is { Count: > 0 } dsegs)
            {
                mc.Lines = [];
                // an overflowing image GROWS the cell's content box — the
                // paragraphs below it wrap at the image's width (measured:
                // the report paragraphs break at the 612 pt photo, not the
                // 504 pt column)
                if (mc.ImgBytes is not null && mc.ImgWPt > effW)
                    effW = mc.ImgWPt;
                double dh = 0, prevMb = 0;
                foreach (var sg in dsegs)
                {
                    var sgFs = sg.FontSize ?? mps.fontSize;
                    // newsletter segments pace on the cell line model (hhea);
                    // the calibrated div-seg dialects keep their win metrics
                    double sgLineH;
                    if (paragraphCells)
                        sgLineH = CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, new MetricCell
                            { Face = sg.Face, Bold = sg.Bold, FontSize = sg.FontSize }, sgFs);
                    else
                    {
                        var sgFmv = sg.Face is { } sgf ? (WinMetricsFor(sgf) ?? fm) : fm;
                        var sgSum = sgFmv.sum <= 1.0 ? 1.2 : sgFmv.sum;
                        sgLineH = MetricLineHeight(sgFs, sgSum);
                    }
                    var sgFaceN = sg.Face is { } f2
                        ? f2 + (sg.Bold ? " Bold" : "")
                        : (sg.Bold ? boldFace : face);
                    var nLines = sg.Text.Length == 0 ? 0
                        : MeasuredWordWrap(sg.Text, effW - sg.PadLeft, sgFaceN, sgFs).Length;
                    // paragraph segments carry the UA block margins,
                    // adjacent margins collapsing to the larger one
                    dh += Math.Max(sg.MarginTopPt, prevMb)
                          + Math.Max(sg.LineBoxPt, nLines * sgLineH);
                    prevMb = sg.MarginBottomPt;
                }
                // an intrinsic-aspect JPEG stacks ABOVE the segments — the
                // reserved-box images centre in the band instead
                mc.ContentH = mc.ImgBytes is not null
                    ? dh + mc.ImgHPt : Math.Max(dh, mc.ImgHPt);
                continue;
            }
            // newsletter cells: whitespace GLUE between nested tables
            // (the &nbsp; separators the markup leaves in the container td)
            // holds no line box of its own
            if (paragraphCells && mc.SubTables is { Count: > 0 } && mc.Text.Length > 0)
            {
                var glueWs = true;
                foreach (var ch in mc.Text)
                    if (ch is not (' ' or '\u00A0' or '\u0001')) { glueWs = false; break; }
                if (glueWs) { mc.Text = ""; mc.Lines = []; }
            }
            if (mc.Text.Length > 0)
            mc.Lines = (mps.bordered && mps.layoutFixed) || mc.NoWrap
                ? new[] { mc.Text.Replace('\u0001', ' ') }
                // +0.05: a column sized to its own max-content must not
                // wrap on the equality boundary
                : MeasuredWordWrap(mc.Text, effW + 0.05, CellFaceName(face, boldFace, mc), cellFs);
            mc.ContentH = mc.Lines.Length * CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, mc, cellFs)
                          + (mc.HasSpan ? 3.0 : 0) + mc.PadTopPt
                          // Excel-fragment grid: the in-cell <p>'s margin-bottom
                          // is content height (probed: every row carries it).
                          // The margin-free email grid's rows are sized by
                          // their DECLARED tr heights instead (the bordered
                          // draw applies them) — no margin in the content.
                          + (mps.wtInlineGrid && !mps.wtPMarginDefaulted ? mps.wtPMarginB : 0);
            if (mc.ImgHPt > 0)
                mc.ContentH = mc.ImgBytes is not null
                    ? mc.ContentH + mc.ImgHPt
                    : Math.Max(mc.ContentH, mc.ImgHPt);
            if (mc.SubTables is { Count: > 0 })
                foreach (var sub in mc.SubTables)
                    mc.ContentH += mps.bordered
                        // the bordered draw strokes the row box up front — it
                        // needs the sub-grid's REAL wrapped extent
                        ? NestedTableWrappedHeight(sub, lineH, face, mps.fontSize, effW)
                        : EstimateNestedTableHeight(sub,
                            CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, mc, cellFs) + 2 * p) + s;
        }
        return (nCols, colW, availW, tableX, hheaSum, invc);
    }
}
