using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The render stage of an HTML conversion: page creation, the block render loop, links and running bands, verbatim.</summary>
    private static Document ConvertRenderPages(HtmlLoadOptions? options, ConvertState cv)
    {
        ResolvePageGeometry(cv);

        // UA flow: the page WIDTH also widens for a block image wider than the content
        // box — the image keeps its natural pixel size and the page expands
        // (753px image on default A4 → 90+6+564.75+90 = 750.75pt wide).
        // Word-filtered pages keep their measured sheet: an 817px absolutely
        // positioned banner does NOT grow the page (it overflows and clips).
        ResolveUaFlowAndFaces(cv, options);

        // DataWorks form flow: UA serif at the 16px base (12 pt body, 2 em h1),
        // the classic link styling (#0000EE + underline), hollow bullets past
        // the first list level, and the browser 1.125 em line box.
        ResolveFormDocLayout(cv);

        OpenDocumentAndSeatFlow(cv);
        // Standards mode charges the LEADING PARAGRAPH's UA top margin at the canvas,
        // where the quirks calibration collapses it away (measured A/B:
        // the same body seats its first 12 pt <p> baseline at 96.24 with a DOCTYPE and
        // 88.80 without; a document opening with BARE TEXT keeps the quirks seat either
        // way, so the charge belongs to the element, not the mode alone). Scoped to the
        // UA-serif flow with the default calibrated margins and a plain-face leading
        // paragraph — headings charge a different margin and keep their own model.
        SeatUaSerifTopMargin(cv);
        ResolveEscapedAttrDialect(cv);
        ResetFlowCollections(cv, options);
        // Set right after a form-dialect <hr> draws: the next text block must drop by
        // its own line box before its baseline (text hangs above the cursor; the rule
        // would be overprinted otherwise). Tables/images consume the flag as a no-op.
        ResetFlowScopes(cv);
        ResolveDeadCssAndDialects(cv);
        LayOutBlocks(cv, options);

        // Radio groups the GRID tables built through the factory: their options'
        // widgets were placed at the drawn glyphs by the table render pass; the
        // groups themselves surface on Form.Fields here.
        FinishRadioGroupsFooterAndLinks(cv, options);

        // The sheet is sized from the ink the FLOW drew, and so before the canvas is
        // painted: the body background covers the whole content box, so once it is down
        // it is always the rightmost ink and the measure would only read the page back.
        GrowSheetToInk(cv);

        // A body background colour paints the page canvas behind everything else: the
        // BODY box's background propagates to the canvas, so it covers the page's whole
        // content box (page margins, not the 6pt UA body margin the left/top defaults
        // bake in) on every page of the conversion. Prepended so the flow's own content
        // — text, rules, cell fills — draws over it.
        FinishCanvasAndPageFit(cv, options);

        return cv.doc;
    }
}
