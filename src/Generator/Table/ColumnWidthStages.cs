using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
// Column-width helpers: whether an HTML column is fixed.
    private bool HtmlColFixed(ColumnWidthState pw, int i)
        => HtmlColFixedCols is { } fx && i < fx.Length && fx[i];

    /// <summary>Widen the width array to the widest row, fit the columns to the window when asked, and clamp them to the available width.</summary>
    private void FinishColumnWidths(ColumnWidthState pw, double availableWidth, bool clampToAvailable)
    {
        for (var i = 0; i < Rows.Count; i++)
            if (Rows.At(i).Cells.Count > pw.neededCols) pw.neededCols = Rows.At(i).Cells.Count;
        if (pw.neededCols > pw.widths.Length)
        {
            var fill = pw.widths.Length > 0 ? pw.widths[pw.widths.Length - 1] : 100;
            var extended = new double[pw.neededCols];
            Array.Copy(pw.widths, extended, pw.widths.Length);
            for (var i = pw.widths.Length; i < pw.neededCols; i++) extended[i] = fill;
            pw.widths = extended;
        }

        // AutoFitToWindow with declared widths: they are SHARES, scaled to fill the
        // usable band ("8 10" lays out as 356.4/445.6 across the
        // 802 pt content width — the cell text never wraps to the 8 pt literal).
        // The rule is the adjustment mode's, not the XML dialect's: a DOM
        // table declaring "50 50 50" spreads the three
        // columns across the whole 415 pt band.
        if (ColumnAdjustment == ColumnAdjustment.AutoFitToWindow
            && availableWidth > 0 && pw.widths.Length > 0)
        {
            double xmlTotal = 0;
            foreach (var w in pw.widths) xmlTotal += w;
            if (xmlTotal > 0 && Math.Abs(xmlTotal - availableWidth) > 0.01)
                for (var i = 0; i < pw.widths.Length; i++)
                    pw.widths[i] *= availableWidth / xmlTotal;
        }

        // Clamp to the page's usable width: when the (fixed) column widths overflow
        // the content area, shrink them proportionally to fit — matching the
        // generator's shrink-to-window behaviour. Without this a column wider than
        // the page (e.g. ColumnWidths="600" inside a ~415pt content band) pushes the
        // cell content — notably an image fitted to the cell width — off the page's
        // right edge. Skipped when RepeatingColumnsCount > 0, where an over-wide table
        // is sliced across pages (column-pagination) rather than shrunk, and for
        // Broken=VerticalInSamePage, where the overflow columns wrap into bands
        // stacked below on the same page.
        if (clampToAvailable && RepeatingColumnsCount == 0
            && Broken != TableBroken.VerticalInSamePage && Broken != TableBroken.Vertical
            && availableWidth > 0
            // The auto-rule resolution above already fitted (or deliberately
            // overflowed at min floors) — the sequential clamp would squeeze
            // columns BELOW their floors and mid-word-break their headers.
            && HtmlColMinPt is null)
        {
            double total = 0;
            for (var i = 0; i < pw.widths.Length; i++) total += pw.widths[i];
            // An over-wide centred nested grid scales EVERY column alike to its
            // target sum (see FitCentredNestedGrid), not the last ones to the remainder.
            if ((NestedOverwideTarget > 0 || FitColumnsToBand) && total > availableWidth + 1e-3)
            {
                for (var i = 0; i < pw.widths.Length; i++) pw.widths[i] *= availableWidth / total;
                total = availableWidth;
            }
            if (total > availableWidth + 1e-3)
            {
                // The generator keeps the DECLARED widths and squeezes only the
                // columns that no longer fit: each column is clamped to the width
                // remaining after its predecessors (a "25 17 390 90" table in a
                // 468 pt band keeps 25/17/390 and gives the last column the
                // 36 pt remainder).
                double cum = 0;
                for (var i = 0; i < pw.widths.Length; i++)
                {
                    var wRemain = Math.Max(0, availableWidth - cum);
                    if (pw.widths[i] > wRemain) pw.widths[i] = wRemain;
                    cum += pw.widths[i];
                }
            }
        }
    }

    /// <summary>The width the declared percent columns still want after the surplus, taken from the
    /// auto columns in proportion to their slack above min-content and handed to the declared ones in
    /// proportion to their want.</summary>
    private static void YieldAutoColumnsToDeclared(ColumnWidthState pw, double[] hMins, double[] hMaxs)
    {
        double want = 0, slack = 0;
        for (var i = 0; i < pw.widths.Length; i++)
        {
            if (pw.declShare[i] > 0) want += Math.Max(0, pw.declShare[i] - pw.widths[i]);
            else slack += Math.Max(0, pw.widths[i] - hMins[i]);
        }
        if (want <= 0.01 || slack <= 0.01) return;
        var move = Math.Min(want, slack);
        for (var i = 0; i < pw.widths.Length; i++)
        {
            if (pw.declShare[i] > 0) pw.widths[i] += move * Math.Max(0, pw.declShare[i] - pw.widths[i]) / want;
            else pw.widths[i] -= move * Math.Max(0, pw.widths[i] - hMins[i]) / slack;
        }
    }

    /// <summary>An HTML table with per-column minimum widths: the declared shares, fixed columns and content minimums resolved against the available width.</summary>
    private void FitHtmlColumnWidths(ColumnWidthState pw, double availableWidth)
    {
        // A grid whose sheet states its cells' box wears its own frame OUTSIDE its columns: what the
        // columns share is the box less that frame, and handing them the frame as well hands every
        // column a share of width the grid does not have.
        if (HtmlCellBoxSheet && Border is { } ownFrame && availableWidth > 0)
        {
            if ((ownFrame.Side & BorderSide.Left) != 0) availableWidth -= ownFrame.Width;
            if ((ownFrame.Side & BorderSide.Right) != 0) availableWidth -= ownFrame.Width;
        }
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_TABLEW") == "1")
            Console.Error.WriteLine($"[tablew] avail={availableWidth:0.##} mins={(HtmlColMinPt is null ? "-" : string.Join(",", HtmlColMinPt.Select(v => v.ToString("0.#"))))} maxs={(HtmlColMaxPt is null ? "-" : string.Join(",", HtmlColMaxPt.Select(v => v.ToString("0.#"))))} widths={string.Join(",", pw.widths.Select(v => v.ToString("0.#")))} surplusCol={HtmlSurplusCol}");
        if (HtmlColMinPt is { } hPct && hPct.Length == pw.widths.Length && availableWidth > 0
            && FitDeclaredPercentColumns(pw, availableWidth, hPct)) return;
        FitHtmlColumnMinima(pw, availableWidth);
    }

    /// <summary>A grid whose sheet states its cells' box shares the box out by what its columns ASK for:
    /// a DECLARED PERCENT is the column's want, a column with none wants no more than its floor, and a
    /// floor already past its percent asks for nothing. What the box has over the floors goes out in
    /// proportion to what is still wanted - so a column never takes its widest content just because the
    /// box happens to hold it, and a grid squeezed to its floors keeps them. False when this grid
    /// declares no percents and the calibrated max-content rule below stands.</summary>
    private bool FitDeclaredPercentColumns(ColumnWidthState pw, double availableWidth, double[] hMins)
    {
        if (HtmlColumnPercents is not { Length: > 0 } pctText) return false;
        var parts = pctText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != pw.widths.Length) return false;
        double floorSum = 0, wantGap = 0;
        var gaps = new double[pw.widths.Length];
        for (var i = 0; i < pw.widths.Length; i++)
        {
            floorSum += hMins[i];
            var pct = double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0;
            gaps[i] = pct > 0 ? Math.Max(0, pct / 100.0 * availableWidth - hMins[i]) : 0;
            wantGap += gaps[i];
        }
        var free = availableWidth - floorSum;
        for (var i = 0; i < pw.widths.Length; i++)
            pw.widths[i] = hMins[i] + (wantGap > 0 && free > 0 ? free * gaps[i] / wantGap : 0);
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_TABLEW") == "1")
            Console.Error.WriteLine($"[tablepct] avail={availableWidth:0.##} mins={string.Join(",", hMins.Select(v => v.ToString("0.#")))} pct={pctText} out={string.Join(",", pw.widths.Select(v => v.ToString("0.#")))}");
        return true;
    }

    /// <summary>Parse the declared column widths: absolute points, percentages and the HTML column shares.</summary>
    private void ParseDeclaredWidths(ColumnWidthState pw, double availableWidth)
    {
        pw.hasSpaceSep = ColumnWidths!.IndexOfAny(new[] { ' ', '\t' }) >= 0;
        pw.parts = pw.hasSpaceSep
            ? ColumnWidths.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries)
            : ColumnWidths.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        pw.widths = new double[pw.parts.Length];
        pw.declShare = new double[pw.parts.Length];
        pw.declMask = HtmlColPctDeclaredCols;
        for (var i = 0; i < pw.parts.Length; i++)
        {
            var tok = pw.parts[i].TrimEnd(',');
            var isPercent = tok.EndsWith("%", StringComparison.Ordinal);
            var num = isPercent ? tok.Substring(0, tok.Length - 1) : tok;
            if (TryParseWidthToken(num) is not { } w) w = 100;
            if (isPercent)
                w = availableWidth > 0 ? w / 100.0 * availableWidth : w;
            pw.widths[i] = w;
            if (isPercent && pw.declMask is not null && i < pw.declMask.Length && pw.declMask[i])
                pw.declShare[i] = w;
        }


        // The lifted HTML dialect resolves its grids by the AUTO column rule at
        // DRAW time, when the real box is finally known (the build's available
        // width is the outer table's stand-in): when the max-content columns fit
        // the box they are taken whole with the surplus ∝ max; otherwise each
        // column floors at max(declared, min-content) and a deficit squeezes the
        // above-floor slack / a surplus grows ∝ the post-floor width.
    }

    /// <summary>No declared widths, or content overrides them: equal shares, content-fitted widths or the HTML column minimums.</summary>
    private double[]? ResolveUndeclaredWidths(ColumnWidthState pw, double availableWidth)
    {
        if (string.IsNullOrWhiteSpace(ColumnWidths) || pw.contentOverridesWidths)
        {
            // AutoFitToWindow with no explicit widths: distribute the usable
            // width equally across the columns.
            if (ColumnAdjustment == ColumnAdjustment.AutoFitToWindow && availableWidth > 0)
            {
                var cols = 1;
                for (var i = 0; i < Rows.Count; i++)
                {
                    var cc = Rows.At(i).Cells.Count;
                    if (cc > cols) cols = cc;
                }
                var equal = new double[cols];
                for (var i = 0; i < cols; i++) equal[i] = availableWidth / cols;
                return equal;
            }
            // AutoFitToContent with no explicit widths: each column takes its
            // MAX-content width — the full unwrapped text — so a fitting table never
            // wraps a cell it could lay on one line (the widest cell stays whole:
            // its full text draws on a single line). The exception is an
            // HtmlFragment cell, which measures MIN-content (the widest unbreakable
            // word) because the HTML shrink-to-fit measure reports that: "Crossing
            // Type" sizes its column to "Crossing" and wraps.
            if (ColumnAdjustment == ColumnAdjustment.AutoFitToContent)
            {
                if (FitColumnsToContent(availableWidth) is { } fitColumnsToContentResult) return fitColumnsToContentResult;
            }
            // Default: one column per max cells in any row. DefaultColumnWidth names
            // that width when the caller set it (a caller paces a 28-column factor sheet
            // at "1cm" and lets TableBroken.VerticalInSamePage slice it); 100 pt is the
            // fallback when it is unset or unreadable.
            var maxCols = 1;
            for (var i = 0; i < Rows.Count; i++)
            {
                var cc = Rows.At(i).Cells.Count;
                if (cc > maxCols) maxCols = cc;
            }
            var defWidth = 100.0;
            if (!string.IsNullOrWhiteSpace(DefaultColumnWidth))
            {
                var dtok = DefaultColumnWidth!.Trim();
                if (dtok.EndsWith("%", StringComparison.Ordinal)
                    && TryParseWidthToken(dtok.Substring(0, dtok.Length - 1)) is { } dpct
                    && availableWidth > 0)
                    defWidth = dpct / 100.0 * availableWidth;
                else if (TryParseWidthToken(dtok) is { } dpts && dpts > 0)
                    defWidth = dpts;
            }
            var result = new double[maxCols];
            for (var i = 0; i < maxCols; i++) result[i] = defWidth;
            return result;
        }
        return null;
    }

    /// <summary>Auto-fit to content: each column as wide as its widest cell, scaled into the available width when the sum overflows.</summary>
    private double[]? FitColumnsToContent(double availableWidth)
    {
        var cols = 1;
        for (var i = 0; i < Rows.Count; i++)
            if (Rows.At(i).Cells.Count > cols) cols = Rows.At(i).Cells.Count;
        var w = new double[cols];
        // A column-paginating auto-fit table (TableBroken.Vertical /
        // VerticalInSamePage / repeating columns) is max-content for the same
        // reason plus one more: the slicing carries the overflow to further
        // pages/bands instead of wrapping (the 97-cell factor sheet lays every
        // header on one line across 13 pages).
        var sliceMaxContent = RepeatingColumnsCount > 0
            || Broken is TableBroken.Vertical or TableBroken.VerticalInSamePage;
        var anyMinContent = false;
        // Columns whose cells hold block-structured HTML: they fill whatever the
        // measured columns leave (see HasFillHtmlContent).
        var fill = new bool[cols];
        for (var ri = 0; ri < Rows.Count; ri++)
        {
            var row = Rows.At(ri);
            for (var ci = 0; ci < row.Cells.Count && ci < cols; ci++)
            {
                var cell = row.Cells[ci];
                // A cell that spans columns sizes NONE of them: its content is
                // laid out across the span and wraps there, so it never widens a
                // single column. Probed on the 17-column repeating-column sheet —
                // the five spanned header cells leave their columns at the data
                // cells' width while the six single-column headers set theirs.
                if (cell.ColSpan > 1) continue;
                var pad = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
                // An HtmlFragment cell shrinks to its widest word; a slicing
                // table keeps every cell max-content regardless of dialect.
                var minContent = !sliceMaxContent && HasHtmlContent(cell);
                if (minContent) anyMinContent = true;
                if (!sliceMaxContent && HasFillHtmlContent(cell)) fill[ci] = true;
                // A bold-serif HTML cell with no declared padding butts its column
                // against the text width exactly (no padding is added).
                // Max-content columns carry the generator's +0.01 pt measure
                // guard on top of text + declared padding (probed: the factor
                // sheet's columns land at text+pad+0.01 to the third decimal,
                // and GetWidth reports 44.47 for a 44.46 pt cell).
                // A picture in the cell does not wrap: it floors both boxes.
                var picture = WidestPicture(cell);
                var need = minContent
                    ? Math.Max(MaxWordWidth(cell, row), picture) + (pad is null && AllBoldSerifHtml(cell)
                        ? 0
                        : (pad?.Left ?? 2) + (pad?.Right ?? 2))
                    : Math.Max(MaxLineWidth(cell, row, exact: true), picture) + (pad?.Left ?? 0) + (pad?.Right ?? 0)
                      + AutoFitMeasureGuardPt;
                if (need > w[ci]) w[ci] = need;
            }
        }
        // Max-content cells NEVER wrap — the inflated wrap estimate would split
        // lines the generator draws whole. Withheld when any cell in the table
        // was sized min-content, because that cell is MEANT to wrap.
        if (!anyMinContent) AutoFitMaxContentCells = true;
        for (var i = 0; i < cols; i++) if (w[i] <= 0) w[i] = 100;
        // Never exceed the usable band: proportionally shrink an over-wide
        // auto-fit table to the page content width. Skipped when the table
        // column-paginates instead of shrinking (TableBroken.Vertical /
        // VerticalInSamePage / repeating columns slice the overflow away).
        if (availableWidth > 0 && RepeatingColumnsCount == 0
            && Broken is not TableBroken.Vertical and not TableBroken.VerticalInSamePage)
        {
            // A fill column takes an equal share of what the measured columns
            // leave of the content box — never less than its own min-content.
            var fillCount = 0;
            double measured = 0;
            for (var i = 0; i < w.Length; i++)
                if (i < fill.Length && fill[i]) fillCount++;
                else measured += w[i];
            if (fillCount > 0 && availableWidth - measured > 0)
            {
                var share = (availableWidth - measured) / fillCount;
                for (var i = 0; i < w.Length; i++)
                    if (i < fill.Length && fill[i] && share > w[i]) w[i] = share;
            }
            double total = 0;
            foreach (var cw in w) total += cw;
            if (total > availableWidth + 1e-3)
                for (var i = 0; i < w.Length; i++) w[i] *= availableWidth / total;
        }
        return w;
    }
}
