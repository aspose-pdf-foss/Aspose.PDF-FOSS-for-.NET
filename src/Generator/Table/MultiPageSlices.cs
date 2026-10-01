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
    /// <summary>Table multi-page: a table wider than the page split into column slices, one run of pages per slice.</summary>
    private List<byte[]>? BuildColumnSlicePages(MultiPageSliceState mp)
    {
        if ((mp.repeat > 0 || Broken == TableBroken.Vertical)
            && Broken != TableBroken.VerticalInSamePage
            && mp.colWidths.Length > Math.Max(1, mp.repeat))
        {
            var usableCp = GetTableUsableWidth(mp.page);
            double totalW = 0;
            for (var i = 0; i < mp.colWidths.Length; i++) totalW += mp.colWidths[i];
            if (totalW > usableCp + 1e-3)
            {
                double repeatW = 0;
                for (var i = 0; i < mp.repeat; i++) repeatW += mp.colWidths[i];
                var chunkBudget = Math.Max(1, usableCp - repeatW);
                var allPages = new List<byte[]>();
                var chunkStart = mp.repeat;
                var firstSlice = true;
                // Continuation slices start each run of pages at the fresh-page
                // content top (below the page's top margin), like the row
                // paginator's own overflow pages do.
                var contTop = mp.topMargin > 0 ? mp.topMargin : (mp.page.PageInfo?.Margin?.Top ?? 0);
                if (contTop <= 0) contTop = 72;
                Table? lastSlice = null;
                while (chunkStart < mp.colWidths.Length)
                {
                    var chunkEnd = chunkStart;
                    double w = 0;
                    while (chunkEnd < mp.colWidths.Length &&
                           (chunkEnd == chunkStart || w + mp.colWidths[chunkEnd] <= chunkBudget))
                    {
                        w += mp.colWidths[chunkEnd];
                        chunkEnd++;
                    }
                    if (chunkEnd == chunkStart) chunkEnd = chunkStart + 1;

                    var sliceTable = BuildColumnSliceTable(mp.colWidths, mp.repeat, chunkStart, chunkEnd);
                    sliceTable.ColumnSliceChild = true;
                    var slicePages = sliceTable.BuildMultiPage(
                        mp.page, firstSlice ? mp.startY : mp.page.LayoutFrameHeight - contTop,
                        mp.bottomMargin, mp.topMargin);
                    var pageBase = allPages.Count;
                    allPages.AddRange(slicePages);
                    for (var pi = 0; pi < sliceTable._pageImages.Count; pi++)
                    {
                        var slot = pageBase + pi;
                        while (_pageImages.Count <= slot) _pageImages.Add(new List<(byte[], Rectangle)>());
                        _pageImages[slot].AddRange(sliceTable._pageImages[pi]);
                    }
                    for (var pi = 0; pi < sliceTable._pageBlocks.Count; pi++)
                    {
                        var slot = pageBase + pi;
                        while (_pageBlocks.Count <= slot) _pageBlocks.Add(new List<(ReservedBlock, ReservedPart, Rectangle)>());
                        _pageBlocks[slot].AddRange(sliceTable._pageBlocks[pi]);
                    }
                    for (var pi = 0; pi < sliceTable._pageGraphs.Count; pi++)
                    {
                        var slot = pageBase + pi;
                        while (_pageGraphs.Count <= slot) _pageGraphs.Add(new List<byte[]>());
                        _pageGraphs[slot].AddRange(sliceTable._pageGraphs[pi]);
                    }
                    lastSlice = sliceTable;
                    firstSlice = false;
                    chunkStart = chunkEnd;
                }
                if (lastSlice is not null)
                {
                    LastPageEndY = lastSlice.LastPageEndY;
                    LastRenderedHeight = lastSlice.LastRenderedHeight;
                    LastPageConsumedH.Clear();
                    LastPageConsumedH.AddRange(lastSlice.LastPageConsumedH);
                }
                return allPages;
            }
        }
        return null;
    }

    /// <summary>Table multi-page: a table wider than the page wrapped into vertical bands on the same page.</summary>
    private List<byte[]>? BuildVerticalBandPages(MultiPageSliceState mp)
    {
        // Column-pagination: when RepeatingColumnsCount > 0 and the table is wider
        // than the page can fit, split it horizontally. Each "column slice" renders
        // the first N (repeating) cells alongside one contiguous chunk of the
        // remaining cells, with the chunk packed greedily to fit page width.
        // TableBroken.VerticalInSamePage: a table WIDER than the page's usable
        // width wraps its overflow columns into bands stacked vertically on the
        // SAME page. Each band is a slice of the column
        // range rendered as its own sub-table; a ColSpan cell crossing a band
        // boundary contributes its remaining span to the next band as an EMPTY
        // cell that keeps the background/border (the text renders once, in the
        // band where the cell starts).
        if (Broken == TableBroken.VerticalInSamePage && mp.colWidths.Length > 1)
        {
            var marginLeftVb = Margin?.Left ?? 0;
            var tableXVb = FlowLeftOffset + Left + marginLeftVb;
            var pageRightMarginVb = mp.page.PageInfo?.Margin?.Right ?? 0;
            if (pageRightMarginVb <= 0) pageRightMarginVb = 36;
            var usableVb = mp.page.Width - tableXVb - pageRightMarginVb;
            double totalWVb = 0;
            foreach (var w in mp.colWidths) totalWVb += w;
            // With repeating columns the repeat block leads every band, so a
            // band's chunk packs against what is left after it.
            var repeatVb = Math.Max(0, Math.Min(RepeatingColumnsCount, mp.colWidths.Length));
            double repeatWVb = 0;
            for (var i = 0; i < repeatVb; i++) repeatWVb += mp.colWidths[i];
            var bandBudget = Math.Max(1, usableVb - repeatWVb);
            if (totalWVb > usableVb + 1e-3)
            {
                var bands = new List<(int start, int end)>();
                var bStart = repeatVb;
                while (bStart < mp.colWidths.Length)
                {
                    var bEnd = bStart;
                    double bw = 0;
                    while (bEnd < mp.colWidths.Length
                           && (bEnd == bStart || bw + mp.colWidths[bEnd] <= bandBudget + 1e-3))
                    {
                        bw += mp.colWidths[bEnd];
                        bEnd++;
                    }
                    bands.Add((bStart, bEnd));
                    bStart = bEnd;
                }
                if (bands.Count <= 1 && repeatVb == 0)
                {
                    // Single over-wide column: nothing to wrap — fall back to the
                    // proportional shrink the clamp in ParseColumnWidths skipped.
                    var scaleVb = usableVb / totalWVb;
                    for (var i = 0; i < mp.colWidths.Length; i++) mp.colWidths[i] *= scaleVb;
                }
                else
                {
                    var mergedPages = new List<byte[]>();
                    var curStartY = mp.startY;
                    double heightSum = 0;
                    foreach (var (bs, be) in bands)
                    {
                        var bandTable = BuildColumnSliceTable(mp.colWidths, repeatVb, bs, be);
                        var bandPages = bandTable.BuildMultiPage(mp.page, curStartY, mp.bottomMargin, mp.topMargin);
                        MergeBandPages(mergedPages, bandPages);
                        AdoptBandBlits(bandTable);
                        curStartY = bandTable.LastPageEndY;
                        heightSum += bandTable.LastRenderedHeight;
                        LastPageEndY = bandTable.LastPageEndY;
                    }
                    LastRenderedHeight = heightSum;
                    return mergedPages;
                }
            }
        }
        return null;
    }

    /// <summary>Add one band's rendered pages to the merged run: its first page joins the
    /// SAME page as the bands before it (streams concatenated), later pages append.</summary>
    private static void MergeBandPages(List<byte[]> mergedPages, List<byte[]> bandPages)
    {
        for (var pi = 0; pi < bandPages.Count; pi++)
        {
            if (pi == 0 && mergedPages.Count > 0)
            {
                // Band content joins the SAME page: concatenate streams.
                var first = mergedPages[0];
                var joined = new byte[first.Length + 1 + bandPages[0].Length];
                Array.Copy(first, joined, first.Length);
                joined[first.Length] = (byte)'\n';
                Array.Copy(bandPages[0], 0, joined, first.Length + 1, bandPages[0].Length);
                mergedPages[0] = joined;
            }
            else
                mergedPages.Add(bandPages[pi]);
        }
    }

    /// <summary>Image/graph blits of a band sub-table ride along per page slot.</summary>
    private void AdoptBandBlits(Table bandTable)
    {
        for (var pi = 0; pi < bandTable._pageImages.Count; pi++)
        {
            while (_pageImages.Count <= pi) _pageImages.Add(new List<(byte[], Rectangle)>());
            _pageImages[pi].AddRange(bandTable._pageImages[pi]);
        }
        for (var pi = 0; pi < bandTable._pageBlocks.Count; pi++)
        {
            while (_pageBlocks.Count <= pi) _pageBlocks.Add(new List<(ReservedBlock, ReservedPart, Rectangle)>());
            _pageBlocks[pi].AddRange(bandTable._pageBlocks[pi]);
        }
        for (var pi = 0; pi < bandTable._pageGraphs.Count; pi++)
        {
            while (_pageGraphs.Count <= pi) _pageGraphs.Add(new List<byte[]>());
            _pageGraphs[pi].AddRange(bandTable._pageGraphs[pi]);
        }
    }
}
