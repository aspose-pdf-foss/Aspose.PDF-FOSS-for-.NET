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
// Span-block content of a table slice.
    // A row-spanning cell is ONE piece of content seated on the block it covers,
    // and a reader meets it in the middle of the rows it spans - not after them. It
    // is therefore written into the page where it is read: after the slice for the
    // block's middle row. Its background and border go down before any row so the
    // fill cannot cover the text that follows it.
    private void EmitSpanBlockContent(SlicesContentState slc, string fontName, Page? linkPage, SpanBlock block, double x, double w, double h,
        double top, double bottom)
    {
        slc.cell = block.Cell;
        slc.row = block.Row;
        slc.padding = EffectivePad(slc.cell, slc.row);
        slc.dp = DefaultPad(slc.cell, slc.row);
        slc.padLeft = slc.padding?.Left ?? slc.dp;
        slc.padRight = slc.padding?.Right ?? slc.dp;
        slc.padTop = slc.padding?.Top ?? 0;
        // A collapsed grid's block sits inside the rules on its top and left
        // boundaries, exactly as an ordinary cell does: half of each of its own
        // rules inside the lines, which stand half a skeleton rule inside the box.
        if (_collapsedRules is { } resolved && resolved.CellSides.TryGetValue(slc.cell, out var sides))
        {
            var skeleton = CollapsedSkeleton() / 2;
            slc.padLeft += skeleton + sides.Left / 2;
            slc.padTop += skeleton + sides.Top / 2;
        }
        // A grid whose rules stand inside its column widths seats the block's text
        // inside the block's own rules, as its ordinary cells do (see SeatCellLineRange).
        else if (RulesInsideColumnWidth && CellRuleBorder(slc.cell, slc.row) is { } rules)
        {
            slc.padLeft += OccupiedSideWidth(rules, BorderSide.Left, rules.LeftAssigned, rules.RawLeft);
            slc.padTop += OccupiedSideWidth(rules, BorderSide.Top, rules.TopAssigned, rules.RawTop);
        }
        var (lineStart, lineCount) = TakeSpanPortion(block);
        slc.gapsTotal = 0.0;
        for (var li = lineStart; li < lineStart + lineCount; li++) slc.gapsTotal += block.Lines[li].TopGap;
        slc.blockH = lineCount == 0 ? 0
            : (lineCount - 1) * block.LineHeight + block.TightLine + slc.gapsTotal;
        slc.effVA = slc.cell.VerticalAlignment != VerticalAlignment.None ? slc.cell.VerticalAlignment : slc.row.VerticalAlignment;
        slc.offset = slc.effVA == VerticalAlignment.Bottom
            ? Math.Max(slc.padTop, h - (slc.padding?.Bottom ?? 0) - slc.blockH)
            : slc.effVA == VerticalAlignment.Top
                ? slc.padTop
                : Math.Max(slc.padTop, (h - slc.blockH) / 2);
        slc.gapAccum = 0.0;
        for (var li = lineStart; li < lineStart + lineCount; li++)
        {
            // The line's SEAT is its place in the portion this page draws, not
            // its place in the block: a continuation opens at its portion's top.
            if (!EmitSpanBlockLine(slc, fontName, linkPage, block, x, w, top, li, li - lineStart)) break;
        }
    }

    /// <summary>Register the slice's hyperlinks, radio options and checkboxes on the link page.</summary>
    private void RegisterSliceLinks(SlicesContentState slc, Page? linkPage)
    {
        if (linkPage is not null && slc.links is { Count: > 0 })
        {
            foreach (var (rect, link) in slc.links)
            {
                if (link is WebHyperlink wh && !string.IsNullOrEmpty(wh.Url))
                    // Record the target URL as the link's /Contents so it is
                    // recoverable from the annotation after a save/reload round-trip.
                    linkPage.Annotations.AddLinkAnnotation(rect, wh.Url).Contents = wh.Url;
                else if (link is LocalHyperlink lh && lh.TargetPageNumber > 0)
                    linkPage.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(lh.TargetPageNumber, 0, 0, 0)));
            }
        }

        // Radio-option widgets laid out in cells: place each option's widget at its
        // glyph rectangle and add it to the page /Annots so it round-trips as an
        // interactive control alongside the drawn glyph.
        if (linkPage is not null && slc.optionSink is { Count: > 0 })
            foreach (var (opt, rect) in slc.optionSink)
                opt.OwnerRadio?.PlaceOptionWidget(opt, linkPage, rect);

        // Checkbox widgets laid out in cells: move each widget to its glyph rectangle
        // (its /AP appearance draws the box and check at that position).
        if (linkPage is not null && slc.checkboxSink is { Count: > 0 })
            foreach (var (cbf, rect) in slc.checkboxSink)
                cbf.PlaceWidget(linkPage, rect);
        _pageCheckboxes.Add(slc.checkboxSink ?? new List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>());

        RestorePageMacros(slc.macroSwaps);
    }

    /// <summary>Seat the span blocks: each block's column span, row band and the content emitted inside it.</summary>
    private void PlaceSpanBlocks(SlicesContentState slc, List<RowSlice> slices, double[] colWidths, double tableX, List<SpanBlock>? spanBlocks)
    {
        foreach (var block in spanBlocks!)
        {
            double top = double.MinValue, bottom = double.MaxValue;
            int minRow = int.MaxValue, maxRow = int.MinValue;
            foreach (var slice in slices)
            {
                if (slice.RowIndex < block.StartRow || slice.RowIndex >= block.EndRow) continue;
                if (slice.TopY > top) top = slice.TopY;
                if (slice.TopY - slice.Height < bottom) bottom = slice.TopY - slice.Height;
                if (slice.RowIndex < minRow) minRow = slice.RowIndex;
                if (slice.RowIndex > maxRow) maxRow = slice.RowIndex;
            }
            if (top <= double.MinValue) continue;   // no rows of this span on this page
            // The block's content is written with the LAST row it spans, among
            // that row's own cells: a spanning cell is finished when the rows it
            // covers are, and that is where the text layer carries it (probed
            // against the reference on a two-row and a four-row span alike). A
            // block whose columns OPEN that row is written before the row's other
            // cells, which is where the column order puts it.
            // ⚠ A block that opens in the MIDDLE of a row follows the whole row
            // rather than the cells to its left only -- the row draws in one pass.
            // A block split by a page break draws its content once, on the page
            // holding that last row; the pages before it get background and
            // border only.
            var contentRow = block.EndRow - 1;
            var drawContent = contentRow >= minRow && contentRow <= maxRow;
            // A block that SPLITS is written on every page it reaches, with the
            // last row it has there; only the page holding its final row still
            // writes the lines that are left.
            if (SpanBlockSplitsAtPageBreak && !drawContent && maxRow >= minRow)
            {
                contentRow = maxRow;
                drawContent = true;
            }

            // A block whose last row HERE is the page's last row closes its own
            // box: there is no row below it on this page to draw the boundary, so
            // the block is ruled off at the cut like the rest of the grid. It is
            // read off the block itself -- the per-slice flag still holds the
            // PREVIOUS page's last slice at this point, which ruled an uncut block
            // off at its foot on every page after the first.
            var blockClosesPage = slices.Count > 0 && maxRow == slices[slices.Count - 1].RowIndex;

            var x = tableX + ColumnBoxOffset(colWidths, block.GridCol);
            var w = GetCellWidth(colWidths, block.GridCol, block.ColSpan);
            var h = top - bottom;
            var cell = block.Cell;
            var row = block.Row;

            var bgColor = cell.BackgroundColor ?? row.BackgroundColor;
            // A collapsed grid paints the block like any other box of it.
            if (CollapsedRulesFor(colWidths.Length) is { } rules)
                PaintResolvedSpanBlock(slc.builder, rules, slices, colWidths, block, x, top, bottom, bgColor, blockClosesPage);
            else
                PaintSeparateSpanBlock(slc, block, x, w, top, bottom, bgColor);

            // Record the block's page-space rect (union across pages) for Cell.Rect readers.
            cell.Width = w;
            var blockRect = new Rectangle(x, bottom, x + w, top);
            cell.Rect = cell.Rect is null
                ? blockRect
                : new Rectangle(
                    Math.Min(cell.Rect.LLX, blockRect.LLX), Math.Min(cell.Rect.LLY, blockRect.LLY),
                    Math.Max(cell.Rect.URX, blockRect.URX), Math.Max(cell.Rect.URY, blockRect.URY));

            if (block.Lines.Count > 0 && drawContent)
                slc.spanContent.Add((block, x, w, h, top, bottom, contentRow));
        }
    }

    /// <summary>A row-spanning block of a SEPARATE grid: in pitch mode its rules
    /// stroke inside its box and its fill stays inside them (see RenderRowSlice);
    /// otherwise both take the box as it is.</summary>
    private void PaintSeparateSpanBlock(SlicesContentState slc, SpanBlock block, double x, double w,
        double top, double bottom, Color? bgColor)
    {
        var h = top - bottom;
        var blockBorder = block.Cell.IsNoBorder ? null
            : block.Cell.Border ?? block.Row.DefaultCellBorder ?? block.Row.Border ?? DefaultCellBorder;
        var blockPitch = _columnPitch > 0 && blockBorder is not null;
        if (bgColor is not null)
        {
            slc.builder.SetFillColor(bgColor);
            // (a block whose rules stand inside its column width is filled whole, like its grid's cells)
            if (blockPitch && !RulesInsideColumnWidth)
            {
                var (fl, fb, fr, ft) = SideInsets(blockBorder!, half: false);
                slc.builder.Rectangle(x + fl, bottom + fb, w - fl - fr, h - fb - ft);
            }
            else
                slc.builder.Rectangle(x, bottom, w, h);
            slc.builder.Fill();
        }
        if (blockBorder is null) return;
        if (blockPitch && RulesInsideColumnWidth)
            DrawRulesInsideBox(slc.builder, blockBorder, x, bottom, w, h);
        else if (blockPitch)
        {
            var (sl, sb, sr, st) = SideInsets(blockBorder, half: true);
            DrawPitchBorder(slc.builder, blockBorder, x + sl, bottom + sb, w - sl - sr, h - sb - st);
        }
        else
            DrawBorder(slc.builder, blockBorder, x, bottom, w, h);
    }

    /// <summary>One line of a span block: its gap, text runs with their faces and colours, and the link rectangle it books.</summary>
    private bool EmitSpanBlockLine(SlicesContentState slc, string fontName, Page? linkPage, SpanBlock block, double x, double w, double top, int li, int seat)
    {
        slc.line = block.Lines[li];
        slc.gapAccum += slc.line.TopGap;
        if (slc.line.Text.Length == 0) return true;
        slc.tw = slc.line.KernedWidth > 0 ? slc.line.KernedWidth
            // A line laid out to a declared box is measured exactly (see
            // MeasureStyledLine).
            : DeclaresLineBox(slc.line) ? MeasureWidthExactAfm(slc.line.Text, slc.line.FontSize)
            : MeasureWidth(slc.line.Text, slc.line.FontSize);
        slc.lineX = slc.line.Align == HorizontalAlignment.Center
            ? x + Math.Max(slc.padLeft, (w - slc.tw) / 2)
            : slc.line.Align == HorizontalAlignment.Right
                ? Math.Max(x + slc.padLeft, x + w - slc.padRight - slc.tw)
                : x + slc.padLeft;
        // A declared line box advances by its own height and seats its baseline
        // half its surplus leading below its top, the same as in an ordinary cell.
        slc.lineBase = DeclaresLineBox(slc.line)
            ? top - slc.offset - slc.gapAccum
              - seat * DeclaredLinePitch(slc.line, block.LineHeight) - DeclaredLineBoxBaseOff(slc.line)
            : top - slc.offset - slc.gapAccum - seat * block.LineHeight - slc.line.FontSize;
        // Styled segment runs: each piece draws in its OWN face, size,
        // colour and underline on the line's shared baseline.
        if (slc.line.SegRuns is { Count: > 0 } spanRuns)
        {
            if (EmitSpanBlockRuns(slc, fontName, linkPage, spanRuns)) return true;
        }
        // Embedded-serif span line (HonorCellFontFaces): draw through the
        // Type0 path with kerned advances, like the grid-cell renderer.
        if (slc.line.Type0Ttf is not null && linkPage is not null)
        {
            if (EmitSpanBlockType0(slc, linkPage)) return true;
        }
        slc.builder.BeginText();
        slc.builder.SetFont(slc.line.BaseFont is { } face && linkPage is not null ? RegisterFont(linkPage, face)
            : slc.line.Bold && linkPage is not null ? RegisterFont(linkPage, "Helvetica-Bold") : fontName, slc.line.FontSize);
        ApplyColor(slc.builder, slc.line.ForegroundColor);
        slc.builder.MoveTextPosition(slc.lineX, slc.lineBase);
        slc.builder.ShowText(slc.line.Text);
        slc.builder.EndText();
        return true;
    }

    /// <summary>A line set in an embedded Type0 face: the subset glyph ids shown at the line origin with the link rectangle.</summary>
    private bool EmitSpanBlockType0(SlicesContentState slc, Page? linkPage)
    {
        var sbFontDict = ResolvePageFontDict(linkPage!);
        var (sbRes, sbHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
            sbFontDict, slc.line.Type0Ttf!, slc.line.Type0FontName ?? "Arial", slc.line.Text,
            stripSpacesInBaseFont: true);
        slc.builder.BeginText();
        slc.builder.SetFont(sbRes, slc.line.FontSize);
        ApplyColor(slc.builder, slc.line.ForegroundColor);
        slc.builder.MoveTextPosition(slc.lineX, slc.lineBase);
        if (slc.line.KernTj && KernAdjustments(slc.line.Text, slc.line.Type0Ttf!) is { } sbKern)
            slc.builder.ShowTextHexKerned(sbHex, sbKern);
        else
            slc.builder.ShowTextHex(sbHex);
        slc.builder.EndText();
        // Redline decorations (the colspan block path mirrors the
        // slice renderer's strokes).
        if (slc.line.Decors is { Count: > 0 } bdDecs && slc.tw > 0)
        {
            foreach (var (bdK, bdC) in bdDecs)
            {
                var bdCol = bdK <= 2
                    ? slc.line.ForegroundColor ?? Color.FromArgb(0, 0, 0)
                    : bdC ?? Color.FromArgb(0, 0, 0);
                slc.builder.SetStrokeColor(bdCol.R / 255.0, bdCol.G / 255.0, bdCol.B / 255.0);
                slc.builder.SetLineWidth(bdK <= 2
                    ? Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineDecorWidthEm * slc.line.FontSize
                    : 0.75);
                if (bdK == 4) slc.builder.SetDashPattern(new double[] { 1.5, 0.75 }, 0);
                var bdY = bdK switch
                {
                    1 => slc.lineBase - Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineUnderDropEm * slc.line.FontSize,
                    2 => slc.lineBase + Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineStrikeRiseEm * slc.line.FontSize,
                    _ => slc.lineBase - Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineBorderDropEm * slc.line.FontSize,
                };
                slc.builder.MoveTo(slc.lineX, bdY).LineTo(slc.lineX + slc.tw, bdY).Stroke();
                if (bdK == 4) slc.builder.SetDashPattern(System.Array.Empty<double>(), 0);
            }
            slc.builder.SetStrokeColor(0, 0, 0);
        }
        return true;
    }

    /// <summary>A line of segment runs: each run with its own face, size and colour at the walked pen.</summary>
    private bool EmitSpanBlockRuns(SlicesContentState slc, string fontName, Page? linkPage, List<SpanRun> spanRuns)
    {
        // A picture among the runs stands on the baseline at its offset: drawn
        // in place when a page can hold it, else blitted with the page's images.
        if (linkPage is null)
            foreach (var run in spanRuns)
                if (run.Picture is { } picture)
                    slc.pageImages.Add((picture, new Rectangle(slc.lineX + run.X, slc.lineBase,
                        slc.lineX + run.X + run.Width, slc.lineBase + run.PictureHeight)));
        EmitStyledRuns(slc.builder, fontName, linkPage, spanRuns, slc.lineX, slc.lineBase, linkPage);
        return true;
    }
}
