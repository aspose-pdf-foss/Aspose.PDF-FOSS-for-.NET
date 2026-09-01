using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;
namespace Aspose.Pdf;

public sealed partial class Document : IDisposable
{
    private void LayoutGraphParagraph(Aspose.Pdf.Drawing.Graph graph, FlowLayout flow, Page page, double marginLeft, double marginTop, double marginBottom)
    {
        // Shapes carry graph-local coordinates with the origin at the
        // graph box's bottom-left corner. Translate the rendered stream
        // so that corner lands at the correct page position.
        double originX, originY;
        if (!graph.IsChangePosition && !graph.PositionAssigned)
        {
            // Absolute placement: Left from the left edge, Top from the top edge,
            // outside the flow.
            originX = graph.Left;
            originY = flow.CurrentPage.Height - graph.Top - graph.Height;
            flow.CurrentPage.AddContentStream(graph.Build(flow.CurrentPage, originX, originY));
            EmitGraphLink(graph, flow, originX, originY, deferred: false);
            return;
        }

        // Flow placement, per axis: an assigned Left anchors the box's left edge
        // at margin + Left and re-anchors the flow there for everything that
        // follows; an assigned Top seats the box top at content top - Top. An
        // unassigned axis flows: the left edge is the flow's current left edge
        // (a previous anchor included), the top is the cursor. Repeated graphs
        // with the same Left/Top therefore overlay, and the cursor continues
        // under the box either way (measured 2026-08-23).
        if (!graph.TopAssigned)
        {
            // Push to a fresh page if it doesn't fit below the cursor (but
            // never when the cursor is already at the page top — an oversized
            // graph still renders on the current page rather than looping).
            if (flow.CurrentY - graph.Height < flow.BottomMargin
                && flow.CurrentY < flow.ContentTop - 0.5)
                flow.BreakPageIfContent();
        }
        if (graph.LeftAssigned)
            flow.AnchorLeft = marginLeft + graph.Left;
        originX = flow.CurrentLeft;
        var boxTop = graph.TopAssigned ? flow.ContentTop - graph.Top : flow.CurrentY;
        originY = boxTop - graph.Height;
        flow.InjectContentAtCursor(graph.Build(flow.CurrentPage, originX, originY));
        EmitGraphLink(graph, flow, originX, originY, deferred: true);
        flow.MoveCursorTo(originY);
        // The graph's Title is a text paragraph flowed directly under the box
        // (measured: the title's line box starts at the box bottom, at the
        // flow's left edge, in the title's own text state).
        if (graph.Title is { } title && !string.IsNullOrEmpty(title.Text))
            flow.WriteTextFragment(title);
    }

    /// <summary>A Graph's paragraph Hyperlink becomes one Link annotation over the
    /// whole graph box.</summary>
    private static void EmitGraphLink(Aspose.Pdf.Drawing.Graph graph, FlowLayout flow,
        double originX, double originY, bool deferred)
    {
        if (graph.Hyperlink is null) return;
        var rect = new Rectangle(originX, originY, originX + graph.Width, originY + graph.Height);
        if (deferred) flow.QueueLink(rect, graph.Hyperlink);
        else flow.EmitLinkNow(flow.CurrentPage, rect, graph.Hyperlink);
    }

    private void LayoutHeadingParagraph(Heading heading, FlowLayout flow, Page page, System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries, PageLayoutState pl, Dictionary<int, int> headingAutoCounters, ref string? fontName, double marginLeft, double marginRight)
    {
        // A heading whose TocPage is this page is a TOC entry authored
        // directly on the TOC page — render its TOC line AT the flow
        // cursor (paragraph order), not as plain content text, so page
        // content authored before it (e.g. spacer fragments) stays
        // above it. Headings on other pages still render as their
        // content heading (and also appear in the TOC list).
        if (ReferenceEquals(heading.TocPage, page))
        {
            var tocIdx = tocEntries.FindIndex(e => ReferenceEquals(e.h, heading));
            if (tocIdx >= 0)
            {
                var yAfter = RenderTocEntry(pl, heading, tocEntries[tocIdx].pageIdx, flow.CurrentY);
                flow.AdvanceY(flow.CurrentY - yAfter);
            }
            return;
        }

        fontName ??= Table.RegisterFont(page);
        // The heading's own Margin.Top is leading reserved above it —
        // each heading drops by it before its line box.
        if (heading.Margin?.Top > 0) flow.AdvanceY(heading.Margin.Top);
        var headingPage = flow.CurrentPage;
        var headingY = flow.CurrentY;
        // Auto-sequenced headings get a formatted number prefix
        // (roman/alpha/decimal per Style), counting per level. The
        // DEFAULT style (None) still numbers in arabic — a plain
        // IsAutoSequence heading prints as "1  Heading 0"
        // — and the number is followed by
        // TWO spaces (no dot), the standard
        // prefix fragment ("1  " at the margin).
        var headingPrefix = NextHeadingPrefix(headingAutoCounters, heading);
        var (content, height) = heading.Build(headingPage, marginLeft, headingY, fontName, headingPrefix);
        // A heading that no longer fits above the bottom margin moves to the
        // next page whole (Heading.Build draws at the supplied Y verbatim, so
        // the cursor alone cannot spill it) — a long run of headings paginates
        // instead of piling below the page foot. Re-record the position so a
        // TOC leader resolves to the page it really landed on.
        if (headingY - height < flow.BottomMargin
            && height <= flow.ContentTop - flow.BottomMargin + 0.5)
        {
            flow.ForceNewPage();
            flow.RecordPosition(heading);
            headingY = flow.CurrentY;
            (content, height) = heading.Build(flow.CurrentPage, marginLeft, headingY, fontName, headingPrefix);
        }
        flow.InjectContentAtCursor(content);

        // Create a link annotation for the heading
        var destPage = heading.DestinationPage;
        if (destPage is not null)
        {
            var linkRect = new Rectangle(marginLeft, headingY - height, headingPage.Width - marginRight, headingY);
            var destPageIdx = 0;
            for (int pi = 1; pi <= PageCount; pi++)
            {
                if (Pages.At(pi) == destPage) { destPageIdx = pi; break; }
            }
            if (destPageIdx > 0)
            {
                // Link via a GoTo action with an explicit XYZ destination at
                // the target page's upper-left corner, so Annotation.Action
                // resolves to a GoToAction whose Destination exposes the page
                // and coordinates (a /Dest [page /Fit] form leaves Action null).
                // Destination coordinates are in unrotated page space; map the
                // visual top-left (0, rotated-height) back through the page's
                // rotation so it lands correctly on rotated pages too.
                var destRect = destPage.GetPageRect(true);
                var (destLeft, destTop) = destPage.RotationMatrix
                    .InverseTransformPoint(0, destRect.Height);
                headingPage.Annotations.AddLinkAnnotation(linkRect,
                    new Aspose.Pdf.Annotations.GoToAction(
                        new Aspose.Pdf.Annotations.XYZExplicitDestination(
                            destPageIdx, destLeft, destTop, 0)));
            }
        }

        // Mirror the heading into the document outlines when its
        // TOC page asks for it (Heading.TocPage.TocInfo.CopyToOutlines).
        // A synthetic TOC asserts the saved PDF carries a flat
        // list of bookmarks, one per heading.
        if (heading.TocPage?.TocInfo?.CopyToOutlines == true
            && heading.Segments.Count > 0)
        {
            var headingPageIdx = 0;
            for (int pi = 1; pi <= PageCount; pi++)
            {
                if (Pages.At(pi) == headingPage) { headingPageIdx = pi; break; }
            }
            if (headingPageIdx > 0)
            {
                var item = new OutlineItemCollection(Outlines)
                {
                    Title = heading.Segments[1].Text ?? string.Empty,
                };
                item.Action = new Aspose.Pdf.Annotations.GoToAction(
                    new Aspose.Pdf.Annotations.XYZExplicitDestination(
                        headingPageIdx, marginLeft, headingY, 0));
                Outlines.Add(item);
            }
        }

        // A heading line consumes exactly its own box (font size per
        // line) — the next paragraph chains one of ITS
        // OWN font sizes below the heading's bottom, with no extra
        // padding (758 → 748 for a 12 pt heading followed by 10 pt
        // text; the old +4 pushed every following line down) — plus
        // the heading's own Margin.Bottom when the caller set one.
        flow.AdvanceY(height);
        if (heading.Margin?.Bottom > 0) flow.AdvanceY(heading.Margin.Bottom);
    }

    private void LayoutImageParagraph(Image img, FlowLayout flow, Page page, ref double pendingInlineLineHeight, double marginLeft, double marginRight, double marginTop, double marginBottom)
    {
        byte[]? imgData = null;
        if (img.ImageStream is not null)
        {
            var pos = img.ImageStream.CanSeek ? img.ImageStream.Position : -1L;
            // Rewind when seekable: callers commonly hand us a stream after
            // reading dimensions with `new Bitmap(stream)`, which leaves the
            // position at end-of-stream. Without this the image silently disappears.
            if (img.ImageStream.CanSeek) img.ImageStream.Position = 0;
            using var imgMem = new System.IO.MemoryStream();
            img.ImageStream.CopyTo(imgMem);
            imgData = imgMem.ToArray();
            if (pos >= 0) img.ImageStream.Position = pos;
        }
        else
        {
            imgData = img.ReadSourceBytes();
        }
        if (imgData is null) return;

        // ImageFileType.Base64: the stream carries base64 TEXT (optionally a
        // full data:image/...;base64, URI), not raw image bytes — decode it
        // first or the raster embed below silently drops the image.
        if (img.FileType == ImageFileType.Base64 && imgData.Length > 0)
        {
            try
            {
                var s64 = System.Text.Encoding.ASCII.GetString(imgData).Trim();
                var comma = s64.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    ? s64.IndexOf(',') : -1;
                if (comma >= 0) s64 = s64[(comma + 1)..];
                imgData = System.Convert.FromBase64String(s64);
            }
            catch (FormatException) { /* not base64 after all: keep the raw bytes */ }
        }

        // An SVG source rasterises through the built-in SVG converter first —
        // the raster embed path below can't decode vector data and the image
        // would drop silently from the flow. The natural size is the SVG
        // viewport in points (attrs read 1:1) so the layout below doesn't
        // read raster pixels as points.
        double svgNatW = 0, svgNatH = 0;
        if (XImageCollection.IsSvg(imgData))
        {
            // With a Fix box the artwork is letterboxed (aspect-fit,
            // centred) onto a canvas of the box's aspect;
            // the box itself may then stretch/clamp freely.
            var svgPng = img.FixWidth > 0 && img.FixHeight > 0
                ? ImageRasterizer.RasterizeSvgOnCanvas(imgData, img.FixWidth, img.FixHeight)
                : ImageRasterizer.RasterizeSvg(imgData, out svgNatW, out svgNatH);
            if (svgPng is not null)
            {
                if (img.FixWidth > 0 && img.FixHeight > 0)
                    (svgNatW, svgNatH) = (img.FixWidth, img.FixHeight);
                imgData = svgPng;
            }
            else { svgNatW = 0; svgNatH = 0; }
        }

        // IsBlackWhite fast path: a bilevel Group 4 TIFF embeds its existing
        // CCITT strips directly (no re-encode), giving the compact 1-bit output
        // the property promises instead of a bulky re-rasterised copy.
        if (img.IsBlackWhite
            && IO.CcittTiffExtractor.TryExtract(imgData) is { Count: > 0 } g4Frames)
        {
            var availWbw = page.Width - marginLeft - marginRight;
            var availHbw = page.Height - marginTop - marginBottom;
            for (int fi = 0; fi < g4Frames.Count; fi++)
            {
                var g4 = g4Frames[fi];
                double imgWbw, imgHbw;
                if (img.FixWidth > 0 || img.FixHeight > 0)
                {
                    // A Fix dimension counts on its own and the OTHER axis keeps the
                    // source's own pixel measure — see LoadFlowImage.
                    imgWbw = Math.Min(img.FixWidth > 0 ? img.FixWidth : g4.Width, availWbw);
                    imgHbw = Math.Min(img.FixHeight > 0 ? img.FixHeight : g4.Height, availHbw);
                }
                else
                {
                    // Pixels map 1:1 to points (optionally scaled by ImageScale),
                    // clamped per-axis into the content box.
                    var scaleBw = img.ImageScale > 0 ? img.ImageScale : 1.0;
                    imgWbw = Math.Min(g4.Width * scaleBw, availWbw);
                    imgHbw = Math.Min(g4.Height * scaleBw, availHbw);
                }
                Page targetPageBw;
                double yTopBw;
                if (fi == 0 && flow.CurrentY - imgHbw >= marginBottom)
                {
                    targetPageBw = flow.CurrentPage;
                    yTopBw = flow.CurrentY;
                }
                else
                {
                    flow.Commit();
                    targetPageBw = Pages.Add();
                    targetPageBw.MediaBox = new Rectangle(0, 0, page.Width, page.Height);
                    Table.RegisterFont(targetPageBw);
                    yTopBw = page.Height - marginTop;
                }
                var rectBw = new Rectangle(marginLeft, yTopBw - imgHbw,
                                           marginLeft + imgWbw, yTopBw);
                targetPageBw.AddCcittImage(g4.Data, g4.Width, g4.Height, g4.BlackIs1, rectBw);
                if (img.Hyperlink is not null)
                    targetPageBw.EmitHyperlinkAnnotation(rectBw, img.Hyperlink);
                if (ReferenceEquals(targetPageBw, flow.CurrentPage))
                    flow.AdvanceY(imgHbw);
                else
                    flow.ResetToTopOfNextPage();
            }
            return;
        }

        // Page.AddImage embeds JPEG / PNG / raw RGB directly. Other raster
        // formats (TIFF / BMP / GIF, possibly multi-frame) are decoded with the
        // platform image codec to one PNG per frame; each frame is placed on its
        // own page, matching how a multi-page TIFF expands into multiple pages.
        var hdr0 = imgData.Length > 0 ? imgData[0] : (byte)0;
        var hdr1 = imgData.Length > 1 ? imgData[1] : (byte)0;
        // Baseline JPEG and PNG embed directly. Progressive JPEG is routed
        // through the codec re-encode (the embedded-image decoder is
        // baseline-only, so a progressive frame would render blank).
        var isJpeg = hdr0 == 0xFF && hdr1 == 0xD8 && !IsProgressiveJpeg(imgData);
        var isPng = imgData.Length >= 4 && hdr0 == 0x89 && hdr1 == 0x50
                    && imgData[2] == 0x4E && imgData[3] == 0x47;
        // JPEG 2000 (.jp2/.jpx) — the platform codec can't decode it, so keep the
        // raw bytes and let Page.AddImage route them through the built-in JPXDecode
        // decoder (System.Drawing returns null for these, which used to drop the image).
        var isJpx = (imgData.Length >= 12 && hdr0 == 0x00 && hdr1 == 0x00
                     && imgData[2] == 0x00 && imgData[3] == 0x0C && imgData[4] == 0x6A
                     && imgData[5] == 0x50 && imgData[6] == 0x20 && imgData[7] == 0x20)
                    || (imgData.Length >= 4 && hdr0 == 0xFF && hdr1 == 0x4F
                        && imgData[2] == 0xFF && imgData[3] == 0x51);
        var frames = isJpeg || isPng || isJpx
            ? new System.Collections.Generic.List<byte[]> { imgData }
            : TryDecodeImageFramesAsPng(imgData);
        if (frames is null || frames.Count == 0) return;

        // A genuinely bilevel source embeds losslessly as a compact 1-bit
        // image instead of an 8-bit re-encode (a scanned/fax page would
        // otherwise balloon the output).
        var embedBlackWhite = img.IsBlackWhite || ImageStamp.IsBilevelSource(imgData);

        var availW = page.Width - marginLeft - marginRight;
        var availH = page.Height - marginTop - marginBottom;
        // The image's own top margin is space reserved above it: the cursor
        // drops by it before the image seats (measured: a 50 pt Margin.Top
        // puts the image 50 pt under the preceding paragraph); the bottom
        // margin is the gap after it.
        if (img.Margin?.Top > 0) flow.AdvanceY(img.Margin.Top);
        for (int frameIdx = 0; frameIdx < frames.Count; frameIdx++)
        {
            var frameData = frames[frameIdx];
            double imgW, imgH;
            var haveNat = TryGetImageNaturalSizePt(frameData, img.IsApplyResolution,
                              out var fixNatW, out var fixNatH)
                          && fixNatW > 0 && fixNatH > 0;
            if (img.FixWidth > 0 || img.FixHeight > 0)
            {
                // EITHER Fix dimension counts on its own and the OTHER axis keeps the
                // source's own pixel measure — probed 2026-08-26: a 240x60 picture
                // under FixHeight 20 draws 240x20, under FixWidth 100 draws 100x60.
                // (Spanning the band on the unset axis stretched every lone-Fix
                // picture to the content width.)
                imgW = img.FixWidth > 0 ? img.FixWidth : haveNat ? fixNatW : availW;
                imgH = img.FixHeight > 0 ? img.FixHeight : haveNat ? fixNatH : availH;
                // A Fix box larger than the content band is squashed to the
                // band on that axis (never clipped or spilled to a fresh
                // page): a 662 pt FixHeight on a 451 pt landscape band
                // draws as a 451 pt image, width kept.
                imgW = Math.Min(imgW, availW);
                imgH = Math.Min(imgH, availH);
            }
            else if (svgNatW > 0 && svgNatH > 0)
            {
                // Vector (SVG) source: the authored viewport size in points
                // (1:1, rounded), each axis independently clamped into the
                // content box — a too-wide chart is squeezed
                // to the page width while keeping vertical scale 1:1.
                var scale = img.ImageScale > 0 ? img.ImageScale : 1.0;
                imgW = Math.Min(Math.Round(svgNatW) * scale, availW);
                imgH = Math.Min(Math.Round(svgNatH) * scale, availH);
            }
            else if (haveNat)
            {
                var natWpt = fixNatW;
                var natHpt = fixNatH;
                // No explicit size: start from the image's intrinsic dimensions
                // (pixels mapped 1:1 to points unless IsApplyResolution honours the
                // embedded DPI), optionally scaled by ImageScale.
                var scale = img.ImageScale > 0 ? img.ImageScale : 1.0;
                imgW = natWpt * scale;
                imgH = natHpt * scale;
                if (img.IsApplyResolution)
                {
                    // Resolution-aware: fit to the content width preserving the
                    // aspect ratio (the IsApplyResolution contract).
                    if (imgW > availW && imgW > 0)
                    {
                        imgH *= availW / imgW;
                        imgW = availW;
                    }
                }
                else
                {
                    // Default: an oversized image is fitted into the content area by
                    // clamping each axis independently to the available width/height
                    // -- no aspect preservation.
                    imgW = Math.Min(imgW, availW);
                    imgH = Math.Min(imgH, availH);
                }
            }
            else
            {
                imgW = availW;
                imgH = availH;
            }

            Page targetPage;
            double yTop;
            // The first frame follows the flow; every extra frame starts a fresh page.
            // An image too tall for ANY page (taller than the full content band)
            // stays on the current page and lets the page clip it — pushing it to
            // a fresh page would still not fit and loses the flow position.
            var fitsNowhere = imgH > page.Height - marginTop - marginBottom;
            if (frameIdx == 0 &&
                (flow.CurrentY - imgH >= marginBottom
                 || (fitsNowhere && flow.CurrentY >= page.Height - marginTop - 1e-6)))
            {
                targetPage = flow.CurrentPage;
                yTop = flow.CurrentY;
            }
            else
            {
                flow.Commit();
                targetPage = Pages.Add();
                targetPage.MediaBox = new Rectangle(0, 0, page.Width, page.Height);
                Table.RegisterFont(targetPage);
                yTop = page.Height - marginTop;
            }
            // Honour the image's horizontal alignment within the content
            // box; without this every image is pinned to the left margin
            // regardless of HorizontalAlignment.Right / Center.
            double imgX = img.HorizontalAlignment switch
            {
                HorizontalAlignment.Right => page.Width - marginRight - imgW,
                HorizontalAlignment.Center => marginLeft + (availW - imgW) / 2,
                _ => marginLeft,
            };
            var rect = new Rectangle(imgX, yTop - imgH,
                                     imgX + imgW, yTop);
            try
            {
                targetPage.AddImage(frameData, rect, embedBlackWhite);
            }
            catch (ArgumentException)
            {
                continue;
            }
            // A Hyperlink on the image covers its placed rectangle with a
            // Link annotation, the same way hyperlinked text runs do.
            if (img.Hyperlink is not null)
                targetPage.EmitHyperlinkAnnotation(rect, img.Hyperlink);
            if (ReferenceEquals(targetPage, flow.CurrentPage))
            {
                // Inline images keep the cursor on the shared line and only
                // record their height; the line is closed (cursor dropped) by
                // the next block image or the end-of-flow flush below.
                if (img.IsInLineParagraph && frames.Count == 1)
                    pendingInlineLineHeight = Math.Max(pendingInlineLineHeight, imgH);
                else
                    flow.AdvanceY(imgH);
            }
            else
                flow.ResetToTopOfNextPage();
        }
        if (img.Margin?.Bottom > 0) flow.AdvanceY(img.Margin.Bottom);
    }

    /// <summary>Read a generator <see cref="Image"/>'s bytes and the size it draws
    /// at in a flow of <paramref name="availW"/> x <paramref name="availH"/> points:
    /// a Fix dimension counts on its own and the OTHER axis keeps the source's own
    /// pixel measure (probed 2026-08-26: a 240x60 picture under FixHeight 20 draws
    /// 240x20, under FixWidth 100 draws 100x60 — the unset axis is neither scaled to
    /// preserve the aspect nor stretched to the band); with neither set the pixels
    /// map 1 px = 1 pt, scaled by <see cref="Image.ImageScale"/>. A zero
    /// <paramref name="availW"/>/<paramref name="availH"/> means "no band to squash
    /// into" — a note band lets its picture overhang.</summary>
    private static bool LoadFlowImage(Image img, double availW, double availH,
        out byte[] data, out double w, out double h)
    {
        w = h = 0;
        byte[]? bytes;
        if (img.ImageStream is { } ist)
        {
            using var ms = new MemoryStream();
            if (ist.CanSeek) ist.Position = 0;
            ist.CopyTo(ms);
            bytes = ms.ToArray();
        }
        else
            bytes = img.ReadSourceBytes();
        if (bytes is null) { data = System.Array.Empty<byte>(); return false; }
        data = bytes;
        var haveNatural = TryGetImageNaturalSizePt(bytes, img.IsApplyResolution, out var natW, out var natH)
                          && natW > 0 && natH > 0;
        if (img.FixWidth > 0 || img.FixHeight > 0)
        {
            w = img.FixWidth > 0 ? img.FixWidth : haveNatural ? natW : availW;
            h = img.FixHeight > 0 ? img.FixHeight : haveNatural ? natH : availH;
        }
        else if (haveNatural)
        {
            var scale = img.ImageScale > 0 ? img.ImageScale : 1.0;
            w = natW * scale;
            h = natH * scale;
        }
        else { w = 100; h = 100; }
        if (availW > 0) w = Math.Min(w, availW);
        if (availH > 0) h = Math.Min(h, availH);
        return true;
    }

    private void LayoutTableParagraph(Table table, FlowLayout flow, Page page, HashSet<Table> renderedTables, List<(byte[] content, double width, double height)> overflowPages, Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages, double marginLeft, double marginTop)
    {
        if (!renderedTables.Add(table)) return;

        // Container-table unwrap: a table whose every row is one ColSpan
        // cell holding only Tables is a transparent wrapper — its
        // inner tables lay out as consecutive blocks (with
        // their own margins, whole blocks moving to the next page when
        // they don't fit) rather than being flattened into cell text.
        List<Table>? containerInners = null;
        if (table.Rows.Count > 0)
        {
            containerInners = new List<Table>();
            for (var ri = 0; ri < table.Rows.Count && containerInners is not null; ri++)
            {
                var wrapRow = table.Rows.At(ri);
                var wrapCell = wrapRow.Cells.Count == 1 ? wrapRow.Cells.At(0) : null;
                if (wrapCell is null || wrapCell.Paragraphs.Count == 0)
                { containerInners = null; break; }
                // A span-1 single cell only counts as a container when it is pure
                // chrome-less wrapping — one row, no borders, no background — so a
                // real bordered one-cell table keeps its cell rendering (the inner
                // grid then draws inside the visible cell box).
                if (wrapCell.ColSpan < 2
                    && !(table.Rows.Count == 1
                         && wrapCell.Border is null && wrapCell.BackgroundColor is null
                         && table.Border is null && table.DefaultCellBorder is null
                         && wrapRow.Border is null && wrapRow.DefaultCellBorder is null))
                { containerInners = null; break; }
                foreach (var ip in wrapCell.Paragraphs)
                {
                    if (ip is Table innerT) containerInners.Add(innerT);
                    else { containerInners = null; break; }
                }
            }
            if (containerInners is { Count: 0 }) containerInners = null;
        }
        if (containerInners is not null)
        {
            // The wrapper's own Top anchors its blocks that far below the page
            // top, as for any table; a block the anchor leaves no room for then
            // moves whole to the next page (the fit check below).
            if (table.Top > 0 && flow.CurrentY > page.LayoutFrameHeight - table.Top)
                flow.AdvanceY(flow.CurrentY - (page.LayoutFrameHeight - table.Top));
            foreach (var inner in containerInners)
            {
                renderedTables.Add(inner);
                inner.HtmlEngineMetrics = true;
                if (inner.Margin.Top > 0) flow.AdvanceY(inner.Margin.Top);
                var innerPage = flow.CurrentPage;
                inner.FlowLeftOffset = marginLeft;
                var innerSpillTop = PageInfo?.Margin is { TopTouched: true } idm ? idm.Top : marginTop;
                // Keep the block together: measure its one-page height and
                // move it whole to a fresh page when it doesn't fit here.
                inner.BuildMultiPage(innerPage, flow.ContentTop, flow.BottomMargin, measureOnly: true);
                var innerH = inner.LastRenderedHeight;
                var innerAvail = flow.CurrentY - flow.BottomMargin;
                var innerBudget = flow.ContentTop - flow.BottomMargin;
                if (innerH > innerAvail + 0.5 && innerH <= innerBudget + 0.5
                    && flow.CurrentY < flow.ContentTop - 0.5)
                    flow.ForceNewPage();
                var innerContents = inner.BuildMultiPage(innerPage, flow.CurrentY, flow.BottomMargin, innerSpillTop);
                var innerImages = inner.LastImageDraws;
                var innerGraphs = inner.LastGraphDraws;
                flow.InjectContentAtCursor(innerContents[0]);
                if (innerGraphs.Count > 0)
                    foreach (var gc in innerGraphs[0])
                        flow.InjectContentAtCursor(gc);
                if (!flow.HasOverflowed && innerImages.Count > 0)
                    foreach (var (data, rect) in innerImages[0])
                        innerPage.AddImage(data, rect);
                if (innerContents.Count == 1)
                {
                    flow.AdvanceY(inner.LastRenderedHeight);
                }
                else
                {
                    for (var pi = 1; pi < innerContents.Count - 1; pi++)
                    {
                        if (pi < innerImages.Count && innerImages[pi].Count > 0)
                            overflowImages[overflowPages.Count] = innerImages[pi];
                        overflowPages.Add((innerContents[pi], innerPage.Width, innerPage.Height));
                    }
                    var innerLastIdx = innerContents.Count - 1;
                    var innerSlot = flow.ContinueOnPrebuiltSpill(innerContents[innerLastIdx], inner.LastPageEndY);
                    if (innerLastIdx < innerImages.Count && innerImages[innerLastIdx].Count > 0)
                        overflowImages[innerSlot] = innerImages[innerLastIdx];
                }
                if (inner.Margin.Bottom > 0) flow.AdvanceY(inner.Margin.Bottom);
            }
            return;
        }

        // Report-band wrapper: a one-column table whose first row is an
        // HtmlFragment carrying only a title block and a <thead> caption
        // table, and whose second row's fragment is one big data <table> —
        // the escaped-attribute report shape. The band renders as
        // a page header REPEATED on every sheet (RepeatingRows),
        // the data table as text rows on a fixed grid: Times 11.25 on a
        // 14.25 pitch, wrap lines 11.25, the three value columns standing
        // at fixed x, a multi-line row's values centred on its lines.
        // All of it exact to 0.01 pt.
        if (table.RepeatingRowsCount >= 1 && table.Rows.Count == 2
            && RbTryParse(table) is { } rb)
        {
            const double rbTitleFs = 9.0;        // 12px body over the title block
            const double rbTitlePitch = 10.80;   // 1.2 em of the 12px body
            const double rbTitleBase1 = 13.44;   // first title baseline from the page top
            const double rbCaptionFs = 11.25;    // th { font-size: 15px }
            const double rbCaptionBase = 50.20;  // caption baseline from the page top
            const double rbCaptionX = 7.25;      // caption left inset
            const double rbDescX = 14.75;        // description column left
            const double rbRowFs = 11.25;        // data rows set at the th size
            const double rbRowPitch = 14.25;     // single-line row pitch
            const double rbWrapPitch = 11.25;    // a wrapped description's inner pitch
            const double rbFirstRowGap = 24.37;  // caption → first row, first sheet
            const double rbSpillRowGap = 16.87;  // caption → first row, later sheets
            const double rbBottomLimit = 837;    // page bottom content limit (margin 5)
            const double rbBlockSeam = 9.0;      // extra seam where a new data table opens
            var rbColX = new[] { 453.32, 496.63, 539.94 };
            // the description wraps in its own 49% column, whose box runs
            // a little past the first value column's text start:
            // 434.6 pt one-liners stay whole and everything wraps
            // from 443.1 pt up — the window's midpoint
            var rbWrapW = 439.0;

            var pageH = flow.CurrentPage.Height;
            // Overflow pages pre-register F1 = Helvetica and the resource
            // merge skips names already taken — register Helvetica first
            // so the band's Times faces keep their names across pages.
            Table.RegisterFont(flow.CurrentPage);
            var yTop = 0.0;   // running baseline, measured from the page top
            var first = true;
            void RbHeader(Content.ContentStreamBuilder b)
            {
                var bold = Table.RegisterFont(flow.CurrentPage, "Times-Bold");
                var ty = rbTitleBase1;
                foreach (var t in rb.Titles)
                {
                    var w = MeasureStd14Width(t, "Times-Bold", rbTitleFs);
                    b.BeginText().SetFont(bold, rbTitleFs)
                     .MoveTextPosition((flow.CurrentPage.Width - w) / 2, pageH - ty)
                     .ShowText(t).EndText();
                    ty += rbTitlePitch;
                }
                b.BeginText().SetFont(bold, rbCaptionFs)
                 .MoveTextPosition(rbCaptionX, pageH - rbCaptionBase)
                 .ShowText(rb.Caption).EndText();
                yTop = rbCaptionBase + (first ? rbFirstRowGap : rbSpillRowGap);
                first = false;
            }

            var rbB = new Content.ContentStreamBuilder();
            rbB.SaveState();
            RbHeader(rbB);
            var reg = Table.RegisterFont(flow.CurrentPage, "Times-Roman");
            var atPageHead = true;
            foreach (var (desc, vals, newBlock) in rb.Rows)
            {
                // a fresh data table opens one seam lower (its own top
                // margin) — unless it opens the page, where the band's
                // fixed first-row seat already places it
                if (newBlock && !atPageHead) yTop += rbBlockSeam;
                var wrapped = Text.TextPaginator.WrapToWidth(
                    desc, "Times-Roman", rbRowFs, rbWrapW);
                if (wrapped.Count == 0) wrapped.Add("");
                // the row travels whole; a row that cannot fit above the
                // sheet's bottom opens the next sheet under a fresh band
                var rowBottom = yTop + rbWrapPitch * (wrapped.Count - 1);
                if (rowBottom > rbBottomLimit)
                {
                    rbB.RestoreState();
                    flow.InjectContentAtCursor(rbB.Build());
                    flow.ForceNewPage();
                    rbB = new Content.ContentStreamBuilder();
                    rbB.SaveState();
                    RbHeader(rbB);
                    reg = Table.RegisterFont(flow.CurrentPage, "Times-Roman");
                    atPageHead = true;
                }
                for (var li = 0; li < wrapped.Count; li++)
                    if (wrapped[li].Length > 0)
                        rbB.BeginText().SetFont(reg, rbRowFs)
                           .MoveTextPosition(rbDescX, pageH - (yTop + rbWrapPitch * li))
                           .ShowText(wrapped[li]).EndText();
                // values centre on the description's lines
                var vy = yTop + rbWrapPitch * (wrapped.Count - 1) / 2.0;
                for (var ci = 0; ci < 3 && ci < vals.Count; ci++)
                    if (vals[ci].Length > 0)
                        rbB.BeginText().SetFont(reg, rbRowFs)
                           .MoveTextPosition(rbColX[ci], pageH - vy)
                           .ShowText(vals[ci]).EndText();
                yTop += rbWrapPitch * (wrapped.Count - 1) + rbRowPitch;
                atPageHead = false;
            }
            rbB.RestoreState();
            flow.InjectContentAtCursor(rbB.Build());
            // leave the cursor under the last row for anything that follows
            if (flow.CurrentY > pageH - yTop) flow.AdvanceY(flow.CurrentY - (pageH - yTop));
            return;
        }

        // An unbreakable table marked to continue on the next page moves WHOLE to a
        // fresh page when what is left of this one cannot hold it. Probed against the
        // generator: NEITHER flag does this alone — IsBroken=false on its own lets the
        // table run past the page foot on the page it starts, and IsInNextPage on its own
        // splits it where it stands; only the pair moves it. Once it is on the fresh page
        // a table taller than a whole page still splits (43 twenty-point rows fill page
        // two and spill nine onto page three), and its own top margin is dropped there.
        var movedToOwnPage = false;
        if (!table.IsBroken && table.Broken == TableBroken.IsInNextPage)
        {
            var room = flow.CurrentY - flow.BottomMargin;
            var wholePage = flow.ContentTop - flow.BottomMargin;
            // GetHeight is the whole-table measurement (every row, margins included);
            // LastRenderedHeight would only report the last page's slice.
            if (room < wholePage - 1e-6 && table.GetHeight(flow.CurrentPage) > room + 1e-6)
            {
                flow.ForceNewPage();
                movedToOwnPage = true;
            }
        }

        // Start the table at the current flow cursor — not at the top of the page —
        // and indent it to the page's left content margin so it lines up with the
        // surrounding text flow. Render onto whatever page the cursor is on now.
        // The table's own Margin.Top is leading reserved above it (an
        // invoice info table drops 15 pt below the title
        // via table.Margin.Top = 15) — same rule the container-table
        // unwrap branch already applies.
        if (table.Margin?.Top > 0 && !movedToOwnPage) flow.AdvanceY(table.Margin.Top);
        // An explicit Table.Top anchors the table that far below the
        // PAGE top (a Top=400 table starts its rows at
        // y = height−400) — drop the cursor when it is still above
        // that anchor.
        if (table.Top > 0 && flow.CurrentY > page.LayoutFrameHeight - table.Top)
            flow.AdvanceY(flow.CurrentY - (page.LayoutFrameHeight - table.Top));
        var tablePage = flow.CurrentPage;
        table.FlowLeftOffset = marginLeft;
        // Overflow pages inset by the margin a freshly-added page would get:
        // the document-level top margin when the caller set one (explicitly
        // "for new pages added"), otherwise this page's effective top margin.
        var spillTopMargin = PageInfo?.Margin is { TopTouched: true } dm ? dm.Top : marginTop;
        // Fragments nested in the table's cells are LocalHyperlink
        // targets too (a page-level link jumping to a table cell):
        // record each at the table's own position so the deferred
        // link resolution finds them — otherwise the annotation is
        // silently dropped and the page ends up one link short.
        for (var tri = 0; tri < table.Rows.Count; tri++)
        {
            var trow = table.Rows.At(tri);
            for (var tci = 0; tci < trow.Cells.Count; tci++)
                foreach (var cellPara in trow.Cells.At(tci).Paragraphs)
                    if (cellPara is Text.TextFragment cellTf)
                        flow.RecordPosition(cellTf);
        }
        // Rows stop at the page's REAL bottom content margin, not at a fixed 36 pt
        // inset: the generator fills a page until the NEXT row would cross the margin
        // (a 90 pt bottom margin on US Letter takes 76 eight-point rows, a 72 pt one on
        // A4 takes 34 twenty-point rows).
        // Spill pages take the margins of the pages the flow prepares for them
        // (an OnBeforePageGenerate handler re-margining page 2 moves the table's
        // page-2 rows); the buffer in flight is committed first so the spill
        // pages queue behind the slice injected into it.
        if (flow.OnPageBreak is not null)
        {
            flow.FlushInFlightBuffer();
            var spillBaseSlot = overflowPages.Count;
            table.SpillPageMargins = spill => flow.MarginsForSlot(spillBaseSlot + spill - 1);
        }
        List<byte[]> pageContents;
        try
        {
            pageContents = table.BuildMultiPage(tablePage, flow.CurrentY, flow.BottomMargin,
                spillTopMargin, contentFlow: true);
        }
        finally { table.SpillPageMargins = null; }
        var tableImages = table.LastImageDraws;
        var tableGraphs = table.LastGraphDraws;
        // Footnotes on cell fragments: draw each superscript marker at the
        // recorded end-of-text position and queue the note body into this
        // page's bottom band.
        if (table.LastFootnoteMarks is { Count: > 0 } tableFoots)
            foreach (var (fNote, fx, fBase, fSize) in tableFoots[0])
            {
                var fMarker = flow.NextFootnoteMarker(fNote);
                flow.EmitFootnoteMarkerAt(fNote, fMarker, fx, fBase, fSize);
                flow.QueueMarkedFootnote(fNote, fMarker, fSize);
            }
        // Inject at the flow's CURRENT page position — once the flow has
        // page-broken (e.g. a kept-with-next pair moved here), the first
        // slice belongs to the overflow buffer, not the start page.
        flow.InjectContentAtCursor(pageContents[0]);
        if (tableImages.Count > 0 && tableImages[0].Count > 0)
        {
            if (!flow.HasOverflowed)
                foreach (var (data, rect) in tableImages[0])
                    tablePage.AddImage(data, rect);
            else
            {
                if (!overflowImages.TryGetValue(flow.CurrentSlot, out var slotImgs))
                    overflowImages[flow.CurrentSlot] = slotImgs = new List<(byte[], Rectangle)>();
                slotImgs.AddRange(tableImages[0]);
            }
        }
        if (tableGraphs.Count > 0)
            foreach (var gc in tableGraphs[0])
                flow.InjectContentAtCursor(gc);
        if (pageContents.Count == 1)
        {
            // Single-page table: consume exactly its height so following
            // paragraphs continue immediately below on the same page.
            flow.AdvanceY(table.LastRenderedHeight);
            // The table's Margin.Bottom is space reserved below it
            // (the next heading chains a full bottom margin under
            // an empty spacer table).
            if (table.Margin?.Bottom > 0) flow.AdvanceY(table.Margin.Bottom);
        }
        else
        {
            // Intermediate spill pages become standalone pages; the LAST spill
            // page is handed back to the flow so trailing paragraphs continue
            // on it, below the table, rather than starting a fresh page.
            // ⚠ The page still in flight has to be COMMITTED first: appending a spill
            // page straight to the queue while an unflushed buffer holds an earlier
            // page puts the later content in the earlier slot, and the two pages come
            // out swapped.
            flow.FlushInFlightBuffer();
            var tableCheckboxes = table.LastCheckboxDraws;
            for (var pi = 1; pi < pageContents.Count - 1; pi++)
            {
                if (pi < tableImages.Count && tableImages[pi].Count > 0)
                    overflowImages[overflowPages.Count] = tableImages[pi];
                if (pi < tableCheckboxes.Count && tableCheckboxes[pi].Count > 0)
                    _overflowCheckboxes[overflowPages.Count] = tableCheckboxes[pi];
                overflowPages.Add((pageContents[pi], tablePage.Width, tablePage.Height));
            }
            var lastIdx = pageContents.Count - 1;
            var lastSlot = flow.ContinueOnPrebuiltSpill(pageContents[lastIdx], table.LastPageEndY);
            if (lastIdx < tableImages.Count && tableImages[lastIdx].Count > 0)
                overflowImages[lastSlot] = tableImages[lastIdx];
            if (lastIdx < tableCheckboxes.Count && tableCheckboxes[lastIdx].Count > 0)
                _overflowCheckboxes[lastSlot] = tableCheckboxes[lastIdx];
        }
    }


}
