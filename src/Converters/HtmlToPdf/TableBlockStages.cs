using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Configure the built table for the document's dialect, place its page slices and settle the flow below it.</summary>
    private static void LayoutBuiltTable(TableBlockState tb, Table table, Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, List<(Page page, byte[] ops)> floatFirstOps, Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack, string? bodyCssFace, bool dwFormDoc, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth)
    {
        ConfigureTableForDialect(tb, table, flow, profile, bandStack, dwFormDoc, marginLeft, pageWidth);
        PlaceTableSlices(tb, table, block, flow, profile, doc, docFontDict, floatFirstOps, bandStack, bodyCssFace, marginBottom, marginTop, pageHeight, pageWidth);
        SettleFlowBelowTable(tb, table, flow, profile, marginBottom);
    }

    /// <summary>Build the table's page slices, then draw each slice with its graphs and images on its page, clipping a float-band column to one page.</summary>
    private static void PlaceTableSlices(TableBlockState tb, Table table, Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, List<(Page page, byte[] ops)> floatFirstOps, Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack, string? bodyCssFace, double marginBottom, double marginTop, double pageHeight, double pageWidth)
    {
        tb.slices = table.BuildMultiPage(flow.page, flow.y, marginBottom,
            bodyCssFace is not null ? marginTop
            // Escaped-attr dialect: continuation slices resume at the
            // page's real top margin (the flow's fresh-page top), not at
            // the sheet edge.
            : profile.escapedAttrDoc ? pageHeight - FreshPageTopY(profile, pageHeight, marginTop)
            // Chain-dialect documents likewise resume below the top
            // margin — a spilled report row must not draw at the sheet
            // edge (the y≈9 artefact).
            : profile.docChainRules is not null ? marginTop
            // …and the over-declared grid document's continuation
            // slices resume at the page's top margin too.
            : profile.overDeclaredGridDoc ? marginTop
            // …and an inline-styled grid's do as well (the Verdana
            // report resumes 50 pt below the sheet edge).
            : table.InlineFaceGridRatio > 0 ? marginTop
            : 0);
        tb.graphs = table.LastGraphDraws;
        tb.imageDraws = table.LastImageDraws;
        tb.bandClipped = false;
        if (bandStack.Count > 0 && tb.slices.Count > 1)
        {
            tb.bandClipped = true;
            tb.slices = new List<byte[]> { tb.slices[0] };
            if (tb.graphs.Count > 1) tb.graphs = new List<List<byte[]>> { tb.graphs[0] };
            if (tb.imageDraws.Count > 1)
                tb.imageDraws = new List<List<(byte[] data, Rectangle rect)>> { tb.imageDraws[0] };
        }
        for (var si = 0; si < tb.slices.Count; si++)
        {
            if (si > 0)
            {
                flow.page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(flow.page, docFontDict);
            }
            // A floated table's ops are collected and PREPENDED to its page's
            // content after the flow pass — floats paint first,
            // so their text leads the fragment order. Geometry is unchanged.
            if (block.FloatFirst)
            {
                if (si < tb.graphs.Count)
                    foreach (var g in tb.graphs[si]) floatFirstOps.Add((flow.page, g));
                floatFirstOps.Add((flow.page, tb.slices[si]));
            }
            else
            {
                if (si < tb.graphs.Count)
                    foreach (var g in tb.graphs[si]) flow.page.AddContentStream(g);
                flow.page.AddContentStream(tb.slices[si]);
            }
            // Cell images (logos, SVG diagrams) recorded by the layout pass;
            // blit them onto the slice's page at their resolved rectangles.
            if (si < tb.imageDraws.Count)
                foreach (var (imgData, imgRect) in tb.imageDraws[si])
                    try { flow.page.AddImage(imgData, imgRect); }
                    catch { /* undecodable image: keep the table flow */ }
        }
        // A clipped band column consumed its page down to the bottom margin —
        // LastRenderedHeight/LastPageEndY describe the discarded overflow pages.
    }

    /// <summary>Inside a UA fieldset the grid stands at the frame's content box.</summary>
    private static void InsetTableForUaFieldset(Table table, HtmlFlowCursor flow, HtmlDocProfile profile)
    {
        if (flow.fsIndentLive > 0 && profile.uaFieldsetContent && profile.fsBoxW > 0)
        {
            table.FlowLeftOffset += flow.fsIndentLive;
            table.UsableWidthOverride = profile.fsBoxW - 2 * (FsPadLeftPt - UaFieldsetSideInsetPt);
        }
    }

    /// <summary>Set the built table's width override, cell face rules and first-row seat for the document's dialect.</summary>
    /// <summary>The page margin a grown sheet keeps past its ink.</summary>
    private const double UaPageMarginPt = 90.0;

    private static void ConfigureTableForDialect(TableBlockState tb, Table table, HtmlFlowCursor flow, HtmlDocProfile profile, Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack, bool dwFormDoc, double marginLeft, double pageWidth)
    {
        // The over-declared grid document's tables keep the STANDARD
        // content box — the same pageW−201 the nested grids resolve
        // against (margins, the UA body gutter, and the host chrome
        // pair all come off) — while their row-band FILLS bleed to
        // the page's right EDGE: the section bands paint
        // page-wide with the content staying put.
        table.FlowLeftOffset = marginLeft;
        InsetTableForUaFieldset(table, flow, profile);
        // Word mail: a table's own margin-left (Word's cell-margin offset) shifts it right inside the same content box.
        if (profile.wordMailDoc && WordMailTableMarginLeftPt(tb.fhTableHtml) is > 0 and var wmMl)
        {
            table.FlowLeftOffset += wmMl;
            table.UsableWidthOverride = flow.contentWidth - wmMl;
        }
        ConfigureOverDeclaredGridTable(tb, profile, table, flow, marginLeft, pageWidth);
        // Inside a float column the usable width IS the column width —
        // the symmetric-margin guess in GetTableUsableWidth reads a
        // right column's offset as a right margin and collapses it.
        if (bandStack.Count > 0) table.UsableWidthOverride = flow.contentWidth;
        // A sectioned report's grid that grew the sheet lays out on the box the sheet grew to: one
        // page margin past its natural width, no body inset on the right (measured: the job list's
        // 600 px grid stands 96..546 on its 636 sheet, the flow's text box keeping W - 96).
        // (…and a UA-grid document's grid declaring an ABSOLUTE box wider than the flow lays out on that box:
        //  the quotation's 650 px grids stand 96..583.5 on their 673.5 sheet)
        var uaGridNatW = profile.uaGridBoxes && table.HtmlDeclaredBoxAbs
            ? Math.Max(tb.renderNatW, table.HtmlDeclaredBoxPt) : tb.renderNatW;
        if ((profile.sectionedReport || (profile.uaGridBoxes)) && uaGridNatW > flow.contentWidth
            && pageWidth - marginLeft - UaPageMarginPt >= uaGridNatW - 1e-3)
            table.UsableWidthOverride = pageWidth - marginLeft - UaPageMarginPt;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_TABLEW") == "1")
            Console.Error.WriteLine($"[tablebox] usable={table.UsableWidthOverride:0.##} left={table.FlowLeftOffset:0.##} natW={tb.renderNatW:0.##} avail={tb.tableAvailW:0.##} cw={flow.contentWidth:0.##} frame={flow.frameContentW:0.##} pref={table.HtmlPreferredWidthPt:0.##}");
        // A form-horizontal row keeps its natural cell widths even when
        // they overflow the float column (browser floats overflow; the
        // squeeze would re-wrap value text that belongs on
        // one line).
        if (tb.fhRow)
        {
            var fhw = Regex.Match(tb.fhTableHtml, @"data-fhw=""([\d.]+)""");
            if (fhw.Success && double.TryParse(fhw.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var fhwPx)
                && fhwPx * 0.75 > flow.contentWidth)
                table.UsableWidthOverride = fhwPx * 0.75;
        }
        // …and the certificate grid keeps the width it declared (above),
        // overflowing the content box rather than re-wrapping into it.
        if (tb.certTableW > 0) table.UsableWidthOverride = tb.certTableW;
        // Band-card tables render serif cell fragments in the real serif
        // face (see Table.HonorCellFontFaces) — the Helvetica fallback
        // over-wraps their serif-measured columns.
        table.HonorCellFontFaces = profile.floatBandDoc;
        // Form-document dialect: cells wrap and draw in their resolved
        // real faces (td { font: 10px Verdana }) — see HonorCellTtfFaces.
        // The pt-styled fragment's cells carry inline Verdana spans the
        // same way.
        // …and the redline diff document's cells carry inline Times
        // spans; their runs draw with the real face.
        // (an inline-face grid set the flag at build — keep it)
        table.HonorCellTtfFaces |= profile.formDialectTables || profile.ptStyledFragment
            || profile.redlineDiffDoc || dwFormDoc;
        table.RedlineCellSeat = profile.redlineDiffDoc;
        table.HtmlWrapInsetsCellMargins |= profile.ptStyledFragment;   // (a collapsed UA control grid set it at build time)
        // Sectioned report: the cursor runs in baseline space, so the table's
        // own box top — the top edge of its first row band — sits one baseline
        // offset above it. Without this the whole grid hangs a full ascent too
        // low and every row band misses the one a browser paints.
        if ((profile.sectionedReport || profile.ptStyledFragment) && flow.prevFlowFontSize > 0)
            flow.y += BaselineInLineBoxPt(flow.prevFlowFontSize)
                // pt-styled fragment: the grid's TOP STROKE anchors the
                // seat (probed against the drawn border positions —
                // the generic rise leaves every mid-flow table 0.3 low).
                + (profile.ptStyledFragment ? PtTableBoxRisePt : 0);
        // Redline: the flow's last line spent a full 1.125 em box;
        // a table opens at the paragraph's BOTTOM (DescLead below
        // the last baseline) — repay the difference.
        else if (profile.redlineDiffDoc && flow.prevFlowFontSize > 0)
            flow.y += (RedlineLineFactor - RedlineDescLeadEm) * flow.prevFlowFontSize;
        // Paginate the table from the current cursor; the first slice lands on this
        // page, further slices spill onto fresh pages (matching a browser splitting a
        // long table across pages). Borders/graphics come back via LastGraphDraws.
        // A table that spills onto a fresh page resumes at the page's TOP
        // MARGIN, not at the sheet edge: without the page margin the
        // continuation draws right off the top of the paper.
        // Every page this conversion adds shares docFontDict (see
        // EnsureFonts) — spill slices may embed real faces through it.
        table.SpillPagesShareFontDict = true;
    }

    /// <summary>Move the flow below the placed table: the clipped-band bottom, the pinned-body and sectioned-report gaps, the pending drops the next block spends.</summary>
    private static void SettleFlowBelowTable(TableBlockState tb, Table table, HtmlFlowCursor flow, HtmlDocProfile profile, double marginBottom)
    {
        flow.y = tb.bandClipped ? marginBottom
            : tb.slices.Count > 1 ? table.LastPageEndY : flow.y - table.LastRenderedHeight;
        // …and an inline margin-bottom is real space below it (the
        // pinned report's `margin-bottom: 5px` summary grid).
        if (profile.bodyPinnedW > 0
            && Regex.Match(tb.fhTableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase)
                is { Success: true } pbTag2
            && Regex.Match(DivStyleOf(pbTag2.Value),
                @"(?<![-\w])margin-bottom\s*:\s*([\d.]+)\s*px",
                RegexOptions.IgnoreCase) is { Success: true } pbMb
            && double.TryParse(pbMb.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pbMbPx)
            && pbMbPx > 0)
            flow.y -= pbMbPx * 0.75;
        // Back into baseline space: the cursor sits on the table's bottom
        // EDGE, and the next text block draws its baseline one offset below
        // its own box top — the mirror of the entry adjustment above.
        if (profile.sectionedReport && flow.prevFlowFontSize > 0)
            flow.y -= BaselineInLineBoxPt(flow.prevFlowFontSize);
        flow.contentPage = flow.page;
        // The cursor now sits ON the table's bottom edge. Text draws its
        // first BASELINE at the cursor, so in the form-document dialect
        // the next text block drops a line box first — else its ink rides
        // up into the last (bordered) row. The legacy flow keeps its
        // calibrated tight rhythm outside the dialect.
        // The chain dialect owes the same drop: its report footnote drew its
        // baseline ON the table's bottom border, striking through the last
        // row — and never reached the due page break.
        flow.pendingTableDrop = profile.formDialectTables || profile.docChainRules is not null
            // the pt-styled fragment's paragraphs seat one line box
            // below each grid the same way
            || profile.ptStyledFragment;
        if (profile.redlineDiffDoc)
        {
            // the cursor sits on the table's bottom edge; the next
            // paragraph's first baseline seats one AscLead below it
            flow.prevFlowFontSize = 0;
            flow.prevFlowLineHeight = 0;
            flow.pendingTableDrop = false;
        }
        if (profile.ptStyledFragment)
        {
            flow.pendingTableDropBordered = false;
            foreach (Row ptbR in table.Rows)
            {
                foreach (Cell ptbC in ptbR.Cells)
                    if (ptbC.Border is { Width: > 0 } ptbB
                        && ptbB.Side != BorderSide.None) { flow.pendingTableDropBordered = true; break; }
                if (flow.pendingTableDropBordered) break;
            }
        }
    }

    /// <summary>The `margin-left: N pt` of a table tag, 0 when it states none.</summary>
    private static double WordMailTableMarginLeftPt(string tableHtml)
    {
        var tag = Regex.Match(tableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase).Value;
        return Regex.Match(tag, @"margin-left\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase) is { Success: true } m
            && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
