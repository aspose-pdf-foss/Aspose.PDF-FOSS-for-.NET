using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The metric layouter draws the table block: its rows, its nested grids and the flow cursor it leaves behind. False means the caller's loop moves on.</summary>
    /// <summary>A metric table's available width.</summary>
    private static double MetricTableAvailW(Block block, HtmlFlowCursor flow, HtmlDocProfile profile)
    {
        // (a grid under a wrapper whose rule declares a box lays out in that box)
        if (profile.uaStdSerif && block.HostWidthPt > 0) return block.HostWidthPt;
        // inside a fieldset the FRAME's content box is the
        // table's available width, not the page content box
        if (flow.fsIndentLive > 0 && profile.fsBoxW > 0)
            return profile.fsBoxW - (profile.uaFieldsetContent ? 2 * (FsPadLeftPt - UaFieldsetSideInsetPt) : FsPadLeftPt + FsPadRightPt);
        if (flow.frameContentW > 0) return flow.frameContentW - FramedTableOffsetPt(flow, block.TableHtml ?? "");
        return flow.contentWidth - flow.fsIndentLive;
    }

    /// <summary>The size a metric table's cells type at when they state none.</summary>
    private static double MetricTableBaseFontPt(TableBlockState tb, Block block, HtmlDocProfile profile)
    {
        if (profile.printGrid) return profile.printGridBase;
        if (profile.ptReportDoc && profile.ptTableFontPt > 0) return profile.ptTableFontPt;
        // (…and at the size the wrapper's rule states)
        if (profile.uaStdSerif && block.HostFontPt > 0) return block.HostFontPt;
        // (…or the body's own size where its tag or class states one: a standards
        //  document's cells inherit it - the change-control page's 9.5 pt th/td)
        if (profile.uaStdSerif && profile.bodyOwnFontPt > 0) return profile.bodyOwnFontPt;
        return profile.uaStdSerif || tb.quirksRunTable ? 12 : 11;
    }

    private static void LayoutMetricTableBlock(TableBlockState tb, Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, Dictionary<string, Dictionary<string, string>> css, HtmlLoadOptions? options, double marginLeft, double marginTop, double marginBottom, double pageWidth, double pageHeight, bool tableAfterText, bool tableAfterSpacer, (double asc, double sum) tfm)
    {
        // Measured on the width:100%-body sheet only — the plain-
        // body serif docs are calibrated without this gap.
        if (profile.uaStdSerif && profile.bodyWidthFullDoc && tableAfterText)
            flow.y -= TableAfterTextGapPt;
        // A table that follows a SPACER break paragraph opens the
        // break's bottom UA margin (a text neighbour would have
        // realised it through its own margin-top; the table path
        // reads none).
        // (a quirks document keeps none: its grid opens right under the spacer's line)
        if (profile.uaBareDoc && tableAfterSpacer && !_quirksRowStrut)
            flow.y -= UaParagraphMarginPt;
        // A quirks document opens its grid right under a blank spacer paragraph: the
        // paragraph's bottom margin the text flow spent comes back (measured: the
        // doctype-less invoice's grid rule sits 1.1 pt under the nbsp line's box).
        if (_quirksRowStrut && tableAfterText && flow.prevBlockWasBlank && flow.prevFlowMarginBottom > 0)
            flow.y += flow.prevFlowMarginBottom;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MROW") == "1")
            Console.WriteLine($"[mtable] top={pageHeight - flow.y:0.##} afterSpacer={tableAfterSpacer} afterText={tableAfterText} bare={profile.uaBareDoc} quirks={_quirksRowStrut} serif={profile.uaStdSerif} deadCss={profile.deadExternalCss} stacks={(profile.uaStdSerif && !profile.deadExternalCss) || profile.ptReportDoc} x={marginLeft + flow.fsIndentLive:0.##} avail={flow.contentWidth - flow.fsIndentLive:0.##}");
        // (inside a framed wrapper div the tables stand in its content box, centred when it centres)
        var frameX = flow.frameContentW > 0 ? flow.frameInset + FramedTableOffsetPt(flow, block.TableHtml ?? "") : 0;
        RenderMetricTable(doc, flow, block.TableHtml ?? "", css,
            marginLeft + flow.fsIndentLive + frameX + TableRelativeShiftPt(block.TableHtml),
            MetricTableAvailW(block, flow, profile),
            pageWidth, pageHeight,
            marginTop, marginBottom,
            // (a UA grid under a wrapper whose rule states a face draws in that face)
            profile.uaStdSerif && block.HostFace is { } hostFace && WinMetricsFor(hostFace) is { } ? hostFace : tb.tableFace!,
            profile.uaStdSerif && block.HostFace is { } hostFace2 && WinMetricsFor(hostFace2) is { } hostFm2 ? hostFm2 : tfm,
            docFontDict,
            // (a pt-sized form document grids on the UA table laws in its own face)
            stdSerif: profile.uaStdSerif || (profile.ptReportDoc && profile.uaFormCells),
            baseFontSize: MetricTableBaseFontPt(tb, block, profile),
            // The wrapper-stack recursion serves the legacy nested-
            // markup corpus; the dead-css greens were calibrated on
            // the flat merge and keep it. A zero body margin has no
            // symmetric body inset for the grid either.
            wrapperStacks: (profile.uaStdSerif && !profile.deadExternalCss) || profile.ptReportDoc,
            // (...and a sheet sized to an in-cell declared box keeps no body inset on the right of
            //  its grids either: the content box already spans the overflow)
            symInsetPt: profile.bodyZeroMargin || profile.inCellSheet ? 0.0 : UaBodyMarginPt,
            // (on a sheet a min-floor grid grew, an auto-width table keeps the inset on its right)
            keepRightInset: profile.minFloorSheet,
            ptFormCells: profile.ptFormDoc,
            rtl: profile.rtlDoc,
            // the SSRS report export drives the serif flow's cells
            // through the paragraph-segment model too
            paragraphCells: profile.emailNewsletterDoc || profile.ssrsReportDoc || profile.uaBlockCells,
            serifReportCells: profile.ssrsReportDoc || profile.uaBlockCells,
            uaBlockCells: profile.uaBlockCells,
            uaFormCells: profile.uaFormCells,
            loadOptions: options,
            siblingCellRules: profile.docSiblingCellRules);
        flow.lastWasMetricTable = true;
        // The grid was the document's opening block: the page-top margin collapse is spent
        // on it, and the block after it charges its own top margin in full (probed: a
        // <p> under an opening table seats 13.44 below the table box).
        flow.uaTopMarginPending = false;
        // A PAGE-BREAK-AFTER div closed with this table (the close tag
        // parses in a later segment): open the fresh page here.
        if (block.PageBreakAfterTable)
        {
            flow.page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(flow.page, docFontDict);
            flow.y = pageHeight - marginTop;
            flow.pendingTopDrop = profile.hasZeroTopMargin;
            flow.contentPage = null;
        }
        flow.lastWasHardBreak = false;
    }
}
