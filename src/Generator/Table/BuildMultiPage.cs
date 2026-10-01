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
        var mp = new MultiPageSliceState();
        PageEndYs.Clear();
        mp.page = page;
        mp.startY = startY;
        mp.bottomMargin = bottomMargin;
        mp.topMargin = topMargin;
        mp.measureOnly = measureOnly;
        mp.contentFlow = contentFlow;
        _contentFlow = mp.contentFlow;
        _measureOnly = mp.measureOnly;
        _buildPage = mp.page;
        _emittedPages = 0;
        _pageImages.Clear();
        _pageBlocks.Clear();
        _pageCheckboxes.Clear();
        _pageGraphs.Clear();
        _pageFootnotes.Clear();
        mp.fontName = RegisterFont(mp.page);
        mp.colWidths = ParseColumnWidths(GetTableUsableWidth(mp.page));
        // The cell border joins every column's pitch BEFORE the grid is chunked into
        // column slices, so a slice packs against the real box widths and its clone
        // inherits them (a "30 30 …" table with 5 pt GraphInfo borders lays out on a
        // 40 pt column pitch with abutting double borders between cells).
        var (pitchL, pitchR) = CellBorderPitch();
        _columnPitch = pitchL + pitchR;
        // A collapsed grid's declared widths ARE the pitch — the shared rules
        // already live inside them — so only a separate-bordered grid grows its
        // columns by the strokes here; and not one whose rules were declared to
        // stand inside its widths (see RulesInsideColumnWidth).
        if (_columnPitch > 0 && !ColumnPitchResolved && !IsBordersCollapsed && !RulesInsideColumnWidth)
        {
            mp.colWidths = (double[])mp.colWidths.Clone();
            for (var i = 0; i < mp.colWidths.Length; i++) mp.colWidths[i] += _columnPitch;
            // The pitch is added AFTER the declared widths were fitted, so a grid that
            // exactly filled its band now overruns it by one pitch per column. The last
            // column's BOX keeps its pitched width past the band; only its CONTENT is
            // clamped at the page's right content edge -- the right rule and padding
            // come off the overrun first (probed: 4 x 135 with 0.5 pt rules in a 540
            // band keeps the 125 pt text box, 1 pt rules clip it to 123, 3 x 180 with
            // 2 pt rules to 165) -- otherwise that column wraps a line early.
            if (RepeatingColumnsCount == 0
                && Broken is not TableBroken.Vertical and not TableBroken.VerticalInSamePage)
            {
                var band = GetTableUsableWidth(mp.page);
                double pitched = 0;
                foreach (var w in mp.colWidths) pitched += w;
                var padR = DefaultCellPadding?.Right ?? 0;
                var over = pitched - band - pitchR - padR;
                if (band > 0 && over > 1e-3)
                {
                    var last = mp.colWidths.Length - 1;
                    if (mp.colWidths[last] - over > _columnPitch)
                    {
                        mp.colWidths[last] -= over;
                        LastColBoxOverhang = over;
                    }
                }
            }
        }
        if (!ColumnPitchResolved) WidenColumnsForNestedGrids(mp.colWidths, GetTableUsableWidth(mp.page));

        if (BuildVerticalBandPages(mp) is { } buildVerticalBandPagesResult) return buildVerticalBandPagesResult;
        mp.repeat = Math.Max(0, Math.Min(RepeatingColumnsCount, mp.colWidths.Length));
        if (BuildColumnSlicePages(mp) is { } buildColumnSlicePagesResult) return buildColumnSlicePagesResult;
        mp.identity = new int[mp.colWidths.Length];
        for (var i = 0; i < mp.colWidths.Length; i++) mp.identity[i] = i;
        mp.built = BuildMultiPageInternal(mp.page, mp.startY, mp.bottomMargin, mp.colWidths, mp.identity, mp.fontName, mp.topMargin);
        ApplyLaidOutCellGrid(mp.colWidths);
        return mp.built;
    }
}
