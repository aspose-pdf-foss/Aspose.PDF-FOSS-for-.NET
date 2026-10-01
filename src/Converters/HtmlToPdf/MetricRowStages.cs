using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Metric rows: the collapsed grid's shared row and column rules drawn for this row.</summary>
    private static void DrawCollapsedGridRules(MetricRowsState mr)
    {
        // collapsed class grid: the shared 1px borders — row rule across the
        // grid, column rules down this row, in the class rule's colour.
        if (mr.mps.collapsedGrid)
        {
            var gInv = System.Globalization.CultureInfo.InvariantCulture;
            var gW = mr.availW - mr.symInsetPt;
            var gsb = new StringBuilder(Compat.Format(gInv,
                $"q {mr.mps.collapsedCol.R / 255.0:0.###} {mr.mps.collapsedCol.G / 255.0:0.###} {mr.mps.collapsedCol.B / 255.0:0.###} RG 0.75 w "));
            gsb.Append(Compat.Format(gInv,
                $"{mr.tableX:F2} {mr.cursor.y - 0.38:F2} m {mr.tableX + gW:F2} {mr.cursor.y - 0.38:F2} l S "));
            gsb.Append(Compat.Format(gInv,
                $"{mr.tableX:F2} {mr.cursor.y - mr.s - mr.rowBoxH + 0.38:F2} m {mr.tableX + gW:F2} {mr.cursor.y - mr.s - mr.rowBoxH + 0.38:F2} l S "));
            var gx = mr.tableX;
            gsb.Append(Compat.Format(gInv,
                $"{gx + 0.38:F2} {mr.cursor.y:F2} m {gx + 0.38:F2} {mr.cursor.y - mr.s - mr.rowBoxH:F2} l S "));
            for (var gc = 0; gc < mr.nCols; gc++)
            {
                gx += (gc == 0 ? mr.s : 0) + mr.colW[gc] + 2 * mr.p + mr.s;
                var gxe = gc == mr.nCols - 1 ? mr.tableX + gW - 0.38 : gx + 0.38;
                gsb.Append(Compat.Format(gInv,
                    $"{gxe:F2} {mr.cursor.y:F2} m {gxe:F2} {mr.cursor.y - mr.s - mr.rowBoxH:F2} l S "));
            }
            gsb.Append("Q\n");
            mr.cursor.page.AddContentStream(Encoding.ASCII.GetBytes(gsb.ToString()));
        }
    }

    /// <summary>Metric rows: the inline-style band's background filled under the first row.</summary>
    private static void FillMetricRowBand(MetricRowsState mr, int ri)
    {
        // The inline-style band's background fills the declared width × height
        // rectangle before any cell ink (probed: 96 118.5 361.5 101.25 re — one
        // uniform fill under the whole band).
        if (ri == 0 && mr.mps.tableStyleBg is { } tsBand2 && mr.mps.tableStyleHPt > 0)
        {
            var bandW2 = mr.tableWpt > 0 ? mr.tableWpt : mr.availW - mr.symInsetPt;
            mr.cursor.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                $"q {tsBand2.R / 255.0:0.###} {tsBand2.G / 255.0:0.###} {tsBand2.B / 255.0:0.###} rg " +
                $"{mr.tableX:F2} {mr.cursor.y - mr.mps.tableStyleHPt:F2} {bandW2:F2} {mr.mps.tableStyleHPt:F2} re f Q\n")));
        }
    }

    /// <summary>Metric rows: the row moves whole to a fresh page when its box would cross the bottom margin.</summary>
    private static void BreakMetricRowPage(MetricRowsState mr)
    {
        // Pagination: the row moves whole to the next page when its box bottom
        // would cross the bottom margin; the continuation page resumes at the raw
        // content top (no body top margin).
        // A row taller than a whole page gains nothing from a fresh one: its nested grids
        // paginate themselves from where they stand.
        // A UA row paginates through its content: a flow cell breaks its own lines and
        // grids, and a row holding nested grids moves ahead only for the lines it draws
        // itself - the grids split between THEIR rows (measured on the research report:
        // the detail section opens on page 1 and its comment grid continues on page 2;
        // on the test form the patient grid keeps its first row on page 1).
        var breakH = mr.rowBoxH;
        if (mr.stdSerif)
        {
            var hasGrid = false;
            var ownLinesH = 0.0;
            foreach (var fc in mr.r)
            {
                if (fc.Flow is { Count: > 0 } || BandCellPaginates(mr, fc)) return;
                if (fc.SubTables is { Count: > 0 }) hasGrid = true;
                ownLinesH = Math.Max(ownLinesH, fc.Lines.Length * mr.lineH + fc.PadTopPt + fc.PadBottomPt);
            }
            if (hasGrid) breakH = ownLinesH + 2 * mr.p;
        }
        if (mr.cursor.y - mr.s - breakH < mr.marginBottom
            && (mr.s + breakH <= mr.pageHeight - mr.marginTop - mr.marginBottom
                // (the field-list dialect moves an over-tall row too, once the page is more than half spent)
                || (_fieldListDoc && mr.cursor.y - mr.marginBottom < (mr.pageHeight - mr.marginTop - mr.marginBottom) / 2)))
        {
            mr.cursor.page = MetricNextPage(mr, mr.cursor.page);
            // (a nested grid in a UA form cell resumes under its host row's restart)
            mr.cursor.y = mr.pageHeight - mr.marginTop - mr.mps.continuationInsetPt;
        }
    }

    /// <summary>The page a paginating cell continues on: the one after its page when a sibling
    /// cell of the row has opened it already (every cell of a UA row flows down the same pages -
    /// probed on the Words letter, whose right-hand block carries on at the top of the page its
    /// address grid opened), a fresh one otherwise.</summary>
    private static Page MetricNextPage(MetricRowsState mr, Page from)
    {
        var idx = mr.doc.Pages.IndexOf(from);
        if (idx > 0 && idx < mr.doc.Pages.Count) return mr.doc.Pages[idx + 1];
        var page = mr.doc.Pages.Add(mr.pageWidth, mr.pageHeight);
        EnsureFonts(page, mr.docFontDict);
        return page;
    }

    /// <summary>Whether the sheet asks its cells or rows to avoid a page break inside (`table tr td {
    /// page-break-inside: avoid }`): such a row moves whole (measured on the five-line div rows: the
    /// row that does not fit opens the next page whole).</summary>
    private static bool CellBreakInsideAvoided(MetricRowsState mr)
    {
        if (mr.cellBreakInsideAvoided is { } known) return known;
        var avoided = false;
        foreach (var (selector, rule) in mr.css)
        {
            var last = selector.Trim();
            var cut = last.LastIndexOfAny(new[] { ' ', '>' });
            if (cut >= 0) last = last[(cut + 1)..];
            if (!(last.Equals("td", StringComparison.OrdinalIgnoreCase) || last.Equals("tr", StringComparison.OrdinalIgnoreCase)
                || last.Equals("th", StringComparison.OrdinalIgnoreCase))) continue;
            if ((rule.TryGetValue("page-break-inside", out var pbi) && pbi.Contains("avoid", StringComparison.OrdinalIgnoreCase))
                || (rule.TryGetValue("break-inside", out var bi) && bi.Contains("avoid", StringComparison.OrdinalIgnoreCase)))
            { avoided = true; break; }
        }
        mr.cellBreakInsideAvoided = avoided;
        return avoided;
    }

    /// <summary>A UA cell of plain paragraph bands - no fill, box, rule, image or class height -
    /// breaks its own lines across the page bottom, as a flow cell does (probed on the Words
    /// letter: the right-hand block's 43-line paragraph fills page 1 beside the address grid and
    /// carries its last lines to page 2, where a whole-row move would leave a page empty).</summary>
    private static bool BandCellPaginates(MetricRowsState mr, MetricCell fc)
    {
        if (!mr.stdSerif || mr.mps.uaBlockCells || fc.DivSegs is not { Count: > 0 } segs
            || fc.ImgBytes is not null || fc.AbsPng is not null || CellBreakInsideAvoided(mr)) return false;
        foreach (var sg in segs)
            if (sg.Bg is not null || sg.BoxOpen || sg.BoxClose || sg.LineBoxPt > 0 || sg.BorderBottom || sg.NestedTable >= 0) return false;
        return true;
    }

    /// <summary>Metric rows: the row's box height settled from its content, class height and declared height.</summary>
    private static void MeasureMetricRowBox(MetricRowsState mr, int ri)
    {
        // a row with no cell at all has no padding box: its band is the spacing alone
        // (…except the pt form's empty row under a spanning cell: it takes its share of the span
        //  and its padding box - probed: the title's rowspan=2 over an empty row bands 11.25 + 11.25)
        mr.rowBoxH = mr.r.Count == 0
            ? (mr.mps.ptFormCells && ri < mr.rowSpanExtra.Length && mr.rowSpanExtra[ri] > 0 ? mr.rowSpanExtra[ri] + 2 * mr.p : 0)
            : mr.rowContentH + 2 * mr.p + (mr.mps.collapsedGrid ? 0.75 : 0);
        mr.rowNaturalBoxH = mr.rowBoxH;
        mr.rowCellClassH = 0;
        mr.rowHasText = false;
        foreach (var mc in mr.r)
        {
            // (a UA cell's inline STYLE height is its CONTENT box: its own paddings and the collapsed
            //  rule stand on top - probed on the e-mail cards: `height: 30px` cells with 5 px class
            //  pads in a collapsed grid pitch 41 px; a height ATTRIBUTE keeps the calibrated band)
            var cellClassH = mr.stdSerif && mr.mps.collapsedGrid && mc.HeightStylePt > 0 && (mc.PadTopPt > 0 || mc.PadBottomPt > 0)
                ? Math.Max(mc.HeightPt, mc.HeightStylePt + mc.PadTopPt + mc.PadBottomPt + (mr.mps.collapsedGrid ? 0.75 : 0))
                : mc.HeightPt;
            if (mc.RowSpan <= 1 || !mr.stdSerif) mr.rowCellClassH = Math.Max(mr.rowCellClassH, cellClassH);
            // A cell holds its ink in SEGMENTS and images as well as in text, and a
            // declared row height is a MIN wherever there is ink - only a row holding
            // NOTHING takes the declared height as its exact band (measured: a chart row
            // whose cells declare 50 px carries drawings 40 pt tall and keeps them, where
            // reading "no text" as "no content" shrank it to the declared 37.5).
            if (mc.Text.Length > 0
                || mc.DivSegs is { Count: > 0 } || mc.SubTables is { Count: > 0 }
                || mc.ImgHPt > 0
                // (a form control is content too: its row outgrows a declared height)
                || mc.InputBoxes is { Count: > 0 } || mc.LeadCheckboxName is not null)
                mr.rowHasText = true;
        }
        mr.rowStyleH = 0;
        foreach (var mc in mr.r) if (mc.HeightStylePt > mr.rowStyleH && (mc.RowSpan <= 1 || !mr.stdSerif)) mr.rowStyleH = mc.HeightStylePt;
        mr.rowClassH = mr.rowCellClassH;
        if (ri < mr.mps.rowHeights.Count && mr.mps.rowHeightExact[ri])
            mr.rowClassH = Math.Max(mr.rowClassH, mr.mps.rowHeights[ri]);
        if (mr.rowClassH > 0)
        {
            // a class height is a MIN-height: the two-line address row
            // outgrows its h12; an EMPTY spacer/rule row is EXACTLY the
            // declared height (the 1px .cut tear-off keeps no line floor)
            if (!mr.rowHasText) mr.rowBoxH = mr.rowClassH;
            else if (mr.rowClassH > mr.rowBoxH) mr.rowBoxH = mr.rowClassH;
        }
        else if (ri < mr.mps.rowHeights.Count && mr.mps.rowHeights[ri] > mr.rowBoxH) mr.rowBoxH = mr.mps.rowHeights[ri];
        // A report grid's WIDTH-SETTER row (every cell empty, inline
        // WIDTH+MIN-WIDTH pairs, no height anywhere) sizes the columns
        // and occupies NO band of its own.
        if (!mr.rowHasText && mr.rowCellClassH == 0
            && !(ri < mr.mps.rowHeights.Count && mr.mps.rowHeights[ri] > 0))
        {
            var wsSetter = false;
            var wsBare = true;
            foreach (var mc in mr.r)
            {
                if (mc.Text.Length > 0 || mc.SubTables is { Count: > 0 }
                    || mc.DivSegs is { Count: > 0 } || mc.ImgHPt > 0)
                { wsBare = false; break; }
                if (mc.WidthSetterCell) wsSetter = true;
            }
            if (wsBare && wsSetter) mr.rowBoxH = 0;
        }
        // report mode: a WHITESPACE-only row (an &nbsp; spacer) with a
        // declared height IS that height — its blank line box carries no
        // strut of its own (the sidebar's 13px separator rows)
        if (mr.paragraphCells && !mr.stdSerif && mr.wrapperStacks && ri < mr.mps.rowHeights.Count && mr.mps.rowHeights[ri] > 0)
        {
            var allWsRow = true;
            foreach (var mc in mr.r)
            {
                if (mc.SubTables is { Count: > 0 } || mc.DivSegs is { Count: > 0 }
                    || mc.ImgHPt > 0) { allWsRow = false; break; }
                foreach (var ch in mc.Text)
                    if (ch is not (' ' or '\u00A0' or '\u0001')) { allWsRow = false; break; }
                if (!allWsRow) break;
            }
            if (allWsRow) mr.rowBoxH = mr.mps.rowHeights[ri];
        }

        mr.bandCenterPad = mr.mps.tableStyleHPt > 0 && mr.rowBoxH > mr.rowNaturalBoxH
            ? (mr.rowBoxH - mr.rowNaturalBoxH) / 2 : 0.0;
    }

    /// <summary>Metric rows: the row's content height measured from its cells' own line boxes.</summary>
    private static void MeasureMetricRowContent(MetricRowsState mr, int ri)
    {
        mr.classPaced = false;
        if (mr.mps.widthClassTable)
            foreach (var mc in mr.r)
            {
                if (mc.Text.Length == 0) continue;
                if (mc.FontFromClass || (mr.mps.tableClassFont && mc.FontSize is null))
                { mr.classPaced = true; }
                else { mr.classPaced = false; break; }
            }
        // An authored line-height IS the line box: a row whose text cells all
        // declare one pitches on those boxes alone, with no table strut under
        // them (probed on the letter grid: the 14 px cells band 10.5, not the
        // table's 18 px base line).
        // ...and a row whose text all floats has no line box either: it pitches on
        // the floats' own boxes (measured: 10px floated labels band 11 pt, not the
        // 12 pt base line's 13.5 + padding).
        // ...nor has a quirks-mode row whose text is all inside sized font tags (the
        // line-height quirk: the cell's own strut does not count).
        // A UA block grid has NO strut at all (probed, ~100 cases): a row is as tall as the
        // tallest line box its cells' INK makes - an nbsp is ink in its own font, an empty
        // cell is nothing, the table's and the cell's declared sizes add nothing by themselves.
        // The report export's grids (the same UA table model, measured on its
        // header grid: 8 pt rows pitch 9.0, a 1.76 mm spacer row is 4.99) follow it too.
        var inkPacedGrid = mr.mps.uaBlockCells || mr.serifReportCells || mr.mps.uaFormCells;
        mr.rowContentH = mr.tableHasText && !mr.classPaced && !inkPacedGrid
            && !MetricRowDeclaresLineBox(mr.r) && !MetricRowTextAllFloated(mr.r)
            && !(_quirksRowStrut && MetricRowTextAllFontTagSized(mr.r))
            // ...nor a quirks-mode row whose text cells are all sized by their CLASS: they pitch on
            // their own line boxes and a declared row height bands them (probed on the land-register
            // order: 8 pt Courier rows declaring 15 px band 11.25 and 12 px rows 9, where the strut
            // gave 13.5 to both; measured on the enterprise summary: `td.label` Arial 9 rows with no
            // height pitch on the 9 pt line - the 18 px rows the calibration kept declare that
            // line-height on their class, which MetricRowDeclaresLineBox honours above).
            // (…a class that also names the cells' FACE: the enterprise summary's `.label { Arial 12px }`
            // rows pitch on their own 10.5 line, while the land-register order's `.ListTableLabel
            // { 12px }` rows in the table's own serif keep the 13.5 strut, as calibrated)
            && !(_quirksRowStrut && MetricRowTextAllClassSized(mr.r) && (MetricRowDeclaresHeight(mr, ri) || MetricRowTextAllClassFaced(mr.r)))
            // ...nor a row that states its OWN typography inline: it pitches on the boxes it
            // declared, not on the table's 12 pt base line (measured: rows declaring Arial 10
            // band 11.25, where the strut gave 13.5).
            // (a UA block grid's nbsp spacer cell that states its size bands on that size too)
            && !MetricRowTextAllRowStyled(mr.r, nbspCounts: mr.mps.uaBlockCells) ? mr.lineH : 0;
        mr.rowRealTextH = 0.0;
        mr.rowHasRealText = false;
        foreach (var mc in mr.r)
            if (mc.RowSpan <= 1 && mc.Text.Replace(" ", "").Trim().Length > 0)
            {
                mr.rowHasRealText = true;
                mr.rowRealTextH = Math.Max(mr.rowRealTextH, mc.ContentH);
            }
        foreach (var mc in mr.r)
            if (mc.RowSpan <= 1)
            {
                var mcH = mc.ContentH;
                if (mr.rowHasRealText && mr.stdSerif
                    && mc.Text.Length > 0
                    && mc.Text.Replace(" ", "").Trim().Length == 0)
                    mcH = Math.Min(mcH, mr.rowRealTextH);
                mr.rowContentH = Math.Max(mr.rowContentH, mcH);
            }
        // …and a declared height on ANY of its cells (a content-box height) or on the row
        // itself (an outer height) bands the row to at least that, every cell seating in the
        // band by its own valign (probed: a `height:20px` cell bands 15 and a 10 pt middle line
        // seats 1.875 down; top seats 0, bottom the whole 3.75).
        // (a quirks-mode row of class-sized cells bands on its declared height the same way)
        ApplyMetricRowStrutAndBands(mr, ri, inkPacedGrid);
        if (ri < mr.rowSpanExtra.Length) mr.rowContentH += mr.rowSpanExtra[ri];
        // A row whose every cell is TRULY empty (no text, not even an
        // &nbsp;) keeps no line strut — its band is the padding alone
        // (measured: the empty spacer row is exactly 2p + s;
        // nbsp spacer rows keep their calibrated line boxes).
        if (mr.stdSerif && mr.wrapperStacks && mr.rowContentH > 0
            && MetricRowIsBare(mr.r))
            mr.rowContentH = ri < mr.rowSpanExtra.Length ? mr.rowSpanExtra[ri] : 0;
        // …and a bare row that declares a HEIGHT of its own bands at exactly
        // that height whatever the dialect: with no content there is no line
        // box to floor it (probed on the letter grid: the height="10" spacer
        // rows band 7.5, not the table's 14 px line).
        if (ri < mr.mps.rowHeights.Count && mr.mps.rowHeights[ri] > 0
            && mr.rowContentH > 0 && MetricRowIsBare(mr.r))
            mr.rowContentH = ri < mr.rowSpanExtra.Length ? mr.rowSpanExtra[ri] : 0;
        // Outer-frame collapse grid: an all-empty row is its padding alone
        // (the height-0 width-setter and blank separator rows: 1.5 pt bands).
        if (mr.collapseBoxW > 0)
        {
            var cbRowHasText = false;
            foreach (var mc in mr.r)
                if (mc.Text.Trim().Length > 0) { cbRowHasText = true; break; }
            if (!cbRowHasText) mr.rowContentH = 0;
        }
    }
}
