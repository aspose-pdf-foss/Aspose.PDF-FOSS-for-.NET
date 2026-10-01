using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The collapsed-border fit: pixel columns fixed at their box, percent columns on the base that remains, the rest measured on their text; an inline grid with a declared table width rescales the declared boxes or shares the width per first-row cell.</summary>
    private static void FitCollapsedColumns(FitBorderedState fb)
    {
        // Shared borders: a pixel column's box = its content plus the two
        // half-borders it absorbs; the percent columns split what the
        // table box leaves beside those (each taking its declared share
        // and losing its own shared borders). No symmetric inset and no
        // over-full shrink — the declared table keeps its width.
        // (a cell that pads its own sides declares a CONTENT width: its pads widen the box -
        // measured on the kit grid: a 148.35 pt column padded 5.4 a side seats its neighbour
        // 160.15 on, and its text still wraps at 148.35)
        double pxBoxes = 0;
        for (var c = 0; c < fb.nCols; c++)
            if (fb.colPx[c] > 0)
            {
                fb.colW[c] = fb.colPx[c] + ColumnSidePads(fb, c);
                fb.colFixed[c] = true;
                pxBoxes += fb.colW[c] + 2 * fb.bw;
            }
        // …unless the cells pad themselves: then each declared width is a CONTENT width, the
        // pads widen its box, and the declared table caps the grid (below).
        // (a WIDTH-LESS grid is capped by the box it sits in the same way: the Word export's
        // five 95.75 pt padded columns over-fill the 403 pt body and come back to 59 each,
        // while the column whose longest word is wider than its declaration grows to it)
        if (!fb.mps.wtInlineGrid) ShrinkPaddedDeclaredColumns(fb, fb.tableWpt > 0 ? fb.tableWpt : fb.availW);
        var pctBase = fb.availW - 2 * fb.bw - pxBoxes;
        for (var c = 0; c < fb.nCols; c++)
        {
            if (fb.colFixed[c]) continue;
            if (fb.colPct[c] > 0)
                fb.colW[c] = fb.colPct[c] / 100.0 * pctBase - 2 * fb.p - 2 * fb.bw;
            else
                foreach (var r in fb.rows)
                    if (c < r.Count && r[c].Text.Length > 0)
                        fb.colW[c] = Math.Max(fb.colW[c], MeasureFaceText(
                            CellFaceName(fb.face, fb.boldFace, r[c]), r[c].Text.Replace('\u0001', ' '),
                            r[c].FontSize ?? fb.mps.fontSize));
            fb.colFixed[c] = true;
        }
        // Excel-fragment grid with a declared table box: row-1 cells split
        // the declared width ∝ 1/colspan (probed: the colspan-2 lead cell
        // takes one third beside its plain sibling). Style widths are CSS
        // content boxes (+ padding + one shared border); attribute widths
        // are border boxes; the LAST spanned column of each row-1 cell
        // takes the cell's remainder.
        FitInlineGridCollapsedColumns(fb);
    }

    /// <summary>A collapsed attribute grid whose every column declares a content width and whose
    /// cells pad their own sides over-fills its declared table box: the box caps the grid, and the
    /// deficit comes back off the columns in proportion to their slack above min-content, floored
    /// there (CSS automatic layout; measured on the kit-lot grid: 84.1/49.05/54.55/77.1/55 pt
    /// content widths padded 5.4 pt a side inside a 319.8 pt table land every column at its widest
    /// word - `Manufacturing` keeps its column wider than declared, `SeCore Group` wraps).</summary>
    private static void ShrinkPaddedDeclaredColumns(FitBorderedState fb, double cap)
    {
        var pad = new double[fb.nCols];
        var anyPad = false;
        for (var c = 0; c < fb.nCols; c++)
        {
            if (fb.colPx[c] <= 0) return;
            pad[c] = ColumnSidePads(fb, c);
            anyPad |= pad[c] > 0;
        }
        if (!anyPad) return;
        // shared borders: one per boundary; a column whose widest word is wider than its
        // declaration stands at that word (its box grows), and the others give the deficit back
        var minB = new double[fb.nCols];
        var boxes = (fb.nCols + 1) * fb.bw;
        double slack = 0;
        for (var c = 0; c < fb.nCols; c++)
        {
            minB[c] = MetricColumnMinContentWithSegs(fb, c) + pad[c];
            fb.colW[c] = Math.Max(fb.colPx[c] + pad[c], minB[c]);
            boxes += fb.colW[c] + 2 * fb.p;
            slack += fb.colW[c] - minB[c];
        }
        var deficit = boxes - cap;
        if (deficit <= UaFace.CssPxToPt) return;
        for (var c = 0; c < fb.nCols; c++)
            fb.colW[c] = Math.Max(minB[c], fb.colW[c]
                - (slack > 0 ? deficit * Math.Max(0, fb.colW[c] - minB[c]) / slack : 0));
    }

    /// <summary>The widest own side padding (left + right, beyond the table's) any plain cell of the column declares.</summary>
    private static double ColumnSidePads(FitBorderedState fb, int c)
    {
        double pad = 0;
        foreach (var r in fb.rows)
            if (c < r.Count && r[c].ColSpan <= 1)
                pad = Math.Max(pad, CellPadLeftExtra(r[c], fb.p) + CellPadRightExtra(r[c], fb.p));
        return pad;
    }

    /// <summary>A column's min-content over its cells' plain text AND their stacked div bands.</summary>
    private static double MetricColumnMinContentWithSegs(FitBorderedState fb, int c)
    {
        var minC = MetricColumnMinContent(fb, c);
        foreach (var r in fb.rows)
        {
            if (c >= r.Count || r[c].ColSpan > 1 || r[c].DivSegs is not { Count: > 0 } segs) continue;
            foreach (var sg in segs)
                foreach (var word in sg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                foreach (var seg in CjkWordSegments(word))
                    minC = Math.Max(minC, MeasureFaceText(
                        sg.Bold || r[c].Bold ? fb.boldFace : sg.Face ?? fb.face, seg,
                        sg.FontSize ?? r[c].FontSize ?? fb.mps.fontSize));
        }
        return minC;
    }

    /// <summary>A column's min-content: the widest single word any of its cells holds.</summary>
    private static double MetricColumnMinContent(FitBorderedState fb, int c)
    {
        double minC = 0;
        foreach (var r in fb.rows)
        {
            if (c >= r.Count || r[c].Text.Length == 0 || r[c].ColSpan > 1) continue;
            // (a UA nowrap cell's min-content is its whole text - measured on the case report's
            // `Service Type:` header, which its column kept whole while the long columns shrank)
            if (fb.stdSerif && r[c].NoWrap)
            {
                minC = Math.Max(minC, MeasureFaceText(CellFaceName(fb.face, fb.boldFace, r[c]),
                    r[c].Text.Replace('\u0001', ' '), r[c].FontSize ?? fb.mps.fontSize));
                continue;
            }
            foreach (var word in fb.stdSerif ? DashSegments(r[c].Text.Replace('\u0001', ' '))
                         : r[c].Text.Replace('\u0001', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries))
            foreach (var seg in CjkWordSegments(word))
                minC = Math.Max(minC, MeasureFaceText(
                    CellFaceName(fb.face, fb.boldFace, r[c]), seg,
                    r[c].FontSize ?? fb.mps.fontSize));
        }
        return minC;
    }

    /// <summary>The separated-border fit: natural and percent shares per column, pixel columns held, a hugging border squeezed onto the last free column, the natural widths restored when the bank overflows, then shrunk in proportion to slack above the word minimum; a filling table takes the leftover on its last column.</summary>
    private static void FitSeparatedColumns(FitBorderedState fb)
    {
        fb.chromeB = 2 * fb.bw + (fb.nCols + 1) * fb.s + fb.nCols * (2 * fb.p + 2 * fb.bw);
        fb.naturalB = new double[fb.nCols];
        MeasureSeparatedColumnBands(fb);
        fb.availB = fb.availW - fb.symInsetPt;
        fb.gridChrome = fb.chromeB - 2 * fb.bw;
        if (fb.mps.borderHugs) ShrinkHuggingGridColumns(fb);
        fb.sumB = 0; foreach (var w in fb.colW) fb.sumB += w;
        if (fb.sumB + fb.chromeB > fb.availW && !fb.mps.borderHugs)
        {
            Array.Copy(fb.naturalB, fb.colW, fb.nCols);
            fb.sumB = 0; foreach (var w in fb.colW) fb.sumB += w;
        }
        fb.bankAvail = fb.mps.borderHugs ? fb.availB - fb.gridChrome + fb.chromeB : fb.availW;
        fb.sumB = 0; foreach (var w in fb.colW) fb.sumB += w;
        if (fb.sumB + fb.chromeB > fb.bankAvail)
        {
            var minB = new double[fb.nCols];
            for (var c = 0; c < fb.nCols; c++) minB[c] = MetricColumnMinContent(fb, c);
            double slackSum = 0;
            for (var c = 0; c < fb.nCols; c++) slackSum += Math.Max(0, fb.colW[c] - minB[c]);
            var deficitB = fb.sumB + fb.chromeB - fb.bankAvail;
            if (slackSum > 0)
                for (var c = 0; c < fb.nCols; c++)
                    fb.colW[c] -= Math.Min(
                        deficitB * Math.Max(0, fb.colW[c] - minB[c]) / slackSum,
                        Math.Max(0, fb.colW[c] - minB[c]));
        }
        // A PERCENT-width grid whose columns leave its box unfilled shares the surplus over
        // its auto columns in proportion to their natural width (CSS automatic layout: a
        // one-column `width=100%` grid gives the column the whole box, so its cell wraps at
        // the box and not at its own max-content - the review report's header bands run
        // on one line, as the reference draws them).
        if (fb.tablePct > 0 && fb.tableWpt <= 0 && fb.nCols > 0)
        {
            fb.sumB = 0; foreach (var w in fb.colW) fb.sumB += w;
            double autoNat = 0;
            for (var c = 0; c < fb.nCols; c++)
                if (fb.colPct[c] <= 0 && fb.colPx[c] <= 0) autoNat += fb.colW[c];
            var pctSurplus = fb.tablePct / 100.0 * fb.availW - fb.chromeB - fb.sumB;
            if (pctSurplus > 0 && autoNat > 0)
                for (var c = 0; c < fb.nCols; c++)
                    if (fb.colPct[c] <= 0 && fb.colPx[c] <= 0) fb.colW[c] += pctSurplus * fb.colW[c] / autoNat;
        }
        if (fb.tableFills)
        {
            var leftoverB = fb.availW - fb.chromeB - fb.sumB;
            if (leftoverB > 0 && fb.nCols > 0) fb.colW[fb.nCols - 1] += leftoverB;
        }
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1")
            Console.Error.WriteLine($"[fitsep] nCols={fb.nCols} fills={fb.tableFills} pct={fb.tablePct} wpt={fb.tableWpt:0.##} availW={fb.availW:0.##} chromeB={fb.chromeB:0.##} sumB={fb.sumB:0.##} natural=[{string.Join(",", Array.ConvertAll(fb.naturalB, w => w.ToString("0.#")))}] w=[{string.Join(",", Array.ConvertAll(fb.colW, w => w.ToString("0.#")))}] hugs={fb.mps.borderHugs} cell0='{(fb.rows.Count > 0 && fb.rows[0].Count > 0 ? (fb.rows[0][0].Text.Length > 30 ? fb.rows[0][0].Text[..30] : fb.rows[0][0].Text) : "")}' bold0={(fb.rows.Count > 0 && fb.rows[0].Count > 0 ? fb.rows[0][0].Bold : false)} face='{fb.face}'");
    }

    /// <summary>A hugging grid over its box: a width-less grid gives the deficit back in proportion to
    /// its columns' slack (CSS automatic layout); a grid with a declared width squeezes it onto the last
    /// free column, floored at that column's min-content (the calibrated statement grids).</summary>
    private static void ShrinkHuggingGridColumns(FitBorderedState fb)
    {
        double sumH = 0; foreach (var w in fb.colW) sumH += w;
        if (sumH + fb.gridChrome > fb.availB)
        {
            var minH = new double[fb.nCols];
            double slackH = 0;
            for (var c = 0; c < fb.nCols; c++)
            {
                if (fb.colPx[c] > 0 || fb.colW[c] <= 0) continue;
                foreach (var r in fb.rows)
                    if (c < r.Count && r[c].Text.Length > 0)
                        foreach (var seg in DashSegments(r[c].Text.Replace('\u0001', ' ')))
                        foreach (var seg2 in CjkWordSegments(seg))
                            minH[c] = Math.Max(minH[c], MeasureFaceText(
                                r[c].Bold ? fb.boldFace : fb.face, seg2, r[c].FontSize ?? fb.mps.fontSize));
                slackH += Math.Max(0, fb.colW[c] - minH[c]);
            }
            var deficitH = sumH + fb.gridChrome - fb.availB;
            if (fb.tablePct <= 0 && fb.tableWpt <= 0 && slackH > 0)
            {
                // A WIDTH-LESS hugging grid over its box gives the deficit back over its free
                // columns in proportion to their slack above min-content (CSS automatic layout;
                // measured on the surgery-lights grids: both tables solve 232.2/168 from 330/222
                // and 835/541 max-contents), floored at the widest unbreakable word.
                for (var c = 0; c < fb.nCols; c++)
                    if (fb.colPx[c] <= 0 && fb.colW[c] > 0)
                        fb.colW[c] -= Math.Min(deficitH * Math.Max(0, fb.colW[c] - minH[c]) / slackH,
                            Math.Max(0, fb.colW[c] - minH[c]));
            }
            else
            {
                // A grid with a DECLARED width squeezes the deficit onto its last free column,
                // floored at that column's min-content (the calibrated statement grids).
                for (var c = fb.nCols - 1; c >= 0; c--)
                    if (fb.colPx[c] <= 0 && fb.colW[c] > 0)
                    {
                        double others = 0;
                        for (var o = 0; o < fb.nCols; o++) if (o != c) others += fb.colW[o];
                        fb.colW[c] = Math.Max(minH[c], fb.availB - fb.gridChrome - others);
                        break;
                    }
            }
        }
    }
}
