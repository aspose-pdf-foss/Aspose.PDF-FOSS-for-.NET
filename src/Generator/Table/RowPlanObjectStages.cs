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
    /// <summary>Nested-table render: the inner table is measured at the cell width and its rendered height becomes blank lines of the outer row.</summary>
    /// <summary>A CENTRED nested grid whose declared widths overrun its generator host
    /// cell is scaled, every column alike, to the host cell width plus its own
    /// column pitch per column (see <see cref="NestedCentreBandPt"/>), and centred in the flat
    /// <see cref="NestedCentreBandPt"/> band (probed; a grid that fits, and a
    /// left-aligned one, keep their declared columns).</summary>
    private void FitCentredNestedGrid(Table inner, double hostCellWidth)
    {
        inner.NestedOverwideTarget = 0;
        if (!GeneratorCellModel || inner.Alignment != HorizontalAlignment.Center
            || string.IsNullOrWhiteSpace(inner.ColumnWidths)) return;
        double declared = 0;
        var n = 0;
        foreach (var w in inner.ColumnWidths.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
            declared += v;
            n++;
        }
        var (pitchL, pitchR) = inner.CellBorderPitch();
        var target = hostCellWidth + (pitchL + pitchR) * n;
        if (n == 0 || declared <= target + 1e-3) return;
        inner.NestedOverwideTarget = target;
        inner.NestedDeclaredSum = declared;
        inner.UsableWidthOverride = target;
    }

    private bool? PlanNestedRenderInnerTable(Table inner, RowPlanColumnState pc, RowPlanState rp, int col)
    {
        double innerH = 0;
        try
        {
            inner.NestedTableRender = true;
            inner.FlowLeftOffset = 0;
            inner.UsableWidthOverride = pc.availWidth - 2 * inner.HtmlCapsuleOutsetHPt
                - inner.HtmlListIndentPt;
            inner.BuildMultiPage(_buildPage!, 1_000_000, 0, 0, measureOnly: true);
            innerH = inner.LastRenderedHeight;
            // A capsule-wrapped grid reserves its WRAPPER's box: the
            // pill's padding, spacing band and margin are real space
            // in the host cell, not paint that hangs outside it.
            if (innerH > 0) innerH += 2 * inner.HtmlCapsuleOutsetVPt
                + inner.HtmlMarginTopPt + inner.HtmlMarginBottomPt;
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
        return null;
    }

    /// <summary>Generator cell model: the inner grid renders in place at the cell origin and its height is booked as lines.</summary>
    private bool? PlanGeneratorCellInnerTable(Table inner, RowPlanColumnState pc, RowPlanState rp, int col, Row row)
    {
        double innerH = 0;
        try
        {
            inner.FitFixedRowsToHost(row.FixedRowHeight);
            inner.FlowLeftOffset = 0;
            inner.UsableWidthOverride = Math.Max(1,
                pc.cellWidth - _columnPitch - (pc.padding?.Left ?? 0) - (pc.padding?.Right ?? 0));
            FitCentredNestedGrid(inner, pc.cellWidth);
            inner.BuildMultiPage(_buildPage!, 1_000_000, 0, 0, measureOnly: true);
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
        return null;
    }

    /// <summary>Book the image's draw at the cell origin and the blank lines that reserve its height in the row.</summary>
    private void BookImageLines(RowPlanImageState ri, Image cellImg, RowPlanColumnState pc, RowPlanState rp, int col, Row row)
    {
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
                var (scPxW, scPxH) = cellImg.ImageScale > 0 ? ImageDimensions.Read(ri.imgBytes) : (0, 0);
                if (cellImg.ImageScale > 0 && scPxW > 0 && scPxH > 0)
                {
                    ri.dispW = Math.Min(scPxW * cellImg.ImageScale, innerW);
                    ri.dispH = Math.Min(scPxH * cellImg.ImageScale, innerH);
                }
                else { ri.dispW = innerW; ri.dispH = innerH; }
            }
        }
        var (pictureMarginL, pictureMarginT, pictureMarginB) = CellPictureBoxesAreExact
            ? (cellImg.Margin?.Left ?? 0, cellImg.Margin?.Top ?? 0, cellImg.Margin?.Bottom ?? 0)
            : (0, 0, 0);
        AddCellImage(rp.plan, col, new CellImage
        {
            Data = ri.imgBytes, Width = ri.dispW, Height = ri.dispH, Align = cellImg.HorizontalAlignment,
            XOffset = ri.imgXOffset + pictureMarginL,
            // An exact picture's box is its height plus its margins, priced as such.
            BoxHeight = CellPictureBoxesAreExact && (pictureMarginT > 0 || pictureMarginB > 0)
                ? ri.dispH + pictureMarginT + pictureMarginB
                : ri.imgBoxHeight > ri.dispH ? ri.imgBoxHeight : 0,
            LineOffset = pc.lines.Count,
            OwnSeat = CellPictureBoxesAreExact,
            MarginTop = pictureMarginT,
        });
        ri.imgBoxH = ri.imgBoxHeight > ri.dispH ? ri.imgBoxHeight : ri.dispH;
        if (CellPictureBoxesAreExact)
        {
            // Exactly the box, margins included, as one line of its own pitch; the
            // row's UNIFORM pitch is left to the text (a box is not a line height).
            var own = ri.imgBoxH + pictureMarginT + pictureMarginB;
            Consider(rp, pc.defaultFontSize * 1.2, pc.defaultFontSize * 1.2);
            pc.lines.Add(new CellLine { Text = "", FontSize = pc.defaultFontSize, ImgReserve = true, OwnPitch = own });
            return;
        }
        // A UA-boxed cell's image reserves exactly its box: one line the image's height, no font-line
        // rounding (measured on the mailing: the 77 px logo row stands 57.75).
        ri.imgLineH = UaCellBoxes ? ri.imgBoxH : pc.defaultFontSize * 1.2;
        ri.imgLines = UaCellBoxes ? 1 : Math.Max(1, (int)Math.Ceiling(ri.imgBoxH / ri.imgLineH));
        ri.imgLinePt = UaCellBoxes ? ri.imgBoxH : NestedTableRender ? ri.imgBoxH / ri.imgLines : pc.defaultFontSize;
        Consider(rp, ri.imgLineH, ri.imgLineH);
        for (var k = 0; k < ri.imgLines; k++)
            pc.lines.Add(new CellLine { Text = "", FontSize = ri.imgLinePt, ImgReserve = true });
    }

    /// <summary>Resolve the image's display size: the explicit Fix* box, the natural size fitted to the cell, or the fallback square.</summary>
    private void ResolveImageDisplaySize(RowPlanImageState ri, Image cellImg, RowPlanColumnState pc, double svgFillHeight)
    {
        ri.imgBytes = ri.svgData ? ImageRasterizer.RasterizeSvg(ri.rawBytes!) ?? ri.rawBytes! : ri.rawBytes!;
        if (cellImg.FixWidth > 0 && cellImg.FixHeight > 0)
        {
            ri.dispW = cellImg.FixWidth;
            ri.dispH = cellImg.FixHeight;
            // A fixed box wider than its cell shrinks to the cell's inner width and
            // keeps its height (probed: a 74 x 80 box in a 70 pt column draws 70 x 80;
            // the same box in a 100 pt column draws 74 x 80).
            var innerW = pc.cellWidth - pc.padLeft - pc.padRight;
            if (GeneratorCellModel && innerW > 0 && ri.dispW > innerW) ri.dispW = innerW;
            // A vector (SVG) source keeps its aspect ratio inside the
            // declared Fix box and is centred in it horizontally,
            // instead of being stretched like a raster source. The BOX is
            // still the declared one: the picture rides letterboxed in it,
            // and the row keeps the full FixHeight of room (
            // a 120×78 viewBox in a 45×45 box draws 45 × 30 centred in 45).
            if (ri.svgSource && TryGetCellImageSizePt(ri.imgBytes) is (var svgW, var svgH)
                && svgW > 0 && svgH > 0)
            {
                var fit = Math.Min(ri.dispW / svgW, ri.dispH / svgH);
                var fw = svgW * fit;
                var fh = svgH * fit;
                ri.imgXOffset = (ri.dispW - fw) / 2;
                ri.imgBoxHeight = ri.dispH;
                ri.dispW = fw;
                ri.dispH = fh;
            }
        }
        else if (NaturalCellImageSizePt(ri) is (var natW, var natH) && natW > 0 && natH > 0)
        {
            // Generator dialects: 1 source pixel = 1 pt, regardless of the
            // file's DPI header (a 240×60 bmp draws as a
            // 240×60 pt box; a 350×100 png draws 100 pt tall, its width
            // clamped to the column). Only the HTML converter reads a source
            // resolution, because a CSS pixel is three quarters of a point.
            // ⚠ A VECTOR source is exempt: its intrinsic size is already in
            // points, and the pixels here are our rasteriser's choice, not
            // the author's.
            if (GeneratorDialect && !ri.svgData && !ri.svgSource)
            {
                var (pxW, pxH) = ImageDimensions.Read(ri.imgBytes);
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
                if (ri.imgAvailWidth > 0 && natW > ri.imgAvailWidth)
                {
                    ri.dispH = natH * (ri.imgAvailWidth / natW);
                    ri.dispW = ri.imgAvailWidth;
                }
                else
                {
                    ri.dispW = natW;
                    ri.dispH = natH;
                }
            }
            else
            {
                // Default (no resolution applied): the width is clamped to the cell's
                // content width while the height stays at the image's natural
                // point-height (aspect is not preserved — a wide image is squeezed to
                // the column and rendered at full height). Explicit Fix* sizing above
                // is the documented way to avoid this stretch.
                ri.dispW = ri.imgAvailWidth > 0 && natW > ri.imgAvailWidth ? ri.imgAvailWidth : natW;
                ri.dispH = natH;
                // …and the height is capped by the PAGE: an unsized picture
                // never grows past the band from the table's top down to the
                // bottom content margin (an 800×600 jpg in a 100 pt
                // column draws 100 × 451 — the whole height a landscape page
                // has left — rather than paginating at its natural 600).
                if (GeneratorDialect && !XmlGeneratorModel && svgFillHeight > 0 && ri.dispH > svgFillHeight)
                    ri.dispH = svgFillHeight;
            }
        }
        else
        {
            ri.dispW = pc.availWidth > 0 ? pc.availWidth : 100;
            ri.dispH = ri.dispW;
        }
        // Generator dialect, FIXED-height row: an unsized image is stretched to
        // the cell's inner box — the column inside its rules (no default
        // padding) by the row height inside its rules (probed: the 200×107 px
        // logo in a 60.42 pt column of a 12.96 pt row draws 60.42 × 10.96).
    }

    /// <summary>The unsized image's natural point size: a vector source answers with the
    /// size its root declares (a 300 x 300 SVG is 300 x 300 pt, whatever pixel canvas the
    /// rasteriser chose), a raster with its pixel size read as points.</summary>
    private static (double w, double h)? NaturalCellImageSizePt(RowPlanImageState ri)
    {
        if (ri.svgData && ImageRasterizer.SvgRootSizePt(ri.rawBytes!) is { } declared) return declared;
        return TryGetCellImageSizePt(ri.imgBytes);
    }

    /// <summary>Read the image bytes, note SVG sources and the available width; a natural-size image that fits needs no more.</summary>
    private bool? ResolveImageSource(RowPlanImageState ri, Image cellImg, RowPlanColumnState pc, RowPlanState rp, int col, double[] colWidths, double svgFillHeight)
    {
        ri.rawBytes = ReadRawImageBytes(cellImg);
        if (ri.rawBytes is null) return true;
        ri.svgSource = cellImg.FileType == ImageFileType.Svg;
        ri.svgData = ri.rawBytes.Length > 0 && IsSvg(cellImg, ri.rawBytes);
        ri.imgXOffset = 0;
        ri.imgBoxHeight = 0;
        ri.imgAvailWidth = pc.availWidth;
        if (GeneratorDialect && !XmlGeneratorModel && LastColBoxOverhang > 0
            && col + Math.Max(1, Math.Min(pc.cell.ColSpan, colWidths.Length - col))
               >= colWidths.Length)
            ri.imgAvailWidth += LastColBoxOverhang;
        // A vector source with NO intrinsic size (viewBox-only or bare root)
        // fills the space it sits in: the full column footprint wide, and
        // down from the row top to the page's bottom content margin. The
        // artwork stretches to that box regardless of viewBox aspect — a
        // circle in the resulting tall cell renders as a portrait ellipse.
        // Explicit root width/height (or Fix*) sizing keeps the paths below.
        if (cellImg.FixWidth <= 0 && cellImg.FixHeight <= 0
            && ri.svgData && svgFillHeight > 0 && SvgLacksIntrinsicSize(ri.rawBytes))
        {
            ri.dispW = pc.cellWidth;
            ri.dispH = svgFillHeight;
            ri.imgXOffset = -pc.padLeft;
            var sized = ImageRasterizer.RasterizeSvgOnPageCanvas(ri.rawBytes);
            if (sized is null) return true;
            AddCellImage(rp.plan, col, new CellImage
            {
                Data = sized, Width = ri.dispW, Height = ri.dispH, Align = cellImg.HorizontalAlignment,
                XOffset = ri.imgXOffset,
                FillsBand = true,
                LineOffset = pc.lines.Count,
            });
            // Reserve lines summing EXACTLY to the fill height (n lines of
            // dispH/n each, n = how many default-leading lines fit) so the
            // row bottom lands on the page's bottom content margin instead
            // of a line-quantised
            // overshoot.
            var fillLines = Math.Max(1, (int)Math.Floor(ri.dispH / (pc.defaultFontSize * 1.2)));
            var fillLineH = ri.dispH / fillLines;
            Consider(rp, fillLineH, fillLineH);
            for (var k = 0; k < fillLines; k++)
                pc.lines.Add(new CellLine { Text = "", FontSize = fillLineH / 1.2, ImgReserve = true });
            return true;
        }
        return null;
    }
}
