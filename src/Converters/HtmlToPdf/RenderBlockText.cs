using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The text stage of a block render: padding, line layout and the trailing cursor state, verbatim.</summary>
    private static void RenderBlockText(ConvertState cv, RenderBlockState rb, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Block block)
    {
        // Padding is box space, not margin — it never collapses.
        var yBeforeSpacing = cv.flow.y;
        ApplyBlockTopSpacing(cv, rb, block);
        ResolveBlockWidth(cv, rb, block);
        WrapBlockLines(cv, rb, block);
        // ASPOSE_TRACE_SEAT=1: the vertical chain of every text block on the sheet-typography flow.
        if ((cv.profile.sheetTypographyDoc || cv.profile.escapedAttrDoc || cv.profile.uaStdSerif || cv.profile.wordMailDoc) && Environment.GetEnvironmentVariable("ASPOSE_TRACE_SEAT") == "1")
            Console.WriteLine(FormattableString.Invariant(
                $"[seat] cw={cv.flow.contentWidth:0.##} avail={rb.availWidth:0.##} li={block.LeftIndent:0.##} ri={block.RightInsetPt:0.##} top={cv.pageHeight - cv.marginTop:0.##} entry={rb.yAtBlockEntry:0.##} afterDrop={yBeforeSpacing:0.##} seat={cv.flow.y:0.##} fs={rb.metrics.blockFontSize:0.##} lh={rb.metrics.lineHeight:0.##} mt={block.MarginTop:0.##} mb={block.MarginBottom:0.##} prevLh={cv.flow.prevFlowLineHeight:0.##} prevFs={cv.flow.prevFlowFontSize:0.##} prevText={cv.flow.prevBlockWasText} hb={cv.flow.lastWasHardBreak} fam={block.FontFamily} mw={block.MaxWidthPt:0.##} band={rb.metrics.bandFace} qw={cv.quirksWrapW:0.##} fl={rb.floatLines} ua={cv.uaFlow} mf={cv.profile.metricFlow} md={rb.metrics.metricDrop:0.#} mface={rb.metrics.metricMeasureFace} pts={cv.profile.ptStyledFragment} l0w={(rb.metrics.lines.Length > 0 && !string.IsNullOrEmpty(block.FontFamily) ? MeasureFaceText(block.FontFamily!, rb.metrics.lines[0], rb.metrics.blockFontSize) : 0):0.#} lines={rb.metrics.lines.Length} '{(block.Text.Length > 24 ? block.Text[..24] : block.Text)}'"));
        ResolveBackgroundImage(block, options);
        WriteBlockLines(cv, rb, block);
        // The sheet-typography flow: the lines stepped one plain ascent side past the last
        // baseline's descent side; give it back so the cursor is the block's box bottom.
        if (cv.profile.sheetBoxFlow && rb.metrics.blockFontSize > 0 && !string.IsNullOrEmpty(block.Text)
            && block.FontFamily is { Length: > 0 } bottomFace && WinMetricsFor(bottomFace) is not null)
            cv.flow.y += LineBoxAbove(bottomFace, rb.metrics.blockFontSize, rb.metrics.lineHeight);
        DrawBlockBoxes(cv, rb, block);
        DrawDeclaredBoxAndInlineIcon(cv, rb, block);
        if (rb.coverDrop > 0 && !(cv.profile.floatBothSidesDoc && rb.paddingBelow > 0)) cv.flow.y += rb.coverDrop;
        // (the field-list dialect: the element's padding-bottom is box space under its last line)
        if (block.PadBottomPt > 0) cv.flow.y -= block.PadBottomPt;
        cv.flow.y -= block.MarginBottom;
        // a label gives its row's height back — the span beside it advances
        // (…a field label the LAST of its lines only: the value seats on the inline-block's baseline)
        if (block.NoAdvanceY) cv.flow.y = cv.profile.dwFormDoc ? rb.yAtBlockEntry
            : block.FieldLabel && rb.metrics.lines.Length > 1
                ? rb.metrics.yBeforeBlockLines - (rb.metrics.lines.Length - 1) * rb.metrics.lineHeight
                : rb.metrics.yBeforeBlockLines;
        cv.flow.prevFlowMarginBottom = block.MarginBottom;
        cv.flow.prevBlockWasBlank = string.IsNullOrWhiteSpace(block.Text.Replace('\u00A0', ' '));
        cv.flow.prevFlowLineHeight = rb.metrics.lineHeight;
        cv.flow.prevFlowFontSize = rb.metrics.blockFontSize;
        cv.flow.prevFlowFace = block.FontFamily;
        cv.flow.uaPrevMarginBottom = block.MarginBottom;
        cv.flow.uaClosingGap = block.UaClosingGapPt;
        // A right-floated inline run is out of the flow: its lines drew at the right edge, the
        // cursor returns to where they began and the next block's lines leave the run's box free.
        if (cv.profile.uaStdSerif && block.FloatRight && !block.FloatLeft && !block.IsTable
            && !string.IsNullOrEmpty(block.Text))
        {
            var floatFace = block.FontRes == "F2" ? "Times New Roman Bold" : "Times New Roman";
            double floatW = 0;
            foreach (var fl in rb.metrics.lines) floatW = Math.Max(floatW, MeasureFaceText(floatFace, fl, rb.metrics.blockFontSize));
            cv.flow.pendingFloatRightW = floatW;
            cv.flow.y = rb.metrics.yBeforeBlockLines;
            cv.flow.prevFlowMarginBottom = 0;
            cv.flow.uaPrevMarginBottom = 0;
        }
    }
}
