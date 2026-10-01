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

public sealed partial class Document
{
    private void LayoutHtmlFragmentParagraph(HtmlFragment html, FlowLayout flow, Page page, Text.TextBuilder tb, HashSet<Table> renderedTables, List<(byte[] content, double width, double height)> overflowPages, Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages, double marginLeft, double marginRight, double marginTop, double marginBottom)
    {
        // Inline <svg> elements become <img src="inline-svg:i"> placeholders
        // rendered through the SVG engine by RenderHtmlImages.
        // A meeting-agenda fragment draws straight onto this page: its levels
        // carry their own indents and numbering boxes, which the block flow
        // below has no model for (see Agenda.cs).
        if (Converters.HtmlToPdfConverter.TryRenderAgendaOutline(
                html.HtmlContent ?? "", page, marginLeft, marginRight, marginTop))
            return;
        // A class-width form letter lays its columns out on the declared body
        // width rather than the page's (see QuoteScheduleFragment.cs).
        if (TryRenderQuoteScheduleFragment(html.HtmlContent ?? "", flow, page,
                marginLeft, marginBottom, html.HtmlLoadOptions))
            return;
        var hl = new HtmlFragmentLayoutState();
        hl.html = html;
        hl.flow = flow;
        hl.page = page;
        hl.tb = tb;
        hl.renderedTables = renderedTables;
        hl.overflowPages = overflowPages;
        hl.overflowImages = overflowImages;
        hl.marginLeft = marginLeft;
        hl.marginRight = marginRight;
        hl.marginTop = marginTop;
        hl.marginBottom = marginBottom;
        (hl.htmlContent, var inlineSvgs) = Converters.HtmlToPdfConverter.ExtractInlineSvgs(Converters.HtmlToPdfConverter.ApplyKnockoutTextBindings(hl.html.HtmlContent ?? ""));
        hl.htmlColor = hl.html.TextState?.ForegroundColor;
        hl.htmlFragmentLinkEmitted = false;

        hl.htmlFrameIndent = 0.0;

        hl.htmlFrames = Converters.HtmlToPdfConverter.FramedBlockSpans(hl.htmlContent);
        hl.htmlOpenFrames = new List<(int Index, int Slot, double Top)>();
        if (hl.html.HtmlLoadOptions?.PageInfo is { MarginAssigned: true } mfPi)
        {
            if (!LayoutMarginAssignedFragment(hl, mfPi)) return;
        }
        else if (Converters.HtmlToPdfConverter.TryParseFilingLetter(hl.htmlContent) is { } flItems)
        {
            if (!LayoutFilingLetter(hl, flItems)) return;
        }
        else if (Converters.HtmlToPdfConverter.TryParseProcedureStepRows(hl.htmlContent,
                     hl.html.IsParagraphHasMargin, hl.html.HtmlLoadOptions) is { } psRows)
        {
            LayoutProcedureStepRows(psRows, hl.html, hl.flow, hl.page, hl.marginLeft, hl.marginRight, hl.marginTop, hl.marginBottom);
        }
        else if (Converters.HtmlToPdfConverter.ContainsTable(hl.htmlContent))
        {
            if (!LayoutHtmlTableFragment(hl, inlineSvgs)) return;
        }
        else if (TryRenderListDialect(hl))
        {
            // The list dialects (a class-styled outline list, a step list of heading
            // blocks) render on the HTML-engine serif metrics path.
        }
        else if (Converters.HtmlToPdfConverter.TryParseInlineEmphasisFont(hl.htmlContent) is (var iefFace, var iefPt, var iefRuns)
                 && RenderInlineEmphasisRuns(iefFace, iefPt, iefRuns, hl.flow, hl.page, hl.marginLeft, hl.marginRight))
        {
            // Single-font emphasis dialect rendered as styled runs.
        }
        else if (Converters.HtmlToPdfConverter.TryParseNestedStyledSpans(hl.htmlContent) is { } nsRuns)
        {
            LayoutNestedStyledSpans(hl, nsRuns);
        }
        else if (Converters.HtmlToPdfConverter.TryParseDelInsRuns(hl.htmlContent) is { } diRuns
                 && LayoutDelInsRuns(hl, diRuns))
        {
            // A del / ins fragment: UA serif runs with their backgrounds and rules.
        }
        else if (Converters.HtmlToPdfConverter.TryParseMonoFontLineBoxes(hl.htmlContent) is (var mfPt, var mfLines))
        {
            LayoutMonoFontLineBoxes(hl, mfPt, mfLines);
        }
        else if (TryLayoutUaFlowFragment(hl))
        {
            // A fragment of inline content, paragraphs, divisions, headings and lists
            // sets on the UA flow's CSS line boxes.
        }
        else if (Converters.HtmlToPdfConverter.HasBlockStructure(hl.htmlContent))
        {
            HtmlFramesOpening(hl, 0, hl.htmlContent.Length, hl.htmlContent);
            RenderHtmlBlocks(hl.htmlContent, hl.html, hl.flow, hl.page, hl.tb, hl.htmlColor, inlineSvgs, hl, hl.htmlFrameIndent, hl.marginLeft, hl.marginRight, hl.marginTop);
            HtmlFramesClosing(hl, 0, hl.htmlContent.Length);
        }
        else
        {
            LayoutHtmlPlainFragment(hl, inlineSvgs);
        }
    }

}
