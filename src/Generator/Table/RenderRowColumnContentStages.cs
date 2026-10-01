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
    /// <summary>Left-aligned single-face lines draw as one text object: a Tm seat, then a T* pitched line per row with its own leading and font.</summary>
    private void RenderPlainCellLines(RowColumnState rc)
    {
        rc.first = rc.cellLines![rc.firstLine];
        rc.textX = rc.cellX + rc.padLeft;
        rc.engineLift = SuppressBaselineLift
            ? 0
            : (HtmlEngineMetrics ? 0.207 : rc.borderLiftFactor) * rc.first.FontSize;
        var uaSeatDrop = UaSeatDropPt(rc.first.FontSize);
        // A DECLARED line box seats the baseline outright — the drop already
        // carries the half-leading, so the leading must not be counted again.
        var declaredDrop = DeclaredLineBoxBaseOff(rc.first);
        rc.textY = declaredDrop > 0
            ? rc.slice.TopY - rc.contentTop - declaredDrop
            : rc.slice.TopY - rc.contentTop - rc.first.Leading
                   - (uaSeatDrop > 0 ? uaSeatDrop : rc.first.FontSize - rc.engineLift);

        rc.cellHasInk = !NestedTableRender;
        for (var li = rc.firstLine; li < rc.lastLine && !rc.cellHasInk; li++)
            if (rc.cellLines[li].Text.Length > 0) rc.cellHasInk = true;
        if (rc.cellHasInk)
        {
            DrawPlainCellInk(rc);
        }

        // Collect link annotations over hyperlinked lines (page-space rects).
        RecordPlainCellLinksAndFootnotes(rc);
    }

    /// <summary>Lines with mixed alignment, Type0 faces, CSS cells, boxes, links or mixed XML sizes render line by line, each seated, clipped and decorated on its own.</summary>
    private void RenderStyledCellLines(RowColumnState rc)
    {
        rc.padRight = rc.padding?.Right ?? rc.dp;
        // (the right-hand rule of a resolved grid narrows the box by its own half,
        // less the half skeleton rule the pitch box already leaves out)
        if (CollapsedCellSides(rc.cell, rc.colWidths.Length) is { } sides)
            rc.padRight += (sides.Right - CollapsedSkeleton()) / 2;
        rc.cssCum = 0.0;
        // vertical-align: middle for a css-box stack — the whole stack
        // shifts down by half the cell's slack (the uniform path does
        // this through vaOffset; the css stack owns its own cursor).
        // A stack the slice holds WHOLE centres; a stack cut across slices seats at the
        // top of each (probed: a split terms cell fills its first page from the top and
        // continues overleaf from the top, while its short neighbour centres in that
        // first slice).
        var wholeStack = rc.firstLine == 0 && rc.lastLine >= rc.cellLines!.Count;
        if (rc.cssCell && rc.effVA == VerticalAlignment.Center && wholeStack)
        {
            double cssTotal = 0;
            var anyPlate = false;
            for (var li2 = rc.firstLine; li2 < rc.lastLine; li2++)
            {
                var cl2 = rc.cellLines![li2];
                cssTotal += cl2.BoxH > 0
                    ? (cl2.HtmlEngine && rc.generatorCell ? EngineLineExtent(cl2, li2 == rc.lastLine - 1) : cl2.BoxH)
                    : rc.slice.Plan.LineHeight;
                if (cl2.Boxes is { } bxs)
                    foreach (var b7 in bxs)
                        if (b7.Height > 0) anyPlate = true;
            }
            // Form-grid cells centre within their BORDER-inset content
            // band — the cell border pair is not distributable slack. A generator
            // cell's block centres in its inner box the same way (its row is the
            // stack plus padding plus rules, so a two-line 12 pt <li> cell seats at
            // its pad top, not half a rule lower).
            var availV = rc.slice.Height - rc.padTop - rc.padBot
                - (FormGridCells ? 2 * rc.borderInsetTop
                    : rc.generatorCell ? rc.borderInsetTop + rc.borderInsetBottom : 0);
            // cssTop subtracts the cursor, so a POSITIVE seed moves
            // the stack down into the slack. A stack holding a
            // declared-height plate is near-exact — split only its
            // small residual tail (clamped), the outer band does the
            // real centring.
            if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_CSSDEBUG") is not null)
                Console.WriteLine($"[csscentre] col={rc.col} sliceH={rc.slice.Height:F2} lineStart={rc.slice.LineStart} lines={rc.firstLine}..{rc.lastLine} of {rc.cellLines!.Count} availV={availV:F2} cssTotal={cssTotal:F2} planLH={rc.slice.Plan.LineHeight:F2} pad={rc.padTop:F2}/{rc.padBot:F2}");
            if (availV > cssTotal && !anyPlate)
                rc.cssCum = (availV - cssTotal) / 2;
        }
        rc.dwTextWalk = 0.0;
        rc.xmlCum = 0.0;
        for (var li = rc.firstLine; li < rc.lastLine; li++)
        {
            RenderStyledCellLine(rc, li);
        }
    }

    /// <summary>One pass over the slice's lines decides the render path: form controls, non-left alignment, Type0 faces, boxes, links and mixed XML sizes.</summary>
    private void ScanCellLineKinds(RowColumnState rc)
    {
        rc.hasOption = false;
        rc.anyNonLeft = false;
        rc.anyType0 = false;
        rc.anyBoxes = false;
        rc.anyLinks = false;
        rc.xmlMixedSizes = false;
        for (var li = rc.firstLine; li < rc.lastLine; li++)
        {
            var cl = rc.cellLines![li];
            if (cl.Option is not null || cl.Checkbox is not null
                || cl.InlineOptions is not null
                || cl.Text.IndexOf(InlineButtonChar) >= 0
                || cl.InputBoxes is not null
                || cl.Text.IndexOf(InlineCheckChar) >= 0
                || cl.Text.IndexOf(InlineCheckboxGapChar) >= 0) { rc.hasOption = true; break; }
            if (cl.Align is HorizontalAlignment.Center or HorizontalAlignment.Right) rc.anyNonLeft = true;
            if (cl.Type0Ttf is not null) rc.anyType0 = true;
            // A line whose pieces sit at their OWN x offsets (an HTML-engine
            // line: a list marker in its column, the item's text at the list
            // indent) cannot be expressed by the uniform text object, which
            // walks one pen from the cell's left edge.
            if (cl.Runs is { Count: > 0 }) rc.anyType0 = true;
            // A line of runs in their own sizes and faces draws run by run too.
            if (cl.SegRuns is { Count: > 0 }) rc.anyType0 = true;
            if (cl.Boxes is { Count: > 0 }) rc.anyBoxes = true;
            // Linked lines draw per-line so the anchor runs can take the
            // link blue + underline styling (lifted dialect only —
            // the legacy stream stays byte-stable).
            if (NestedTableRender
                && (cl.LinkRuns is { Count: > 0 } || cl.Hyperlink is not null)) rc.anyLinks = true;
            // XML-generator: mixed-size cell lines pitch at their own
            // sizes, and a document LineSpacing seats every baseline
            // deeper — the uniform text object expresses neither.
            // (A DOM cell's declared leading needs neither: its pitch IS
            // the row's uniform LineHeight, and only the first baseline
            // moves — see the uniform branch's textY.)
            // The GENERATOR dialect stacks a mixed-size cell the same way:
            // its lines occupy their own sizes, which the uniform text
            // object cannot express either.
            if ((XmlGeneratorModel || GeneratorCellModel)
                && (XmlLineSpacing > 0
                    || Math.Abs(cl.FontSize - rc.cellLines[rc.firstLine].FontSize) > 0.01))
                rc.xmlMixedSizes = true;
        }
    }

    /// <summary>The content top and the line range this slice draws; false when the cell is inline, has no lines or none fall in the slice.</summary>
    private bool SeatCellLineRange(RowColumnState rc)
    {
        if (rc.borderInsetLeft > 0 && rc.padding?.Left is null)
            rc.padLeft = 0;
        rc.padLeft += rc.borderInsetLeft;
        rc.engineCssStack = false;
        if (rc.cssCell && rc.cellLines is not null)
        {
            double engSeatSz = -1;
            foreach (var cl in rc.cellLines)
            {
                if (!cl.HtmlEngine || cl.FontSize <= 0) continue;
                if (engSeatSz < 0) engSeatSz = cl.FontSize;
                else if (Math.Abs(cl.FontSize - engSeatSz) > 0.5) { rc.engineCssStack = true; break; }
            }
        }
        rc.contentTop = rc.padTop + rc.vaOffset + (rc.engineCssStack ? 0 : rc.borderInsetTop);
        rc.borderLiftFactor = rc.cellDescentEm;

        if (!(!rc.cellIsInline && rc.cellLines is { Count: > 0 } && rc.slice.LineCount > 0)) return false;
        rc.firstLine = rc.slice.LineStart;
        rc.lastLine = Math.Min(rc.firstLine + rc.slice.LineCount, rc.cellLines.Count);
        // A cell whose whole content is SHORTER than the row's line window has
        // nothing at this offset, and a split row would leave it blank on every
        // page after the first. It is re-drawn instead: a row broken
        // across pages carries its "1." and "Test Date" cells at the top of each
        // continuation slice, beside the long cell that is still running.
        // An HTML-engine cell (a fragment's CSS stack) is not: it draws in the slice
        // its stack falls in and leaves the continuation slices blank (probed: the
        // signature cell beside a split terms fragment appears on the first page only).
        // A grid whose rows split under their glyphs (RowsCloseUnderTheirGlyphs) leaves such a
        // cell blank on the later parts, as a cell whose lines ran out shows nothing there.
        if (rc.generatorCell && !RowsCloseUnderTheirGlyphs && rc.firstLine >= rc.cellLines.Count && !rc.cellLines.TrueForAll(l => l.HtmlEngine))
        {
            rc.firstLine = 0;
            rc.lastLine = Math.Min(rc.slice.LineCount, rc.cellLines.Count);
        }
        return true;
    }

    /// <summary>A centre- or bottom-aligned cell drops its lines by the slack between the slice height and the content height.</summary>
    /// <summary>What an HTML-engine line adds to the block a generator cell centres: its
    /// box as the row was priced with it - a plain cell's last line ends on its baseline
    /// (probed: a 15 pt Times line in a 30 pt row seats its line top (30 - 13.686) / 2
    /// below the cell's inner top, 13.686 being the line's baseline drop), an inline run
    /// cell's last line on its descent, a block's or a list item's on its full box; the
    /// planner already sized each last box that way, so the stack fills its band.</summary>
    private static double EngineLineExtent(CellLine line, bool last) => line.BoxH;

    private void OffsetVerticallyAlignedLines(RowColumnState rc)
    {
        rc.vaOffset = 0.0;
        if (!rc.cssCell && (rc.effVA is VerticalAlignment.Center or VerticalAlignment.Bottom) && rc.cellLines is { Count: > 0 } && rc.slice.LineCount > 0)
        {
            var visLines = Math.Max(0, Math.Min(rc.slice.LineCount, rc.cellLines.Count - rc.slice.LineStart));
            // A FixedRowHeight row centres on the cell's FULL content height —
            // an overflowing cell clamps to the top pad (its excess lines clip
            // at the row bottom) while a fitting neighbour centres normally.
            // Other rows centre the lines the slice actually shows.
            var countForVa = rc.slice.Plan.Row.FixedRowHeight > 0
                ? Math.Max(0, rc.cellLines.Count - rc.slice.LineStart)
                : visLines;
            // The content block stacks at each line's OWN height (pitch =
            // font size; a checkbox line is its box height).
            double blockH = 0;
            for (var bi = rc.slice.LineStart; bi < rc.slice.LineStart + countForVa && bi < rc.cellLines.Count; bi++)
                blockH += rc.cellLines[bi].Checkbox is { Height: > 0 } vcb ? vcb.Height
                    // UA cell boxes: a line occupies its LINE BOX, not its glyph
                    // height — measuring the block at the bare font size would
                    // leave a full-height cell looking short and nudge it down.
                    // The lifted nested-table render prices its slices AND walks
                    // its draw at the uniform line box too, so the centering must
                    // measure with the same ruler — at bare font size every line
                    // fabricates (lineBox − fontSize) of slack and a tall cell
                    // sinks by half of it. A reserve line's box is its own
                    // FontSize (a share of the nested grid's real height).
                    : NestedTableRender && rc.cellLines[bi].ImgReserve && rc.cellLines[bi].FontSize > 0
                        ? rc.cellLines[bi].FontSize
                    : UaCellBoxes || NestedTableRender ? rc.slice.Plan.LineHeight
                    // An HTML-engine line is its CSS line box here as well (the row
                    // was priced at the boxes; at the bare font size a two-line 12 pt
                    // cell sinks by half of 2 x 1.5 of leading it never had).
                    : rc.cellLines[bi].HtmlEngine && rc.cellLines[bi].BoxH > 0
                        ? EngineLineExtent(rc.cellLines[bi], bi == rc.slice.LineStart + countForVa - 1)
                    // A declared leading is part of the line's pitch, so it is part
                    // of the block being centred — pricing the line at the bare
                    // font size would leave a full cell looking short and sink its
                    // text by half the leading it actually occupies.
                    : rc.cellLines[bi].FontSize + rc.cellLines[bi].Leading;
            // Generator dialect: the block centres in the INNER box — the bottom
            // border is outside it too (probed: a column-slice row 12 pt tall
            // with 1 pt rules seats its 10 pt line at 761.07, not 760.57).
            var avail = rc.slice.Height - rc.padTop - rc.padBot - rc.borderInsetTop
                - (rc.generatorCell ? rc.borderInsetBottom : 0);
            if (avail > blockH)
                rc.vaOffset = rc.effVA == VerticalAlignment.Center ? (avail - blockH) / 2 : (avail - blockH);
        }
    }

    /// <summary>The cell's content box: inline state, line list, face and descent, clip padding, effective vertical alignment and the inset border's insets.</summary>
    private void ResolveCellContentBox(RowColumnState rc)
    {
        rc.cellIsInline = rc.slice.Plan.CellInline?.ContainsKey(rc.col) ?? false;
        rc.cellLines = rc.col < rc.slice.Plan.CellLines.Count ? rc.slice.Plan.CellLines[rc.col] : null;
        rc.generatorCell = GeneratorCellModel;
        (rc.cellDescentEm, rc.cellFace, rc.cellFragmentFace) = rc.generatorCell ? CellFontDescentEm(rc.cell, rc.row) : (HelveticaDescentEm, (string?)null, true);
        rc.clipMark = rc.builder.Mark;
        rc.builder.ResetTextExtent();
        rc.clipPadL = rc.padding?.Left ?? 0;
        rc.clipPadR = rc.padding?.Right ?? 0;
        // A cell that holds only a nested grid's reserve draws no text of its own
        // (the grid draws in the nested pass below) — no empty marker run.
        if (rc.generatorCell && rc.cellLines is not null
            && rc.slice.Plan.CellTables is { } gridCells && gridCells.ContainsKey(rc.col)
            && rc.cellLines.TrueForAll(l => l.ImgReserve || l.Text.Length == 0))
            rc.cellLines = null;

        rc.padBot = rc.padding?.Bottom ?? 0;
        rc.effVA = rc.cell.VerticalAlignment != VerticalAlignment.None ? rc.cell.VerticalAlignment : rc.row.VerticalAlignment;
        // Generator tables: a vertical alignment set on ANY cell of the row seats
        // the whole row's cells (a Center set on the second cell centres the
        // first cell's block in the row as well — both rows of the probed grid).
        if (rc.effVA == VerticalAlignment.None && rc.generatorCell)
            rc.effVA = RowCellVerticalAlignment(rc.row);
        // Generator tables centre a cell's block in its row by default: the
        // reference seats a one-line cell beside a six-line neighbour at the
        // row's vertical middle (probed 2026-08-23: 2- and 6-line rows, with and
        // without padding/margins; explicit Top/Bottom behave as named).
        if (rc.effVA == VerticalAlignment.None && rc.generatorCell)
            rc.effVA = VerticalAlignment.Center;
        // Text sharing a row with a (taller) cell image is vertically centred
        // even under the default alignment.
        if (rc.effVA is VerticalAlignment.Top or VerticalAlignment.None
            && rc.slice.Plan.CellImages is { Count: > 0 } && !rc.slice.Plan.CellImages.ContainsKey(rc.col)
            // …but an EXPLICIT top seat under the over-declared grid dialect
            // holds: the owner row's tick-box image must not re-centre the
            // name/address cells its `<tr valign="top">` pinned to the top.
            && !(HtmlOverDeclaredDraw && rc.cell.VerticalAlignment == VerticalAlignment.Top)
            // …and a generator cell's explicit Top behaves as named (probed
            // 2026-08-23: Top/Bottom seat where they say, only None centres).
            && !(rc.generatorCell && rc.cell.VerticalAlignment == VerticalAlignment.Top))
            rc.effVA = VerticalAlignment.Center;
        // HTML-engine metrics: cells centre in their row by default (a short label
        // sharing a row with a tall HTML cell sits at the row's vertical middle).
        // The same holds under UA cell boxes and the lifted nested-table render,
        // where it is simply the `vertical-align: middle` a browser gives every
        // cell (a chain rule's explicit `vertical-align: top` was set on the cell
        // and skips this default).
        // (…and a UA-boxed cell's EXPLICIT `vertical-align: top` - its own or its sheet's td rule - holds:
        //  measured on the quotation's grids, whose `table td { vertical-align: top }` seats the row numbers
        //  beside a five-line cell at the top)
        if ((HtmlEngineMetrics || UaCellBoxes)
            && (rc.effVA == VerticalAlignment.None
                || (rc.effVA == VerticalAlignment.Top && !(UaCellBoxes && rc.cell.VerticalAlignment == VerticalAlignment.Top))))
            rc.effVA = VerticalAlignment.Center;
        else if ((NestedTableRender || FormGridCells) && rc.effVA is VerticalAlignment.None)
            rc.effVA = VerticalAlignment.Center;
        // XML-generator dialect: cell content is centred in its row by default
        // (single-line cells beside a two-line header sit at
        // the row's vertical middle; text beside a 60 pt logo too).
        else if (XmlGeneratorModel && rc.effVA is VerticalAlignment.None)
            rc.effVA = VerticalAlignment.Center;
        // A column-pagination slice: a row floored to the FULL grid's height
        // centres the lines this slice shows in that box (probed: the report
        // rows whose far columns wrap sit their one visible line at the
        // vertical middle of the two-line row).
        else if (ColumnSliceChild && rc.effVA is VerticalAlignment.None)
            rc.effVA = VerticalAlignment.Center;
        // An engine cell keeps its CSS seat on every slice; other css cells only on their first.
        rc.cssCell = rc.slice.Plan.CssCells?.Contains(rc.col) == true
            // (…and a UA-boxed cell on every slice: its continuation lines keep their faces and boxes)
            && (rc.slice.LineStart == 0 || SliceOpensOnEngineLine(rc) || UaCellBoxes);
        rc.insetBorder = rc.cell.Border ?? rc.row.DefaultCellBorder ?? rc.row.Border ?? DefaultCellBorder;
        rc.borderInsetLeft = 0.0;
        rc.borderInsetTop = 0.0;
        rc.borderInsetBottom = 0.0;
        if (rc.insetBorder is not null)
        {
            // A DOUBLED side insets by its whole band, not just the inner rule.
            rc.borderInsetLeft = OccupiedSideWidth(rc.insetBorder, BorderSide.Left,
                rc.insetBorder.LeftAssigned, rc.insetBorder.RawLeft);
            rc.borderInsetTop = OccupiedSideWidth(rc.insetBorder, BorderSide.Top,
                rc.insetBorder.TopAssigned, rc.insetBorder.RawTop);
            // A collapsed grid's box carries the whole of the rule on its top
            // boundary and none of the one below it (the box is shifted half a
            // rule up from the grid), so nothing is owed at the bottom.
            rc.borderInsetBottom = IsBordersCollapsed ? 0
                : OccupiedSideWidth(rc.insetBorder, BorderSide.Bottom,
                    rc.insetBorder.BottomAssigned, rc.insetBorder.RawBottom);
        }
        // A grid resolved boundary by boundary seats the box half of its OWN rules
        // inside the lines around it. The pitch box already stands half a skeleton
        // rule above and to the left of those lines, so that half is added back on
        // the top and left and taken off at the bottom.
        if (CollapsedCellSides(rc.cell, rc.colWidths.Length) is { } sides)
        {
            var skeleton = CollapsedSkeleton() / 2;
            rc.borderInsetLeft = skeleton + sides.Left / 2;
            rc.borderInsetTop = skeleton + sides.Top / 2;
            rc.borderInsetBottom = sides.Bottom / 2 - skeleton;
        }
    }

    /// <summary>One styled cell line: seated at its own alignment, drawn run by run in its faces and sizes, with its boxes, links, decorations and footnotes.</summary>
    private void RenderStyledCellLine(RowColumnState rc, int li)
    {
        // A reserved block booked on this line has its place marked in the content here, between
        // the lines around it (the runs each open and close their own text object).
        if (rc.cellLines![li].ImgReserve && ReservedBlockMarkerAt(rc, li) is { } marker) rc.builder.Raw("% " + marker);
        if (!MeasureStyledLine(rc, li)) return;
        SeatStyledLineAndDecorate(rc, li);
        // Inline boxes behind this line (title plates, status pills):
        // rounded fill, the trailing traffic-light circle with its
        // letter, and each box's OWN positioned text run. When the
        // boxes carry text they own the whole line's ink.
        DrawStyledLineBoxes(rc, li);
        DrawListMarker(rc);
        // A spill slice has no Page of its own to embed a face into
        // (its page does not exist yet, and only a caller that shares
        // one /Font dict across pages can reach back to the first).
        // Its runs still keep their PLACES: the marker column and the
        // item indent survive, drawn in the table's Standard-14 face.
        if (!DrawStyledLineRuns(rc)) return;
        rc.plResolvedFont = rc.line.BaseFont is { } namedFace && rc.page is not null
            ? RegisterFont(rc.page, namedFace)
            : rc.line.Bold && rc.page is not null ? RegisterFont(rc.page, "Helvetica-Bold") : rc.fontName;
        if (NestedTableRender
            && (rc.line.LinkRuns is { Count: > 0 } || rc.line.Hyperlink is not null))
            ShowLineWithLinks(rc.builder, rc.line, rc.plResolvedFont, rc.lineX, rc.lineBase);
        else
        {
            rc.builder.BeginText();
            rc.builder.SetFont(rc.plResolvedFont, rc.line.FontSize);
            ApplyColor(rc.builder, rc.line.ForegroundColor);
            rc.builder.MoveTextPosition(rc.lineX, rc.lineBase);
            rc.builder.ShowText(rc.line.Text);
            rc.builder.EndText();
        }
        if (rc.links is not null && rc.line.Hyperlink is not null)
            rc.links.Add((new Rectangle(rc.lineX, rc.lineBase, rc.lineX + rc.w, rc.lineTop), rc.line.Hyperlink));
        // Inline <a> runs: annotate each hyperlinked glyph run
        // at its pre-measured offset within the line.
        if (rc.links is not null && rc.line.LinkRuns is { Count: > 0 })
            foreach (var (xo, rw, rlink) in rc.line.LinkRuns)
                rc.links.Add((new Rectangle(rc.lineX + xo, rc.lineBase,
                    rc.lineX + xo + rw, rc.lineTop), rlink));
        if (rc.footnoteSink is not null && rc.line.FootNote is not null)
            rc.footnoteSink.Add((rc.line.FootNote,
                rc.lineX + MeasureWidthExact(rc.line.Text, rc.line.FontSize),
                rc.lineBase, rc.line.FontSize));
    }

    /// <summary>The line's text: styled runs without a page fall back to a single face, runs with a page draw in their own faces, and a Type0 line draws through its CID face; false when the runs drew the whole line.</summary>
    private bool DrawStyledLineRuns(RowColumnState rc)
    {
        // A line of a paragraph flowing as runs: each run in its own face, size and
        // colour on the line's baseline. A continuation slice has no page yet, so
        // its faces register on the table's start page, whose resources the
        // overflow pages inherit by name.
        if (rc.line.SegRuns is { Count: > 0 } segRuns)
        {
            // A picture among the runs stands on the baseline at its offset: drawn
            // in place when a page can hold it, else blitted by the caller.
            if (rc.page is null)
                foreach (var run in segRuns)
                    if (run.Picture is { } picture)
                        rc.imageSink?.Add((picture, new Rectangle(rc.lineX + run.X, rc.lineBase,
                            rc.lineX + run.X + run.Width, rc.lineBase + run.PictureHeight)));
            EmitStyledRuns(rc.builder, rc.fontName, rc.page ?? _buildPage, segRuns, rc.lineX, rc.lineBase, rc.page);
            return false;
        }
        if (rc.line.Runs is { Count: > 0 } fallbackRuns && rc.page is null && _buildPage is null)
        {
            foreach (var run in fallbackRuns)
            {
                if (run.Text.Length == 0) continue;
                rc.builder.BeginText();
                rc.builder.SetFont(rc.fontName, run.Size);
                ApplyColor(rc.builder, rc.line.ForegroundColor);
                rc.builder.MoveTextPosition(rc.lineX + run.X, rc.lineBase);
                rc.builder.ShowText(run.Text);
                rc.builder.EndText();
            }
            return false;
        }
        // HTML-engine line: styled serif runs at per-run x-offsets on
        // the common baseline (mixed bold/small within one line).
        if (!DrawHtmlRunLine(rc)) return false;
        // Embedded-font line (Arabic/Unicode): embed the TrueType as a Type0/CID
        // font on this page and draw the shaped glyphs as hex glyph IDs.
        if (!DrawType0Line(rc)) return false;
        return true;
    }

    /// <summary>A list item's marker: its own run in the item's face, right-aligned one marker gap before the line's text.</summary>
    private void DrawListMarker(RowColumnState rc)
    {
        if (rc.line.Marker is not { } mk || mk.Text.Length == 0) return;
        var serif = mk.Family is null || mk.Family.Contains("Times", StringComparison.OrdinalIgnoreCase)
            || mk.Family.Contains("serif", StringComparison.OrdinalIgnoreCase);
        var mkFont = serif && rc.page is not null ? RegisterFont(rc.page, "Times-Roman") : rc.fontName;
        var mkW = serif ? MeasureTimesRoman(mk.Text, mk.Size)
            : HonorCellTtfFaces ? MeasureFaceExact(mk.Text, mk.Size, false)
            : MeasureWidth(mk.Text, mk.Size);
        rc.builder.BeginText();
        rc.builder.SetFont(mkFont, mk.Size);
        ApplyColor(rc.builder, rc.line.ForegroundColor);
        rc.builder.MoveTextPosition(rc.lineX - UaListMarkerGapEm * mk.Size - mkW, rc.lineBase);
        rc.builder.ShowText(mk.Text);
        rc.builder.EndText();
    }

    /// <summary>The line's boxes draw at their measured seats: fills, borders and the text each box carries.</summary>
    private void DrawStyledLineBoxes(RowColumnState rc, int li)
    {
        if (rc.line.Boxes is { Count: > 0 } lineBoxes)
        {
            var lineBoxH = rc.line.BoxH > 0 ? rc.line.BoxH
                : Math.Max(rc.slice.Plan.LineHeight, rc.line.FontSize * 1.2);
            var boxesCarryText = false;
            // Boxes stay where the pen put them — LEFT-anchored
            // (the user-settled model: any column slack belongs to
            // the nested grid's column, never to a shift of the
            // plates; the old right-pack fought the column surplus
            // and kept resurrecting a left gap).
            const double packShift = 0.0;
            foreach (var ib in lineBoxes)
            {
                var ibStretch = 0.0;
                var bx = rc.lineX + ib.XOff + packShift;
                var ibW = ib.Width;
                // A block-level bar spans the cell's BORDER BOX
                // minus the UA pad — a sibling div's padding must
                // not inset it (the bars run border to
                // border with ~1 pt of white).
                if (ib.FullWidth)
                {
                    bx = rc.cellX + rc.bandInset + 0.75;
                    ibW = Math.Max(ib.Width, rc.cellWidth - 2 * rc.bandInset - 1.5);
                }
                if (ib.SpanCell)
                {
                    bx = rc.cellX + rc.padLeft;
                    ibW = rc.cellWidth - rc.padLeft - rc.padRight;
                }
                var drawH = ib.Height > 0 ? ib.Height : lineBoxH - 2 * ib.InsetV;
                // A declared-height plate seats a full inset pair
                // lower — its stack keeps 2·InsetV of breathing and
                // the rect centres in it. A FULL-WIDTH bar opening
                // the cell anchors at the BORDER BOX top (+UA pad):
                // a padded sibling div must not displace it.
                var ibTopOff = ib.Height > 0 ? 2 * ib.InsetV : ib.InsetV;
                var drawTop = ib.SpanCell ? rc.lineTop - ib.InsetV
                    : ib.FullWidth && li == rc.firstLine
                    ? rc.slice.TopY - rc.bandInset - 0.75
                    : rc.lineTop - ibTopOff;
                if (ib.Fill is { } boxFill)
                {
                    rc.builder.SetFillColor(boxFill.R / 255.0, boxFill.G / 255.0, boxFill.B / 255.0);
                    FillRoundedRect(rc.builder, bx, drawTop - drawH, ibW, drawH, ib.Radius);
                }
                if (ib.CircleFill is { } circleFill)
                {
                    var dD = ib.CircleD > 0 ? ib.CircleD : 14.25;
                    var ccx = bx + ibW - ib.PadRight - dD / 2;
                    var ccy = drawTop - drawH / 2;
                    rc.builder.SetFillColor(circleFill.R / 255.0, circleFill.G / 255.0, circleFill.B / 255.0);
                    FillCircle(rc.builder, ccx, ccy, dD / 2);
                    if (ib.CircleLetter is { Length: > 0 } circleLetter && rc.page is not null)
                    {
                        var lw = MeasureWidth(circleLetter, 9);
                        rc.builder.BeginText();
                        rc.builder.SetFont(RegisterFont(rc.page, "Helvetica-Bold"), 9);
                        ApplyColor(rc.builder, ib.CircleLetterColor ?? Color.White);
                        rc.builder.MoveTextPosition(ccx - lw / 2, ccy - 3.1);
                        rc.builder.ShowText(circleLetter);
                        rc.builder.EndText();
                    }
                }
                if (ib.Text is { Length: > 0 } boxText)
                {
                    boxesCarryText = true;
                    var btSize = ib.TextSize > 0 ? ib.TextSize : rc.line.FontSize;
                    var btX = ib.TextCentered
                        ? bx + Math.Max(0, (ibW - MeasureWidth(boxText, btSize)) / 2)
                        : rc.lineX + ib.TextX + packShift + ibStretch / 2;
                    rc.builder.BeginText();
                    rc.builder.SetFont(ib.TextBold && rc.page is not null
                        ? RegisterFont(rc.page, "Helvetica-Bold") : rc.fontName, btSize);
                    if (ib.TextLetterSpacing > 0)
                        rc.builder.SetCharSpacing(ib.TextLetterSpacing);
                    ApplyColor(rc.builder, ib.TextColor ?? rc.line.ForegroundColor);
                    rc.builder.MoveTextPosition(btX,
                        drawTop - ib.PadTop - btSize);
                    rc.builder.ShowText(boxText);
                    if (ib.TextLetterSpacing > 0)
                        rc.builder.SetCharSpacing(0);
                    rc.builder.EndText();
                }
            }
            if (boxesCarryText) return;
        }
    }

    /// <summary>The line's x seat by alignment, its top, collapsed seat and baseline, then the underline and decoration strokes.</summary>
    private void SeatStyledLineAndDecorate(RowColumnState rc, int li)
    {
        rc.lineX = rc.line.Align == HorizontalAlignment.Center
            // A COLLAPSED grid's box is half a rule to the left of its own
            // boundary, so its middle is not the box's middle: centre in the
            // PADDED box, which is the same box the right-aligned arm below
            // already measures from.
            ? (rc.line.LeftIndent + rc.line.RightInsetPt > 0 || IsBordersCollapsed
                ? rc.cellX + rc.padLeft + rc.line.LeftIndent + Math.Max(0,
                    (rc.cellWidth - rc.padLeft - rc.padRight - rc.line.LeftIndent
                     - rc.line.RightInsetPt - rc.w) / 2)
                : rc.cellX + Math.Max(rc.padLeft, (rc.cellWidth - rc.w) / 2))
            : rc.line.Align == HorizontalAlignment.Right
                ? Math.Max(rc.cellX + rc.padLeft,
                    rc.cellX + rc.cellWidth - rc.padRight - rc.w - rc.boxRightPad - rc.line.RightInsetPt)
                : rc.cellX + rc.padLeft + rc.line.LeftIndent;
        rc.lineTop = rc.cssCell && rc.line.BoxH > 0
            ? rc.cssTop
            : DwFormCells
            ? rc.slice.TopY - rc.contentTop - rc.dwLineOff
            : XmlGeneratorModel && rc.xmlMixedSizes
                // XML-generator: cumulative own-size pitch
                // (14/10/14 stacks at 14, then 10, …).
                ? rc.xmlTop
                : DeclaresLineBox(rc.line)
                // Each line the cell declared a box for advances by its OWN box,
                // so the tops are summed rather than multiplied out.
                ? rc.slice.TopY - rc.contentTop - DeclaredLinesHeight(rc, li)
                : rc.slice.TopY - rc.contentTop - (li - rc.firstLine) * rc.slice.Plan.LineHeight;
        rc.collapsedSeat = HtmlWrapInsetsCellMargins ? PtGridSeatDropPt
            // redline cells: AscLead seat (0.929 em) vs the
            // legacy 0.793 em drop, plus the constant 1.2 pt
            // the expected rows sit below the plain seat at every size
            : RedlineCellSeat ? 0.136 * rc.line.FontSize + 1.2
            // DataWorks h1 title: seats 9.8 below the top-aligned
            // legacy drop (measured).
            : DwFormCells
                && rc.line.FontSize >= Converters.HtmlToPdfConverter.DwH1FontPt - 0.1
            ? Converters.HtmlToPdfConverter.DwH1SeatDropPt : 0;
        var uaSeatDrop = UaSeatDropPt(rc.line.FontSize);
        // A DECLARED line box seats this line the same way it seats a plain
        // cell's: half the surplus leading, then the ascent — and no leading
        // subtracted below, because that drop has already spent it.
        var declaredDrop = DeclaredLineBoxBaseOff(rc.line);
        rc.lineBase = declaredDrop > 0
            ? rc.lineTop - declaredDrop
            : rc.lineTop - (rc.cssCell && rc.line.BaseOff > 0
            ? rc.line.BaseOff
            : uaSeatDrop > 0
            ? uaSeatDrop + rc.collapsedSeat
            : rc.line.FontSize + rc.collapsedSeat
              - (SuppressBaselineLift ? 0 : rc.borderLiftFactor * rc.line.FontSize))
            // The declared leading sits ABOVE the glyphs, pushing
            // the baseline that much deeper into the line box.
            - rc.line.Leading;
        // <u> underline: one bar under the drawn run, in the
        // text colour, at the link-underline seat. Dialect-gated:
        // legacy corpora were calibrated without <u> ink.
        if ((HtmlOverDeclaredDraw || XmlGeneratorModel) && rc.line.Underline && rc.w > 0)
        {
            if (rc.line.ForegroundColor is { } ulc)
                rc.builder.SetStrokeColor(ulc.R / 255.0, ulc.G / 255.0, ulc.B / 255.0);
            else
                rc.builder.SetStrokeColor(0, 0, 0);
            rc.builder.SetLineWidth(LinkUnderlineWPt * rc.line.FontSize / LinkProbeBasePt);
            var ulY = rc.lineBase - LinkUnderlineDropPt * rc.line.FontSize / LinkProbeBasePt;
            rc.builder.MoveTo(rc.lineX, ulY).LineTo(rc.lineX + rc.w, ulY).Stroke();
        }
        // Redline decorations: stroke the line's strike /
        // underline / marker-border ink at the converter's
        // probed offsets (see the Redline* constants).
        if (rc.line.Decors is { Count: > 0 } rdDecs && rc.w > 0)
        {
            foreach (var (rdK, rdC) in rdDecs)
            {
                var rdCol = rdK <= 2
                    ? rc.line.ForegroundColor ?? Color.FromArgb(0, 0, 0)
                    : rdC ?? Color.FromArgb(0, 0, 0);
                rc.builder.SetStrokeColor(rdCol.R / 255.0, rdCol.G / 255.0, rdCol.B / 255.0);
                rc.builder.SetLineWidth(rdK <= 2
                    ? Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineDecorWidthEm * rc.line.FontSize
                    : 0.75);
                if (rdK == 4) rc.builder.SetDashPattern(new double[] { 1.5, 0.75 }, 0);
                var rdY = rdK switch
                {
                    1 => rc.lineBase - Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineUnderDropEm * rc.line.FontSize,
                    2 => rc.lineBase + Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineStrikeRiseEm * rc.line.FontSize,
                    _ => rc.lineBase - Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineBorderDropEm * rc.line.FontSize,
                };
                rc.builder.MoveTo(rc.lineX, rdY).LineTo(rc.lineX + rc.w, rdY).Stroke();
                if (rdK == 4) rc.builder.SetDashPattern(System.Array.Empty<double>(), 0);
            }
            rc.builder.SetStrokeColor(0, 0, 0);
        }
    }

    /// <summary>The line's CSS and XML tops, its kerned width and right box padding; false for an empty line with no boxes, which draws nothing.</summary>
    private bool MeasureStyledLine(RowColumnState rc, int li)
    {
        rc.line = rc.cellLines![li];
        rc.cssTop = rc.slice.TopY - rc.contentTop - rc.cssCum;
        if (rc.cssCell && rc.line.BoxH > 0) rc.cssCum += rc.line.BoxH;
        rc.dwLineOff = rc.dwTextWalk;
        if (DwFormCells)
            rc.dwTextWalk += rc.line.OwnLinePt > 0
                ? rc.line.OwnLinePt : rc.slice.Plan.LineHeight;
        rc.xmlTop = rc.slice.TopY - rc.contentTop - rc.xmlCum;
        // A margin spacer's box IS its advance; it carries no
        // glyphs and so no font size of its own.
        rc.xmlCum += GeneratorCellModel && rc.line.BoxH > 0
            ? rc.line.BoxH : rc.line.FontSize + rc.line.Leading;
        // A blank line still draws its inline boxes (the
        // standalone badge circles).
        if (rc.line.Text.Length == 0 && rc.line.Boxes is null) return false;
        rc.w = rc.line.KernedWidth > 0 ? rc.line.KernedWidth
            // A line in a named Standard-14 face measures on that face's advances.
            : rc.line.BaseFont is { } measuredFace ? Text.TextPaginator.CreateMeasurer(measuredFace, rc.line.FontSize, null)(rc.line.Text)
            // A line that DECLARED its box is being laid out to an exact model,
            // so it is measured exactly: the bare AFM advance its own planner
            // wrapped it with, not the HTML dialects' ~5 % estimate, which would
            // seat a centred line half the inflation off its middle.
            : DeclaresLineBox(rc.line) ? MeasureWidthExactAfm(rc.line.Text, rc.line.FontSize)
            : MeasureWidth(rc.line.Text, rc.line.FontSize);
        // Over-declared grid dialect: a Type0 line centres on the
        // width its OWN face will draw — the Standard-14 measure
        // prices CJK at half an em and the centred title lands
        // half its width to the right.
        if (HtmlOverDeclaredDraw && rc.line.Type0Ttf is not null && rc.line.KernedWidth <= 0)
        {
            if (rc.line.StyleRuns is { Count: > 0 })
            {
                rc.w = 0;
                foreach (var (rt, rf, _) in rc.line.StyleRuns)
                    rc.w += MeasureWidthWithFont(rt, rc.line.FontSize, rf);
            }
            else rc.w = MeasureWidthWithFont(rc.line.Text, rc.line.FontSize, rc.line.Type0Ttf);
        }
        rc.boxRightPad = rc.line.Align == HorizontalAlignment.Right
            && rc.line.Boxes is { Count: > 0 } ? UaCellBoxPadPt + rc.bandInset : 0;
        return true;
    }

    /// <summary>A Type0 line draws through its CID face, glyph by glyph, with its own advances and the line's decorations; false when it drew the whole line.</summary>
    private bool DrawType0Line(RowColumnState rc)
    {
        if (rc.line.Type0Ttf is not null && rc.page is not null)
        {
            var fontDict = ResolvePageFontDict(rc.page);
            // Mixed-style form-grid line: each run in its own face,
            // advancing by its kerned width.
            if (rc.line.StyleRuns is { Count: > 0 })
            {
                var runX = rc.lineX;
                foreach (var (runText, runTtf, runName) in rc.line.StyleRuns)
                {
                    var (runRes, runHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                        fontDict, runTtf, runName, runText,
                        stripSpacesInBaseFont: true);
                    rc.builder.BeginText();
                    rc.builder.SetFont(runRes, rc.line.FontSize);
                    ApplyColor(rc.builder, rc.line.ForegroundColor);
                    rc.builder.MoveTextPosition(runX, rc.lineBase);
                    if (rc.line.KernTj && KernAdjustments(runText, runTtf) is { } runKern)
                        rc.builder.ShowTextHexKerned(runHex, runKern);
                    else
                        rc.builder.ShowTextHex(runHex);
                    rc.builder.EndText();
                    runX += MeasureWidthKerned(runText, rc.line.FontSize, runTtf);
                }
                return false;
            }
            if (rc.line.Type0SplitTokens)
            {
                // Space-separated CJK: emit each token as its own positioned
                // Type0 run so the absorber surfaces per-token fragments. Embed
                // the font ONCE for the whole line and slice the returned hex per
                // token — embedding per token would duplicate the full font
                // program for every glyph (pathological on large documents).
                var (rn, fullHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    fontDict, rc.line.Type0Ttf, rc.line.Type0FontName ?? "Arial", rc.line.Text,
                    stripSpacesInBaseFont: true);
                var tokX = rc.lineX;
                var spaceW = MeasureWidthWithFont(" ", rc.line.FontSize, rc.line.Type0Ttf);
                var charIdx = 0; // char index into line.Text (2 hex bytes per char)
                foreach (var token in rc.line.Text.Split(' '))
                {
                    if (token.Length > 0 && (charIdx + token.Length) * 2 <= fullHex.Length)
                    {
                        var tokHex = new byte[token.Length * 2];
                        System.Array.Copy(fullHex, charIdx * 2, tokHex, 0, tokHex.Length);
                        rc.builder.BeginText();
                        rc.builder.SetFont(rn, rc.line.FontSize);
                        ApplyColor(rc.builder, rc.line.ForegroundColor);
                        rc.builder.MoveTextPosition(tokX, rc.lineBase);
                        rc.builder.ShowTextHex(tokHex);
                        rc.builder.EndText();
                        tokX += MeasureWidthWithFont(token, rc.line.FontSize, rc.line.Type0Ttf);
                    }
                    charIdx += token.Length + 1; // token chars + the space separator
                    tokX += spaceW;
                }
                return false;
            }
            var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                fontDict, rc.line.Type0Ttf, rc.line.Type0FontName ?? "Arial", rc.line.Text,
                stripSpacesInBaseFont: true);
            rc.builder.BeginText();
            rc.builder.SetFont(resName, rc.line.FontSize);
            ApplyColor(rc.builder, rc.line.ForegroundColor);
            rc.builder.MoveTextPosition(rc.lineX, rc.lineBase);
            // Bold-serif HTML lines are kerned;
            // all other Type0 lines (shaped Arabic, CJK) stay unkerned.
            if (rc.line.KernTj && KernAdjustments(rc.line.Text, rc.line.Type0Ttf) is { } kernAdj)
                rc.builder.ShowTextHexKerned(hex, kernAdj);
            else
                rc.builder.ShowTextHex(hex);
            rc.builder.EndText();
            return false;
        }
        return true;
    }

    /// <summary>Styled HTML runs with a page draw each run in its own face and size along the line; false when the runs drew the whole line.</summary>
    private bool DrawHtmlRunLine(RowColumnState rc)
    {
        // A continuation slice has no page yet: its runs embed on the table's start
        // page, whose font resources the overflow pages inherit by name.
        if (rc.line.Runs is { Count: > 0 } htmlRuns && (rc.page ?? _buildPage) is { } runPage)
        {
            var runFontDict = ResolvePageFontDict(runPage);
            foreach (var run in htmlRuns)
            {
                if (run.Text.Length == 0) continue;
                var runTtf = run.Face?.Ttf ?? (run.Bold ? _serifBoldTtf : _serifTtf);
                if (runTtf is null) continue;
                var (runRes, runHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    runFontDict, runTtf, EngineRunFontName(run),
                    run.Text, stripSpacesInBaseFont: true, resNameHint: run.Face?.ResName);
                rc.builder.BeginText();
                rc.builder.SetFont(runRes, run.Size);
                ApplyColor(rc.builder, run.Color ?? rc.line.ForegroundColor);
                rc.builder.MoveTextPosition(rc.lineX + run.X, rc.lineBase);
                if (rc.line.KernTj && KernAdjustments(run.Text, runTtf) is { } runKern)
                    rc.builder.ShowTextHexKerned(runHex, runKern);
                else
                    rc.builder.ShowTextHex(runHex);
                rc.builder.EndText();
                // A text-decoration: underline span strokes under its
                // own ink, in the run's colour, at the link-underline
                // seat scaled by the run's size.
                if (run.Underline)
                {
                    var ulW = MeasureWidthKerned(run.Text.TrimEnd(' '), run.Size, runTtf);
                    if (ulW > 0)
                    {
                        var ulCol = run.Color ?? rc.line.ForegroundColor;
                        if (ulCol is { } uc)
                            rc.builder.SetStrokeColor(uc.R / 255.0, uc.G / 255.0, uc.B / 255.0);
                        else
                            rc.builder.SetStrokeColor(0, 0, 0);
                        rc.builder.SetLineWidth(LinkUnderlineWPt * run.Size / LinkProbeBasePt);
                        var ulSeat = rc.lineBase - LinkUnderlineDropPt * run.Size / LinkProbeBasePt;
                        rc.builder.MoveTo(rc.lineX + run.X, ulSeat)
                            .LineTo(rc.lineX + run.X + ulW, ulSeat).Stroke();
                        rc.builder.SetStrokeColor(0, 0, 0);
                    }
                }
            }
            // An <a> run inside the engine line annotates over its own
            // glyphs, on the same baseline the runs drew at.
            if (rc.links is not null && rc.line.LinkRuns is { Count: > 0 })
                foreach (var (xo, rw, rlink) in rc.line.LinkRuns)
                    rc.links.Add((new Rectangle(rc.lineX + xo, rc.lineBase,
                        rc.lineX + xo + rw, rc.lineTop), rlink));
            return false;
        }
        return true;
    }

    /// <summary>The plain lines' hyperlinks and footnotes are recorded at their seats for the page pass.</summary>
    private void RecordPlainCellLinksAndFootnotes(RowColumnState rc)
    {
        if (rc.links is not null)
        {
            for (var li = rc.firstLine; li < rc.lastLine; li++)
            {
                var line = rc.cellLines![li];
                if (line.Text.Length == 0) continue;
                var lineTop = rc.slice.TopY - rc.contentTop - (li - rc.firstLine) * rc.slice.Plan.LineHeight;
                var lineBottom = lineTop - line.FontSize;
                if (line.Hyperlink is not null)
                {
                    var w = MeasureWidth(line.Text, line.FontSize);
                    rc.links.Add((new Rectangle(rc.textX, lineBottom, rc.textX + w, lineTop), line.Hyperlink));
                }
                // Inline <a> runs: annotate each hyperlinked glyph run
                // at its pre-measured offset within the line.
                if (line.LinkRuns is { Count: > 0 })
                    foreach (var (xo, rw, rlink) in line.LinkRuns)
                        rc.links.Add((new Rectangle(rc.textX + xo, lineBottom,
                            rc.textX + xo + rw, lineTop), rlink));
            }
        }
        if (rc.footnoteSink is not null)
        {
            for (var li = rc.firstLine; li < rc.lastLine; li++)
            {
                var line = rc.cellLines![li];
                if (line.FootNote is null || line.Text.Length == 0) continue;
                var lb = rc.slice.TopY - rc.contentTop
                         - (li - rc.firstLine) * rc.slice.Plan.LineHeight
                         - line.FontSize + rc.engineLift;
                rc.footnoteSink.Add((line.FootNote,
                    rc.textX + MeasureWidthExact(line.Text, line.FontSize),
                    lb, line.FontSize));
            }
        }
    }

    /// <summary>The plain lines draw as one text object: a Tm seat, then one T* pitched line per row in its own leading and font.</summary>
    private void DrawPlainCellInk(RowColumnState rc)
    {
        string FontFor(CellLine l) => l.BaseFont is { } face && rc.page is not null
            ? RegisterFont(rc.page, face)
            : l.Bold && rc.page is not null ? RegisterFont(rc.page, "Helvetica-Bold") : rc.fontName;
        // The lifted dialect keeps deliberate blank line boxes as
        // vertical space; they must not EMIT — an empty Tj reads
        // back as an empty text fragment and shifts every absorber
        // index after it. Legacy keeps its byte-exact stream
        // (a leading empty run round-trips through it).
        var fi = rc.firstLine;
        var leadDy = 0.0;
        if (NestedTableRender)
            while (fi < rc.lastLine && rc.cellLines![fi].Text.Length == 0)
            {
                // A reserve line's box is its own FontSize (a share
                // of the nested grid's real height), not the pitch.
                leadDy += rc.cellLines[fi].ImgReserve && rc.cellLines[fi].FontSize > 0
                    ? rc.cellLines[fi].FontSize
                    // (a UA-boxed cell's blank line is its own box, not the row's pitch)
                    : UaCellBoxes && rc.cellLines[fi].BoxH > 0 ? rc.cellLines[fi].BoxH
                    : rc.slice.Plan.LineHeight;
                fi++;
            }
        var firstInk = fi < rc.lastLine ? rc.cellLines![fi] : rc.first;
        // A reserved block standing on the first line is marked ahead of the text.
        if (firstInk.ImgReserve && ReservedBlockMarkerAt(rc, fi) is { } firstMarker) rc.builder.Raw("% " + firstMarker);
        rc.builder.BeginText();
        rc.builder.SetFont(FontFor(firstInk), firstInk.FontSize);
        ApplyColor(rc.builder, firstInk.ForegroundColor);
        var penX = rc.textX + firstInk.LeftIndent;
        var penY = rc.textY - leadDy;
        rc.builder.MoveTextPosition(penX, penY);
        rc.builder.ShowText(firstInk.Text);

        var lastFontSize = firstInk.FontSize;
        var lastBold = firstInk.Bold;
        var lastBaseFont = firstInk.BaseFont;
        var lastIndent = firstInk.LeftIndent;
        var skippedDy = 0.0;
        for (var li = fi + 1; li < rc.lastLine; li++)
        {
            var line = rc.cellLines![li];
            if (NestedTableRender && line.Text.Length == 0)
            {
                skippedDy += line.ImgReserve && line.FontSize > 0
                    ? line.FontSize
                    : UaCellBoxes && line.BoxH > 0 ? line.BoxH
                    : rc.slice.Plan.LineHeight;
                continue;
            }
            if (line.FontSize != lastFontSize || line.Bold != lastBold || line.BaseFont != lastBaseFont)
            {
                rc.builder.SetFont(FontFor(line), line.FontSize);
                lastFontSize = line.FontSize;
                lastBold = line.Bold;
                lastBaseFont = line.BaseFont;
            }
            ApplyColor(rc.builder, line.ForegroundColor);
            if (line.ImgReserve && ReservedBlockMarkerAt(rc, li) is { } marker)
            {
                // A reserved block stands on this line: the text object closes, the block's place is
                // marked between the lines, and the text goes on below it from where the pen was
                // (the font and colour stay in the graphics state; only the text matrix starts over).
                rc.builder.EndText();
                rc.builder.Raw("% " + marker);
                rc.builder.BeginText();
                rc.builder.MoveTextPosition(penX, penY);
            }
            var dx = line.LeftIndent - lastIndent;
            var dy = -DeclaredLinePitch(line, rc.slice.Plan.LineHeight) - skippedDy;
            rc.builder.MoveTextPosition(dx, dy);
            penX += dx;
            penY += dy;
            skippedDy = 0;
            lastIndent = line.LeftIndent;
            rc.builder.ShowText(line.Text);
        }
        rc.builder.EndText();
    }

    /// <summary>The marker of the reserved block booked on line <paramref name="li"/> of the
    /// slice's column, or null when the line holds none.</summary>
    private static string? ReservedBlockMarkerAt(RowColumnState rc, int li)
    {
        if (rc.slice.Plan.CellImages is not { } images || !images.TryGetValue(rc.col, out var columnImages)) return null;
        foreach (var image in columnImages)
            if (image.Block is not null && image.LineOffset == li) return image.Part?.Marker;
        return null;
    }

    /// <summary>The height of the lines this slice draws before
    /// <paramref name="li"/>, each at its own declared box.</summary>
    private static double DeclaredLinesHeight(RowColumnState rc, int li)
    {
        double height = 0;
        for (var i = rc.firstLine; i < li && i < rc.cellLines!.Count; i++)
            height += DeclaredLinePitch(rc.cellLines[i], rc.slice.Plan.LineHeight);
        return height;
    }

    /// <summary>Whether the slice's first line is an HTML-engine line (a split engine cell's
    /// continuation keeps the engine seat and faces).</summary>
    private bool SliceOpensOnEngineLine(RowColumnState rc)
    {
        var lines = rc.slice.Plan.CellLines is { } all && rc.col < all.Count ? all[rc.col] : null;
        return lines is not null && rc.slice.LineStart < lines.Count && lines[rc.slice.LineStart].HtmlEngine;
    }
}
