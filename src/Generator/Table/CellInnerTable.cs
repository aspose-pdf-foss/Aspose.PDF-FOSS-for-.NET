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
    /// <summary>Row column chrome: one nested table of a cell built onto the page.</summary>
    private bool BuildCellInnerTable(RowColumnState rc, double cellX, List<CellNestedTable> colTabs, CellNestedTable ct)
    {
        // The reserve may span several slices (the host row splits at its
        // reserve-line boundaries); this slice participates when their
        // line windows overlap.
        if (ct.LineOffset >= rc.slice.LineStart + rc.slice.LineCount
            || ct.LineOffset + ct.LineCount <= rc.slice.LineStart) return true;
        var innerT = ct.Table;
        if (ct.Slices is null)
        {
            // First covering slice: build the grid ONCE against the same
            // page bounds as this host build, so it breaks where the page
            // really ends and its continuation slices are positioned for
            // the same fresh-page top the host's own continuation uses.
            var ctRel = ct.LineOffset - rc.slice.LineStart;
            // Lines preceding the reserve advance by the same ruler the
            // draw walk uses: an earlier grid's reserve lines at their
            // own FontSize, everything else at the uniform pitch.
            // The lines come from the PLAN when the cell draws no text of its own
            // (rc.cellLines is dropped then): an empty paragraph's line and the grid's
            // own margin spacer still stand above the grid (probed: a "" cell text
            // followed by a grid with Margin.Top 10 seats the grid 20 below the cell top).
            var leadLines = rc.cellLines
                ?? (rc.col < rc.slice.Plan.CellLines.Count ? rc.slice.Plan.CellLines[rc.col] : null);
            var ctLead = 0.0;
            for (var pli = rc.slice.LineStart;
                 pli < ct.LineOffset && leadLines is not null && pli < leadLines.Count; pli++)
                ctLead += leadLines[pli].ImgReserve && leadLines[pli].FontSize > 0
                    ? leadLines[pli].FontSize
                    // A generator cell holding a grid is an EXACT stack, and
                    // its text is drawn by the per-line walk -- measuring the
                    // lead on the row's uniform pitch instead put the grid
                    // above the heading it belongs under.
                    // (…and a UA-boxed cell is an exact stack too: its grid seats under the boxes its lines
                    //  stand on - the mailing's itemized grid under its heading, not a hundred points lower)
                    : rc.generatorCell || UaCellBoxes
                        ? (leadLines[pli].BoxH > 0
                            ? leadLines[pli].BoxH
                            : leadLines[pli].FontSize + leadLines[pli].Leading)
                    : rc.slice.Plan.LineHeight;
            // (…and a UA-boxed cell's continuation slice carries no top padding: the box was padded on its first page)
            var tabTopY = rc.slice.TopY - (UaCellBoxes && rc.slice.LineStart > 0 ? 0 : rc.padTop) - ctLead
                // Generator dialect: the grid starts inside the host's border.
                - (rc.generatorCell ? rc.borderInsetTop : 0);
            // vertical-align: middle — a nested grid shorter than its row
            // centres in the cell band (the cell-image precedent). Only
            // when the reserve is the cell's LAST content: interleaved
            // lines after the grid occupy the rest of the slice, so the
            // band available to the grid is its own reserve window.
            if (rc.effVA == VerticalAlignment.Center && colTabs.Count == 1
                && (rc.cellLines is null || ct.LineOffset + ct.LineCount >= rc.cellLines.Count))
            {
                var ctAvail = rc.slice.Height - rc.padTop - rc.padBot - ctRel * rc.slice.Plan.LineHeight;
                if (ctAvail > ct.HeightPt) tabTopY -= (ctAvail - ct.HeightPt) / 2;
            }
            // The reserved box holds the capsule's wrapper; the grid itself
            // starts one outset inside it on both axes.
            tabTopY -= innerT.HtmlCapsuleOutsetVPt + innerT.HtmlMarginTopPt;
            innerT.FlowLeftOffset = cellX + rc.padLeft + innerT.HtmlCapsuleOutsetHPt
                + innerT.HtmlListIndentPt
                // DataWorks results grid: the draw pen sits past the
                // reference's full widget footprints while the width
                // model keeps the smaller reserves (see DwNestedDrawShiftPt).
                + (DwFormCells && innerT.HtmlDwGapReservePt > 0
                    ? Converters.HtmlToPdfConverter.DwNestedDrawShiftPt : 0);
            // Generator dialect: a grid inside a FIXED-height host row is bounded
            // by the host cell's inner bottom — rows that do not fit there go
            // to a continuation slice nobody consumes (a 35.1 pt
            // second logo row never draws inside the 27 pt host row).
            var gridBottom = rc.generatorCell && rc.row.FixedRowHeight > 0
                ? Math.Max(_curPageBottom, rc.slice.TopY - rc.slice.Height
                    + (rc.pitchBorder is not null ? SideInsets(rc.pitchBorder, half: false).b : 0))
                : _curPageBottom;
            try
            {
                ct.Slices = innerT.BuildMultiPage(_buildPage!, tabTopY,
                    gridBottom, _curFreshTopMargin);
            }
            catch { ct.Slices = new List<byte[]>(); }
            ct.Consumed = 0;
        }
        if (ct.Slices.Count > ct.Consumed)
        {
            // Splice the grid's next page slice into THIS stream, right
            // where its cell draws, instead of appending it to the page as
            // a separate stream: the page is then laid out in document
            // order for anything that reads the operators back (a text
            // absorber walks streams in order, and a whole grid arriving
            // early shifts every fragment index after it).
            var s = ct.Consumed++;
            rc.builder.AppendStream(ct.Slices[s]);
            if (rc.graphSink is not null && innerT.LastGraphDraws.Count > s)
                foreach (var ig in innerT.LastGraphDraws[s]) rc.graphSink.Add(ig);
            if (rc.imageSink is not null && innerT.LastImageDraws.Count > s)
                foreach (var im in innerT.LastImageDraws[s]) rc.imageSink.Add(im);
        }
        return true;
    }
}
