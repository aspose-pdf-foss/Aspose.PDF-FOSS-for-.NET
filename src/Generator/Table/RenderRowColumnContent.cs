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
    /// <summary>The content stage of a column render: the cell lines, images and controls, verbatim.</summary>
    private void RenderRowColumnContent(RowColumnState rc, int col, ref double cellX, ContentStreamBuilder builder, RowSlice slice,
        double[] colWidths, string fontName, int[] cellMap,
        List<(Rectangle rect, Hyperlink link)>? links, List<(byte[] data, Rectangle rect)>? imageSink,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink, List<byte[]>? graphSink,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink, Page? page,
        List<(Note note, double x, double baseline, double size)>? footnoteSink)
    {
        rc.cellIsInline = slice.Plan.CellInline?.ContainsKey(col) ?? false;
        rc.cellLines = col < slice.Plan.CellLines.Count ? slice.Plan.CellLines[col] : null;
        rc.generatorCell = GeneratorCellModel;
        (rc.cellDescentEm, rc.cellFace, rc.cellFragmentFace) = rc.generatorCell ? CellFontDescentEm(rc.cell, rc.row) : (HelveticaDescentEm, (string?)null, true);
        rc.clipMark = builder.Mark;
        builder.ResetTextExtent();
        rc.clipPadL = rc.padding?.Left ?? 0;
        rc.clipPadR = rc.padding?.Right ?? 0;
        // A cell that holds only a nested grid's reserve draws no text of its own
        // (the grid draws in the nested pass below) — no empty marker run.
        if (rc.generatorCell && rc.cellLines is not null
            && slice.Plan.CellTables is { } gridCells && gridCells.ContainsKey(col)
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
            && slice.Plan.CellImages is { Count: > 0 } && !slice.Plan.CellImages.ContainsKey(col)
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
        if ((HtmlEngineMetrics || UaCellBoxes)
            && rc.effVA is VerticalAlignment.Top or VerticalAlignment.None)
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
        rc.cssCell = slice.LineStart == 0 && slice.Plan.CssCells?.Contains(col) == true;
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
            rc.borderInsetBottom = OccupiedSideWidth(rc.insetBorder, BorderSide.Bottom,
                rc.insetBorder.BottomAssigned, rc.insetBorder.RawBottom);
        }
        rc.vaOffset = 0.0;
        if (!rc.cssCell && (rc.effVA is VerticalAlignment.Center or VerticalAlignment.Bottom) && rc.cellLines is { Count: > 0 } && slice.LineCount > 0)
        {
            var visLines = Math.Max(0, Math.Min(slice.LineCount, rc.cellLines.Count - slice.LineStart));
            // A FixedRowHeight row centres on the cell's FULL content height —
            // an overflowing cell clamps to the top pad (its excess lines clip
            // at the row bottom) while a fitting neighbour centres normally.
            // Other rows centre the lines the slice actually shows.
            var countForVa = slice.Plan.Row.FixedRowHeight > 0
                ? Math.Max(0, rc.cellLines.Count - slice.LineStart)
                : visLines;
            // The content block stacks at each line's OWN height (pitch =
            // font size; a checkbox line is its box height).
            double blockH = 0;
            for (var bi = slice.LineStart; bi < slice.LineStart + countForVa && bi < rc.cellLines.Count; bi++)
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
                    : UaCellBoxes || NestedTableRender ? slice.Plan.LineHeight
                    // A declared leading is part of the line's pitch, so it is part
                    // of the block being centred — pricing the line at the bare
                    // font size would leave a full cell looking short and sink its
                    // text by half the leading it actually occupies.
                    : rc.cellLines[bi].FontSize + rc.cellLines[bi].Leading;
            // Generator dialect: the block centres in the INNER box — the bottom
            // border is outside it too (probed: a column-slice row 12 pt tall
            // with 1 pt rules seats its 10 pt line at 761.07, not 760.57).
            var avail = slice.Height - rc.padTop - rc.padBot - rc.borderInsetTop
                - (rc.generatorCell ? rc.borderInsetBottom : 0);
            if (avail > blockH)
                rc.vaOffset = rc.effVA == VerticalAlignment.Center ? (avail - blockH) / 2 : (avail - blockH);
        }
        // A bordered cell's content starts at the border's inner edge with no
        // implicit horizontal padding ("x" seats 5 pt in from a
        // 5 pt border, not 5+2); explicit padding still applies.
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
        // pt-styled fragment seat: every grid cell's text bottom rides a
        // CONSTANT 1.75 pt deeper than the legacy seat (measured on the
        // render: 1.8 at 10 pt rows, 1.7 at the 8 pt card — a fixed
        // offset, not an em share, and independent of the cell's borders).
        const double PtGridSeatDropPt = 1.75;

        if (!rc.cellIsInline && rc.cellLines is { Count: > 0 } && slice.LineCount > 0)
        {
            var firstLine = slice.LineStart;
            var lastLine = Math.Min(firstLine + slice.LineCount, rc.cellLines.Count);
            // A cell whose whole content is SHORTER than the row's line window has
            // nothing at this offset, and a split row would leave it blank on every
            // page after the first. It is re-drawn instead: a row broken
            // across pages carries its "1." and "Test Date" cells at the top of each
            // continuation slice, beside the long cell that is still running.
            if (rc.generatorCell && firstLine >= rc.cellLines.Count)
            {
                firstLine = 0;
                lastLine = Math.Min(slice.LineCount, rc.cellLines.Count);
            }
            if (firstLine < lastLine)
            {
                var hasOption = false;
                var anyNonLeft = false;
                var anyType0 = false;
                var anyBoxes = false;
                var anyLinks = false;
                var xmlMixedSizes = false;
                for (var li = firstLine; li < lastLine; li++)
                {
                    var cl = rc.cellLines[li];
                    if (cl.Option is not null || cl.Checkbox is not null
                        || cl.InlineOptions is not null
                        || cl.Text.IndexOf(InlineButtonChar) >= 0
                        || cl.InputBoxes is not null
                        || cl.Text.IndexOf(InlineCheckChar) >= 0
                        || cl.Text.IndexOf(InlineCheckboxGapChar) >= 0) { hasOption = true; break; }
                    if (cl.Align is HorizontalAlignment.Center or HorizontalAlignment.Right) anyNonLeft = true;
                    if (cl.Type0Ttf is not null) anyType0 = true;
                    // A line whose pieces sit at their OWN x offsets (an HTML-engine
                    // line: a list marker in its column, the item's text at the list
                    // indent) cannot be expressed by the uniform text object, which
                    // walks one pen from the cell's left edge.
                    if (cl.Runs is { Count: > 0 }) anyType0 = true;
                    if (cl.Boxes is { Count: > 0 }) anyBoxes = true;
                    // Linked lines draw per-line so the anchor runs can take the
                    // link blue + underline styling (lifted dialect only —
                    // the legacy stream stays byte-stable).
                    if (NestedTableRender
                        && (cl.LinkRuns is { Count: > 0 } || cl.Hyperlink is not null)) anyLinks = true;
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
                            || Math.Abs(cl.FontSize - rc.cellLines[firstLine].FontSize) > 0.01))
                        xmlMixedSizes = true;
                }

                if (hasOption)
                {
                    // Form-control lines need path drawing (the glyph) interleaved with
                    // text, which a single text object can't hold — render line by line.
                    RenderControlLines(builder, rc.cellLines, firstLine, lastLine,
                        cellX + rc.padLeft, slice.TopY - rc.contentTop, slice.Plan.LineHeight, fontName, optionSink, checkboxSink,
                        slice.TopY - slice.Height + (rc.padding?.Bottom ?? 0), page);
                }
                else if (anyNonLeft || anyType0 || rc.cssCell || anyBoxes || anyLinks || xmlMixedSizes)
                {
                    // The cell mixes alignments (e.g. a centred title above a left HtmlFragment)
                    // or carries an embedded-font (Arabic/Type0) line, an inline-box
                    // decoration, so each line is positioned
                    // absolutely. Lines can differ in width, hence per-line placement.
                    var padRight = rc.padding?.Right ?? rc.dp;
                    var cssCum = 0.0;
                    // vertical-align: middle for a css-box stack — the whole stack
                    // shifts down by half the cell's slack (the uniform path does
                    // this through vaOffset; the css stack owns its own cursor).
                    if (rc.cssCell && rc.effVA == VerticalAlignment.Center)
                    {
                        double cssTotal = 0;
                        var anyPlate = false;
                        for (var li2 = firstLine; li2 < lastLine; li2++)
                        {
                            var cl2 = rc.cellLines[li2];
                            cssTotal += cl2.BoxH > 0 ? cl2.BoxH : slice.Plan.LineHeight;
                            if (cl2.Boxes is { } bxs)
                                foreach (var b7 in bxs)
                                    if (b7.Height > 0) anyPlate = true;
                        }
                        // Form-grid cells centre within their BORDER-inset content
                        // band — the cell border pair is not distributable slack.
                        var availV = slice.Height - rc.padTop - rc.padBot
                            - (FormGridCells ? 2 * rc.borderInsetTop : 0);
                        // cssTop subtracts the cursor, so a POSITIVE seed moves
                        // the stack down into the slack. A stack holding a
                        // declared-height plate is near-exact — split only its
                        // small residual tail (clamped), the outer band does the
                        // real centring.
                        if (availV > cssTotal && !anyPlate)
                            cssCum = (availV - cssTotal) / 2;
                    }
                    // DataWorks text cells walk each line by its OWN 1.125-em
                    // box (the label columns pitch 13.5 while a sibling option
                    // column steps 14.76 — the widget pitch never propagates
                    // into the labels).
                    var dwTextWalk = 0.0;
                    var xmlCum = 0.0;
                    for (var li = firstLine; li < lastLine; li++)
                    {
                        var line = rc.cellLines[li];
                        // A css-box line advances the stack by its OWN box height —
                        // including deliberate blank lines, which occupy space silently.
                        var cssTop = slice.TopY - rc.contentTop - cssCum;
                        if (rc.cssCell && line.BoxH > 0) cssCum += line.BoxH;
                        // (advance the dw walk for every line, blanks included)
                        var dwLineOff = dwTextWalk;
                        if (DwFormCells)
                            dwTextWalk += line.OwnLinePt > 0
                                ? line.OwnLinePt : slice.Plan.LineHeight;
                        // XML own-size pitch: consume the line's height whether
                        // or not it draws (a blank spacer line still advances).
                        var xmlTop = slice.TopY - rc.contentTop - xmlCum;
                        // A margin spacer's box IS its advance; it carries no
                        // glyphs and so no font size of its own.
                        xmlCum += GeneratorCellModel && line.BoxH > 0
                            ? line.BoxH : line.FontSize + line.Leading;
                        // A blank line still draws its inline boxes (the
                        // standalone badge circles).
                        if (line.Text.Length == 0 && line.Boxes is null) continue;
                        var w = line.KernedWidth > 0 ? line.KernedWidth
                            : MeasureWidth(line.Text, line.FontSize);
                        // Over-declared grid dialect: a Type0 line centres on the
                        // width its OWN face will draw — the Standard-14 measure
                        // prices CJK at half an em and the centred title lands
                        // half its width to the right.
                        if (HtmlOverDeclaredDraw && line.Type0Ttf is not null && line.KernedWidth <= 0)
                        {
                            if (line.StyleRuns is { Count: > 0 })
                            {
                                w = 0;
                                foreach (var (rt, rf, _) in line.StyleRuns)
                                    w += MeasureWidthWithFont(rt, line.FontSize, rf);
                            }
                            else w = MeasureWidthWithFont(line.Text, line.FontSize, line.Type0Ttf);
                        }
                        // A right-anchored BOX line (the overall-signal pill) keeps the
                        // UA cell pad off the border box, the same white the full-width
                        // bars below reserve — the pill must never sit flush against
                        // the frame.
                        var boxRightPad = line.Align == HorizontalAlignment.Right
                            && line.Boxes is { Count: > 0 } ? UaCellBoxPadPt + rc.bandInset : 0;
                        // A line carrying its paragraph's own margins centers on
                        // the margin-inset content box, not the full padded cell.
                        var lineX = line.Align == HorizontalAlignment.Center
                            ? (line.LeftIndent + line.RightInsetPt > 0
                                ? cellX + rc.padLeft + line.LeftIndent + Math.Max(0,
                                    (rc.cellWidth - rc.padLeft - padRight - line.LeftIndent
                                     - line.RightInsetPt - w) / 2)
                                : cellX + Math.Max(rc.padLeft, (rc.cellWidth - w) / 2))
                            : line.Align == HorizontalAlignment.Right
                                ? Math.Max(cellX + rc.padLeft,
                                    cellX + rc.cellWidth - padRight - w - boxRightPad - line.RightInsetPt)
                                : cellX + rc.padLeft + line.LeftIndent;
                        var lineTop = rc.cssCell && line.BoxH > 0
                            ? cssTop
                            : DwFormCells
                            ? slice.TopY - rc.contentTop - dwLineOff
                            : XmlGeneratorModel && xmlMixedSizes
                                // XML-generator: cumulative own-size pitch
                                // (14/10/14 stacks at 14, then 10, …).
                                ? xmlTop
                                : slice.TopY - rc.contentTop - (li - firstLine) * slice.Plan.LineHeight;
                        // Baseline: ascent + half-leading below the box top for css-box
                        // lines; the legacy full-em drop otherwise, lifted a descender
                        // for explicitly-bordered cells (see borderLiftFactor).
                        var collapsedSeat = HtmlWrapInsetsCellMargins ? PtGridSeatDropPt
                            // redline cells: AscLead seat (0.929 em) vs the
                            // legacy 0.793 em drop, plus the constant 1.2 pt
                            // the expected rows sit below the plain seat at every size
                            : RedlineCellSeat ? 0.136 * line.FontSize + 1.2
                            // DataWorks h1 title: seats 9.8 below the top-aligned
                            // legacy drop (measured).
                            : DwFormCells
                                && line.FontSize >= Converters.HtmlToPdfConverter.DwH1FontPt - 0.1
                            ? Converters.HtmlToPdfConverter.DwH1SeatDropPt : 0;
                        var lineBase = lineTop - (rc.cssCell && line.BaseOff > 0
                            ? line.BaseOff
                            : line.FontSize + collapsedSeat
                              - (SuppressBaselineLift ? 0 : rc.borderLiftFactor * line.FontSize))
                            // The declared leading sits ABOVE the glyphs, pushing
                            // the baseline that much deeper into the line box.
                            - line.Leading;
                        // <u> underline: one bar under the drawn run, in the
                        // text colour, at the link-underline seat. Dialect-gated:
                        // legacy corpora were calibrated without <u> ink.
                        if ((HtmlOverDeclaredDraw || XmlGeneratorModel) && line.Underline && w > 0)
                        {
                            if (line.ForegroundColor is { } ulc)
                                builder.SetStrokeColor(ulc.R / 255.0, ulc.G / 255.0, ulc.B / 255.0);
                            else
                                builder.SetStrokeColor(0, 0, 0);
                            builder.SetLineWidth(LinkUnderlineWPt * line.FontSize / LinkProbeBasePt);
                            var ulY = lineBase - LinkUnderlineDropPt * line.FontSize / LinkProbeBasePt;
                            builder.MoveTo(lineX, ulY).LineTo(lineX + w, ulY).Stroke();
                        }
                        // Redline decorations: stroke the line's strike /
                        // underline / marker-border ink at the converter's
                        // probed offsets (see the Redline* constants).
                        if (line.Decors is { Count: > 0 } rdDecs && w > 0)
                        {
                            foreach (var (rdK, rdC) in rdDecs)
                            {
                                var rdCol = rdK <= 2
                                    ? line.ForegroundColor ?? Color.FromArgb(0, 0, 0)
                                    : rdC ?? Color.FromArgb(0, 0, 0);
                                builder.SetStrokeColor(rdCol.R / 255.0, rdCol.G / 255.0, rdCol.B / 255.0);
                                builder.SetLineWidth(rdK <= 2
                                    ? Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineDecorWidthEm * line.FontSize
                                    : 0.75);
                                if (rdK == 4) builder.SetDashPattern(new double[] { 1.5, 0.75 }, 0);
                                var rdY = rdK switch
                                {
                                    1 => lineBase - Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineUnderDropEm * line.FontSize,
                                    2 => lineBase + Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineStrikeRiseEm * line.FontSize,
                                    _ => lineBase - Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineBorderDropEm * line.FontSize,
                                };
                                builder.MoveTo(lineX, rdY).LineTo(lineX + w, rdY).Stroke();
                                if (rdK == 4) builder.SetDashPattern(System.Array.Empty<double>(), 0);
                            }
                            builder.SetStrokeColor(0, 0, 0);
                        }
                        // Inline boxes behind this line (title plates, status pills):
                        // rounded fill, the trailing traffic-light circle with its
                        // letter, and each box's OWN positioned text run. When the
                        // boxes carry text they own the whole line's ink.
                        if (line.Boxes is { Count: > 0 } lineBoxes)
                        {
                            var lineBoxH = line.BoxH > 0 ? line.BoxH
                                : Math.Max(slice.Plan.LineHeight, line.FontSize * 1.2);
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
                                var bx = lineX + ib.XOff + packShift;
                                var ibW = ib.Width;
                                // A block-level bar spans the cell's BORDER BOX
                                // minus the UA pad — a sibling div's padding must
                                // not inset it (the bars run border to
                                // border with ~1 pt of white).
                                if (ib.FullWidth)
                                {
                                    bx = cellX + rc.bandInset + 0.75;
                                    ibW = Math.Max(ib.Width, rc.cellWidth - 2 * rc.bandInset - 1.5);
                                }
                                var drawH = ib.Height > 0 ? ib.Height : lineBoxH - 2 * ib.InsetV;
                                // A declared-height plate seats a full inset pair
                                // lower — its stack keeps 2·InsetV of breathing and
                                // the rect centres in it. A FULL-WIDTH bar opening
                                // the cell anchors at the BORDER BOX top (+UA pad):
                                // a padded sibling div must not displace it.
                                var ibTopOff = ib.Height > 0 ? 2 * ib.InsetV : ib.InsetV;
                                var drawTop = ib.FullWidth && li == firstLine
                                    ? slice.TopY - rc.bandInset - 0.75
                                    : lineTop - ibTopOff;
                                if (ib.Fill is { } boxFill)
                                {
                                    builder.SetFillColor(boxFill.R / 255.0, boxFill.G / 255.0, boxFill.B / 255.0);
                                    FillRoundedRect(builder, bx, drawTop - drawH, ibW, drawH, ib.Radius);
                                }
                                if (ib.CircleFill is { } circleFill)
                                {
                                    var dD = ib.CircleD > 0 ? ib.CircleD : 14.25;
                                    var ccx = bx + ibW - ib.PadRight - dD / 2;
                                    var ccy = drawTop - drawH / 2;
                                    builder.SetFillColor(circleFill.R / 255.0, circleFill.G / 255.0, circleFill.B / 255.0);
                                    FillCircle(builder, ccx, ccy, dD / 2);
                                    if (ib.CircleLetter is { Length: > 0 } circleLetter && page is not null)
                                    {
                                        var lw = MeasureWidth(circleLetter, 9);
                                        builder.BeginText();
                                        builder.SetFont(RegisterFont(page, "Helvetica-Bold"), 9);
                                        ApplyColor(builder, ib.CircleLetterColor ?? Color.White);
                                        builder.MoveTextPosition(ccx - lw / 2, ccy - 3.1);
                                        builder.ShowText(circleLetter);
                                        builder.EndText();
                                    }
                                }
                                if (ib.Text is { Length: > 0 } boxText)
                                {
                                    boxesCarryText = true;
                                    var btSize = ib.TextSize > 0 ? ib.TextSize : line.FontSize;
                                    var btX = ib.TextCentered
                                        ? bx + Math.Max(0, (ibW - MeasureWidth(boxText, btSize)) / 2)
                                        : lineX + ib.TextX + packShift + ibStretch / 2;
                                    builder.BeginText();
                                    builder.SetFont(ib.TextBold && page is not null
                                        ? RegisterFont(page, "Helvetica-Bold") : fontName, btSize);
                                    if (ib.TextLetterSpacing > 0)
                                        builder.SetCharSpacing(ib.TextLetterSpacing);
                                    ApplyColor(builder, ib.TextColor ?? line.ForegroundColor);
                                    builder.MoveTextPosition(btX,
                                        drawTop - ib.PadTop - btSize);
                                    builder.ShowText(boxText);
                                    if (ib.TextLetterSpacing > 0)
                                        builder.SetCharSpacing(0);
                                    builder.EndText();
                                }
                            }
                            if (boxesCarryText) continue;
                        }
                        // A spill slice has no Page of its own to embed a face into
                        // (its page does not exist yet, and only a caller that shares
                        // one /Font dict across pages can reach back to the first).
                        // Its runs still keep their PLACES: the marker column and the
                        // item indent survive, drawn in the table's Standard-14 face.
                        if (line.Runs is { Count: > 0 } fallbackRuns && page is null)
                        {
                            foreach (var run in fallbackRuns)
                            {
                                if (run.Text.Length == 0) continue;
                                builder.BeginText();
                                builder.SetFont(fontName, run.Size);
                                ApplyColor(builder, line.ForegroundColor);
                                builder.MoveTextPosition(lineX + run.X, lineBase);
                                builder.ShowText(run.Text);
                                builder.EndText();
                            }
                            continue;
                        }
                        // HTML-engine line: styled serif runs at per-run x-offsets on
                        // the common baseline (mixed bold/small within one line).
                        if (line.Runs is { Count: > 0 } htmlRuns && page is not null)
                        {
                            var runFontDict = ResolvePageFontDict(page);
                            foreach (var run in htmlRuns)
                            {
                                if (run.Text.Length == 0) continue;
                                var runTtf = run.Bold ? _serifBoldTtf : _serifTtf;
                                if (runTtf is null) continue;
                                var (runRes, runHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                    runFontDict, runTtf,
                                    run.Bold ? "Times New Roman Bold" : "Times New Roman",
                                    run.Text, stripSpacesInBaseFont: true);
                                builder.BeginText();
                                builder.SetFont(runRes, run.Size);
                                ApplyColor(builder, run.Color ?? line.ForegroundColor);
                                builder.MoveTextPosition(lineX + run.X, lineBase);
                                if (line.KernTj && KernAdjustments(run.Text, runTtf) is { } runKern)
                                    builder.ShowTextHexKerned(runHex, runKern);
                                else
                                    builder.ShowTextHex(runHex);
                                builder.EndText();
                                // A text-decoration: underline span strokes under its
                                // own ink, in the run's colour, at the link-underline
                                // seat scaled by the run's size.
                                if (run.Underline)
                                {
                                    var ulW = MeasureWidthKerned(run.Text.TrimEnd(' '), run.Size, runTtf);
                                    if (ulW > 0)
                                    {
                                        var ulCol = run.Color ?? line.ForegroundColor;
                                        if (ulCol is { } uc)
                                            builder.SetStrokeColor(uc.R / 255.0, uc.G / 255.0, uc.B / 255.0);
                                        else
                                            builder.SetStrokeColor(0, 0, 0);
                                        builder.SetLineWidth(LinkUnderlineWPt * run.Size / LinkProbeBasePt);
                                        var ulSeat = lineBase - LinkUnderlineDropPt * run.Size / LinkProbeBasePt;
                                        builder.MoveTo(lineX + run.X, ulSeat)
                                            .LineTo(lineX + run.X + ulW, ulSeat).Stroke();
                                        builder.SetStrokeColor(0, 0, 0);
                                    }
                                }
                            }
                            // An <a> run inside the engine line annotates over its own
                            // glyphs, on the same baseline the runs drew at.
                            if (links is not null && line.LinkRuns is { Count: > 0 })
                                foreach (var (xo, rw, rlink) in line.LinkRuns)
                                    links.Add((new Rectangle(lineX + xo, lineBase,
                                        lineX + xo + rw, lineTop), rlink));
                            continue;
                        }
                        // Embedded-font line (Arabic/Unicode): embed the TrueType as a Type0/CID
                        // font on this page and draw the shaped glyphs as hex glyph IDs.
                        if (line.Type0Ttf is not null && page is not null)
                        {
                            var fontDict = ResolvePageFontDict(page);
                            // Mixed-style form-grid line: each run in its own face,
                            // advancing by its kerned width.
                            if (line.StyleRuns is { Count: > 0 })
                            {
                                var runX = lineX;
                                foreach (var (runText, runTtf, runName) in line.StyleRuns)
                                {
                                    var (runRes, runHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                        fontDict, runTtf, runName, runText,
                                        stripSpacesInBaseFont: true);
                                    builder.BeginText();
                                    builder.SetFont(runRes, line.FontSize);
                                    ApplyColor(builder, line.ForegroundColor);
                                    builder.MoveTextPosition(runX, lineBase);
                                    if (line.KernTj && KernAdjustments(runText, runTtf) is { } runKern)
                                        builder.ShowTextHexKerned(runHex, runKern);
                                    else
                                        builder.ShowTextHex(runHex);
                                    builder.EndText();
                                    runX += MeasureWidthKerned(runText, line.FontSize, runTtf);
                                }
                                continue;
                            }
                            if (line.Type0SplitTokens)
                            {
                                // Space-separated CJK: emit each token as its own positioned
                                // Type0 run so the absorber surfaces per-token fragments. Embed
                                // the font ONCE for the whole line and slice the returned hex per
                                // token — embedding per token would duplicate the full font
                                // program for every glyph (pathological on large documents).
                                var (rn, fullHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                    fontDict, line.Type0Ttf, line.Type0FontName ?? "Arial", line.Text,
                                    stripSpacesInBaseFont: true);
                                var tokX = lineX;
                                var spaceW = MeasureWidthWithFont(" ", line.FontSize, line.Type0Ttf);
                                var charIdx = 0; // char index into line.Text (2 hex bytes per char)
                                foreach (var token in line.Text.Split(' '))
                                {
                                    if (token.Length > 0 && (charIdx + token.Length) * 2 <= fullHex.Length)
                                    {
                                        var tokHex = new byte[token.Length * 2];
                                        System.Array.Copy(fullHex, charIdx * 2, tokHex, 0, tokHex.Length);
                                        builder.BeginText();
                                        builder.SetFont(rn, line.FontSize);
                                        ApplyColor(builder, line.ForegroundColor);
                                        builder.MoveTextPosition(tokX, lineBase);
                                        builder.ShowTextHex(tokHex);
                                        builder.EndText();
                                        tokX += MeasureWidthWithFont(token, line.FontSize, line.Type0Ttf);
                                    }
                                    charIdx += token.Length + 1; // token chars + the space separator
                                    tokX += spaceW;
                                }
                                continue;
                            }
                            var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                fontDict, line.Type0Ttf, line.Type0FontName ?? "Arial", line.Text,
                                stripSpacesInBaseFont: true);
                            builder.BeginText();
                            builder.SetFont(resName, line.FontSize);
                            ApplyColor(builder, line.ForegroundColor);
                            builder.MoveTextPosition(lineX, lineBase);
                            // Bold-serif HTML lines are kerned;
                            // all other Type0 lines (shaped Arabic, CJK) stay unkerned.
                            if (line.KernTj && KernAdjustments(line.Text, line.Type0Ttf) is { } kernAdj)
                                builder.ShowTextHexKerned(hex, kernAdj);
                            else
                                builder.ShowTextHex(hex);
                            builder.EndText();
                            continue;
                        }
                        var plResolvedFont = line.Bold && page is not null
                            ? RegisterFont(page, "Helvetica-Bold") : fontName;
                        if (NestedTableRender
                            && (line.LinkRuns is { Count: > 0 } || line.Hyperlink is not null))
                            ShowLineWithLinks(builder, line, plResolvedFont, lineX, lineBase);
                        else
                        {
                            builder.BeginText();
                            builder.SetFont(plResolvedFont, line.FontSize);
                            ApplyColor(builder, line.ForegroundColor);
                            builder.MoveTextPosition(lineX, lineBase);
                            builder.ShowText(line.Text);
                            builder.EndText();
                        }
                        if (links is not null && line.Hyperlink is not null)
                            links.Add((new Rectangle(lineX, lineBase, lineX + w, lineTop), line.Hyperlink));
                        // Inline <a> runs: annotate each hyperlinked glyph run
                        // at its pre-measured offset within the line.
                        if (links is not null && line.LinkRuns is { Count: > 0 })
                            foreach (var (xo, rw, rlink) in line.LinkRuns)
                                links.Add((new Rectangle(lineX + xo, lineBase,
                                    lineX + xo + rw, lineTop), rlink));
                        if (footnoteSink is not null && line.FootNote is not null)
                            footnoteSink.Add((line.FootNote,
                                lineX + MeasureWidthExact(line.Text, line.FontSize),
                                lineBase, line.FontSize));
                    }
                }
                else
                {
                    var first = rc.cellLines[firstLine];
                    var textX = cellX + rc.padLeft;
                    // HTML-engine metrics: the baseline sits a descender ABOVE the
                    // block bottom (box = n·size), i.e. lifted by 0.207·size vs the
                    // legacy full-em drop (0.207 = the Helvetica AFM descender).
                    // Explicitly-bordered cells get the same lift.
                    var engineLift = SuppressBaselineLift
                        ? 0
                        : (HtmlEngineMetrics ? 0.207 : rc.borderLiftFactor) * first.FontSize;
                    // A caller-declared leading lies ABOVE the glyphs, so it moves
                    // only the FIRST baseline down: every later line already rides
                    // the row's uniform LineHeight, which the leading grew.
                    var textY = slice.TopY - rc.contentTop - first.FontSize + engineLift
                                - first.Leading;

                    string FontFor(CellLine l) => l.Bold && page is not null
                        ? RegisterFont(page, "Helvetica-Bold") : fontName;
                    // A truly empty <td> collapses in a browser and shows NO text —
                    // unlike an &nbsp; cell, whose text is U+00A0 and survives this
                    // test. Emitting the empty run anyway reads back as a spurious
                    // text fragment, shifting every fragment
                    // index after it. Lifted render only; the legacy dialects round
                    // trip their leading empty segment through it.
                    var cellHasInk = !NestedTableRender;
                    for (var li = firstLine; li < lastLine && !cellHasInk; li++)
                        if (rc.cellLines[li].Text.Length > 0) cellHasInk = true;
                    if (cellHasInk)
                    {
                        // The lifted dialect keeps deliberate blank line boxes as
                        // vertical space; they must not EMIT — an empty Tj reads
                        // back as an empty text fragment and shifts every absorber
                        // index after it. Legacy keeps its byte-exact stream
                        // (a leading empty run round-trips through it).
                        var fi = firstLine;
                        var leadDy = 0.0;
                        if (NestedTableRender)
                            while (fi < lastLine && rc.cellLines[fi].Text.Length == 0)
                            {
                                // A reserve line's box is its own FontSize (a share
                                // of the nested grid's real height), not the pitch.
                                leadDy += rc.cellLines[fi].ImgReserve && rc.cellLines[fi].FontSize > 0
                                    ? rc.cellLines[fi].FontSize : slice.Plan.LineHeight;
                                fi++;
                            }
                        var firstInk = fi < lastLine ? rc.cellLines[fi] : first;
                        builder.BeginText();
                        builder.SetFont(FontFor(firstInk), firstInk.FontSize);
                        ApplyColor(builder, firstInk.ForegroundColor);
                        builder.MoveTextPosition(textX + firstInk.LeftIndent, textY - leadDy);
                        builder.ShowText(firstInk.Text);

                        var lastFontSize = firstInk.FontSize;
                        var lastBold = firstInk.Bold;
                        var lastIndent = firstInk.LeftIndent;
                        var skippedDy = 0.0;
                        for (var li = fi + 1; li < lastLine; li++)
                        {
                            var line = rc.cellLines[li];
                            if (NestedTableRender && line.Text.Length == 0)
                            {
                                skippedDy += line.ImgReserve && line.FontSize > 0
                                    ? line.FontSize : slice.Plan.LineHeight;
                                continue;
                            }
                            if (line.FontSize != lastFontSize || line.Bold != lastBold)
                            {
                                builder.SetFont(FontFor(line), line.FontSize);
                                lastFontSize = line.FontSize;
                                lastBold = line.Bold;
                            }
                            ApplyColor(builder, line.ForegroundColor);
                            builder.MoveTextPosition(line.LeftIndent - lastIndent,
                                -slice.Plan.LineHeight - skippedDy);
                            skippedDy = 0;
                            lastIndent = line.LeftIndent;
                            builder.ShowText(line.Text);
                        }
                        builder.EndText();
                    }

                    // Collect link annotations over hyperlinked lines (page-space rects).
                    if (links is not null)
                    {
                        for (var li = firstLine; li < lastLine; li++)
                        {
                            var line = rc.cellLines[li];
                            if (line.Text.Length == 0) continue;
                            var lineTop = slice.TopY - rc.contentTop - (li - firstLine) * slice.Plan.LineHeight;
                            var lineBottom = lineTop - line.FontSize;
                            if (line.Hyperlink is not null)
                            {
                                var w = MeasureWidth(line.Text, line.FontSize);
                                links.Add((new Rectangle(textX, lineBottom, textX + w, lineTop), line.Hyperlink));
                            }
                            // Inline <a> runs: annotate each hyperlinked glyph run
                            // at its pre-measured offset within the line.
                            if (line.LinkRuns is { Count: > 0 })
                                foreach (var (xo, rw, rlink) in line.LinkRuns)
                                    links.Add((new Rectangle(textX + xo, lineBottom,
                                        textX + xo + rw, lineTop), rlink));
                        }
                    }
                    if (footnoteSink is not null)
                    {
                        for (var li = firstLine; li < lastLine; li++)
                        {
                            var line = rc.cellLines[li];
                            if (line.FootNote is null || line.Text.Length == 0) continue;
                            var lb = slice.TopY - rc.contentTop
                                     - (li - firstLine) * slice.Plan.LineHeight
                                     - line.FontSize + engineLift;
                            footnoteSink.Add((line.FootNote,
                                textX + MeasureWidthExact(line.Text, line.FontSize),
                                lb, line.FontSize));
                        }
                    }
                }
            }
        }

    }
}
