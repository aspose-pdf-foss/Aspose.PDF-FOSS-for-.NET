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
    /// <summary>The object stage of a row-plan paragraph: nested tables, images and form controls, verbatim. Returns true when the paragraph is done.</summary>
    private bool RowPlanParagraphObjects(BaseParagraph paragraph, RowPlanColumnState pc, RowPlanState rp, int col, Row row, double[] colWidths, int[] cellMap, int[]? gridToCell, int[]? effRowSpan, double svgFillHeight)
    {
        StampLeading(pc.lines, pc.paraLineStart, pc.paraLeading);
        StampDeclaredLineBox(pc.lines, pc.paraLineStart, pc.paraLineBox);
        pc.paraLineStart = pc.lines.Count;
        pc.paraLeading = XmlGeneratorModel ? XmlLineSpacing : CallerLineSpacing(paragraph, pc.cell, row);
        pc.paraLineBox = DeclaredCellLineBox(paragraph);
        // A nested TABLE declares its vertical margin like any other cell
        // paragraph — the grid it draws is inset by it (an inner grid that
        // asks for 8 pt above itself gets it).
        if (GeneratorCellModel && paragraph is TextFragment or HtmlFragment or Table)
        {
            var genMargin = ParagraphMargin(paragraph);
            var genGap = pc.genPendingBottom + Math.Max(0, genMargin?.Top ?? 0);
            pc.genPendingBottom = Math.Max(0, genMargin?.Bottom ?? 0);
            if (genGap > 0)
                pc.lines.Add(new CellLine { Text = "", BoxH = genGap, MarginSpacer = true });
        }
        // Nested table: flatten each inner row into one line per row so
        // height accounting and pagination see the inner content. Cell
        // text from each inner cell is joined with " | " as a visual
        // separator; proper nested-table rendering would need its own
        // slice pass, but this keeps pagination honest.
        if (paragraph is Table inner)
        {
            if (PlanInnerTableParagraph(inner, pc, rp, col, row) is { } planInnerTableParagraphResult) return planInnerTableParagraphResult;
        }

        // A reserved block in a cell is a box of the height its caller lays it out at, kept
        // as a picture's box is; where the cell puts it is reported when the page is drawn.
        if (paragraph is ReservedBlock reserved)
        {
            PlanReservedBlockParagraph(reserved, pc, rp, col);
            return true;
        }

        // An Image paragraph in a cell is a variable-height block. Resolve its
        // display size (explicit Fix* or natural, fit to the cell width), reserve
        // matching vertical space as blank lines so the row's height budget and
        // pagination cover it, and stash the bytes for the render pass to blit.
        if (paragraph is Image cellImg)
        {
            if (PlanImageParagraph(cellImg, pc, rp, col, row, colWidths, svgFillHeight) is { } planImageParagraphResult) return planImageParagraphResult;
        }

        // A FloatingBox seated in a cell holds a positioned picture: out of the
        // cell's flow, it draws from the cell's content origin and leaves the
        // row's height to the in-flow content.
        if (paragraph is FloatingBox seat && seat.Paragraphs.Count == 1 && seat.Paragraphs[0] is Image seatImg)
        {
            PlanSeatedImage(seat, seatImg, pc, rp, col);
            return true;
        }

        // A radio-button option in a cell renders as a glyph (circle) followed
        // by its caption. Emit one line carrying the option so the row's height
        // budget covers the glyph and the render pass can draw it.
        if (paragraph is Aspose.Pdf.Forms.RadioButtonOptionField opt)
        {
            var capSize = opt.Caption?.TextState.FontSize > 0
                ? opt.Caption!.TextState.FontSize
                : pc.defaultFontSize;
            var glyphH = opt.Height > 0 ? opt.Height : capSize;
            // A control row is sized to its glyph/caption without the extra
            // text leading — the glyph is a fixed box, not a line of type.
            var lh = Math.Max(glyphH, capSize);
            Consider(rp, lh, lh);
            pc.lines.Add(new CellLine
            {
                Text = opt.Caption?.Text ?? "",
                FontSize = capSize,
                ForegroundColor = opt.Caption?.TextState.ForegroundColor ?? pc.textState?.ForegroundColor,
                Option = opt,
            });
            return true;
        }

        // A checkbox in a cell occupies a fixed glyph box; record a control line so
        // the row height covers it and the render pass repositions its widget.
        if (paragraph is Aspose.Pdf.Forms.CheckboxField cbf)
        {
            if (HtmlUaControlGrid)
            {
                // the UA inline checkbox: a line box of the em plus the strut descent, the
                // margin box centred in an align=center cell (measured on the worksheet)
                var uaFs = HtmlUaControlFontPt > 0 ? HtmlUaControlFontPt : pc.defaultFontSize;
                var (uaAbove, uaBelow) = UaCheckboxLine(uaFs);
                var uaLineH = uaAbove + uaBelow;
                Consider(rp, uaLineH, uaLineH);
                var uaIndent = pc.cellAlign == HorizontalAlignment.Center
                    ? Math.Max(0, (pc.availWidth - UaCheckboxMarginBoxPt) / 2) : 0;
                pc.lines.Add(new CellLine { Text = "", FontSize = uaFs, Checkbox = cbf, BoxH = uaLineH, Leading = uaLineH - uaFs, HtmlEngine = true, LeftIndent = uaIndent, Align = pc.cellAlign });
                return true;
            }
            var boxH = cbf.Height > 0 ? cbf.Height : pc.defaultFontSize;
            Consider(rp, boxH, boxH);
            pc.lines.Add(new CellLine { Text = "", FontSize = pc.defaultFontSize, Checkbox = cbf });
            return true;
        }

        return false;
    }
}
