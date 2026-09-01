using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private void StampImage(StampParagraphsState hf, Image img)
    {
        var si = new StampImageState();
        si.imgData = null;
        if (!LoadStampImageData(hf, si, img)) return;

        MeasureStampImageBox(hf, si, img);
        if (hf.isHeader)
        {
            // Header: image top edge at current y, growing downward. The FIRST
            // paragraph's image starts at the band top itself — the font-size
            // drop in y is a text baseline allowance, and an image has no baseline.
            var boxTop = (hf.firstParagraph ? hf.pageHeight - hf.mTop : hf.y) - si.boxOffY;
            si.rect = new Rectangle(si.imgX, boxTop - si.imgH, si.imgX + si.imgW, boxTop);
            hf.y = boxTop - si.imgH - si.boxOffY - 4;
        }
        else if (hf.probedFooterBand)
        {
            // Probed band: the image's TOP edge seats at the running band y
            // and the stack continues immediately below it (no gap — the
            // first text line's top is the image's bottom edge).
            var top = hf.y - si.boxOffY;
            si.rect = new Rectangle(si.imgX, top - si.imgH, si.imgX + si.imgW, top);
            hf.y = top - si.imgH - si.boxOffY;
        }
        else
        {
            // Footer: image bottom edge at the bottom margin, growing upward.
            var bottom = hf.mBottom + si.boxOffY;
            si.rect = new Rectangle(si.imgX, bottom, si.imgX + si.imgW, bottom + si.imgH);
            hf.y = bottom + si.imgH + si.boxOffY + 4;
        }
        hf.firstParagraph = false;
        try { hf.page.AddImage(si.imgData!, si.rect); }
        catch (ArgumentException) { return; }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private void StampFloatingBox(StampParagraphsState hf, FloatingBox fb)
    {
        var hm = hf.page.PageInfo?.Margin;
        var leftMargin = hm?.LeftTouched == true ? hm.Left : 90;
        var savedLeft = fb.Left;
        var savedMode = fb.PositioningMode;
        // A fixed-height header box crams its content: a nested
        // table's rows must fit inside the box height. Generator
        // tables default to 2 pt vertical cell padding, which would push
        // later rows below a small box, so tighten a nested table's cell
        // padding to zero (only when the caller left it at the default)
        // while rendering this box, then restore it. Scoped to a header /
        // footer FloatingBox so ordinary body tables are unaffected.
        var tightened = new List<Table>();
        if (fb.Height > 0)
        {
            foreach (var inner in fb.Paragraphs)
            {
                if (inner is Table t && t.DefaultCellPadding is null)
                {
                    t.DefaultCellPadding = new MarginInfo(0, 0, 0, 0);
                    tightened.Add(t);
                }
            }
        }
        fb.Left = savedLeft + leftMargin;
        fb.PositioningMode = ParagraphPositioningMode.Absolute;
        hf.page.AddFloatingBox(fb);
        fb.Left = savedLeft;
        fb.PositioningMode = savedMode;
        foreach (var t in tightened) t.DefaultCellPadding = null;
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private void StampTable(StampParagraphsState hf, Table tbl)
    {
        // Header/footer TABLES belong to the page generator: on a
        // static (imported, never laid-out) page the table stays
        // undrawn while sibling text/HTML fragments still render.
        if (!hf.tableContent) return;
        // $p/$P placeholders inside the band table's cell texts resolve
        // per page. The SAME table object renders on every page, so
        // substitute segment-by-segment and restore after the build.
        var macroSwaps = SubstituteCellMacros(tbl, hf.document, hf.pageNumber);
        // XML band tables: a header's background bleeds to the full page
        // width; an auto-fit footer band sits inside the generator's
        // default 90 pt band margins (both measured off the era template).
        if (tbl.XmlGeneratorModel)
        {
            if (hf.isHeader) tbl.XmlBandBleedWidth = hf.page.Width;
            else if (tbl.ColumnAdjustment == ColumnAdjustment.AutoFitToWindow
                     && string.IsNullOrWhiteSpace(tbl.ColumnWidths)
                     && tbl.FlowLeftOffset == 0)
                tbl.FlowLeftOffset = DefaultBandMargin;
        }
        // A header table anchors at the top margin itself — the running y
        // already carries the first text line's font-size inset, which
        // applies to text baselines, not to a table's top edge. The
        // header's left margin becomes the table's flow offset.
        var tableTop = hf.isHeader ? hf.pageHeight - hf.mTop : hf.y;
        // Per-page working clones of interactive fields in a footer table:
        // the SAME footer renders on every page, and each page must carry
        // its own field + widget (one AcroForm field per page, all at the
        // same footer rectangle).
        List<(Cell cell, int idx, Aspose.Pdf.Forms.CheckboxField proto, Aspose.Pdf.Forms.CheckboxField clone)>? fieldSwaps = null;
        var footerBottomBound = 36.0;
        PlaceFooterTable(hf, tbl, ref fieldSwaps, ref tableTop, ref footerBottomBound);
        if (tbl.FlowLeftOffset == 0) tbl.FlowLeftOffset = hf.mLeft;
        if (!hf.isHeader) tbl.SuppressBaselineLift = true;
        var contents = tbl.BuildMultiPage(hf.page, tableTop, hf.isHeader ? 36 : footerBottomBound);
        if (fieldSwaps is not null)
        {
            foreach (var (c, idx, proto, clone) in fieldSwaps)
            {
                c.Paragraphs[idx] = proto;
                // Register the placed clone in the AcroForm for THIS page (the
                // widget rect was set by the table's layout pass).
                hf.document!.Form.Add(clone, hf.page.Number);
            }
        }
        if (contents.Count > 0) hf.page.AddContentStream(contents[0]);
        // Blit cell images collected for this page — only the flow
        // dispatcher applied LastImageDraws, so a header-table logo
        // (e.g. an SVG rasterised into a cell) was silently dropped.
        if (tbl.LastImageDraws.Count > 0)
            foreach (var (data, rect) in tbl.LastImageDraws[0])
                try { hf.page.AddImage(data, rect); }
                catch (ArgumentException) { /* unsupported format: skip */ }
        if (tbl.LastGraphDraws.Count > 0)
            foreach (var g in tbl.LastGraphDraws[0])
                hf.page.AddContentStream(g);
        RestoreCellMacros(macroSwaps);
        hf.y -= tbl.LastRenderedHeight;
    }

    /// <summary>Seats a footer table above the page's bottom margin and swaps its check boxes for the page's own.</summary>
    private void PlaceFooterTable(StampParagraphsState hf, Table tbl, ref List<(Cell cell, int idx, Aspose.Pdf.Forms.CheckboxField proto, Aspose.Pdf.Forms.CheckboxField clone)>? fieldSwaps, ref double tableTop, ref double footerBottomBound)
    {
        if (!hf.isHeader)
        {
            var hasFields = false;
            foreach (var r in tbl.Rows)
            {
                foreach (var c in r.Cells)
                {
                    foreach (var p in c.Paragraphs)
                        if (p is Aspose.Pdf.Forms.CheckboxField) { hasFields = true; break; }
                    if (hasFields) break;
                }
                if (hasFields) break;
            }
            if (hasFields)
            {
                // Footer tables carrying form fields are BOTTOM-anchored: the
                // table's bottom edge sits on the footer band line —
                // Margin.Bottom when set, else the default band bottom of 60
                // (widget rect (90,60)-(100,70) with a 14 pt caption baseline
                // at 60.6).
                var bandBottom = Margin.BottomTouched ? Margin.Bottom : 60.0;
                tableTop = bandBottom + tbl.GetHeight(hf.page);
                if (hf.document is not null)
                {
                    foreach (var r in tbl.Rows)
                        foreach (var c in r.Cells)
                            for (var pidx = 0; pidx < c.Paragraphs.Count; pidx++)
                                if (c.Paragraphs[pidx] is Aspose.Pdf.Forms.CheckboxField proto)
                                {
                                    var clone = new Aspose.Pdf.Forms.CheckboxField(hf.document)
                                    {
                                        Width = proto.Width,
                                        Height = proto.Height,
                                        Style = proto.Style,
                                    };
                                    (fieldSwaps ??= new()).Add((c, pidx, proto, clone));
                                    c.Paragraphs[pidx] = clone;
                                }
                }
            }
            else
            {
                // A plain FOOTER table hangs BELOW the page's content
                // bottom-margin line and grows downward into the margin (its
                // top edge sits at the page margin, not at the footer's own
                // Margin.Bottom), so pass an extended bottom bound to keep
                // the whole table on this page.
                tableTop = hf.page.PageInfo?.Margin is { BottomTouched: true } pbm ? pbm.Bottom
                    : hf.document?.PageInfo?.Margin is { BottomTouched: true } dbm ? dbm.Bottom
                    : 72;
                // The footer's own Margin.Top is the gap between the page's
                // bottom margin line and the table's top edge (probed: a
                // 14 pt Margin.Top on a 72 pt page margin seats the table
                // top at 734 on a 792 pt page).
                if (Margin.TopTouched && Margin.Top > 0) tableTop -= Margin.Top;
                footerBottomBound = -hf.pageHeight;
            }
        }
    }

    /// <summary>The image's drawn size, its box and its horizontal seat in the band.</summary>
    private void MeasureStampImageBox(StampParagraphsState hf, StampImageState si, Image img)
    {
        si.imgW = img.FixWidth > 0 ? img.FixWidth
            : hf.page.Width - hf.mLeft - (Margin.RightTouched ? Margin.Right : 20);
        if (img.FixHeight > 0)
            si.imgH = img.FixHeight;
        else
        {
            // No explicit height: preserve the image's aspect ratio rather than
            // defaulting to a square (a wide footer bar would otherwise render as a
            // huge block covering the page). The probed band instead takes the
            // image's natural PIXEL height as points at the full band width
            // (a 1000x10 px bar draws 610x10 — stretched, not aspect-fit).
            try
            {
                var probe = new ImageStamp(new System.IO.MemoryStream(si.imgData!));
                si.imgH = hf.probedFooterBand ? probe.PixelHeight
                    : probe.PixelWidth > 0 ? si.imgW * probe.PixelHeight / (double)probe.PixelWidth : si.imgW;
            }
            catch { si.imgH = si.imgW; }
        }
        si.boxW = si.imgW;
        si.boxH = si.imgH;
        si.boxOffX = 0;
        si.boxOffY = 0;
        if (si.svgViewW > 0 && si.svgViewH > 0 && si.boxW > 0 && si.boxH > 0)
        {
            var fit = Math.Min(si.boxW / si.svgViewW, si.boxH / si.svgViewH);
            si.imgW = si.svgViewW * fit;
            si.imgH = si.svgViewH * fit;
            si.boxOffX = (si.boxW - si.imgW) / 2;
            si.boxOffY = (si.boxH - si.imgH) / 2;
        }

        si.imgRight = Margin.RightTouched ? Margin.Right
            : hf.page.PageInfo?.Margin is { RightTouched: true } prm ? prm.Right
            : hf.document?.PageInfo?.Margin is { RightTouched: true } drm ? drm.Right
            : DefaultBandMargin;
        si.boxX = img.HorizontalAlignment switch
        {
            HorizontalAlignment.Right => hf.page.Width - si.imgRight - si.boxW,
            HorizontalAlignment.Center => (hf.page.Width - si.boxW) / 2,
            HorizontalAlignment.Left => hf.mLeft,
            _ => hf.x,
        };
        si.imgX = si.boxX + si.boxOffX;
    }

    /// <summary>Reads the image's bytes (a file or a stream) and rasterises an SVG; false when there is nothing to draw.</summary>
    private bool LoadStampImageData(StampParagraphsState hf, StampImageState si, Image img)
    {
        if (img.ImageStream is not null)
        {
            var pos = img.ImageStream.CanSeek ? img.ImageStream.Position : -1L;
            // Rewind when seekable: callers commonly hand us a stream after
            // reading dimensions with `new Bitmap(stream)`, which leaves the
            // position at end-of-stream. Without this the image silently disappears.
            if (img.ImageStream.CanSeek) img.ImageStream.Position = 0;
            using var imgMem = new System.IO.MemoryStream();
            img.ImageStream.CopyTo(imgMem);
            si.imgData = imgMem.ToArray();
            if (pos >= 0) img.ImageStream.Position = pos;
        }
        else
        {
            si.imgData = img.ReadSourceBytes();
        }
        if (si.imgData is null || si.imgData.Length == 0) return false;

        si.svgViewW = 0;
        si.svgViewH = 0;
        if (Table.IsSvg(img, si.imgData))
        {
            var raster = ImageRasterizer.RasterizeSvg(si.imgData, out var vw, out var vh);
            if (raster is not null) { si.imgData = raster; si.svgViewW = vw; si.svgViewH = vh; }
        }
        return true;
    }
}
