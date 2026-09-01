using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;
namespace Aspose.Pdf;

public partial class Table : BaseParagraph
{
    /// <summary>
    /// Build the table across multiple pages. Returns content bytes per page.
    /// Sets <see cref="Row.IsInNewPage"/> on rows that overflow to subsequent pages.
    /// The first entry is for the given page; additional entries require new pages.
    /// Rows whose wrapped content exceeds the available page height are split at
    /// line boundaries — each chunk becomes a partial-row slice on its own page
    /// with the row's borders and background drawn around the chunk's extent.
    /// </summary>
    public List<byte[]> BuildMultiPage(Page page, double startY = 0, double bottomMargin = 36,
        double topMargin = 0, bool measureOnly = false, bool contentFlow = false)
    {
        _contentFlow = contentFlow;
        _measureOnly = measureOnly;
        _buildPage = page;
        _emittedPages = 0;
        _pageImages.Clear();
        _pageCheckboxes.Clear();
        _pageGraphs.Clear();
        _pageFootnotes.Clear();
        var fontName = RegisterFont(page);
        var colWidths = ParseColumnWidths(GetTableUsableWidth(page));
        // The cell border joins every column's pitch BEFORE the grid is chunked into
        // column slices, so a slice packs against the real box widths and its clone
        // inherits them (a "30 30 …" table with 5 pt GraphInfo borders lays out on a
        // 40 pt column pitch with abutting double borders between cells).
        var (pitchL, pitchR) = CellBorderPitch();
        _columnPitch = pitchL + pitchR;
        if (_columnPitch > 0 && !ColumnPitchResolved)
        {
            colWidths = (double[])colWidths.Clone();
            for (var i = 0; i < colWidths.Length; i++) colWidths[i] += _columnPitch;
            // The pitch is added AFTER the declared widths were fitted, so a grid that
            // exactly filled its band now overruns it by one pitch per column. The last
            // column takes what is left, the way an over-wide column always does --
            // otherwise the table runs past the page's right content margin and that
            // column wraps a line later than expected.
            if (RepeatingColumnsCount == 0
                && Broken is not TableBroken.Vertical and not TableBroken.VerticalInSamePage)
            {
                var band = GetTableUsableWidth(page);
                double pitched = 0;
                foreach (var w in colWidths) pitched += w;
                if (band > 0 && pitched > band + 1e-3)
                {
                    var over = pitched - band;
                    var last = colWidths.Length - 1;
                    if (colWidths[last] - over > _columnPitch)
                    {
                        colWidths[last] -= over;
                        LastColBoxOverhang = over;
                    }
                }
            }
        }
        if (!ColumnPitchResolved) WidenColumnsForNestedGrids(colWidths);

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
        if (Broken == TableBroken.VerticalInSamePage && colWidths.Length > 1)
        {
            var marginLeftVb = Margin?.Left ?? 0;
            var tableXVb = FlowLeftOffset + Left + marginLeftVb;
            var pageRightMarginVb = page.PageInfo?.Margin?.Right ?? 0;
            if (pageRightMarginVb <= 0) pageRightMarginVb = 36;
            var usableVb = page.Width - tableXVb - pageRightMarginVb;
            double totalWVb = 0;
            foreach (var w in colWidths) totalWVb += w;
            // With repeating columns the repeat block leads every band, so a
            // band's chunk packs against what is left after it.
            var repeatVb = Math.Max(0, Math.Min(RepeatingColumnsCount, colWidths.Length));
            double repeatWVb = 0;
            for (var i = 0; i < repeatVb; i++) repeatWVb += colWidths[i];
            var bandBudget = Math.Max(1, usableVb - repeatWVb);
            if (totalWVb > usableVb + 1e-3)
            {
                var bands = new List<(int start, int end)>();
                var bStart = repeatVb;
                while (bStart < colWidths.Length)
                {
                    var bEnd = bStart;
                    double bw = 0;
                    while (bEnd < colWidths.Length
                           && (bEnd == bStart || bw + colWidths[bEnd] <= bandBudget + 1e-3))
                    {
                        bw += colWidths[bEnd];
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
                    for (var i = 0; i < colWidths.Length; i++) colWidths[i] *= scaleVb;
                }
                else
                {
                    var mergedPages = new List<byte[]>();
                    var curStartY = startY;
                    double heightSum = 0;
                    foreach (var (bs, be) in bands)
                    {
                        var bandTable = BuildColumnSliceTable(colWidths, repeatVb, bs, be);
                        var bandPages = bandTable.BuildMultiPage(page, curStartY, bottomMargin, topMargin);
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
                        // Image/graph blits ride along per page slot.
                        for (var pi = 0; pi < bandTable._pageImages.Count; pi++)
                        {
                            while (_pageImages.Count <= pi) _pageImages.Add(new List<(byte[], Rectangle)>());
                            _pageImages[pi].AddRange(bandTable._pageImages[pi]);
                        }
                        for (var pi = 0; pi < bandTable._pageGraphs.Count; pi++)
                        {
                            while (_pageGraphs.Count <= pi) _pageGraphs.Add(new List<byte[]>());
                            _pageGraphs[pi].AddRange(bandTable._pageGraphs[pi]);
                        }
                        curStartY = bandTable.LastPageEndY;
                        heightSum += bandTable.LastRenderedHeight;
                        LastPageEndY = bandTable.LastPageEndY;
                    }
                    LastRenderedHeight = heightSum;
                    return mergedPages;
                }
            }
        }

        // Column pagination across PAGES: a table wider than the page's content
        // band with RepeatingColumnsCount > 0 and/or TableBroken.Vertical splits
        // into column slices, one run of pages per slice. Slicing is GRID-column
        // based (a ColSpan cell crossing a slice boundary contributes an EMPTY
        // continuation cell that keeps its background/border; text renders once,
        // in the slice where the cell starts). Each slice re-renders every row;
        // the first `repeat` grid columns are prepended to every slice. The
        // packing budget is the page content band (page width minus BOTH page
        // margins) minus the repeating block — measured on the
        // 12/6/8-page layouts of the repeating-column shape.
        var repeat = Math.Max(0, Math.Min(RepeatingColumnsCount, colWidths.Length));
        if ((repeat > 0 || Broken == TableBroken.Vertical)
            && Broken != TableBroken.VerticalInSamePage
            && colWidths.Length > Math.Max(1, repeat))
        {
            var usableCp = GetTableUsableWidth(page);
            double totalW = 0;
            for (var i = 0; i < colWidths.Length; i++) totalW += colWidths[i];
            if (totalW > usableCp + 1e-3)
            {
                double repeatW = 0;
                for (var i = 0; i < repeat; i++) repeatW += colWidths[i];
                var chunkBudget = Math.Max(1, usableCp - repeatW);
                var allPages = new List<byte[]>();
                var chunkStart = repeat;
                var firstSlice = true;
                // Continuation slices start each run of pages at the fresh-page
                // content top (below the page's top margin), like the row
                // paginator's own overflow pages do.
                var contTop = topMargin > 0 ? topMargin : (page.PageInfo?.Margin?.Top ?? 0);
                if (contTop <= 0) contTop = 72;
                Table? lastSlice = null;
                while (chunkStart < colWidths.Length)
                {
                    var chunkEnd = chunkStart;
                    double w = 0;
                    while (chunkEnd < colWidths.Length &&
                           (chunkEnd == chunkStart || w + colWidths[chunkEnd] <= chunkBudget))
                    {
                        w += colWidths[chunkEnd];
                        chunkEnd++;
                    }
                    if (chunkEnd == chunkStart) chunkEnd = chunkStart + 1;

                    var sliceTable = BuildColumnSliceTable(colWidths, repeat, chunkStart, chunkEnd);
                    sliceTable.ColumnSliceChild = true;
                    var slicePages = sliceTable.BuildMultiPage(
                        page, firstSlice ? startY : page.LayoutFrameHeight - contTop,
                        bottomMargin, topMargin);
                    var pageBase = allPages.Count;
                    allPages.AddRange(slicePages);
                    for (var pi = 0; pi < sliceTable._pageImages.Count; pi++)
                    {
                        var slot = pageBase + pi;
                        while (_pageImages.Count <= slot) _pageImages.Add(new List<(byte[], Rectangle)>());
                        _pageImages[slot].AddRange(sliceTable._pageImages[pi]);
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

        var identity = new int[colWidths.Length];
        for (var i = 0; i < colWidths.Length; i++) identity[i] = i;
        var built = BuildMultiPageInternal(page, startY, bottomMargin, colWidths, identity, fontName, topMargin);
        ApplyLaidOutCellGrid(colWidths);
        return built;
    }

    /// <summary>Stroke width the current build added to every column's pitch
    /// (left + right), taken back out of the wrap width and the published
    /// <see cref="Cell.Width"/>; 0 outside a pitch-mode build.</summary>
    private double _columnPitch;

    /// <summary>Grid-column geometry for a nested reserve at SLICING time — the same
    /// cell walk the slice renderer does, stopped at the reserve's column, so an
    /// inner grid built during pagination lands at exactly the X and pads the draw
    /// pass would have given it.</summary>
    private (double x, double padLeft, double padTop) NestedCellGeom(
        RowPlan plan, double[] colWidths, double tableX, int[] cellMap, int targetCol)
    {
        var row = plan.Row;
        var cellX = tableX;
        var gridToCell = plan.GridToCell;
        for (var col = 0; col < colWidths.Length; col++)
        {
            int origIdx;
            if (gridToCell is not null)
            {
                origIdx = col < gridToCell.Length ? gridToCell[col] : -1;
                if (origIdx == -2) continue;
                if (origIdx < 0 || origIdx >= row.Cells.Count) { cellX += colWidths[col]; continue; }
            }
            else if (plan.ColToCell is { } colToCell)
            {
                origIdx = col < colToCell.Length ? colToCell[col] : -1;
                if (origIdx == -2) continue;
                if (origIdx < 0) { cellX += colWidths[col]; continue; }
            }
            else
            {
                origIdx = cellMap[col];
                if (origIdx >= row.Cells.Count) { cellX += colWidths[col]; continue; }
            }
            var cell = row.Cells.At(origIdx);
            var span = Math.Max(1, Math.Min(cell.ColSpan, colWidths.Length - col));
            var cellWidth = GetCellWidth(colWidths, col, span);
            if (col == targetCol)
            {
                var padding = EffectivePad(cell, row);
                var dp = DefaultPad(cell, row);
                return (cellX, padding?.Left ?? dp, padding?.Top ?? 0);
            }
            cellX += cellWidth;
        }
        return (tableX, 0, 0);
    }

    /// <summary>The face a fragment's segments name through
    /// <see cref="Text.TextEditOptions.NoCharacterAction.UseCustomReplacementFont"/>, as its
    /// embeddable program plus the PDF base name to write it under (PDF names carry no spaces,
    /// so "Arial Unicode MS" is written "ArialUnicodeMS"). Null when no segment declares one or
    /// the declared font ships no program to embed.</summary>
    private static (byte[] Ttf, string Name)? DeclaredReplacementFace(Aspose.Pdf.Text.TextFragment tf)
    {
        foreach (var seg in tf.Segments)
        {
            var opts = seg.TextEditOptions;
            if (opts.NoCharacterBehavior != Aspose.Pdf.Text.TextEditOptions.NoCharacterAction.UseCustomReplacementFont)
                continue;
            if (opts.ReplacementFont?.SourceFontData?.TtfData is not { Length: > 0 } ttf) continue;
            var name = (opts.ReplacementFont.FontName ?? string.Empty).Replace(" ", string.Empty);
            if (name.Length == 0) continue;
            return (ttf, name);
        }
        return null;
    }

    /// <summary>Split one line of text into same-face runs: each codepoint takes the
    /// primary face when it covers it, else the first coverage-chain face that does.
    /// A codepoint no face covers stays in the current run (its notdef draws there).
    /// Returns null when everything resolved to the primary (no chain needed).</summary>
    private static List<(string Text, byte[] Ttf, string Name)>? SegmentByCoverageChain(
        string s, byte[]? primaryTtf, string primaryName)
    {
        var chain = Aspose.Pdf.Text.CjkFallbackFont.ChainFaces();
        if (chain.Count == 0 && primaryTtf is null) return null;
        var primary = primaryTtf is not null ? (primaryTtf, primaryName)
            : (chain[0].Bytes, chain[0].Name);
        var runs = new List<(string Text, byte[] Ttf, string Name)>();
        var cur = new System.Text.StringBuilder();
        var curFace = primary;
        var sawChain = false;
        void Flush()
        {
            if (cur.Length > 0) runs.Add((cur.ToString(), curFace.Item1, curFace.Item2));
            cur.Clear();
        }
        (byte[], string) FaceFor(int cp)
        {
            var pParser = GetInlineGlyphParser(primary.Item1);
            if (pParser is not null && pParser.CMap.TryGetValue(cp, out var pg) && pg > 0) return primary;
            foreach (var (bytes, name) in chain)
                if (GetInlineGlyphParser(bytes) is { } cParser
                    && cParser.CMap.TryGetValue(cp, out var cg) && cg > 0)
                    return (bytes, name);
            return curFace; // nothing covers it — the notdef stays where it is
        }
        for (var i = 0; i < s.Length; i++)
        {
            var frag = s[i].ToString();
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                frag = s.Substring(i, 2);
                i++;
            }
            // Spaces bind to the run they follow — every face draws a space.
            var face = cp == ' ' ? curFace : FaceFor(cp);
            if (face.Item2 != curFace.Item2)
            {
                Flush();
                curFace = face;
                if (face.Item2 != primary.Item2) sawChain = true;
            }
            cur.Append(frag);
        }
        Flush();
        return sawChain ? runs : null;
    }

    /// <summary>The face a styled segment draws in. A NAMED face resolves to its
    /// bold/italic sibling here: TextState stores those flags only for a fragment that
    /// belongs to no page and leaves the writer to pick the file.</summary>
    private static (byte[]? Ttf, string? Name) StyledSegmentFace(
        Aspose.Pdf.Text.TextState ss, bool bold, bool italic)
    {
        var ttf = ss.Font?.SourceFontData?.TtfData;
        if (ttf is null || !(bold || italic)) return (ttf, ss.Font?.FontName);
        var want = Aspose.Pdf.Text.FontStyles.Regular;
        if (bold) want |= Aspose.Pdf.Text.FontStyles.Bold;
        if (italic) want |= Aspose.Pdf.Text.FontStyles.Italic;
        var family = Aspose.Pdf.Text.FontRepository.FamilyOf(ss.Font!.FontName ?? ss.Font.BaseFont);
        if (string.IsNullOrEmpty(family)) return (ttf, ss.Font?.FontName);
        try
        {
            var styled = Aspose.Pdf.Text.FontRepository.TryFindFont(family!, want, ignoreCase: true);
            if (styled?.SourceFontData?.TtfData is { Length: > 12 } st)
                return (st, styled.FontName);
        }
        catch { }
        return (ttf, ss.Font?.FontName);
    }

    /// <summary>Grid placement for tables using RowSpan: assigns every cell its grid column
    /// the way an HTML table does (cells flow left-to-right past columns still occupied by
    /// spans from earlier rows). Returns null when no cell spans rows — the legacy
    /// column-index-equals-cell-index layout stays in effect for those tables.</summary>
    private (int[][] gridToCell, int[][] effRowSpan, int gridCols, List<SpanBlock> blocks)? ComputeGrid()
    {
        var anySpan = false;
        for (var r = 0; r < Rows.Count && !anySpan; r++)
        {
            var row = Rows.At(r);
            for (var c = 0; c < row.Cells.Count; c++)
                if (row.Cells.At(c).RowSpan > 1) { anySpan = true; break; }
        }
        if (!anySpan) return null;

        var gridToCell = new int[Rows.Count][];
        var effRowSpan = new int[Rows.Count][];
        var blocks = new List<SpanBlock>();
        // Columns still held by rowspans from earlier rows: (colStart, colSpan, rowsLeft).
        var pending = new List<(int colStart, int colSpan, int remaining)>();
        var gridCols = 1;

        for (var r = 0; r < Rows.Count; r++)
        {
            var row = Rows.At(r);
            var occupied = new List<bool>();
            void Reserve(int col) { while (occupied.Count <= col) occupied.Add(false); occupied[col] = true; }
            foreach (var (colStart, colSpan, _) in pending)
                for (var k = 0; k < colSpan; k++) Reserve(colStart + k);

            // -1 = vacant or held by a foreign rowspan (advance x by the column width);
            // -2 = covered by this row's own ColSpan cell (x already advanced by the span).
            var mapping = new List<int>();
            void Put(int col, int val)
            {
                while (mapping.Count <= col) mapping.Add(-1);
                mapping[col] = val;
            }
            var eff = new int[row.Cells.Count];
            var cursor = 0;
            // Spans placed in THIS row start covering rows at r+1; they must not be
            // decremented at the end of row r, so collect them separately.
            var placedThisRow = new List<(int colStart, int colSpan, int remaining)>();
            for (var ci = 0; ci < row.Cells.Count; ci++)
            {
                var cell = row.Cells.At(ci);
                var colSpan = Math.Max(1, cell.ColSpan);
                bool Fits(int at)
                {
                    for (var k = 0; k < colSpan; k++)
                        if (at + k < occupied.Count && occupied[at + k]) return false;
                    return true;
                }
                while (!Fits(cursor)) cursor++;
                Put(cursor, ci);
                for (var k = 1; k < colSpan; k++) Put(cursor + k, -2);
                eff[ci] = Math.Min(Math.Max(1, cell.RowSpan), Rows.Count - r);
                if (eff[ci] > 1)
                {
                    placedThisRow.Add((cursor, colSpan, eff[ci] - 1));
                    blocks.Add(new SpanBlock
                    {
                        StartRow = r, EndRow = r + eff[ci],
                        GridCol = cursor, ColSpan = colSpan,
                        Cell = cell, Row = row,
                    });
                }
                cursor += colSpan;
            }
            if (mapping.Count > gridCols) gridCols = mapping.Count;
            if (occupied.Count > gridCols) gridCols = occupied.Count;
            gridToCell[r] = mapping.ToArray();
            effRowSpan[r] = eff;
            for (var p = pending.Count - 1; p >= 0; p--)
            {
                var (cs, csp, rem) = pending[p];
                if (rem <= 1) pending.RemoveAt(p);
                else pending[p] = (cs, csp, rem - 1);
            }
            pending.AddRange(placedThisRow);
        }

        // Pad every row's mapping to the full grid width (vacant → -1).
        for (var r = 0; r < Rows.Count; r++)
        {
            if (gridToCell[r].Length == gridCols) continue;
            var padded = new int[gridCols];
            for (var c = 0; c < gridCols; c++) padded[c] = c < gridToCell[r].Length ? gridToCell[r][c] : -1;
            gridToCell[r] = padded;
        }
        return (gridToCell, effRowSpan, gridCols, blocks);
    }

    /// <summary>The <c>&lt;a href&gt;</c> runs of an HtmlFragment cell, as (anchor text, url)
    /// pairs in document order — the same shape a TextFragment carries in
    /// <c>HtmlAnchors</c>, so the cell's line layout annotates each run over its own
    /// characters. Null when the markup holds no usable anchor.</summary>
    private static List<(string Text, string Url)>? ParseCellHtmlAnchors(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        List<(string Text, string Url)>? anchors = null;
        foreach (Match m in Regex.Matches(html,
                     @"<a\b[^>]*\bhref\s*=\s*(?:'(?<u>[^']*)'|""(?<u>[^""]*)""|(?<u>[^\s>]+))[^>]*>(?<t>.*?)</a\s*>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var atext = HtmlFragment.StripHtmlTags(m.Groups["t"].Value).Trim();
            var url = m.Groups["u"].Value.Trim();
            if (atext.Length == 0 || url.Length == 0) continue;
            (anchors ??= new()).Add((atext, url));
        }
        return anchors;
    }

}
