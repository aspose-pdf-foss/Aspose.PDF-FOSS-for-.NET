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
    /// <summary>Emit content for the slices that landed on the current page.</summary>
    private byte[] BuildSlicesContent(List<RowSlice> slices, double[] colWidths,
        double tableX, string fontName, int[] cellMap, Page? linkPage = null,
        List<SpanBlock>? spanBlocks = null)
    {
        // A measure-only build (page-break pre-flight) must not add annotations or
        // widgets to the page — only the real build emits them.
        if (_measureOnly) linkPage = null;
        var slc = new SlicesContentState();
        slc.builder = new ContentStreamBuilder();
        slc.links = linkPage is not null ? new List<(Rectangle rect, Hyperlink link)>() : null;
        slc.optionSink = linkPage is not null
            ? new List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>() : null;
        slc.checkboxSink = _measureOnly
            ? null : new List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>();
        slc.pageImages = new List<(byte[] data, Rectangle rect)>();
        slc.pageBlocks = new List<(ReservedBlock block, ReservedPart part, Rectangle rect)>();
        slc.pageGraphs = new List<byte[]>();
        slc.pageFootnotes = _measureOnly
            ? null : new List<(Note note, double x, double baseline, double size)>();
        // See HtmlSpaceClassFirst: unify space/no-break-space on the laid-out lines. The
        // lines are post-wrap and both characters measure at the space width, so this
        // changes only which of the two identical-looking characters the page carries.
        if (HtmlSpaceClassFirst is ' ' or ' ')
            foreach (var slice in slices)
                foreach (var cellLines in slice.Plan.CellLines)
                    foreach (var cl in cellLines)
                        if (cl.Text is { Length: > 0 })
                            cl.Text = HtmlSpaceClassFirst == ' '
                                ? cl.Text.Replace(' ', ' ')
                                : cl.Text.Replace(' ', ' ');
        slc.macroSwaps = SubstitutePageMacros(slices);
        slc.builder.SaveState();
        if (CornerRadii is not null && BackgroundColor is not null && slices.Count > 0)
            PaintRoundedGridBackground(slc.builder, slices, colWidths, tableX);
        slc.spanContent = new List<(SpanBlock block, double x, double w, double h,
            double top, double bottom, int contentRow)>();
        if (spanBlocks is { Count: > 0 })
        {
            PlaceSpanBlocks(slc, slices, colWidths, tableX, spanBlocks);
        }
        foreach (var slice in slices)
        {
            // A collapsed grid closes on the last row it puts on THIS page, not
            // only on the last row of the table: a grid cut by a page break is
            // ruled off at the cut, and picked up again overleaf.
            _sliceClosesPage = ReferenceEquals(slice, slices[slices.Count - 1]);
            // A block that opens its last row is written ahead of that row's own
            // cells; one that sits further along follows them.
            foreach (var sc in slc.spanContent)
                if (sc.contentRow == slice.RowIndex && SpanWrittenAheadOfRow(sc.block, sc.contentRow))
                    EmitSpanBlockContent(slc, fontName, linkPage, sc.block, sc.x, sc.w, sc.h, sc.top, sc.bottom);
            RenderRowSlice(slc.builder, slice, colWidths, tableX, fontName, cellMap, slc.links, slc.pageImages, slc.optionSink, slc.pageGraphs, slc.checkboxSink, linkPage, slc.pageFootnotes, slc.pageBlocks);
            foreach (var sc in slc.spanContent)
                if (sc.contentRow == slice.RowIndex && !SpanWrittenAheadOfRow(sc.block, sc.contentRow))
                    EmitSpanBlockContent(slc, fontName, linkPage, sc.block, sc.x, sc.w, sc.h, sc.top, sc.bottom);
        }
        _pageImages.Add(slc.pageImages);
        _pageBlocks.Add(slc.pageBlocks);
        _pageGraphs.Add(slc.pageGraphs);
        _pageFootnotes.Add(slc.pageFootnotes ?? new List<(Note, double, double, double)>());

        // Row-spanning cells: draw each block once over the union of its rows' slices
        // on this page. A block split by a page break re-draws its background, border
        // and (re-centred) content in the portion visible on each page — matching the
        // generator's continuation rendering.

        // Outer table.Border wraps the slices that landed on this page.
        // Drawn after slices so it sits on top of cell backgrounds/borders.
        // (A resolved collapsed grid has none: its border took part in the boundaries.)
        if (Border is not null && slices.Count > 0 && CollapsedRulesFor(colWidths.Length) is null)
        {
            // The border wraps the grid's BOX: the last column's box keeps its pitched
            // width past the band (see LastColBoxOverhang), and the frame follows it
            // (probed: a "400 50" grid with 0.5 pt rules in a 415 band frames 417).
            // (a spaced grid's frame wraps its gaps as well as its columns)
            var totalWidth = LastColBoxOverhang + GridBoxWidth(colWidths);
            var (topY, bottomY) = GridBoxSpan(slices);
            // The frame strokes ON the column block's edge: its mid-line is the block
            // boundary, so a 0.5 pt frame over 0.5 pt cell rules leaves one 1 pt band
            // from the boundary inward (the corpus templates - a 2025 one among them -
            // draw it there; the 2026 binary strokes it half a width further out, which
            // the templates reject). DrawBorder insets every side by half a width
            // inside the box it is given, so the box is the block grown by half a
            // width all round. A form grid's frame lies fully outside instead.
            // A grid whose rules stand inside its column widths is framed the same
            // way -- the frame is the grid's own box, wholly outside the cells' boxes --
            // and, like its cells' rules, as filled bands.
            var outerWidth = OuterBorderWidth();
            if (RulesInsideColumnWidth)
            {
                var frame = SeparatedFrameBox(slices, colWidths, tableX);
                if (CornerRadii is { } corners)
                    RulePainter.PaintRoundedBox(slc.builder, Border, frame.X, frame.Y, frame.W, frame.H,
                        corners.Resolve(frame.W, frame.H), null, frame, null);
                else
                    DrawRulesInsideBox(slc.builder, Border, frame.X, frame.Y, frame.W, frame.H);
            }
            else if (FormGridCells)
                DrawFormGridBorder(slc.builder, Border, tableX - outerWidth, bottomY - outerWidth,
                    totalWidth + 2 * outerWidth, topY - bottomY + 2 * outerWidth);
            else
                DrawBorder(slc.builder, Border, tableX - outerWidth / 2, bottomY - outerWidth / 2,
                    totalWidth + outerWidth, topY - bottomY + outerWidth);
        }
        slc.builder.RestoreState();

        RegisterSliceLinks(slc, linkPage);
        return slc.builder.Build();
    }
}
