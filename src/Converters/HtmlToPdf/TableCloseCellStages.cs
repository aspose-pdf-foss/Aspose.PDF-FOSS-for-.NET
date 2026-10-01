using System.Text;
using System.Text.RegularExpressions;
using CellLineSpec = (string Text, double FontPt, string? Family, bool Keep, bool JoinNext, System.Collections.Generic.List<(string Text, string Url)>? Anchors, bool Bold, double MarginTopPt, double MarginLeftPt, Aspose.Pdf.Color? Color, bool Italic);

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The measured widths reach the column model: a single-column cell writes its min, max and header widths and declared percents, a spanning cell records its span extents.</summary>
    private static void CommitCellColumnWidths(CloseCellState cc)
    {
        if (cc.widenProbe || cc.chainBase is not null || cc.ptCellWidths || cc.uaCellBoxes)
        {
            bool Occupied(int c)
            {
                foreach (var (oc, os, orem) in cc.ps.rowspanOcc)
                    if (orem > 0 && c >= oc && c < oc + os) return true;
                return false;
            }
            while (Occupied(cc.colModel.colCursor)) cc.colModel.colCursor++;
            // remaining counts the spanning row itself (aged at its own row close),
            // so the occupancy covers exactly the rowSpan−1 rows below it.
            if (cc.ps.cellRowSpan > 1) cc.ps.rowspanOcc.Add((cc.colModel.colCursor, cc.span, cc.ps.cellRowSpan));
        }
        cc.padSideExtra = cc.chainBase is not null && cc.ps.cellCssPadPt > 0 ? 0 : 2 * cc.padSide;
        cc.extra = cc.widenProbe || cc.ptCellWidths ? 0
            : cc.padSideExtra + (cc.hasBorder ? 2 * cc.borderWidth : 0)
                // (a UA-boxed cell of a grid that declares its cellpadding attribute has no UA pad pair:
                //  the legacy 1.5 slack IS that pair, kept where the grid leaves the UA padding on -
                //  measured: the job list's cellPadding="0" grids close at 450, the twelve-page report's
                //  style-padded grids keep their 1160 sheet)
                // (…and a sheet whose td rule zeroes the padding leaves no pair either - measured on the mailing's
                //  600 px content grid: 630 px of padded content, not 634)
                // (…and a sheet that DECLARES its cells' box through a descendant cell rule states the
                //  pair outright: the slack would charge it twice)
                + (cc.tightExtras || cc.cssRunFace is not null || cc.dwFormCells || (cc.ps.sheetTdBoxRule) || (cc.uaCellBoxes && cc.ps.cellPadAttrDeclared) || (cc.uaCellBoxes && cc.ps.sheetTdPadZero) ? 0 : 1.5);
        // The row's own demand: its cells' bare minima side by side, spans included - the true
        // min-content a grid can shrink to is its widest ROW, which the per-column maxima over-count.
        cc.ps.rowMinSum += cc.cellMin;
        cc.colModel.rowNoWrapMinW = Math.Max(cc.colModel.rowNoWrapMinW, cc.ps.rowMinSum);
        cc.colModel.cellExtraPt = cc.extra;
        while (cc.colModel.colMinW.Count < cc.colModel.colCursor + cc.span) { cc.colModel.colMinW.Add(0); cc.colModel.colMaxW.Add(0); cc.colModel.colHdrW.Add(0); cc.colModel.colMinBrkW.Add(0); cc.colModel.colMinFloorW.Add(0); cc.colModel.colDeclW.Add(0); cc.colModel.colMinSerifW.Add(0); cc.colModel.colControlFloor.Add(false); }
        // (a spanning cell's floor sits on its first column: only the SUM of the floors is read)
        // (…and a break-anywhere cell's TEXT sets none: every token in it breaks, so only a nested grid
        //  floors it - the quotation's 525-character string yields to its 650 px grid)
        var minFloor = (cc.cellMinFloor > 0 ? cc.cellMinFloor : cc.breakAnywhereDoc ? 0 : cc.cellMin) + cc.extra;
        if (minFloor > cc.colModel.colMinFloorW[cc.colModel.colCursor]) cc.colModel.colMinFloorW[cc.colModel.colCursor] = minFloor;
        if (cc.span == 1)
        {
            if (cc.cellMin + cc.extra > cc.colModel.colMinW[cc.colModel.colCursor]) cc.colModel.colMinW[cc.colModel.colCursor] = cc.cellMin + cc.extra;
            // (the serif floor is the cell's CONTENT: its padding is chrome the sheet model adds once per
            // column edge, with the spacing, the rules and the frame - probed on the returns grid: 641.75 =
            // 96 + 0.5 + 3 + Σ floors + 13 × 6 + 3 + 0.5 + 90)
            if (cc.uaSerifMin && cc.cellMinSerif > cc.colModel.colMinSerifW[cc.colModel.colCursor])
            {
                cc.colModel.colMinSerifW[cc.colModel.colCursor] = cc.cellMinSerif;
                cc.colModel.colControlFloor[cc.colModel.colCursor] = cc.ps.cellControlBoxPt > 0 && cc.ps.cellControlBoxPt >= cc.cellMinSerif;
            }
            if (cc.cellMinBrk + cc.extra > cc.colModel.colMinBrkW[cc.colModel.colCursor]) cc.colModel.colMinBrkW[cc.colModel.colCursor] = cc.cellMinBrk + cc.extra;
            if (cc.cellMax + cc.extra > cc.colModel.colMaxW[cc.colModel.colCursor]) cc.colModel.colMaxW[cc.colModel.colCursor] = cc.cellMax + cc.extra;
            if (cc.cellHdr > 0 && cc.cellHdr + cc.extra > cc.colModel.colHdrW[cc.colModel.colCursor]) cc.colModel.colHdrW[cc.colModel.colCursor] = cc.cellHdr + cc.extra;
            // A cell holding NOTHING but an image declares its width as surely as a
            // `width=` attribute does: replaced content has one size and the column
            // must not stretch past it. Layout tables gutter with exactly this —
            // a `<td><img width="15" height="1"></td>` spacer.
            // (`cellMax` already carries the image's own box, so "no wider than its
            // image" is the test for a cell that holds nothing else.)
            var cellDeclPt = cc.ps.cellImgWidthPt > 0 && cc.cellMax <= cc.ps.cellImgWidthPt + 0.01
                ? Math.Max(cc.ps.cellWidthPt, cc.ps.cellImgWidthPt) : cc.ps.cellWidthPt;
            // pt-styled fragment: the declared width is the CONTENT box —
            // the cell's own pads ride on top of it in the column.
            if ((cc.ptCellWidths || cc.ps.wordMailCells) && cellDeclPt > 0) cellDeclPt += cc.ps.cellCssPadPt;
            if (cellDeclPt > cc.colModel.colDeclW[cc.colModel.colCursor]) cc.colModel.colDeclW[cc.colModel.colCursor] = cellDeclPt;
        }
        else
        {
            // A spanning cell constrains the SUM of its columns — deferred so it does not
            // floor thin spacer columns it merely crosses (which would starve the wide
            // content column of the width a browser gives it).
            cc.colModel.spanConstraints.Add((cc.colModel.colCursor, cc.span, cc.cellMin + cc.extra, cc.cellMax + cc.extra,
                cc.cellHdr > 0 ? cc.cellHdr + cc.extra : 0));
            // A spanning NOWRAP cell keeps its whole line: the grid is at least that wide, measured in
            // the face the cell draws in (the width probe's stand-in face would size the sheet for a
            // line nothing draws).
            if (cc.ps.cell!.HtmlNoWrap) RecordSpanNoWrapFloor(cc);
        }
        if (cc.ps.cellWidthPct > 0) cc.ps.rowPctSum += cc.ps.cellWidthPct;
        if (cc.ps.colSpan == 1 && cc.ps.cellWidthPct > 0)
        {
            while (cc.colModel.colPctW.Count <= cc.colModel.colCursor) cc.colModel.colPctW.Add(0);
            if (cc.redlineCells && cc.colModel.colPctW[cc.colModel.colCursor] > 0
                && Math.Abs(cc.colModel.colPctW[cc.colModel.colCursor] - cc.ps.cellWidthPct) > 1)
                cc.colModel.colPctConflict = true;
            if (cc.ps.cellWidthPct > cc.colModel.colPctW[cc.colModel.colCursor]) cc.colModel.colPctW[cc.colModel.colCursor] = cc.ps.cellWidthPct;
        }
        // A SPANNING cell's percent splits evenly over its columns (the
        // amounts grid's 35% period group over three columns) — the
        // over-declared grid dialect's fixed-layout draw resolves each
        // column at its share.
        else if (cc.overDeclaredDraw && cc.ps.colSpan > 1 && cc.ps.cellWidthPct > 0)
        {
            while (cc.colModel.colPctW.Count < cc.colModel.colCursor + cc.span) cc.colModel.colPctW.Add(0);
            var perCol = cc.ps.cellWidthPct / cc.span;
            for (var k = 0; k < cc.span; k++)
                if (perCol > cc.colModel.colPctW[cc.colModel.colCursor + k]) cc.colModel.colPctW[cc.colModel.colCursor + k] = perCol;
        }
    }

    /// <summary>Fixed divs, images, nested tables, CSS padding, chain spacing and the UA serif minimum floor the cell's measured widths.</summary>
    private static void ApplyCellContentFloors(CloseCellState cc)
    {
        if (cc.ps.cellFixedDivPt > 0)
        {
            cc.cellMin = cc.ps.cellFixedDivPt;
            cc.cellMinBrk = Math.Min(cc.cellMinBrk, cc.ps.cellFixedDivPt);
            if (cc.ps.cellFixedDivPt > cc.cellMax) cc.cellMax = cc.ps.cellFixedDivPt;
        }
        // …and an image the cell draws claims its own box in every measure.
        if (cc.ps.cellImgWidthPt > 0)
        {
            cc.cellMin = Math.Max(cc.cellMin, cc.ps.cellImgWidthPt);
            cc.cellMinBrk = Math.Max(cc.cellMinBrk, cc.ps.cellImgWidthPt);
            cc.cellMax = Math.Max(cc.cellMax, cc.ps.cellImgWidthPt);
        }
        // …and a UA text control's advance floors the cell the same way: the grid seats
        // the control at its box whatever the column was asked.
        if (cc.ps.cellControlBoxPt > 0)
        {
            cc.cellMin = Math.Max(cc.cellMin, cc.ps.cellControlBoxPt);
            cc.cellMinBrk = Math.Max(cc.cellMinBrk, cc.ps.cellControlBoxPt);
            cc.cellMax = Math.Max(cc.cellMax, cc.ps.cellControlBoxPt);
        }
        // A NESTED table sizes its cell: the grid's own natural width is the
        // cell's min- and max-content (its flattened text lines measure a
        // fraction of the real grid).
        ApplyPendingCellTableFloor(cc);
        // Redline percent grids: the pads inset the DRAWN text only — the
        // columns are shares of the content box, and pads in the measure
        // would widen the sheet past the expected width.
        if (cc.ps.cellCssPadPt > 0 && !cc.redlineCells)
        {
            cc.cellMin += cc.ps.cellCssPadPt; cc.cellMinBrk += cc.ps.cellCssPadPt;
            if (cc.cellMinFloor > 0) cc.cellMinFloor += cc.ps.cellCssPadPt;
            cc.cellMax += cc.ps.cellCssPadPt; if (cc.cellHdr > 0) cc.cellHdr += cc.ps.cellCssPadPt;
        }
        // A declared border-spacing is part of every column's footprint: the band
        // sits OUTSIDE the cell's box, half on each side (the draw insets the
        // border by the same half, so the box itself keeps its content width).
        if (cc.chainSpacingPt > 0)
        {
            cc.cellMin += cc.chainSpacingPt; cc.cellMinBrk += cc.chainSpacingPt;
            cc.cellMax += cc.chainSpacingPt; if (cc.cellHdr > 0) cc.cellHdr += cc.chainSpacingPt;
        }
        cc.cellMinSerif = 0;
        // (a nowrap cell's floor is its whole line - probed on the state analysis: the nowrap
        // grid's sheet is 96 + its unbroken lines + 90)
        if (cc.uaSerifMin)
            foreach (var spec in cc.ps.lines)
                foreach (var seg1 in spec.Text.Split(''))
                foreach (var word in cc.ps.cell?.HtmlNoWrap == true ? new[] { seg1.Trim() } : seg1.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    cc.cellMinSerif = Math.Max(cc.cellMinSerif,
                        MeasureSerifLine(cc.cellFontSize, word, cc.ps.isHeader || spec.Bold,
                            spec.FontPt > 0 ? spec.FontPt : 0.0));
        // (the serif floor holds the cell's text control too - the sheet grows past its box)
        if (cc.uaSerifMin && cc.ps.cellControlBoxPt > 0) cc.cellMinSerif = Math.Max(cc.cellMinSerif, cc.ps.cellControlBoxPt);
    }

    /// <summary>Every line's min-content, max-content, header and break widths are measured in its own face and size; nowrap and box-floored lines widen the minimum.</summary>
    private static void MeasureCellLineExtents(CloseCellState cc)
    {
        cc.cellMin = 0;
        cc.cellMax = 0;
        cc.cellHdr = 0;
        cc.cellMinBrk = 0;
        MeasureCellLineWidths(cc);
        // Break-anywhere sheet: the breakable min is one character — an em of
        // the cell's size (see breakAnywhereDoc above). The NO-BREAK min
        // shrinks the same way: with break-anywhere in force nothing is
        // unbreakable, so neither floor may eat a neighbour's declared share.
        if (cc.breakAnywhereDoc)
        {
            var oneEm = cc.ps.cellClassPt > 0 ? cc.ps.cellClassPt : cc.cellFontSize;
            if (cc.cellMinBrk > 0) cc.cellMinBrk = Math.Min(cc.cellMinBrk, oneEm);
            if (cc.cellMin > 0) cc.cellMin = Math.Min(cc.cellMin, oneEm);
        }
        // white-space:nowrap under the chain dialect: the cell's floor is its
        // whole unwrapped line — nowrap labels never wrap,
        // so a space-broken min under-sizes the column and the cell
        // fill stops mid-text. The page-width probe honours the same rule
        // everywhere: the page widens for a nowrap run rather than
        // wrapping it (the render dialects keep their calibrated floors).
        if (cc.ps.cell!.HtmlNoWrap && cc.dwFormCells)
        {

            // A nowrap cell floors at its TRIMMED line — the
            // trailing spaces hang past the column instead of sizing it.
            double dwTrimMax = 0;
            foreach (var spec2 in cc.ps.lines)
                dwTrimMax = Math.Max(dwTrimMax, MeasureLine(cc.ps, cc.options, cc.cellFontSize, cc.dwFormCells, cc.fullWidthCjkMin, cc.widenProbe, spec2.Text.TrimEnd(),
                    cc.ps.isHeader || spec2.Bold, spec2.FontPt > 0 ? spec2.FontPt : 0.0,
                    spec2.Family ?? cc.ps.cellFamily));
            cc.cellMin = Math.Max(cc.cellMin, dwTrimMax);
            cc.cellMinBrk = Math.Max(cc.cellMinBrk, dwTrimMax);
        }
        else if (cc.ps.cell.HtmlNoWrap && (cc.chainBase is not null || cc.fullWidthCjkMin)
            && cc.cellMax > cc.cellMin)
        {
            cc.cellMin = cc.cellMax;
            cc.cellMinBrk = Math.Max(cc.cellMinBrk, cc.cellMax);
        }
        // An inline-box line's width is its BOX extent (pads, circle and sibling
        // gaps included) — the flat text under-measures the plates by their
        // padding, and the column then leaves dead space beside its neighbour.
        if (cc.boxByLine is not null)
        {
            double bxExt = 0;
            foreach (var bl3 in cc.boxByLine.Values)
                foreach (var b6 in bl3)
                    bxExt = Math.Max(bxExt, b6.XOff + b6.Width);
            if (bxExt > 0)
            {
                cc.cellMin = Math.Max(cc.cellMin, bxExt + InlineBoxColumnSlackPt);
                cc.cellMinBrk = Math.Max(cc.cellMinBrk, bxExt + InlineBoxColumnSlackPt);
                cc.cellMax = Math.Max(cc.cellMax, bxExt + InlineBoxColumnSlackPt);
            }
        }
    }

    /// <summary>Deferred nested tables and images take their place after the text, and the cell's padding and margins are written from the parsed attribute, CSS and chain values.</summary>
    private static void PlaceDeferredCellContentAndPadding(CloseCellState cc)
    {
        if (cc.ps.pendingCellTables is { Count: > 0 })
        {
            foreach (var (ptbl, _) in cc.ps.pendingCellTables) cc.ps.cell!.Paragraphs.Add(ptbl);
            cc.ps.pendingCellTables.Clear();
            cc.cellHadNestedTable = true;
        }
        if (cc.cellHadNestedTable)
        {
            (cc.colModel.nestedTableCols ??= new HashSet<int>()).Add(cc.ps.row!.Cells.Count);
            // This grid really does hold a lifted nested table — the browser line
            // box applies to it (see Table.HtmlLiftedGrid).
            cc.table.HtmlLiftedGrid = true;
        }
        if (cc.ps.pendingCellImgs is { Count: > 0 })
        {
            foreach (var pimg in cc.ps.pendingCellImgs) cc.ps.cell!.Paragraphs.Add(pimg);
            cc.ps.pendingCellImgs.Clear();
            cc.ps.pendingCellImgAt?.Clear();
        }
        // The cell's own CSS padding becomes its box padding: it indents the drawn
        // text and narrows the wrap box exactly as it widened the column. A chain
        // rule's vertical padding rides the same box (the horizontal pair came in
        // through cellCssPadPt/cellPadLeftPt). A full Margin REPLACES the table's
        // DefaultCellPadding wholesale, so the chain dialect keeps the default's
        // vertical band (the cellspacing rhythm) when the cell adds none.
        if (cc.ps.cellPadLeftPt > 0 || cc.ps.cellCssPadPt > 0 || cc.ps.cellChainPadTopPt > 0 || cc.ps.cellChainPadBotPt > 0
            || (cc.redlineCells && cc.ps.cellFirstPMarginTopPt > 0))
        {
            var vTop = Math.Max(cc.pad, cc.ps.cellChainPadTopPt);
            var vBot = Math.Max(cc.pad, cc.ps.cellChainPadBotPt);
            var hExtra = 0.0;
            if (cc.chainBase is not null && cc.table.DefaultCellPadding is { } dcpM)
            {
                vTop = Math.Max(vTop, dcpM.Top);
                vBot = Math.Max(vBot, dcpM.Bottom);
            }
            // A declared border-spacing is a gap OUTSIDE the cell's own padding,
            // so it adds to it rather than competing with it (the pill's detail
            // button keeps its 1ex pad and still sits 2 pt off its neighbours).
            if (cc.chainSpacingPt > 0)
            {
                vTop += cc.chainSpacingPt / 2;
                vBot += cc.chainSpacingPt / 2;
                hExtra = cc.chainSpacingPt / 2;
            }
            // …and so is a UA-boxed grid's cellspacing (declared, or the UA's own 2px): half of it on every
            // side of a cell that pads itself, the way the default padding of its neighbours carries it.
            else if (cc.uaCellBoxes && cc.colModel.tblCellSpacingPt > 0)
            {
                vTop += cc.colModel.tblCellSpacingPt / 2;
                vBot += cc.colModel.tblCellSpacingPt / 2;
                hExtra = cc.colModel.tblCellSpacingPt / 2;
            }
            // …and the table-level padSide is read from the SAME declaration that
            // gave the chain its cellCssPadPt, so adding both insets the text a
            // second pad in and narrows the wrap box by a whole pair — the drawn
            // twin of the column footprint's `padSideExtra` bill.
            var padSideBox = cc.chainBase is not null && cc.ps.cellCssPadPt > 0 ? 0 : cc.padSide;
            // redline: the first paragraph's margin-top rides as cell pad
            // (a cell whose content is entirely hidden spends none of it)
            if (cc.redlineCells && cc.ps.cellFirstPMarginTopPt > 0)
            {
                var rlAnyText = false;
                foreach (var rlSpec in cc.ps.lines)
                    if (rlSpec.Text.Trim(' ', ' ').Length > 0) { rlAnyText = true; break; }
                if (rlAnyText) vTop += cc.ps.cellFirstPMarginTopPt;
            }
            cc.ps.cell!.Margin = new MarginInfo(padSideBox + cc.ps.cellPadLeftPt + hExtra, vBot,
                padSideBox + (cc.ps.cellCssPadPt - cc.ps.cellPadLeftPt) + hExtra, vTop);
        }
        cc.ps.cell!.IsWordWrapped = true;
        // The block margin the cell's last block leaves pending is space at the cell's foot
        // (a closing <p>'s 1.12em under the band it holds, measured on the job status row).
        cc.ps.uaPendingMarginPt = 0;
        cc.ps.cell.ColSpan = Math.Max(1, cc.ps.colSpan);
        // A lifted table is measured by the layout pass, which reads the span from
        // the cell; the legacy grid keeps its own calibrated row mapping.
        if (cc.liftNestedTables && cc.ps.cellRowSpan > 1) cc.ps.cell.RowSpan = cc.ps.cellRowSpan;
        cc.ps.cell.Alignment = cc.ps.alignSet ? cc.ps.cellAlign
            : cc.ps.rowAlign
            ?? (cc.ps.isHeader ? cc.ps.headerAlign ?? HorizontalAlignment.Center : HorizontalAlignment.Left);
        if (cc.ps.isHeader && cc.ps.headerBorder is not null) cc.ps.cell.Border = cc.ps.headerBorder;
        // Declared-zero vertical padding: the cell's box loses the table's
        // cellpadding on the BOTTOM side (over-declared grid dialect). The
        // top half stays — the rows keep their seat while the
        // inter-row pitch tightens (zeroing both drifted every row 3.5 high).
        if (cc.overDeclaredDraw && cc.ps.cellVPadZeroBot && cc.ps.cell.Margin is null)
        {
            var vpSide = Math.Max(0, cc.padSide);
            cc.ps.cell.Margin = new MarginInfo(vpSide, 0, vpSide, vpSide);
        }
    }

    /// <summary>Unstyled cells keep the legacy line structure: the paragraph splits are rejoined so plain-markup tables lay out as before.</summary>
    private static void RejoinUnstyledParagraphSplits(CloseCellState cc)
    {
        cc.anyStyled = cc.cellFontShorthand || cc.cssRunFace is not null || cc.ps.sheetPMarginTopPt > 0;
        foreach (var l in cc.ps.lines) if (l.FontPt > 0) cc.anyStyled = true;
        // Unstyled cells keep the LEGACY line structure: lines split only at <br>/<img>/
        // cell close. Rejoin the paragraph (<p>) splits so plain-markup tables are
        // byte-identical to the pre-styled-cell behaviour.
        if (!cc.anyStyled)
            for (var k = 0; k < cc.ps.lines.Count - 1; k++)
                if (cc.ps.lines[k].JoinNext)
                {
                    var merged = CollapseWs(cc.ps.lines[k].Text + " " + cc.ps.lines[k + 1].Text);
                    var mergedAnchors = cc.ps.lines[k].Anchors;
                    if (cc.ps.lines[k + 1].Anchors is { } nextAnchors)
                        (mergedAnchors ??= new()).AddRange(nextAnchors);
                    cc.ps.lines[k] = (merged, 0, cc.ps.lines[k].Family ?? cc.ps.lines[k + 1].Family,
                        false, cc.ps.lines[k + 1].JoinNext, mergedAnchors,
                        // An empty residue line (the whitespace before a <p>) casts no vote on the merged line's weight or slant.
                        (cc.ps.lines[k].Text.Length == 0 || cc.ps.lines[k].Bold) && (cc.ps.lines[k + 1].Text.Length == 0 || cc.ps.lines[k + 1].Bold),
                        cc.ps.lines[k].MarginTopPt, cc.ps.lines[k].MarginLeftPt,
                        cc.ps.lines[k].Color ?? cc.ps.lines[k + 1].Color,
                        (cc.ps.lines[k].Text.Length == 0 || cc.ps.lines[k].Italic) && (cc.ps.lines[k + 1].Text.Length == 0 || cc.ps.lines[k + 1].Italic));
                    cc.ps.lines.RemoveAt(k + 1);
                    k--;
                }
    }

    /// <summary>The cell's declared width joins the row footprint, an nbsp-only or pending-blank cell keeps its line box, and a fixed-width div inside the cell becomes its own line.</summary>
    private static void RecordCellWidthAndBlankLines(CloseCellState cc)
    {
        cc.ps.rowWidths.Add(cc.ps.cellWidthPt > 0 ? cc.ps.cellWidthPt + cc.ps.cellCssPadPt : cc.ps.cellWidthPt);
        if (cc.ps.colSpan > 1 || cc.ps.cellWidthPt <= 0) cc.ps.rowAllSingleExplicit = false;
        // Band dialect: an &nbsp;-only cell is a text row in a browser — a line
        // box at the cell's font size — unlike a truly empty <td></td>, which
        // collapses. Keep its blank line so the row spacing holds
        // (the corner-mark spacer rows of proxy cards are built from these).
        if (cc.bandDialect && cc.ps.lines.Count == 0
            && IsAllWhitespace(cc.ps.line)
            && cc.ps.line.ToString().IndexOf(' ') >= 0)
        {
            if (cc.ps.lineFontPt <= 0) cc.ps.lineFontPt = cc.ps.curFontPt > 0 ? cc.ps.curFontPt : cc.ps.rowFontPt;
            if (cc.ps.lineFontPt > 0) PushLine(cc.ps, cc.redlineCells, cc.dwFormCells, cc.widenProbe, keepIfBlank: true);
            else PushLine(cc.ps, cc.redlineCells, cc.dwFormCells, cc.widenProbe);
        }
        // The cell ended on a <br> with nothing after it — the break's own empty
        // line box is real vertical space, so it must not be swallowed here.
        else if (cc.ps.cellPendingBrBlank && IsAllWhitespace(cc.ps.line))
        {
            // The blank box takes the type the cell itself sets in — a cell that
            // declares no size of its own still has one, so the break's line is
            // real space rather than a zero-height line that gets swept away.
            if (cc.ps.lineFontPt <= 0)
                cc.ps.lineFontPt = cc.ps.curFontPt > 0 ? cc.ps.curFontPt
                    : cc.ps.rowFontPt > 0 ? cc.ps.rowFontPt
                    : cc.uaDocGrid ? cc.cellFontSize : 0;
            PushLine(cc.ps, cc.redlineCells, cc.dwFormCells, cc.widenProbe, keepIfBlank: cc.ps.lineFontPt > 0);
        }
        else PushLine(cc.ps, cc.redlineCells, cc.dwFormCells, cc.widenProbe);
        // The div box is also the WRAP box: a run too long for it breaks inside the
        // div — after a hyphen when one fits, otherwise mid-token (break-word) —
        // instead of running the full width of the enclosing cell.
        if (cc.ps.cellFixedDivPt > 0 && cc.ps.lines.Count > 0)
        {
            var rewrapped = new List<(string Text, double FontPt, string? Family, bool Keep,
                bool JoinNext, List<(string Text, string Url)>? Anchors, bool Bold,
                double MarginTopPt, double MarginLeftPt, Color? Color, bool Italic)>();
            foreach (var spec in cc.ps.lines)
            {
                var wPt = spec.FontPt > 0 ? spec.FontPt : 0.0;
                Func<string, double> boxMeasure = cc.uaCellBoxes
                    ? s => MeasureSerifLine(cc.cellFontSize, s, spec.Bold, wPt)
                    : s => MeasureLine(cc.ps, cc.options, cc.cellFontSize, cc.dwFormCells, cc.fullWidthCjkMin, cc.widenProbe, s, spec.Bold, wPt, spec.Family);
                if (spec.Text.Length == 0 || boxMeasure(spec.Text) <= cc.ps.cellFixedDivPt)
                {
                    rewrapped.Add(spec);
                    continue;
                }
                var firstPiece = true;
                foreach (var piece in WrapToBox(spec.Text, cc.ps.cellFixedDivPt, boxMeasure))
                {
                    rewrapped.Add((piece, spec.FontPt, spec.Family, spec.Keep,
                        firstPiece && spec.JoinNext, firstPiece ? spec.Anchors : null, spec.Bold,
                        firstPiece ? spec.MarginTopPt : 0, spec.MarginLeftPt, spec.Color,
                        spec.Italic));
                    firstPiece = false;
                }
            }
            cc.ps.lines.Clear();
            cc.ps.lines.AddRange(rewrapped);
        }
    }

    /// <summary>The line's measured boxes, inline options and input boxes are attached to its fragment.</summary>
    private static void PlaceCellLineBoxesAndControls(CloseCellState cc)
    {
        if (cc.boxByLine is not null && cc.boxByLine.TryGetValue(cc.tfLineIdx, out var tfBoxes))
        {
            cc.tf.InlineBoxes = tfBoxes;
            double bxPadT = 0, bxPadB = 0, bxInsetV = 0;
            var bxCircle = false;
            foreach (var b4 in tfBoxes)
            {
                bxPadT = Math.Max(bxPadT, b4.PadTop);
                // A declared-height box (title plate) self-sizes its rect:
                // its bottom pad lives inside the rect, not in the line stack
                // (the continuation line follows at text pitch).
                if (b4.Height <= 0) bxPadB = Math.Max(bxPadB, b4.PadBottom);
                bxInsetV = Math.Max(bxInsetV, b4.InsetV);
                if (b4.CircleFill is not null) bxCircle = true;
            }
            // The 15pt floor exists for the badge CIRCLE's diameter — a bar
            // with no circle keeps its text's own line box (circle-less h2
            // bars are ~12.8, not 15+).
            var bxLineH = bxPadT
                + Math.Max(cc.tf.TextState.FontSize * Table.CssNormalLineHeight,
                    bxCircle ? 15.0 : 0.0)
                + bxPadB + 2 * bxInsetV;
            if (bxLineH > cc.tf.CssLineHeightPt) cc.tf.CssLineHeightPt = bxLineH;
        }
        // Hand this fragment the radio options its marker chars stand for
        // (in document order — options were queued as their inputs were
        // walked, and lines flush in the same order).
        if (cc.ps.cellInlineOptions is { Count: > 0 })
        {
            var nMarks = 0;
            foreach (var mch in cc.ln)
                if (mch is Table.InlineRadioChar or Table.InlineRadioCheckedChar) nMarks++;
            if (nMarks > 0)
            {
                var take = Math.Min(nMarks, cc.ps.cellInlineOptions.Count);
                cc.tf.InlineOptions = cc.ps.cellInlineOptions.GetRange(0, take);
                cc.ps.cellInlineOptions.RemoveRange(0, take);
            }
        }
        if (cc.ps.cellInputBoxes is { Count: > 0 })
        {
            var nBoxMarks = 0;
            foreach (var mch in cc.ln)
                if (mch == Table.InlineInputChar) nBoxMarks++;
            if (nBoxMarks > 0)
            {
                var take = Math.Min(nBoxMarks, cc.ps.cellInputBoxes.Count);
                cc.tf.InlineInputBoxes = cc.ps.cellInputBoxes.GetRange(0, take);
                cc.ps.cellInputBoxes.RemoveRange(0, take);
            }
        }
    }

    /// <summary>The line's colour, decorations, colour runs and margins come from the spec and the cell's parsed padding and alignment.</summary>
    private static void StyleCellLineColorAndMargins(CloseCellState cc, CellLineSpec spec)
    {
        if ((spec.Color ?? cc.ps.cellChainColor ?? cc.ps.cellTextColor) is { } tfColor)
            cc.tf.TextState.ForegroundColor = tfColor;
        // Band dialect: the paragraph's explicit margins become the fragment's
        // margins — a gap above its first line and an indent that narrows its
        // wrap box in the cell layout.
        // …and a UA-boxed cell keeps its lines' own margins too: a list item's hanging
        // indent and a paragraph's top margin are the browser's, not the dialect's.
        if ((cc.bandDialect || cc.liftNestedTables || cc.ps.sheetPMarginTopPt > 0 || (cc.uaCellBoxes))
            && (spec.MarginTopPt > 0 || spec.MarginLeftPt > 0))
            cc.tf.Margin = new MarginInfo { Top = spec.MarginTopPt, Left = spec.MarginLeftPt };
        // pt-styled fragment: the paragraph margin pair insets the WRAP
        // box (see Table.HtmlWrapInsetsCellMargins).
        if ((cc.ptCellWidths || (cc.uaCellBoxes)) && (cc.ps.cellPMarginRightPt > 0 || spec.MarginLeftPt > 0))
        {
            cc.tf.HtmlWrapInsetPt = cc.ps.cellPMarginRightPt + spec.MarginLeftPt;
            cc.tf.HtmlMarginLeftPt = spec.MarginLeftPt;
        }
        if (cc.redlineCells && cc.ps.lineDecorsByIdx is not null
            && cc.ps.lineDecorsByIdx.TryGetValue(cc.tfLineIdx, out var tfDecs))
            cc.tf.HtmlDecors = tfDecs;
        if (cc.dwFormCells && cc.ps.lineColorRunsByIdx is not null
            && cc.ps.lineColorRunsByIdx.TryGetValue(cc.tfLineIdx, out var tfCols))
            cc.tf.HtmlColorRuns = tfCols;
        // The cell's own padding-left indents its text the way it widened the
        // column — a left-aligned run starts that far inside the cell box.
        // (…unless the cell BOX already carries it: a sheet that declares its cells' padding insets the
        //  box itself, and indenting the run as well spends the pad twice)
        if (cc.ps.cellPadLeftPt > 0 && !cc.ps.sheetTdBoxRule && cc.ps.cellAlign != HorizontalAlignment.Right)
            cc.tf.Margin = new MarginInfo
            {
                Top = cc.tf.Margin?.Top ?? 0,
                Left = (cc.tf.Margin?.Left ?? 0) + cc.ps.cellPadLeftPt,
                Right = cc.tf.Margin?.Right ?? 0,
            };
    }

    /// <summary>The line's face, size and line pitch: styled cells resolve their CSS face and size, form grids, own-line-height cells and the dialects each set their pitch.</summary>
    private static void ResolveCellLineFaceAndPitch(CloseCellState cc, CellLineSpec spec)
    {
        if (cc.anyStyled)
        {
            ResolveStyledLineFace(cc, spec);
        }
        // The lifted dialect keeps deliberate blank line boxes (<br> on an
        // empty line) whether or not the cell is styled — the generator
        // needs the flag to price them as real boxes.
        if (cc.liftNestedTables) cc.tf.CssKeepBlank = spec.Keep;
        if (cc.ps.isHeader || spec.Bold || cc.ps.cellBold) cc.tf.TextState.IsBold = true;
        if (spec.Italic) cc.tf.TextState.IsItalic = true;
        // Mixed bold runs on this line (form-grid): the render draws each
        // segment in its own face variant.
        if (cc.ps.lineRunsByIdx is not null
            && cc.ps.lineRunsByIdx.TryGetValue(cc.tfLineIdx, out var fgRuns))
            cc.tf.FormGridRuns = fgRuns;
        // Source page's CSS line-height (e.g. body `font: 1em/1.4em …`):
        // wrapped cell lines pitch at the CSS box, not the bare font size.
        // UA cell boxes: every line takes the `line-height: normal` box of its
        // OWN size, so an 8 pt row pitches at 9 pt while a 10 pt one takes 11.25
        // — a single document-wide pitch oversizes every small-font row.
        if (cc.ps.wordMailCells && cc.ps.lineHeightPctByIdx is not null
            && cc.ps.lineHeightPctByIdx.TryGetValue(cc.tfLineIdx, out var wmPct))
            cc.tf.CssLineHeightPt = cc.tf.TextState.FontSize * wmPct / WholeWidthPercent;
        // …and a Word-mail line with no line-height of its own paces on its face's normal line,
        // as the mail's body paragraphs do (Verdana 9 steps 10.9, Arial 12 steps 13.8).
        else if (cc.ps.wordMailCells && (spec.Family ?? cc.ps.cellFamily) is { } wmLineFam && WinMetricsFor(wmLineFam) is { } wmLineFm)
            cc.tf.CssLineHeightPt = MetricLineHeight(cc.tf.TextState.FontSize, HheaLineSumFor(wmLineFam) ?? wmLineFm.sum);
        // A UA-boxed cell's own line-height (its td or tr style) is its lines' box (the remittance rows' 30 px)…
        else if (cc.uaCellBoxes && cc.ps.cellOwnLineHPt > 0)
        { cc.tf.CssLineHeightPt = cc.ps.cellOwnLineHPt; cc.tf.CssLineHeightDeclared = true; }
        // …a line whose element rule states a factor (`h2 { line-height: 1.2em }`) stands on its own size times
        // it, and so does every line under a body rule stating one (the mailing's `body { line-height: 125% }`
        // steps its 13 px lines 16.25 px and its 12 px form lines 15 px).
        else if (cc.uaCellBoxes && cc.ps.lineHeightPctByIdx is not null
                 && cc.ps.lineHeightPctByIdx.TryGetValue(cc.tfLineIdx, out var uaLinePct) && uaLinePct > 0)
        { cc.tf.CssLineHeightPt = cc.tf.TextState.FontSize * uaLinePct / WholeWidthPercent; cc.tf.CssLineHeightDeclared = true; }
        else if (cc.uaCellBoxes && cc.ps.uaLineFactor > 0)
        { cc.tf.CssLineHeightPt = cc.tf.TextState.FontSize * cc.ps.uaLineFactor; cc.tf.CssLineHeightDeclared = true; }
        // …and a cell whose paragraphs the sheet spaces paces its lines on the normal line box too.
        else if (cc.uaCellBoxes || cc.ps.sheetPMarginTopPt > 0)
            cc.tf.CssLineHeightPt = NormalLineHeightPt(cc.tf.TextState.FontSize);
        // Form-grid dialect: every line takes its own size's px-rounded
        // Verdana line box, floored at the cell's strut — the td's own
        // declared size when it styles one, else the chunk's ambient box.
        // The 8pt rows sit on the strut; the 36pt spacer row grows to
        // its 58px box (43.5).
        else if (cc.formGridDialect)
        {
            PitchFormGridLine(cc, spec);
        }
        // `line-height: normal` is the BASE FACE's own win-metric box, not the
        // serif-calibrated constant — a 24 pt run in a Segoe UI page pitches on
        // Segoe's ratio (32.25), a 9.75 pt one on 12.75.
        else if (cc.cssRunFace is not null && WinMetricsFor(cc.cssRunFace) is { } crm)
            cc.tf.CssLineHeightPt = MetricLineHeight(cc.tf.TextState.FontSize, crm.sum);
        else ResolveCellLinePitchTail(cc, spec);
    }

    /// <summary>A form-cell line pitches on its own font size.</summary>
    private static void PitchFormCellLine(CloseCellState cc, CellLineSpec spec)
    {
        var dwLn = spec.Text;
        cc.tf.CssLineHeightPt = cc.tf.TextState.FontSize >= DwH1FontPt - 0.1
            ? DwH1LineBoxPt

            // …and radio/checkmark option lines pitch at the widget
            // box (14.06 measured between the options).
            : dwLn.IndexOf(Table.InlineRadioChar) >= 0
                || dwLn.IndexOf(Table.InlineRadioCheckedChar) >= 0
                || dwLn.IndexOf(Table.InlineCheckChar) >= 0
                || dwLn.IndexOf('✓') >= 0 ? DwOptionLinePt
            : cc.tf.TextState.FontSize * RedlineLineFactor;
    }

    /// <summary>A form-grid line takes the grid's strut pitch and its drop.</summary>
    private static void PitchFormGridLine(CloseCellState cc, CellLineSpec spec)
    {
        var fgStrut = cc.ps.cellFgStrutPt > 0 ? cc.ps.cellFgStrutPt
            : cc.formGridStrutPt > 0 ? cc.formGridStrutPt : VerdanaGridMinLinePt;
        cc.tf.CssLineHeightPt = Math.Max(
            PxLinePt(cc.tf.TextState.FontSize, VerdanaWinLineRatio), fgStrut);
        // The line's baseline seat: the run's own drop within its box,
        // floored at the strut's (the td-own strut carries its own).
        var fgRunDrop = (cc.tf.CssLineHeightPt
            - cc.tf.TextState.FontSize * VerdanaWinLineRatio) / 2
            + cc.tf.TextState.FontSize * VerdanaWinAscent;
        var fgStrutDrop = cc.ps.cellFgStrutPt > 0
            ? (cc.ps.cellFgStrutPt - cc.ps.cellFgStrutFontPt * VerdanaWinLineRatio) / 2
                + cc.ps.cellFgStrutFontPt * VerdanaWinAscent
            : cc.formGridStrutDropPt;
        cc.tf.CssBaseDrop = Math.Max(fgStrutDrop, fgRunDrop);
        if (Environment.GetEnvironmentVariable("ASPOSE_HTML_DEBUG_FG") is not null
            && spec.FontPt > 12)
            Console.WriteLine($"[fg] specPt={spec.FontPt} tfPt={cc.tf.TextState.FontSize} " +
                $"lh={cc.tf.CssLineHeightPt:0.##} anyStyled={cc.anyStyled} txt='{cc.ln}'");
    }

    /// <summary>A styled line resolves its face and size from the spec, the cell's CSS run face and the base family.</summary>
    private static void ResolveStyledLineFace(CloseCellState cc, CellLineSpec spec)
    {
        var pt = spec.FontPt > 0 ? spec.FontPt
            : cc.ps.cellClassPt > 0 ? cc.ps.cellClassPt
            : (cc.cssBasePt > 0 ? cc.cssBasePt : cc.cellFontSize);
        cc.tf.TextState.FontSize = (float)pt;
        // The TextFragment ctor's segment carries its own default size which the
        // generator prefers — set it too so the styled size actually applies.
        foreach (var seg in cc.tf.Segments)
            if (!string.IsNullOrEmpty(seg.Text)) seg.TextState.FontSize = (float)pt;
        var (asc, desc) = CssFamilyMetrics(spec.Family ?? cc.cssBaseFamily);
        // (a UA-boxed line's `line-height: normal` box is the face's hhea line INCLUDING its line gap:
        //  Arial 9 pt stands on 14 px = 10.5, not the 13 px its win ascent + descent alone give)
        if (cc.uaCellBoxes && (spec.Family ?? cc.cssBaseFamily) is { } uaFam
            && HheaLineSumFor(uaFam) is { } uaHhea && uaHhea > asc) desc = uaHhea - asc;
        cc.tf.CssAscent = asc; cc.tf.CssDescent = desc;
        cc.tf.CssKeepBlank = spec.Keep;
        cc.tf.CssLineBoxAlways = cc.cellFontShorthand || cc.cssRunFace is not null || cc.ps.resetSheetGrid;
    }

    /// <summary>A line's min-content (its widest space-separated word) and max-content (the whole
    /// line) in an installed face at a size.</summary>
    private static (double min, double max) MeasureClassFaceExtents(string face, double pt, string line)
    {
        double min = 0;
        foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            min = Math.Max(min, MeasureFaceText(face, word, pt));
        return (min, MeasureFaceText(face, line.TrimEnd(), pt));
    }

    /// <summary>The widest whole line of a column-spanning nowrap cell, in the cell's class face and
    /// size when the face is installed (else the probe's own measure), floors the grid's width.</summary>
    private static void RecordSpanNoWrapFloor(CloseCellState cc)
    {
        double widest = 0;
        var pt = cc.ps.cellClassPt > 0 ? cc.ps.cellClassPt : cc.cellFontSize;
        foreach (var spec in cc.ps.lines)
        {
            var ln = spec.Text.TrimEnd();
            if (ln.Length == 0) continue;
            var w = cc.ps.cellClassFamily is { } fam
                ? MeasureFaceText(fam, ln, pt)
                : MeasureLine(cc.ps, cc.options, cc.cellFontSize, cc.dwFormCells, cc.fullWidthCjkMin, cc.widenProbe, ln,
                    cc.ps.isHeader || spec.Bold, pt, spec.Family ?? cc.ps.cellFamily);
            widest = Math.Max(widest, w);
        }
        // (the bare line advance: the sheet ends one page margin past the line's ink, no legacy slack)
        if (widest > cc.colModel.spanNoWrapMinW)
        {
            cc.colModel.spanNoWrapMinW = widest;
            cc.colModel.spanNoWrapStart = cc.colModel.colCursor;
            cc.colModel.spanNoWrapCols = cc.span;
        }
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_CELL") == "1")
            Console.Error.WriteLine($"[spannw] span={cc.span} pt={pt} fam={cc.ps.cellClassFamily ?? "-"} lines={cc.ps.lines.Count} widest={widest:0.##} floor={cc.colModel.spanNoWrapMinW:0.##} '{(cc.ps.lines.Count > 0 ? cc.ps.lines[0].Text[..Math.Min(20, cc.ps.lines[0].Text.Length)] : "")}'");
    }

    /// <summary>ASPOSE_TRACE_CELL=1: the face, size and weight a legacy cell line resolved to.</summary>
    private static void TraceCellLine(CloseCellState cc, CellLineSpec spec)
    {
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_CELL") != "1") return;
        var txt = spec.Text.Length > 32 ? spec.Text[..32] : spec.Text;
        Console.Error.WriteLine($"[cell] pt={spec.FontPt} fam={spec.Family ?? "-"} bold={spec.Bold} styled={cc.anyStyled} runFace={cc.cssRunFace ?? "-"} clsPt={cc.ps.cellClassPt} basePt={cc.cssBasePt} baseFam={cc.cssBaseFamily ?? "-"} cellFam={cc.ps.cellFamily ?? "-"} -> fs={cc.tf.TextState.FontSize} font={cc.tf.TextState.Font?.FontName ?? "-"} b={cc.tf.TextState.IsBold} align={cc.ps.cellAlign} keep={spec.Keep} lh={cc.tf.CssLineHeightPt} runs={(cc.tf.FormGridRuns?.Count ?? 0)} padL={cc.ps.cellPadLeftPt:0.##} padLR={cc.ps.cellCssPadPt:0.##} padT={cc.ps.cellChainPadTopPt:0.##} padB={cc.ps.cellChainPadBotPt:0.##} spacing={cc.chainSpacingPt:0.##} pad={cc.pad:0.##} margin={(cc.ps.cell?.Margin is { } cm ? $"{cm.Left:0.##}/{cm.Top:0.##}/{cm.Right:0.##}/{cm.Bottom:0.##}" : "-")} '{txt}'");
    }
}
