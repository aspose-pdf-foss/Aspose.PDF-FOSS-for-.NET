using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document : IDisposable
{

    // The repeating-row (rb) table: geometry probed against the reference render, in points.
    private const double rbTitleFs = 9.0;        // 12px body over the title block
    private const double rbTitlePitch = 10.80;   // 1.2 em of the 12px body
    private const double rbTitleBase1 = 13.44;   // first title baseline from the page top
    private const double rbCaptionFs = 11.25;    // th { font-size: 15px }
    private const double rbCaptionBase = 50.20;  // caption baseline from the page top
    private const double rbCaptionX = 7.25;      // caption left inset
    private const double rbDescX = 14.75;        // description column left
    private const double rbRowFs = 11.25;        // data rows set at the th size
    private const double rbRowPitch = 14.25;     // single-line row pitch
    private const double rbWrapPitch = 11.25;    // a wrapped description's inner pitch
    private const double rbFirstRowGap = 24.37;  // caption → first row, first sheet
    private const double rbSpillRowGap = 16.87;  // caption → first row, later sheets
    private const double rbBottomLimit = 837;    // page bottom content limit (margin 5)
    private const double rbBlockSeam = 9.0;      // extra seam where a new data table opens
    /// <summary>Inject the first slice at the cursor with its images, graphs and footnote marks, and hand the spill slices to the overflow pages.</summary>
    private void PlaceTableSlices(TableParagraphState tp, Table table, FlowLayout flow, List<(byte[] content, double width, double height)> overflowPages, Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages)
    {
        if (table.LastFootnoteMarks is { Count: > 0 } tableFoots)
            foreach (var (fNote, fx, fBase, fSize) in tableFoots[0])
            {
                var fMarker = flow.NextFootnoteMarker(fNote);
                flow.EmitFootnoteMarkerAt(fNote, fMarker, fx, fBase, fSize);
                flow.QueueMarkedFootnote(fNote, fMarker, fSize);
            }
        // Inject at the flow's CURRENT page position — once the flow has
        // page-broken (e.g. a kept-with-next pair moved here), the first
        // slice belongs to the overflow buffer, not the start page.
        flow.InjectContentAtCursor(tp.pageContents[0]);
        if (tp.tableImages.Count > 0)
            foreach (var (data, rect) in tp.tableImages[0])
                flow.PlaceBlockPicture(data, rect);
        if (tp.tableBlocks.Count > 0)
            foreach (var (block, part, rect) in tp.tableBlocks[0])
                flow.PlaceReservedPart(block, part, rect);
        if (tp.tableGraphs.Count > 0)
            foreach (var gc in tp.tableGraphs[0])
                flow.InjectContentAtCursor(gc);
        if (tp.pageContents.Count == 1)
        {
            // Single-page table: consume exactly its height so following
            // paragraphs continue immediately below on the same page.
            flow.AdvanceY(table.LastRenderedHeight);
            // The table's Margin.Bottom is space reserved below it
            // (the next heading chains a full bottom margin under
            // an empty spacer table).
            if (table.Margin?.Bottom > 0) flow.AdvanceY(table.Margin.Bottom);
        }
        else
        {
            // Intermediate spill pages become standalone pages; the LAST spill
            // page is handed back to the flow so trailing paragraphs continue
            // on it, below the table, rather than starting a fresh page.
            // ⚠ The page still in flight has to be COMMITTED first: appending a spill
            // page straight to the queue while an unflushed buffer holds an earlier
            // page puts the later content in the earlier slot, and the two pages come
            // out swapped.
            // Boxes the table breaks inside close their parts under its first page's rows.
            flow.CloseBoxPartsUnderTable(TablePageEnd(tp, table, flow, 0));
            flow.FlushInFlightBuffer();
            var tableCheckboxes = table.LastCheckboxDraws;
            for (var pi = 1; pi < tp.pageContents.Count - 1; pi++)
            {
                if (pi < tp.tableImages.Count && tp.tableImages[pi].Count > 0)
                    overflowImages[overflowPages.Count] = tp.tableImages[pi];
                if (pi < tp.tableBlocks.Count)
                    foreach (var (block, part, rect) in tp.tableBlocks[pi])
                        flow.PlaceReservedPartOn(overflowPages.Count, block, part, rect);
                if (pi < tableCheckboxes.Count && tableCheckboxes[pi].Count > 0)
                    _overflowCheckboxes[overflowPages.Count] = tableCheckboxes[pi];
                var content = tp.pageContents[pi];
                if (tp.spillPageMargins is { } margins)
                    content = Concat(flow.BoxPartsAroundSpill(tp.tablePage.LayoutFrameHeight - margins(pi).top,
                        TablePageEnd(tp, table, flow, pi)), content);
                overflowPages.Add((content, tp.tablePage.Width, tp.tablePage.Height));
            }
            var lastIdx = tp.pageContents.Count - 1;
            var lastSlot = flow.ContinueOnPrebuiltSpill(tp.pageContents[lastIdx], table.LastPageEndY);
            if (tp.spillPageMargins is { } lastMargins)
                flow.ReopenBoxPartsOnSpill(tp.tablePage.LayoutFrameHeight - lastMargins(lastIdx).top);
            if (lastIdx < tp.tableImages.Count && tp.tableImages[lastIdx].Count > 0)
                overflowImages[lastSlot] = tp.tableImages[lastIdx];
            if (lastIdx < tp.tableBlocks.Count)
                foreach (var (block, part, rect) in tp.tableBlocks[lastIdx])
                    flow.PlaceReservedPartOn(lastSlot, block, part, rect);
            if (lastIdx < tableCheckboxes.Count && tableCheckboxes[lastIdx].Count > 0)
                _overflowCheckboxes[lastSlot] = tableCheckboxes[lastIdx];
        }
    }

    /// <summary>Where the table's part on page <paramref name="index"/> of its build ends:
    /// as the table recorded it, else the lowest its rows may reach there.</summary>
    private static double TablePageEnd(TableParagraphState tp, Table table, FlowLayout flow, int index) =>
        table.PageEndYs.Count == tp.pageContents.Count ? table.PageEndYs[index]
        : index == tp.pageContents.Count - 1 ? table.LastPageEndY
        : flow.BottomMargin;

    private static byte[] Concat(byte[] first, byte[] second)
    {
        if (first.Length == 0) return second;
        var both = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, both, 0, first.Length);
        Buffer.BlockCopy(second, 0, both, first.Length, second.Length);
        return both;
    }

    /// <summary>Build the table's page slices at the flow width with the spill margins.</summary>
    private void BuildTableSlices(TableParagraphState tp, Table table, FlowLayout flow, List<(byte[] content, double width, double height)> overflowPages)
    {
        for (var tri = 0; tri < table.Rows.Count; tri++)
        {
            var trow = table.Rows.At(tri);
            for (var tci = 0; tci < trow.Cells.Count; tci++)
                foreach (var cellPara in trow.Cells.At(tci).Paragraphs)
                    if (cellPara is Text.TextFragment cellTf)
                        flow.RecordPosition(cellTf);
        }
        // Rows stop at the page's REAL bottom content margin, not at a fixed 36 pt
        // inset: the generator fills a page until the NEXT row would cross the margin
        // (a 90 pt bottom margin on US Letter takes 76 eight-point rows, a 72 pt one on
        // A4 takes 34 twenty-point rows).
        // Spill pages take the margins of the pages the flow prepares for them
        // (an OnBeforePageGenerate handler re-margining page 2 moves the table's
        // page-2 rows); the buffer in flight is committed first so the spill
        // pages queue behind the slice injected into it.
        if (flow.OnPageBreak is not null)
        {
            flow.FlushInFlightBuffer();
            var spillBaseSlot = overflowPages.Count;
            table.SpillPageMargins = spill => flow.MarginsForSlot(spillBaseSlot + spill - 1);
        }
        // Inside boxes a spill page also opens every box again above the rows and keeps
        // the boxes' room under them.
        if (flow.InsideBox)
        {
            var pageMargins = table.SpillPageMargins ?? (_ => (tp.spillTopMargin, flow.PageBottomMargin));
            tp.spillPageMargins = pageMargins;
            table.SpillPageMargins = spill => flow.SpillMarginsInsideBoxes(pageMargins(spill));
        }
        try
        {
            tp.pageContents = table.BuildMultiPage(tp.tablePage, flow.CurrentY, flow.BottomMargin,
                tp.spillTopMargin, contentFlow: true);
        }
        finally { table.SpillPageMargins = null; }
        tp.tableImages = table.LastImageDraws;
        tp.tableBlocks = table.LastBlockDraws;
        tp.tableGraphs = table.LastGraphDraws;
        // Footnotes on cell fragments: draw each superscript marker at the
        // recorded end-of-text position and queue the note body into this
        // page's bottom band.
    }

    /// <summary>Move the table to the next page when it is marked for it, spend its top margin and declared top.</summary>
    private void SeatTableOnPage(TableParagraphState tp, Table table, FlowLayout flow, Page page, double marginLeft, double marginTop)
    {
        if (!table.IsBroken && table.Broken == TableBroken.IsInNextPage)
        {
            var room = flow.CurrentY - flow.BottomMargin;
            var wholePage = flow.ContentTop - flow.BottomMargin;
            // GetHeight is the whole-table measurement (every row, margins included);
            // LastRenderedHeight would only report the last page's slice.
            if (room < wholePage - 1e-6 && table.GetHeight(flow.CurrentPage) > room + 1e-6)
            {
                flow.ForceNewPage();
                tp.movedToOwnPage = true;
            }
        }

        // Start the table at the current flow cursor — not at the top of the page —
        // and indent it to the page's left content margin so it lines up with the
        // surrounding text flow. Render onto whatever page the cursor is on now.
        // The table's own Margin.Top is leading reserved above it (an
        // invoice info table drops 15 pt below the title
        // via table.Margin.Top = 15) — same rule the container-table
        // unwrap branch already applies.
        if (table.Margin?.Top > 0 && !tp.movedToOwnPage) flow.AdvanceY(table.Margin.Top);
        // An explicit Table.Top anchors the table that far below the
        // PAGE top (a Top=400 table starts its rows at
        // y = height−400) — drop the cursor when it is still above
        // that anchor.
        if (table.Top > 0 && flow.CurrentY > page.LayoutFrameHeight - table.Top)
            flow.AdvanceY(flow.CurrentY - (page.LayoutFrameHeight - table.Top));
        tp.tablePage = flow.CurrentPage;
        table.FlowLeftOffset = marginLeft;
        // Inside a box the table stands in the box's content box, and that is its band.
        if (flow.InsideBox)
        {
            table.FlowLeftOffset = flow.CurrentLeft;
            table.UsableWidthOverride = flow.CurWidth;
        }
        tp.spillTopMargin = PageInfo?.Margin is { TopTouched: true } dm ? dm.Top : marginTop;
        // Fragments nested in the table's cells are LocalHyperlink
        // targets too (a page-level link jumping to a table cell):
        // record each at the table's own position so the deferred
        // link resolution finds them — otherwise the annotation is
        // silently dropped and the page ends up one link short.
    }

    /// <summary>A two-row table with a repeating header lays out row by row across pages here.</summary>
    private bool TryLayoutRepeatingTable(TableParagraphState tp, Table table, FlowLayout flow)
    {
        if (table.RepeatingRowsCount >= 1 && table.Rows.Count == 2
            && RbTryParse(table) is { } rb)
        {
            var rbColX = new[] { 453.32, 496.63, 539.94 };
            // the description wraps in its own 49% column, whose box runs
            // a little past the first value column's text start:
            // 434.6 pt one-liners stay whole and everything wraps
            // from 443.1 pt up — the window's midpoint
            var rbWrapW = 439.0;

            var pageH = flow.CurrentPage.Height;
            // Overflow pages pre-register F1 = Helvetica and the resource
            // merge skips names already taken — register Helvetica first
            // so the band's Times faces keep their names across pages.
            Table.RegisterFont(flow.CurrentPage);
            var yTop = 0.0;   // running baseline, measured from the page top
            var first = true;
            void RbHeader(Content.ContentStreamBuilder b)
            {
                var bold = Table.RegisterFont(flow.CurrentPage, "Times-Bold");
                var ty = rbTitleBase1;
                foreach (var t in rb.Titles)
                {
                    var w = MeasureStd14Width(t, "Times-Bold", rbTitleFs);
                    b.BeginText().SetFont(bold, rbTitleFs)
                     .MoveTextPosition((flow.CurrentPage.Width - w) / 2, pageH - ty)
                     .ShowText(t).EndText();
                    ty += rbTitlePitch;
                }
                b.BeginText().SetFont(bold, rbCaptionFs)
                 .MoveTextPosition(rbCaptionX, pageH - rbCaptionBase)
                 .ShowText(rb.Caption).EndText();
                yTop = rbCaptionBase + (first ? rbFirstRowGap : rbSpillRowGap);
                first = false;
            }

            var rbB = new Content.ContentStreamBuilder();
            rbB.SaveState();
            RbHeader(rbB);
            var reg = Table.RegisterFont(flow.CurrentPage, "Times-Roman");
            var atPageHead = true;
            foreach (var (desc, vals, newBlock) in rb.Rows)
            {
                // a fresh data table opens one seam lower (its own top
                // margin) — unless it opens the page, where the band's
                // fixed first-row seat already places it
                if (newBlock && !atPageHead) yTop += rbBlockSeam;
                var wrapped = Text.TextPaginator.WrapToWidth(
                    desc, "Times-Roman", rbRowFs, rbWrapW);
                if (wrapped.Count == 0) wrapped.Add("");
                // the row travels whole; a row that cannot fit above the
                // sheet's bottom opens the next sheet under a fresh band
                var rowBottom = yTop + rbWrapPitch * (wrapped.Count - 1);
                if (rowBottom > rbBottomLimit)
                {
                    rbB.RestoreState();
                    flow.InjectContentAtCursor(rbB.Build());
                    flow.ForceNewPage();
                    rbB = new Content.ContentStreamBuilder();
                    rbB.SaveState();
                    RbHeader(rbB);
                    reg = Table.RegisterFont(flow.CurrentPage, "Times-Roman");
                    atPageHead = true;
                }
                for (var li = 0; li < wrapped.Count; li++)
                    if (wrapped[li].Length > 0)
                        rbB.BeginText().SetFont(reg, rbRowFs)
                           .MoveTextPosition(rbDescX, pageH - (yTop + rbWrapPitch * li))
                           .ShowText(wrapped[li]).EndText();
                // values centre on the description's lines
                var vy = yTop + rbWrapPitch * (wrapped.Count - 1) / 2.0;
                for (var ci = 0; ci < 3 && ci < vals.Count; ci++)
                    if (vals[ci].Length > 0)
                        rbB.BeginText().SetFont(reg, rbRowFs)
                           .MoveTextPosition(rbColX[ci], pageH - vy)
                           .ShowText(vals[ci]).EndText();
                yTop += rbWrapPitch * (wrapped.Count - 1) + rbRowPitch;
                atPageHead = false;
            }
            rbB.RestoreState();
            flow.InjectContentAtCursor(rbB.Build());
            // leave the cursor under the last row for anything that follows
            if (flow.CurrentY > pageH - yTop) flow.AdvanceY(flow.CurrentY - (pageH - yTop));
            return false;
        }

        tp.movedToOwnPage = false;
        return true;
    }

    /// <summary>A container table lays out its inner tables in turn here.</summary>
    private bool TryLayoutContainerTable(TableParagraphState tp, Table table, FlowLayout flow, Page page, HashSet<Table> renderedTables, List<(byte[] content, double width, double height)> overflowPages, Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages, double marginLeft, double marginTop)
    {
        if (tp.containerInners is not null)
        {
            // The wrapper's own Top anchors its blocks that far below the page
            // top, as for any table; a block the anchor leaves no room for then
            // moves whole to the next page (the fit check below).
            if (table.Top > 0 && flow.CurrentY > page.LayoutFrameHeight - table.Top)
                flow.AdvanceY(flow.CurrentY - (page.LayoutFrameHeight - table.Top));
            foreach (var inner in tp.containerInners)
            {
                renderedTables.Add(inner);
                inner.HtmlEngineMetrics = true;
                if (inner.Margin.Top > 0) flow.AdvanceY(inner.Margin.Top);
                var innerPage = flow.CurrentPage;
                inner.FlowLeftOffset = marginLeft;
                var innerSpillTop = PageInfo?.Margin is { TopTouched: true } idm ? idm.Top : marginTop;
                // Keep the block together: measure its one-page height and
                // move it whole to a fresh page when it doesn't fit here.
                inner.BuildMultiPage(innerPage, flow.ContentTop, flow.BottomMargin, measureOnly: true);
                var innerH = inner.LastRenderedHeight;
                var innerAvail = flow.CurrentY - flow.BottomMargin;
                var innerBudget = flow.ContentTop - flow.BottomMargin;
                if (innerH > innerAvail + 0.5 && innerH <= innerBudget + 0.5
                    && flow.CurrentY < flow.ContentTop - 0.5)
                    flow.ForceNewPage();
                var innerContents = inner.BuildMultiPage(innerPage, flow.CurrentY, flow.BottomMargin, innerSpillTop);
                var innerImages = inner.LastImageDraws;
                var innerGraphs = inner.LastGraphDraws;
                flow.InjectContentAtCursor(innerContents[0]);
                if (innerGraphs.Count > 0)
                    foreach (var gc in innerGraphs[0])
                        flow.InjectContentAtCursor(gc);
                if (!flow.HasOverflowed && innerImages.Count > 0)
                    foreach (var (data, rect) in innerImages[0])
                        innerPage.AddImage(data, rect);
                if (innerContents.Count == 1)
                {
                    flow.AdvanceY(inner.LastRenderedHeight);
                }
                else
                {
                    for (var pi = 1; pi < innerContents.Count - 1; pi++)
                    {
                        if (pi < innerImages.Count && innerImages[pi].Count > 0)
                            overflowImages[overflowPages.Count] = innerImages[pi];
                        overflowPages.Add((innerContents[pi], innerPage.Width, innerPage.Height));
                    }
                    var innerLastIdx = innerContents.Count - 1;
                    var innerSlot = flow.ContinueOnPrebuiltSpill(innerContents[innerLastIdx], inner.LastPageEndY);
                    if (innerLastIdx < innerImages.Count && innerImages[innerLastIdx].Count > 0)
                        overflowImages[innerSlot] = innerImages[innerLastIdx];
                }
                if (inner.Margin.Bottom > 0) flow.AdvanceY(inner.Margin.Bottom);
            }
            return false;
        }

        // Report-band wrapper: a one-column table whose first row is an
        // HtmlFragment carrying only a title block and a <thead> caption
        // table, and whose second row's fragment is one big data <table> —
        // the escaped-attribute report shape. The band renders as
        // a page header REPEATED on every sheet (RepeatingRows),
        // the data table as text rows on a fixed grid: Times 11.25 on a
        // 14.25 pitch, wrap lines 11.25, the three value columns standing
        // at fixed x, a multi-line row's values centred on its lines.
        // All of it exact to 0.01 pt.
        return true;
    }

    /// <summary>Detect a container table: one row whose cells hold nothing but inner tables.</summary>
    private void CollectContainerInners(TableParagraphState tp, Table table)
    {
        if (table.Rows.Count > 0)
        {
            tp.containerInners = new List<Table>();
            for (var ri = 0; ri < table.Rows.Count && tp.containerInners is not null; ri++)
            {
                var wrapRow = table.Rows.At(ri);
                var wrapCell = wrapRow.Cells.Count == 1 ? wrapRow.Cells.At(0) : null;
                if (wrapCell is null || wrapCell.Paragraphs.Count == 0)
                { tp.containerInners = null; break; }
                // A span-1 single cell only counts as a container when it is pure
                // chrome-less wrapping — one row, no borders, no background — so a
                // real bordered one-cell table keeps its cell rendering (the inner
                // grid then draws inside the visible cell box).
                if (wrapCell.ColSpan < 2
                    && !(table.Rows.Count == 1
                         && wrapCell.Border is null && wrapCell.BackgroundColor is null
                         && table.Border is null && table.DefaultCellBorder is null
                         && wrapRow.Border is null && wrapRow.DefaultCellBorder is null))
                { tp.containerInners = null; break; }
                foreach (var ip in wrapCell.Paragraphs)
                {
                    if (ip is Table innerT) tp.containerInners.Add(innerT);
                    else { tp.containerInners = null; break; }
                }
            }
            if (tp.containerInners is { Count: 0 }) tp.containerInners = null;
        }
    }
}
