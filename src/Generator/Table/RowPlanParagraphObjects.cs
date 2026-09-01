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
    /// <summary>The object stage of a row-plan paragraph: nested tables, images and form controls, verbatim. Returns true when the paragraph is done.</summary>
    private bool RowPlanParagraphObjects(BaseParagraph paragraph, RowPlanColumnState pc, RowPlanState rp, int col, Row row, double[] colWidths, int[] cellMap, int[]? gridToCell, int[]? effRowSpan, double svgFillHeight)
    {
        StampLeading(pc.lines, pc.paraLineStart, pc.paraLeading);
        pc.paraLineStart = pc.lines.Count;
        pc.paraLeading = XmlGeneratorModel ? XmlLineSpacing : CallerLineSpacing(paragraph);
        // A nested TABLE declares its vertical margin like any other cell
        // paragraph — the grid it draws is inset by it (an inner grid that
        // asks for 8 pt above itself gets it).
        if (GeneratorCellModel && paragraph is TextFragment or HtmlFragment or Table)
        {
            var genMargin = ParagraphMargin(paragraph);
            var genGap = pc.genPendingBottom + Math.Max(0, genMargin?.Top ?? 0);
            pc.genPendingBottom = Math.Max(0, genMargin?.Bottom ?? 0);
            if (genGap > 0)
                pc.lines.Add(new CellLine { Text = "", BoxH = genGap, MarginSpacer = true });
        }
        // Nested table: flatten each inner row into one line per row so
        // height accounting and pagination see the inner content. Cell
        // text from each inner cell is joined with " | " as a visual
        // separator; proper nested-table rendering would need its own
        // slice pass, but this keeps pagination honest.
        if (paragraph is Table inner)
        {
            // Generator dialect: the inner grid renders IN PLACE as a table of
            // its own (a logo grid's bordered cells and
            // image draw inside the host cell, starting at the host's inner
            // corner). A measure pass at the host cell's inner box sizes it;
            // one exact-height reserve line holds its place in the row.
            if (GeneratorCellModel && _buildPage is not null)
            {
                double innerH = 0;
                try
                {
                    inner.FitFixedRowsToHost(row.FixedRowHeight);
                    inner.FlowLeftOffset = 0;
                    inner.UsableWidthOverride = Math.Max(1,
                        pc.cellWidth - _columnPitch - (pc.padding?.Left ?? 0) - (pc.padding?.Right ?? 0));
                    inner.BuildMultiPage(_buildPage, 1_000_000, 0, 0, measureOnly: true);
                    innerH = inner.LastRenderedHeight;
                }
                catch { innerH = 0; }
                if (innerH > 0)
                {
                    rp.plan.CellTables ??= new Dictionary<int, List<CellNestedTable>>();
                    if (!rp.plan.CellTables.TryGetValue(col, out var gtList))
                        rp.plan.CellTables[col] = gtList = new List<CellNestedTable>();
                    // The reserve is SPLITTABLE: N lines, each carrying an
                    // equal share of the grid's height as its FontSize, give
                    // the host row a page-break point every ~line pitch, and
                    // the draw pass hands the grid's k-th page slice to the
                    // k-th host slice covering it. The exact stack is unchanged
                    // (N · innerH/N = innerH). One line instead -- the
                    // pre-slice-pass reserve -- makes the grid unbreakable, so
                    // a row that cannot fit it whole splits BEFORE the grid and
                    // leaves the gap where its rows should have been.
                    var gtLines = Math.Max(1,
                        (int)Math.Ceiling(innerH / DefaultLineHeightPt));
                    gtList.Add(new CellNestedTable
                    {
                        Table = inner, HeightPt = innerH, LineOffset = pc.lines.Count,
                        LineCount = gtLines,
                    });
                    var gtLineH = innerH / gtLines;
                    for (var gk = 0; gk < gtLines; gk++)
                        pc.lines.Add(new CellLine { Text = "", FontSize = gtLineH, ImgReserve = true });
                    Consider(rp, gtLineH, gtLineH);
                    return true;
                }
            }
            // The REAL slice pass (opt-in): measure the inner grid at the
            // cell's content width and reserve its height; the draw pass
            // renders it in place. Falls back to the legacy flatten when the
            // measurement cannot run.
            if (NestedTableRender && _buildPage is not null)
            {
                double innerH = 0;
                try
                {
                    inner.NestedTableRender = true;
                    inner.FlowLeftOffset = 0;
                    inner.UsableWidthOverride = pc.availWidth - 2 * inner.HtmlCapsuleOutsetHPt
                        - inner.HtmlListIndentPt;
                    inner.BuildMultiPage(_buildPage, 1_000_000, 0, 0, measureOnly: true);
                    innerH = inner.LastRenderedHeight;
                    // A capsule-wrapped grid reserves its WRAPPER's box: the
                    // pill's padding, spacing band and margin are real space
                    // in the host cell, not paint that hangs outside it.
                    if (innerH > 0) innerH += 2 * inner.HtmlCapsuleOutsetVPt
                        + inner.HtmlMarginTopPt;
                }
                catch { innerH = 0; }
                if (innerH > 0)
                {
                    rp.plan.CellTables ??= new Dictionary<int, List<CellNestedTable>>();
                    if (!rp.plan.CellTables.TryGetValue(col, out var ctList))
                        rp.plan.CellTables[col] = ctList = new List<CellNestedTable>();
                    // The reserve is SPLITTABLE: N lines, each carrying an
                    // equal share of the grid's height as its FontSize, give
                    // the host row a page-break point every ~line pitch. The
                    // exact-stack total is unchanged (N · innerH/N = innerH);
                    // the draw pass hands the grid's k-th page slice to the
                    // k-th host slice covering the reserve.
                    var resLines = Math.Max(1,
                        (int)Math.Ceiling(innerH / DefaultLineHeightPt));
                    ctList.Add(new CellNestedTable
                    {
                        Table = inner,
                        HeightPt = innerH,
                        LineOffset = pc.lines.Count,
                        LineCount = resLines,
                    });
                    // The reserve lines carry the grid's height in their
                    // FontSize (an equal share each), and the row sizes
                    // through the EXACT-stack path (cellOwnStack sums the
                    // reserve back to innerH) — pricing them at the row's
                    // final LineHeight instead would let a sibling cell's
                    // taller fragments inflate the reserve past the real
                    // grid (the row grows in LineHeight quanta).
                    // …and they must not raise the row's SHARED line pitch
                    // either: the grid's own height rides in the reserve
                    // FontSize, while the table's default 10 pt em set a
                    // 12 pt pitch that the sibling text columns (8 pt) then
                    // stacked on — overrunning their own row band and
                    // drawing over the row below.
                    if (!NestedTableRender)
                        Consider(rp, pc.defaultFontSize * CssNormalLineHeight,
                            pc.defaultFontSize * CssNormalLineHeight);
                    var resLineH = innerH / resLines;
                    for (var rk = 0; rk < resLines; rk++)
                        pc.lines.Add(new CellLine
                            { Text = "", FontSize = resLineH, ImgReserve = true });
                    return true;
                }
            }
            // Flatten each inner row into lines, preserving block
            // boundaries from HtmlFragment text (via \n after StripHtmlTags)
            // so the outer cell's height budget reflects the inner table's
            // true visual extent. Each inner row contributes at least one
            // line per non-empty segment.
            var innerRows = inner.Rows;
            Consider(rp, pc.defaultFontSize * 1.2, pc.defaultFontSize);
            for (int ri = 0; ri < innerRows.Count; ri++)
            {
                var irow = innerRows.At(ri);
                var segments = new List<string>();
                for (int ici = 0; ici < irow.Cells.Count; ici++)
                {
                    var icell = irow.Cells.At(ici);
                    foreach (var ip in icell.Paragraphs)
                    {
                        string? rawText = null;
                        if (ip is TextFragment itf) rawText = itf.Text;
                        else if (ip is HtmlFragment ihtml) rawText = HtmlFragment.StripHtmlTags(ihtml.HtmlContent ?? "");
                        if (string.IsNullOrEmpty(rawText)) continue;
                        foreach (var part in rawText.Split('\n'))
                        {
                            var trimmed = part.Trim();
                            if (trimmed.Length > 0) segments.Add(trimmed);
                        }
                    }
                }
                // Ensure each inner row renders a minimum of one blank
                // line when it carries non-text content; otherwise the
                // height collapses and pagination under-counts.
                if (segments.Count == 0) segments.Add(" ");
                foreach (var seg in segments)
                {
                    foreach (var l in WrapText(seg, pc.defaultFontSize, pc.availWidth))
                        pc.lines.Add(new CellLine { Text = l, FontSize = pc.defaultFontSize, ForegroundColor = pc.textState?.ForegroundColor });
                }
            }
            return true;
        }

        // An Image paragraph in a cell is a variable-height block. Resolve its
        // display size (explicit Fix* or natural, fit to the cell width), reserve
        // matching vertical space as blank lines so the row's height budget and
        // pagination cover it, and stash the bytes for the render pass to blit.
        if (paragraph is Image cellImg)
        {
            var rawBytes = ReadRawImageBytes(cellImg);
            if (rawBytes is null) return true;
            var svgSource = cellImg.FileType == ImageFileType.Svg;
            var svgData = rawBytes.Length > 0 && IsSvg(cellImg, rawBytes);
            double imgXOffset = 0;
            // Height of the box a letterboxed picture reserves, when that box is
            // larger than the picture drawn inside it (0 = picture IS the box).
            double imgBoxHeight = 0;
            double dispW, dispH;
            // A picture is clamped to the cell's own BOX, not to its text box:
            // the LAST column's box keeps the pitched width that overhangs the
            // content band and the picture fills it, while only the TEXT is
            // clipped at the margin (an image draws 295 wide in a 296 pt box
            // whose caption wraps in 291.5).
            var imgAvailWidth = pc.availWidth;
            if (GeneratorDialect && !XmlGeneratorModel && LastColBoxOverhang > 0
                && col + Math.Max(1, Math.Min(pc.cell.ColSpan, colWidths.Length - col))
                   >= colWidths.Length)
                imgAvailWidth += LastColBoxOverhang;
            // A vector source with NO intrinsic size (viewBox-only or bare root)
            // fills the space it sits in: the full column footprint wide, and
            // down from the row top to the page's bottom content margin. The
            // artwork stretches to that box regardless of viewBox aspect — a
            // circle in the resulting tall cell renders as a portrait ellipse.
            // Explicit root width/height (or Fix*) sizing keeps the paths below.
            if (cellImg.FixWidth <= 0 && cellImg.FixHeight <= 0
                && svgData && svgFillHeight > 0 && SvgLacksIntrinsicSize(rawBytes))
            {
                dispW = pc.cellWidth;
                dispH = svgFillHeight;
                imgXOffset = -pc.padLeft;
                var sized = ImageRasterizer.RasterizeSvgOnPageCanvas(rawBytes);
                if (sized is null) return true;
                AddCellImage(rp.plan, col, new CellImage
                {
                    Data = sized, Width = dispW, Height = dispH, Align = cellImg.HorizontalAlignment,
                    XOffset = imgXOffset,
                    FillsBand = true,
                    LineOffset = pc.lines.Count,
                });
                // Reserve lines summing EXACTLY to the fill height (n lines of
                // dispH/n each, n = how many default-leading lines fit) so the
                // row bottom lands on the page's bottom content margin instead
                // of a line-quantised
                // overshoot.
                var fillLines = Math.Max(1, (int)Math.Floor(dispH / (pc.defaultFontSize * 1.2)));
                var fillLineH = dispH / fillLines;
                Consider(rp, fillLineH, fillLineH);
                for (var k = 0; k < fillLines; k++)
                    pc.lines.Add(new CellLine { Text = "", FontSize = fillLineH / 1.2, ImgReserve = true });
                return true;
            }
            var imgBytes = svgData ? ImageRasterizer.RasterizeSvg(rawBytes) ?? rawBytes : rawBytes;
            if (cellImg.FixWidth > 0 && cellImg.FixHeight > 0)
            {
                dispW = cellImg.FixWidth;
                dispH = cellImg.FixHeight;
                // A vector (SVG) source keeps its aspect ratio inside the
                // declared Fix box and is centred in it horizontally,
                // instead of being stretched like a raster source. The BOX is
                // still the declared one: the picture rides letterboxed in it,
                // and the row keeps the full FixHeight of room (
                // a 120×78 viewBox in a 45×45 box draws 45 × 30 centred in 45).
                if (svgSource && TryGetCellImageSizePt(imgBytes, out var svgW, out var svgH)
                    && svgW > 0 && svgH > 0)
                {
                    var fit = Math.Min(dispW / svgW, dispH / svgH);
                    var fw = svgW * fit;
                    var fh = svgH * fit;
                    imgXOffset = (dispW - fw) / 2;
                    imgBoxHeight = dispH;
                    dispW = fw;
                    dispH = fh;
                }
            }
            else if (TryGetCellImageSizePt(imgBytes, out var natW, out var natH) && natW > 0 && natH > 0)
            {
                // Generator dialects: 1 source pixel = 1 pt, regardless of the
                // file's DPI header (a 240×60 bmp draws as a
                // 240×60 pt box; a 350×100 png draws 100 pt tall, its width
                // clamped to the column). Only the HTML converter reads a source
                // resolution, because a CSS pixel is three quarters of a point.
                // ⚠ A VECTOR source is exempt: its intrinsic size is already in
                // points, and the pixels here are our rasteriser's choice, not
                // the author's.
                if (GeneratorDialect && !svgData && !svgSource)
                {
                    var (pxW, pxH) = ImageDimensions.Read(imgBytes);
                    if (pxW > 0 && pxH > 0)
                    {
                        natW = pxW;
                        natH = pxH;
                    }
                }
                if (cellImg.IsApplyResolution)
                {
                    // Resolution-aware: fit to the cell's content width preserving the
                    // aspect ratio (IsApplyResolution behaviour — a wide
                    // image is scaled down to the column, height shrinks proportionally).
                    if (imgAvailWidth > 0 && natW > imgAvailWidth)
                    {
                        dispH = natH * (imgAvailWidth / natW);
                        dispW = imgAvailWidth;
                    }
                    else
                    {
                        dispW = natW;
                        dispH = natH;
                    }
                }
                else
                {
                    // Default (no resolution applied): the width is clamped to the cell's
                    // content width while the height stays at the image's natural
                    // point-height (aspect is not preserved — a wide image is squeezed to
                    // the column and rendered at full height). Explicit Fix* sizing above
                    // is the documented way to avoid this stretch.
                    dispW = imgAvailWidth > 0 && natW > imgAvailWidth ? imgAvailWidth : natW;
                    dispH = natH;
                    // …and the height is capped by the PAGE: an unsized picture
                    // never grows past the band from the table's top down to the
                    // bottom content margin (an 800×600 jpg in a 100 pt
                    // column draws 100 × 451 — the whole height a landscape page
                    // has left — rather than paginating at its natural 600).
                    if (GeneratorDialect && !XmlGeneratorModel && svgFillHeight > 0 && dispH > svgFillHeight)
                        dispH = svgFillHeight;
                }
            }
            else
            {
                dispW = pc.availWidth > 0 ? pc.availWidth : 100;
                dispH = dispW;
            }
            // Generator dialect, FIXED-height row: an unsized image is stretched to
            // the cell's inner box — the column inside its rules (no default
            // padding) by the row height inside its rules (probed: the 200×107 px
            // logo in a 60.42 pt column of a 12.96 pt row draws 60.42 × 10.96).
            if (GeneratorCellModel && row.FixedRowHeight > 0
                && !(cellImg.FixWidth > 0 && cellImg.FixHeight > 0))
            {
                var imgBorder = pc.cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
                var (il, ib, ir, it) = imgBorder is null ? (0, 0, 0, 0) : SideInsets(imgBorder, half: false);
                var innerW = pc.cellWidth - _columnPitch - (pc.padding?.Left ?? 0) - (pc.padding?.Right ?? 0);
                if (_columnPitch <= 0) innerW -= il + ir;
                var innerH = row.FixedRowHeight - it - ib - (pc.padding?.Top ?? 0) - (pc.padding?.Bottom ?? 0);
                if (innerW > 0 && innerH > 0)
                {
                    // An ImageScale sizes the image from its pixels (1 px = 1 pt,
                    // times the scale) and the inner box only CLAMPS it, per
                    // axis (probed: a 72×84 px logo at 0.2 in a 28 pt row with
                    // 5 pt margins draws 14.4 × 16 — width kept, height cut to
                    // the 16 pt inner box).
                    var (scPxW, scPxH) = cellImg.ImageScale > 0 ? ImageDimensions.Read(imgBytes) : (0, 0);
                    if (cellImg.ImageScale > 0 && scPxW > 0 && scPxH > 0)
                    {
                        dispW = Math.Min(scPxW * cellImg.ImageScale, innerW);
                        dispH = Math.Min(scPxH * cellImg.ImageScale, innerH);
                    }
                    else { dispW = innerW; dispH = innerH; }
                }
            }
            AddCellImage(rp.plan, col, new CellImage
            {
                Data = imgBytes, Width = dispW, Height = dispH, Align = cellImg.HorizontalAlignment,
                XOffset = imgXOffset,
                BoxHeight = imgBoxHeight > dispH ? imgBoxHeight : 0,
                LineOffset = pc.lines.Count,
            });
            // The reserve covers the BOX, which a letterboxed picture is smaller than.
            var imgBoxH = imgBoxHeight > dispH ? imgBoxHeight : dispH;
            var imgLineH = pc.defaultFontSize * 1.2;
            var imgLines = Math.Max(1, (int)Math.Ceiling(imgBoxH / imgLineH));
            // The reserve must sum to the image's OWN height: pricing each line at
            // the table's default font size while counting them at the line BOX
            // left every tall image short by the difference (a 112.5 pt image
            // reserved 100), so a column of them overlapped and the last one ran
            // past the section it sits in. The lifted render sizes the stack
            // exactly; the legacy grid keeps its calibrated quantisation.
            var imgLinePt = NestedTableRender ? imgBoxH / imgLines : pc.defaultFontSize;
            Consider(rp, imgLineH, imgLineH);
            for (var k = 0; k < imgLines; k++)
                pc.lines.Add(new CellLine { Text = "", FontSize = imgLinePt, ImgReserve = true });
            return true;
        }

        // A radio-button option in a cell renders as a glyph (circle) followed
        // by its caption. Emit one line carrying the option so the row's height
        // budget covers the glyph and the render pass can draw it.
        if (paragraph is Aspose.Pdf.Forms.RadioButtonOptionField opt)
        {
            var capSize = opt.Caption?.TextState.FontSize > 0
                ? opt.Caption!.TextState.FontSize
                : pc.defaultFontSize;
            var glyphH = opt.Height > 0 ? opt.Height : capSize;
            // A control row is sized to its glyph/caption without the extra
            // text leading — the glyph is a fixed box, not a line of type.
            var lh = Math.Max(glyphH, capSize);
            Consider(rp, lh, lh);
            pc.lines.Add(new CellLine
            {
                Text = opt.Caption?.Text ?? "",
                FontSize = capSize,
                ForegroundColor = opt.Caption?.TextState.ForegroundColor ?? pc.textState?.ForegroundColor,
                Option = opt,
            });
            return true;
        }

        // A checkbox in a cell occupies a fixed glyph box; record a control line so
        // the row height covers it and the render pass repositions its widget.
        if (paragraph is Aspose.Pdf.Forms.CheckboxField cbf)
        {
            var boxH = cbf.Height > 0 ? cbf.Height : pc.defaultFontSize;
            Consider(rp, boxH, boxH);
            pc.lines.Add(new CellLine { Text = "", FontSize = pc.defaultFontSize, Checkbox = cbf });
            return true;
        }

        return false;
    }
}
