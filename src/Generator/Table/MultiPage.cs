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
    /// <summary>Stroke width the current build added to every column's pitch
    /// (left + right), taken back out of the wrap width and the published
    /// <see cref="Cell.Width"/>; 0 outside a pitch-mode build.</summary>
    private double _columnPitch;

    /// <summary>The slice being drawn is the last one on its page, so a collapsed
    /// grid rules itself off under it.</summary>
    private bool _sliceClosesPage;

    /// <summary>Grid-column geometry for a nested reserve at SLICING time — the same
    /// cell walk the slice renderer does, stopped at the reserve's column, so an
    /// inner grid built during pagination lands at exactly the X and pads the draw
    /// pass would have given it.</summary>
    private (double x, double padLeft, double padTop) NestedCellGeom(
        RowPlan plan, double[] colWidths, double tableX, int[] cellMap, int targetCol)
    {
        var row = plan.Row;
        var cellX = tableX + CellSpacingH;
        var gridToCell = plan.GridToCell;
        for (var col = 0; col < colWidths.Length; col++)
        {
            int origIdx;
            if (gridToCell is not null)
            {
                origIdx = col < gridToCell.Length ? gridToCell[col] : -1;
                if (origIdx == -2) continue;
                if (origIdx < 0 || origIdx >= row.Cells.Count) { cellX += colWidths[col] + CellSpacingH; continue; }
            }
            else if (plan.ColToCell is { } colToCell)
            {
                origIdx = col < colToCell.Length ? colToCell[col] : -1;
                if (origIdx == -2) continue;
                if (origIdx < 0) { cellX += colWidths[col] + CellSpacingH; continue; }
            }
            else
            {
                origIdx = cellMap[col];
                if (origIdx >= row.Cells.Count) { cellX += colWidths[col] + CellSpacingH; continue; }
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
            cellX += cellWidth + CellSpacingH;
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
