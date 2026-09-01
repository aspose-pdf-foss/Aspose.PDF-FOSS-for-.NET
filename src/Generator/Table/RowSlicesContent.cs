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
    /// <summary>Emit content for the slices that landed on the current page.</summary>
    private byte[] BuildSlicesContent(List<RowSlice> slices, double[] colWidths,
        double tableX, string fontName, int[] cellMap, Page? linkPage = null,
        List<SpanBlock>? spanBlocks = null)
    {
        // A measure-only build (page-break pre-flight) must not add annotations or
        // widgets to the page — only the real build emits them.
        if (_measureOnly) linkPage = null;
        var builder = new ContentStreamBuilder();
        var links = linkPage is not null ? new List<(Rectangle rect, Hyperlink link)>() : null;
        var optionSink = linkPage is not null
            ? new List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>() : null;
        // Checkbox placements are recorded for every real page: the first page
        // binds its widgets now, a spill page's are handed to the flow dispatcher
        // (LastCheckboxDraws) for the page that materialises later.
        var checkboxSink = _measureOnly
            ? null : new List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>();
        var pageImages = new List<(byte[] data, Rectangle rect)>();
        var pageGraphs = new List<byte[]>();
        var pageFootnotes = _measureOnly
            ? null : new List<(Note note, double x, double baseline, double size)>();
        // See HtmlSpaceClassFirst: unify space/no-break-space on the laid-out lines. The
        // lines are post-wrap and both characters measure at the space width, so this
        // changes only which of the two identical-looking characters the page carries.
        if (HtmlSpaceClassFirst is ' ' or ' ')
            foreach (var slice in slices)
                foreach (var cellLines in slice.Plan.CellLines)
                    foreach (var cl in cellLines)
                        if (cl.Text is { Length: > 0 })
                            cl.Text = HtmlSpaceClassFirst == ' '
                                ? cl.Text.Replace(' ', ' ')
                                : cl.Text.Replace(' ', ' ');
        var macroSwaps = SubstitutePageMacros(slices);
        builder.SaveState();
        // Blocks whose content is written when the slice for their middle row has been
        // laid down (see EmitSpanBlockContent).
        var spanContent = new List<(SpanBlock block, double x, double w, double h,
            double top, double bottom, int midRow)>();
        if (spanBlocks is { Count: > 0 })
        {
            foreach (var block in spanBlocks)
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
                // A block split by a page break draws its content once, on the page
                // holding the block's middle row (the generator's behaviour);
                // the other pages get background and border only.
                var midRow = (int)Math.Round((block.StartRow + block.EndRow - 1) / 2.0,
                    MidpointRounding.AwayFromZero);
                var drawContent = midRow >= minRow && midRow <= maxRow;

                var x = tableX;
                for (var c = 0; c < block.GridCol && c < colWidths.Length; c++) x += colWidths[c];
                var w = GetCellWidth(colWidths, block.GridCol, block.ColSpan);
                var h = top - bottom;
                var cell = block.Cell;
                var row = block.Row;

                var bgColor = cell.BackgroundColor ?? row.BackgroundColor;
                var blockBorder = cell.IsNoBorder ? null
                    : cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
                // Pitch mode: strokes inside the box, fill inside the strokes (see
                // RenderRowSlice).
                var blockPitch = _columnPitch > 0 && blockBorder is not null;
                if (bgColor is not null)
                {
                    builder.SetFillColor(bgColor);
                    if (blockPitch)
                    {
                        var (fl, fb, fr, ft) = SideInsets(blockBorder!, half: false);
                        builder.Rectangle(x + fl, bottom + fb, w - fl - fr, h - fb - ft);
                    }
                    else
                        builder.Rectangle(x, bottom, w, h);
                    builder.Fill();
                }
                if (blockBorder is not null)
                {
                    if (blockPitch)
                    {
                        var (sl, sb, sr, st) = SideInsets(blockBorder, half: true);
                        DrawPitchBorder(builder, blockBorder, x + sl, bottom + sb, w - sl - sr, h - sb - st);
                    }
                    else
                        DrawBorder(builder, blockBorder, x, bottom, w, h);
                }

                // Record the block's page-space rect (union across pages) for Cell.Rect readers.
                cell.Width = w;
                var blockRect = new Rectangle(x, bottom, x + w, top);
                cell.Rect = cell.Rect is null
                    ? blockRect
                    : new Rectangle(
                        Math.Min(cell.Rect.LLX, blockRect.LLX), Math.Min(cell.Rect.LLY, blockRect.LLY),
                        Math.Max(cell.Rect.URX, blockRect.URX), Math.Max(cell.Rect.URY, blockRect.URY));

                if (block.Lines.Count > 0 && drawContent)
                    spanContent.Add((block, x, w, h, top, bottom, midRow));
            }
        }
        // A row-spanning cell is ONE piece of content seated on the block it covers,
        // and a reader meets it in the middle of the rows it spans - not after them. It
        // is therefore written into the page where it is read: after the slice for the
        // block's middle row. Its background and border go down before any row so the
        // fill cannot cover the text that follows it.
        void EmitSpanBlockContent(SpanBlock block, double x, double w, double h,
            double top, double bottom)
        {
            var cell = block.Cell;
            var row = block.Row;
                    var padding = EffectivePad(cell, row);
                    var dp = DefaultPad(cell, row);
                    var padLeft = padding?.Left ?? dp;
                    var padRight = padding?.Right ?? dp;
                    var padTop = padding?.Top ?? 0;
                    var gapsTotal = 0.0;
                    foreach (var l in block.Lines) gapsTotal += l.TopGap;
                    var blockH = (block.Lines.Count - 1) * block.LineHeight + block.TightLine + gapsTotal;
                    // A spanning cell's content block: an unset alignment centres it
                    // vertically in the portion visible on the page; an EXPLICIT Top
                    // pins it to the span top (stacking down at line pitch), and
                    // Bottom seats it on the span bottom.
                    var effVA = cell.VerticalAlignment != VerticalAlignment.None ? cell.VerticalAlignment : row.VerticalAlignment;
                    var offset = effVA == VerticalAlignment.Bottom
                        ? Math.Max(padTop, h - (padding?.Bottom ?? 0) - blockH)
                        : effVA == VerticalAlignment.Top
                            ? padTop
                            : Math.Max(padTop, (h - blockH) / 2);
                    var gapAccum = 0.0;
                    for (var li = 0; li < block.Lines.Count; li++)
                    {
                        var line = block.Lines[li];
                        gapAccum += line.TopGap;
                        if (line.Text.Length == 0) continue;
                        var tw = line.KernedWidth > 0 ? line.KernedWidth
                            : MeasureWidth(line.Text, line.FontSize);
                        var lineX = line.Align == HorizontalAlignment.Center
                            ? x + Math.Max(padLeft, (w - tw) / 2)
                            : line.Align == HorizontalAlignment.Right
                                ? Math.Max(x + padLeft, x + w - padRight - tw)
                                : x + padLeft;
                        var lineBase = top - offset - gapAccum - li * block.LineHeight - line.FontSize;
                        // Styled segment runs: each piece draws in its OWN face, size,
                        // colour and underline on the line's shared baseline.
                        if (line.SegRuns is { Count: > 0 } spanRuns)
                        {
                            foreach (var run in spanRuns)
                            {
                                if (run.Text.Length == 0) continue;
                                var rx = lineX + run.X;
                                if (run.Ttf is not null && linkPage is not null)
                                {
                                    var srDict = ResolvePageFontDict(linkPage);
                                    var (srRes, srHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                        srDict, run.Ttf, run.FontName ?? "Font", run.Text,
                                        stripSpacesInBaseFont: true);
                                    builder.BeginText();
                                    builder.SetFont(srRes, run.Size);
                                    ApplyColor(builder, run.Color);
                                    builder.MoveTextPosition(rx, lineBase);
                                    builder.ShowTextHex(srHex);
                                    builder.EndText();
                                }
                                else
                                {
                                    var srFont = fontName;
                                    if ((run.Bold || run.Italic) && linkPage is not null)
                                        srFont = RegisterFont(linkPage, run.Bold && run.Italic
                                            ? "Helvetica-BoldOblique"
                                            : run.Bold ? "Helvetica-Bold" : "Helvetica-Oblique");
                                    builder.BeginText();
                                    builder.SetFont(srFont, run.Size);
                                    ApplyColor(builder, run.Color);
                                    builder.MoveTextPosition(rx, lineBase);
                                    builder.ShowText(run.Text);
                                    builder.EndText();
                                }
                                if (run.Underline && run.Width > 0)
                                {
                                    builder.SaveState();
                                    if (run.Color is { } ruc)
                                        builder.SetStrokeColor(ruc.R / 255.0, ruc.G / 255.0, ruc.B / 255.0);
                                    builder.SetLineWidth(LinkUnderlineWPt * run.Size / LinkProbeBasePt);
                                    var ruy = lineBase - LinkUnderlineDropPt * run.Size / LinkProbeBasePt;
                                    builder.MoveTo(rx, ruy).LineTo(rx + run.Width, ruy).Stroke();
                                    builder.RestoreState();
                                }
                            }
                            continue;
                        }
                        // Embedded-serif span line (HonorCellFontFaces): draw through the
                        // Type0 path with kerned advances, like the grid-cell renderer.
                        if (line.Type0Ttf is not null && linkPage is not null)
                        {
                            var sbFontDict = ResolvePageFontDict(linkPage);
                            var (sbRes, sbHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                sbFontDict, line.Type0Ttf, line.Type0FontName ?? "Arial", line.Text,
                                stripSpacesInBaseFont: true);
                            builder.BeginText();
                            builder.SetFont(sbRes, line.FontSize);
                            ApplyColor(builder, line.ForegroundColor);
                            builder.MoveTextPosition(lineX, lineBase);
                            if (line.KernTj && KernAdjustments(line.Text, line.Type0Ttf) is { } sbKern)
                                builder.ShowTextHexKerned(sbHex, sbKern);
                            else
                                builder.ShowTextHex(sbHex);
                            builder.EndText();
                            // Redline decorations (the colspan block path mirrors the
                            // slice renderer's strokes).
                            if (line.Decors is { Count: > 0 } bdDecs && tw > 0)
                            {
                                foreach (var (bdK, bdC) in bdDecs)
                                {
                                    var bdCol = bdK <= 2
                                        ? line.ForegroundColor ?? Color.FromArgb(0, 0, 0)
                                        : bdC ?? Color.FromArgb(0, 0, 0);
                                    builder.SetStrokeColor(bdCol.R / 255.0, bdCol.G / 255.0, bdCol.B / 255.0);
                                    builder.SetLineWidth(bdK <= 2
                                        ? Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineDecorWidthEm * line.FontSize
                                        : 0.75);
                                    if (bdK == 4) builder.SetDashPattern(new double[] { 1.5, 0.75 }, 0);
                                    var bdY = bdK switch
                                    {
                                        1 => lineBase - Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineUnderDropEm * line.FontSize,
                                        2 => lineBase + Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineStrikeRiseEm * line.FontSize,
                                        _ => lineBase - Aspose.Pdf.Converters.HtmlToPdfConverter.RedlineBorderDropEm * line.FontSize,
                                    };
                                    builder.MoveTo(lineX, bdY).LineTo(lineX + tw, bdY).Stroke();
                                    if (bdK == 4) builder.SetDashPattern(System.Array.Empty<double>(), 0);
                                }
                                builder.SetStrokeColor(0, 0, 0);
                            }
                            continue;
                        }
                        builder.BeginText();
                        builder.SetFont(line.Bold && linkPage is not null
                            ? RegisterFont(linkPage, "Helvetica-Bold") : fontName, line.FontSize);
                        ApplyColor(builder, line.ForegroundColor);
                        builder.MoveTextPosition(lineX, lineBase);
                        builder.ShowText(line.Text);
                        builder.EndText();
                    }
        }
        foreach (var slice in slices)
        {
            RenderRowSlice(builder, slice, colWidths, tableX, fontName, cellMap, links, pageImages, optionSink, pageGraphs, checkboxSink, linkPage, pageFootnotes);
            foreach (var sc in spanContent)
                if (sc.midRow == slice.RowIndex)
                    EmitSpanBlockContent(sc.block, sc.x, sc.w, sc.h, sc.top, sc.bottom);
        }
        _pageImages.Add(pageImages);
        _pageGraphs.Add(pageGraphs);
        _pageFootnotes.Add(pageFootnotes ?? new List<(Note, double, double, double)>());

        // Row-spanning cells: draw each block once over the union of its rows' slices
        // on this page. A block split by a page break re-draws its background, border
        // and (re-centred) content in the portion visible on each page — matching the
        // generator's continuation rendering.

        // Outer table.Border wraps the slices that landed on this page.
        // Drawn after slices so it sits on top of cell backgrounds/borders.
        if (Border is not null && slices.Count > 0)
        {
            var totalWidth = 0.0;
            foreach (var w in colWidths) totalWidth += w;
            var topY = slices[0].TopY;
            var bottomY = slices[^1].TopY - slices[^1].Height;
            // Stroke on the mid-line of a border that lies outside the column block:
            // half a width out from the content box on every side.
            var outerWidth = OuterBorderWidth();
            var half = outerWidth / 2;
            if (FormGridCells)
                DrawFormGridBorder(builder, Border, tableX - outerWidth, bottomY - outerWidth,
                    totalWidth + 2 * outerWidth, topY - bottomY + 2 * outerWidth);
            else
                DrawBorder(builder, Border, tableX - half, bottomY - half,
                    totalWidth + outerWidth, topY - bottomY + outerWidth);
        }
        builder.RestoreState();

        if (linkPage is not null && links is { Count: > 0 })
        {
            foreach (var (rect, link) in links)
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
        if (linkPage is not null && optionSink is { Count: > 0 })
            foreach (var (opt, rect) in optionSink)
                opt.OwnerRadio?.PlaceOptionWidget(opt, linkPage, rect);

        // Checkbox widgets laid out in cells: move each widget to its glyph rectangle
        // (its /AP appearance draws the box and check at that position).
        if (linkPage is not null && checkboxSink is { Count: > 0 })
            foreach (var (cbf, rect) in checkboxSink)
                cbf.PlaceWidget(linkPage, rect);
        _pageCheckboxes.Add(checkboxSink ?? new List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>());

        RestorePageMacros(macroSwaps);
        return builder.Build();
    }
}
