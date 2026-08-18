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
        var targetPage = flow.CurrentPage;
        double originX, originY;
        if (graph.PositionAssigned)
        {
            // Explicit position: assigning Left/Top anchors the graph
            // absolutely to the content-area top-left and takes it out of
            // the flow (the cursor is not advanced, so repeated graphs with
            // the same Left/Top overlay). Left/Top are offsets from that
            // anchor; the box is placed by its bottom-left corner.
            originX = marginLeft + graph.Left;
            originY = (targetPage.Height - marginTop) - graph.Top - graph.Height;
        }
        else if (graph.IsChangePosition)
        {
            // Flow placement: the box sits at the current cursor, shifted by
            // any Left/Top the caller set (offsets from the margin origin).
            // Push to a fresh page if it doesn't fit below the cursor (but
            // never when the cursor is already at the page top — an oversized
            // graph still renders on the current page rather than looping).
            if (flow.CurrentY - graph.Top - graph.Height < marginBottom
                && flow.CurrentY < page.Height - marginTop)
            {
                flow.ResetToTopOfNextPage();
                targetPage = flow.CurrentPage;
            }
            originX = marginLeft + graph.Left;
            originY = flow.CurrentY - graph.Top - graph.Height;
        }
        else
        {
            // Absolute placement: Left from the left edge, Top from the top edge.
            originX = graph.Left;
            originY = targetPage.Height - graph.Top - graph.Height;
        }
        targetPage.AddContentStream(graph.Build(targetPage, originX, originY));
        if (!graph.PositionAssigned && graph.IsChangePosition && ReferenceEquals(targetPage, flow.CurrentPage))
            flow.AdvanceY(graph.Top + graph.Height);
    }

    private void LayoutHeadingParagraph(Heading heading, FlowLayout flow, Page page, System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries, System.Func<Heading, int, double, double> renderTocEntry, Dictionary<int, int> headingAutoCounters, ref string? fontName, double marginLeft, double marginRight)
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
                var yAfter = renderTocEntry(heading, tocEntries[tocIdx].pageIdx, flow.CurrentY);
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
        headingPage.AddContentStream(content);

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
        else if (!string.IsNullOrEmpty(img.File) && System.IO.File.Exists(img.File))
        {
            imgData = System.IO.File.ReadAllBytes(img.File);
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
                    imgWbw = img.FixWidth > 0 ? img.FixWidth : availWbw;
                    imgHbw = img.FixHeight > 0 ? img.FixHeight : availHbw;
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
        for (int frameIdx = 0; frameIdx < frames.Count; frameIdx++)
        {
            var frameData = frames[frameIdx];
            double imgW, imgH;
            if (img.FixWidth > 0 || img.FixHeight > 0)
            {
                imgW = img.FixWidth > 0 ? img.FixWidth : availW;
                imgH = img.FixHeight > 0 ? img.FixHeight : availH;
                if (svgNatW > 0 && svgNatH > 0)
                {
                    // Vector source: a Fix box larger than the content band
                    // is squashed to the band on that axis (never
                    // clipped or spilled to a fresh page).
                    imgW = Math.Min(imgW, availW);
                    imgH = Math.Min(imgH, availH);
                }
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
            else if (TryGetImageNaturalSizePt(frameData, img.IsApplyResolution, out var natWpt, out var natHpt))
            {
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
                if (wrapCell is null || wrapCell.ColSpan < 2 || wrapCell.Paragraphs.Count == 0)
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

        // Start the table at the current flow cursor — not at the top of the page —
        // and indent it to the page's left content margin so it lines up with the
        // surrounding text flow. Render onto whatever page the cursor is on now.
        // The table's own Margin.Top is leading reserved above it (an
        // invoice info table drops 15 pt below the title
        // via table.Margin.Top = 15) — same rule the container-table
        // unwrap branch already applies.
        if (table.Margin?.Top > 0) flow.AdvanceY(table.Margin.Top);
        // An explicit Table.Top anchors the table that far below the
        // PAGE top (a Top=400 table starts its rows at
        // y = height−400) — drop the cursor when it is still above
        // that anchor.
        if (table.Top > 0 && flow.CurrentY > page.Height - table.Top)
            flow.AdvanceY(flow.CurrentY - (page.Height - table.Top));
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
        var pageContents = table.BuildMultiPage(tablePage, flow.CurrentY, 36, spillTopMargin,
            contentFlow: true);
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
            for (var pi = 1; pi < pageContents.Count - 1; pi++)
            {
                if (pi < tableImages.Count && tableImages[pi].Count > 0)
                    overflowImages[overflowPages.Count] = tableImages[pi];
                overflowPages.Add((pageContents[pi], tablePage.Width, tablePage.Height));
            }
            var lastIdx = pageContents.Count - 1;
            var lastSlot = flow.ContinueOnPrebuiltSpill(pageContents[lastIdx], table.LastPageEndY);
            if (lastIdx < tableImages.Count && tableImages[lastIdx].Count > 0)
                overflowImages[lastSlot] = tableImages[lastIdx];
        }
    }

    private void LayoutFloatingBoxParagraph(FloatingBox fbox, FlowLayout flow, Page page, System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries, System.Func<Heading, int, double, double> renderTocEntry, Dictionary<int, int> headingAutoCounters, ref string? fontName, List<(byte[] content, double width, double height)> overflowPages, double marginLeft, double marginRight, double marginBottom)
    {
        // A box holding one multi-column article: the article paints its
        // own sized, padded background and pours its paragraphs down each
        // column in turn. The declared width and height size the CONTENT,
        // so the painted box grows by the padding on every side; columns
        // split what is left after the CSS gap between them.
        if (fbox.PositioningMode == ParagraphPositioningMode.Default
            && fbox.Left == 0 && fbox.Top == 0
            && fbox.Paragraphs.Count == 1
            && fbox.Paragraphs[0] is HtmlFragment colFrag
            && Converters.HtmlToPdfConverter.TryParseColumnArticle(
                colFrag.HtmlContent, out var colArt))
        {
            const double caFs = 12.0, caLine = 13.5, caAbove = 10.7989;
            var caPad = colArt.PadPx * 0.75;
            var caContentW = colArt.WidthPx * 0.75;
            var caContentH = colArt.HeightPx * 0.75;
            var caLeft = marginLeft;
            var caTop = flow.CurrentY;                       // pdf y of the box top
            var caGap = caFs;                                // CSS 'normal' column gap = 1 em
            var caColW = (caContentW - caGap * (colArt.Columns - 1)) / colArt.Columns;
            var caFace = "Times-Roman";
            var caRes = Table.RegisterFont(flow.CurrentPage, caFace);

            double CaWidth(string t)
            {
                if (t.Length == 0) return 0;
                try
                {
                    return Text.FontRepository.FindFont(caFace)?.MeasureString(t, caFs)
                           ?? t.Length * caFs * 0.5;
                }
                catch { return t.Length * caFs * 0.5; }
            }

            // wrap every paragraph to the column width, remembering which
            // line ends its paragraph (those never stretch)
            var caLines = new List<(List<string> Words, bool Last)>();
            foreach (var text in colArt.Paragraphs)
            {
                var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var line = new List<string>();
                var w = 0.0;
                var spaceW = CaWidth(" ");
                foreach (var word in words)
                {
                    var ww = CaWidth(word);
                    var need = line.Count == 0 ? ww : w + spaceW + ww;
                    if (line.Count > 0 && need > caColW + 0.01)
                    {
                        caLines.Add((new List<string>(line), false));
                        line.Clear();
                        w = ww;
                    }
                    else w = need;
                    line.Add(word);
                }
                if (line.Count > 0) caLines.Add((new List<string>(line), true));
            }

            var caB = new Content.ContentStreamBuilder();
            caB.SaveState();
            if (colArt.Background is { } caBg)
                caB.SetFillColor(caBg)
                   .Rectangle(caLeft, caTop - (caContentH + 2 * caPad),
                              caContentW + 2 * caPad, caContentH + 2 * caPad)
                   .Fill();
            caB.SetFillGray(0);

            var caPerCol = (int)Math.Floor(caContentH / caLine);
            var caIdx = 0;
            for (var col = 0; col < colArt.Columns && caIdx < caLines.Count; col++)
            {
                var colLeft = caLeft + caPad + col * (caColW + caGap);
                var y = caTop - caPad - caAbove;              // first baseline
                for (var k = 0; k < caPerCol && caIdx < caLines.Count; k++, caIdx++)
                {
                    var (words, last) = caLines[caIdx];
                    var natural = 0.0;
                    foreach (var word in words) natural += CaWidth(word);
                    var gaps = words.Count - 1;
                    // a justified line stretches its gaps to the column edge;
                    // the line that ends a paragraph keeps its natural spaces
                    var gapW = gaps > 0 && colArt.Justify && !last
                        ? (caColW - natural) / gaps
                        : CaWidth(" ");
                    var x = colLeft;
                    foreach (var word in words)
                    {
                        caB.BeginText().SetFont(caRes, caFs)
                           .MoveTextPosition(x, y)
                           .ShowText(word).EndText();
                        x += CaWidth(word) + gapW;
                    }
                    y -= caLine;
                }
            }
            caB.RestoreState();
            flow.InjectContentAtCursor(caB.Build());
            flow.AdvanceY(caContentH + 2 * caPad);
            return;
        }
        // Flow-positioned, no-size FloatingBox is indistinguishable from the
        // ambient paragraph flow. Inline its child paragraphs into the shared
        // cursor so long content paginates via the surrounding FlowLayout.
        // Absolutely-positioned (Left/Top set) boxes still render through
        // AddFloatingBox since they don't participate in the flow.
        // A box that paints a background/border or carries a background
        // image is meant to render as a visible box (e.g. a coloured header
        // band), not be dissolved into the transparent paragraph flow — route
        // it through AddFloatingBox so its fill, border and child images draw.
        var fboxIsVisibleBox = fbox.BackgroundColor is not null
            || fbox.BackgroundImage is not null
            || (fbox.Border is not null && fbox.Border.Side != BorderSide.None);
        if (fbox.PositioningMode == ParagraphPositioningMode.Default
            && fbox.Left == 0 && fbox.Top == 0 && !fboxIsVisibleBox)
        {
            // Multi-column box: lay the children out across N columns
            // (fill column 0 top-to-bottom, then column 1, ... then a
            // fresh page). Columns start at the page's left content
            // margin; the box's own Margin doesn't inset the flow.
            var columnCount = fbox.ColumnInfo?.ColumnCount ?? 0;
            var inColumns = false;
            if (columnCount > 1)
            {
                var (lefts, widths) = BuildColumnGeometry(
                    fbox.ColumnInfo!, marginLeft,
                    page.Width - marginLeft - marginRight);
                if (lefts.Length > 1)
                {
                    flow.BeginColumns(lefts, widths);
                    inColumns = true;
                }
            }

            // Inline-joined styled paragraph accumulator: consecutive
            // IsInLineParagraph fragments/headings merge into ONE
            // flowing paragraph (joined with their
            // per-segment styles, footnote reference marks and heading
            // labels intact). A paragraph flushes when a
            // non-inline child starts the next one.
            var styRuns = new List<FlowLayout.StyledRun>();
            double styLs = 0;
            double styBaseSize = 0;
            var styNotes = new List<(Note note, double size, double ls)>();

            static bool InlineOf(BaseParagraph p) =>
                p is Text.TextFragment ptf ? ptf.IsInLineParagraph : p.IsInLineParagraph;

            // A child needs the styled-run engine when the legacy
            // writers would drop its decorations: an explicit label,
            // an inline join (either side), per-segment colour /
            // underline / superscript, or a footnote.
            bool SegStyled(Text.TextState st) => st.Underline
                || st.ForegroundColor is not null || st.Superscript;

            void FlushStyled()
            {
                if (styRuns.Count > 0)
                    flow.WriteStyledParagraph(styRuns, styLs);
                foreach (var (n, sz, ls) in styNotes)
                    flow.QueueFootnote(n, sz, Math.Max(ls, styLs));
                styRuns = new List<FlowLayout.StyledRun>();
                styNotes = new List<(Note, double, double)>();
                styLs = 0;
                styBaseSize = 0;
            }

            void AppendFragmentRuns(Text.TextFragment tf)
            {
                var parent = tf.TextState;
                if (parent.LineSpacing > styLs) styLs = parent.LineSpacing;
                foreach (var seg in tf.Segments)
                {
                    var st = seg.TextState;
                    if (st.LineSpacing > styLs) styLs = st.LineSpacing;
                    if (string.IsNullOrEmpty(seg.Text)) continue;
                    // A segment with no explicit size falls back to the
                    // document-builder default point size (10), not the
                    // Standard-14 12 — an untyped FloatingBox fragment
                    // renders at the builder default.
                    var size = st.FontSizeTouched ? (double)st.FontSize
                        : parent.FontSizeTouched ? parent.FontSize : 10;
                    var merged = new Text.TextState
                    {
                        ForegroundColor = st.ForegroundColor ?? parent.ForegroundColor,
                        Underline = st.Underline || parent.Underline,
                        IsBold = st.IsBold || parent.IsBold,
                        IsItalic = st.IsItalic || parent.IsItalic,
                    };
                    var font = st.Font?.SourceFontData is not null ? st.Font
                        : parent.Font?.SourceFontData is not null ? parent.Font : null;
                    if (font is not null) merged.Font = font;
                    if (st.FontData is not null) merged.FontData = st.FontData;
                    else if (parent.FontData is not null) merged.FontData = parent.FontData;
                    if (!st.Superscript && size > styBaseSize) styBaseSize = size;
                    styRuns.Add(new FlowLayout.StyledRun
                    {
                        Text = seg.Text, Size = size, State = merged,
                        Sup = st.Superscript, Link = seg.Hyperlink,
                    });
                }
                if (tf.FootNote is { Paragraphs.Count: > 0 } fn)
                {
                    var markSize = styBaseSize > 0 ? styBaseSize
                        : parent.FontSizeTouched ? parent.FontSize : 12;
                    var markState = new Text.TextState();
                    if (parent.Font?.SourceFontData is not null) markState.Font = parent.Font;
                    if (!string.IsNullOrEmpty(fn.Text))
                        styRuns.Add(new FlowLayout.StyledRun
                        {
                            Text = fn.Text, Size = markSize, State = markState, Sup = true,
                        });
                    styNotes.Add((fn, markSize, parent.LineSpacing));
                }
            }

            void AppendHeadingRuns(Heading h)
            {
                var parent = h.TextState;
                var ownerLeft = h.Margin?.Left ?? 0;
                if (parent.LineSpacing > styLs) styLs = parent.LineSpacing;
                if (styRuns.Count == 0)
                {
                    // The label renders only when the heading STARTS the
                    // paragraph — inline-joined headings show no number.
                    var label = h.UserLabel?.Text
                        ?? NextHeadingPrefix(headingAutoCounters, h).TrimEnd();
                    if (!string.IsNullOrEmpty(label))
                    {
                        var lblState = new Text.TextState
                        {
                            IsBold = h.UserLabel?.TextState.IsBold ?? false,
                        };
                        styRuns.Add(new FlowLayout.StyledRun
                        {
                            Text = label,
                            Size = parent.FontSize > 0 ? parent.FontSize : 10,
                            State = lblState, OwnerLeft = ownerLeft, TabAfter = 20,
                        });
                    }
                }
                foreach (var seg in h.Segments)
                {
                    var st = seg.TextState;
                    if (st.LineSpacing > styLs) styLs = st.LineSpacing;
                    if (string.IsNullOrEmpty(seg.Text)) continue;
                    var size = st.FontSizeTouched ? (double)st.FontSize
                        : parent.FontSizeTouched ? parent.FontSize : 10;
                    var merged = new Text.TextState
                    {
                        ForegroundColor = st.ForegroundColor ?? parent.ForegroundColor,
                        Underline = st.Underline || parent.Underline,
                        IsBold = st.IsBold || parent.IsBold,
                        IsItalic = st.IsItalic || parent.IsItalic,
                    };
                    var font = st.Font?.SourceFontData is not null ? st.Font
                        : parent.Font?.SourceFontData is not null ? parent.Font : null;
                    if (font is not null) merged.Font = font;
                    if (!st.Superscript && size > styBaseSize) styBaseSize = size;
                    styRuns.Add(new FlowLayout.StyledRun
                    {
                        Text = seg.Text, Size = size, State = merged,
                        Sup = st.Superscript, OwnerLeft = ownerLeft,
                    });
                }
            }

            var innerList = fbox.Paragraphs.ToList();
            for (var innerIdx = 0; innerIdx < innerList.Count; innerIdx++)
            {
                var inner = innerList[innerIdx];
                // IsInNewPage on a child forces the rest of the box onto a
                // fresh page (the surrounding flow paginates it), mirroring the
                // page-level paragraph rule. Flush the accumulated inline
                // paragraph first so it stays on the current page.
                if (innerIdx > 0 && ParagraphIsInNewPage(inner))
                {
                    FlushStyled();
                    flow.ForceNewPage();
                }
                // IsFirstParagraphInColumn pushes this paragraph to the
                // top of the next column. Never on the
                // very first child — column 0 is already its home.
                if (inColumns && innerIdx > 0 && inner.IsFirstParagraphInColumn)
                {
                    FlushStyled();
                    flow.ForceNextColumn();
                }
                var nextInline = innerIdx + 1 < innerList.Count
                    && InlineOf(innerList[innerIdx + 1]);

                if (inner is Heading innerHeading)
                {
                    // A TOC-page-authored heading inside the box is a TOC
                    // entry (same rule as the page-level branch); anything
                    // else renders as a content heading at the flow cursor
                    // — previously these were silently dropped and the box
                    // rendered blank.
                    if (ReferenceEquals(innerHeading.TocPage, page))
                    {
                        FlushStyled();
                        var tIdx = tocEntries.FindIndex(e => ReferenceEquals(e.h, innerHeading));
                        if (tIdx >= 0)
                        {
                            var yA = renderTocEntry(innerHeading, tocEntries[tIdx].pageIdx, flow.CurrentY);
                            flow.AdvanceY(flow.CurrentY - yA);
                        }
                        continue;
                    }
                    var hStyled = innerHeading.UserLabel is not null
                        || InlineOf(innerHeading) || nextInline
                        || SegStyled(innerHeading.TextState)
                        || innerHeading.Segments.Any(s => SegStyled(s.TextState));
                    if (hStyled)
                    {
                        if (!InlineOf(innerHeading)) FlushStyled();
                        if (styRuns.Count == 0 && innerHeading.Margin is { Top: > 0 } hm)
                        {
                            if (flow.CurrentY - hm.Top - 12 < flow.BottomMargin)
                                flow.ForceNewPage();
                            flow.AdvanceY(hm.Top);
                        }
                        AppendHeadingRuns(innerHeading);
                        if (!nextInline) FlushStyled();
                        continue;
                    }
                    FlushStyled();
                    // Heading top margin advances the flow; when the margin
                    // plus one heading line no longer fits, the heading
                    // moves to a fresh page and re-applies its margin there
                    // (as in the overflowing list case).
                    var hTopM = innerHeading.Margin?.Top ?? 0;
                    if (hTopM > 0)
                    {
                        if (flow.CurrentY - hTopM - 12 < flow.BottomMargin)
                            flow.ForceNewPage();
                        flow.AdvanceY(hTopM);
                    }
                    fontName ??= Table.RegisterFont(page);
                    var innerPrefix = NextHeadingPrefix(headingAutoCounters, innerHeading);
                    var (hContent, hHeight) = innerHeading.Build(
                        flow.CurrentPage, marginLeft + (innerHeading.Margin?.Left ?? 0),
                        flow.CurrentY, fontName, innerPrefix);
                    flow.InjectContentAtCursor(hContent);
                    flow.AdvanceY(hHeight);
                    continue;
                }
                if (inner is Text.TextFragment innerTf)
                {
                    var tfStyled = innerTf.IsInLineParagraph || nextInline
                        || innerTf.FootNote is { Paragraphs.Count: > 0 }
                        || SegStyled(innerTf.TextState)
                        || innerTf.Segments.Any(s => SegStyled(s.TextState));
                    if (tfStyled)
                    {
                        if (!innerTf.IsInLineParagraph) FlushStyled();
                        if (styRuns.Count == 0) flow.RecordPosition(innerTf);
                        AppendFragmentRuns(innerTf);
                        if (!nextInline) FlushStyled();
                        continue;
                    }
                    FlushStyled();
                    flow.RecordPosition(innerTf);
                    flow.WriteTextFragment(innerTf);
                    continue;
                }
                FlushStyled();
                if (inner is Image innerImage)
                {
                    // A block Image in a dissolved box draws at the flow
                    // cursor sized 1 px = 1 pt (the unsized generator
                    // Image rule) and advances the flow below it.
                    byte[]? fbImgBytes = null;
                    if (innerImage.ImageStream is { } fbIst)
                    {
                        using var fbMs = new MemoryStream();
                        if (fbIst.CanSeek) fbIst.Position = 0;
                        fbIst.CopyTo(fbMs);
                        fbImgBytes = fbMs.ToArray();
                    }
                    else if (!string.IsNullOrEmpty(innerImage.File)
                             && System.IO.File.Exists(innerImage.File))
                        fbImgBytes = System.IO.File.ReadAllBytes(innerImage.File);
                    if (fbImgBytes is not null)
                    {
                        double fbIw, fbIh;
                        if (innerImage.FixWidth > 0 && innerImage.FixHeight > 0)
                        { fbIw = innerImage.FixWidth; fbIh = innerImage.FixHeight; }
                        else if (!TryGetImageNaturalSizePt(fbImgBytes,
                                     innerImage.IsApplyResolution, out fbIw, out fbIh)
                                 || fbIw <= 0 || fbIh <= 0)
                        { fbIw = 100; fbIh = 100; }
                        flow.PlaceImageBlock(fbImgBytes, fbIw, fbIh);
                    }
                    continue;
                }
                if (inner is HtmlFragment innerHtml)
                {
                    // A dissolved box renders its HTML as blocks, not as one
                    // tag-stripped run: each block is its own paragraph, drawn in
                    // the browser default serif face at the browser default block
                    // size, and inline <a href> ranges become hyperlinked segments
                    // that the flow turns into Link annotations over their glyphs.
                    var innerBlocks = Converters.HtmlToPdfConverter.ParseHtmlBlocks(
                        innerHtml.HtmlContent ?? "", HtmlUaBlockFontSize);
                    var innerWrote = false;
                    foreach (var ib in innerBlocks)
                    {
                        if (string.IsNullOrWhiteSpace(ib.Text)) continue;
                        var innerFrag = new Text.TextFragment(ib.Text);
                        innerFrag.TextState.FontName = HtmlUaSerifFontName;
                        innerFrag.TextState.FontSize =
                            (float)(ib.FontSize > 0 ? ib.FontSize : HtmlUaBlockFontSize);
                        if (ib.Anchors is { Count: > 0 })
                            ApplyHtmlAnchorSegments(innerFrag, ib.Text, ib.Anchors);
                        flow.WriteTextFragment(innerFrag);
                        innerWrote = true;
                    }
                    if (!innerWrote)
                    {
                        var innerPlain = HtmlFragment.StripHtmlTags(innerHtml.HtmlContent ?? "");
                        if (!string.IsNullOrWhiteSpace(innerPlain))
                            flow.WriteTextFragment(new Text.TextFragment(innerPlain));
                    }
                    continue;
                }
                if (inner is Table innerTable)
                {
                    // The box dissolves into the page flow, so its table
                    // anchors at the page's left content margin (a
                    // margin-less box still honours the page margins).
                    innerTable.FlowLeftOffset = marginLeft;
                    var innerContents = innerTable.BuildMultiPage(flow.CurrentPage, flow.CurrentY);
                    // Inject at the flow's CURRENT page position — after a
                    // page break the slice belongs to the overflow buffer,
                    // not the start page.
                    flow.InjectContentAtCursor(innerContents[0]);
                    var innerGraphs = innerTable.LastGraphDraws;
                    if (innerGraphs.Count > 0)
                        foreach (var gc in innerGraphs[0])
                            flow.InjectContentAtCursor(gc);
                    var innerImgs = innerTable.LastImageDraws;
                    if (!flow.HasOverflowed && innerImgs.Count > 0)
                        foreach (var (data, rect) in innerImgs[0])
                            flow.CurrentPage.AddImage(data, rect);
                    for (var pi = 1; pi < innerContents.Count; pi++)
                        overflowPages.Add((innerContents[pi], flow.CurrentPage.Width, flow.CurrentPage.Height));
                    // A single-slice table consumes exactly its height so
                    // following children continue below it on this page;
                    // only a multi-page spill forces a fresh page.
                    if (innerContents.Count == 1)
                        flow.AdvanceY(innerTable.LastRenderedHeight);
                    else
                        flow.ResetToTopOfNextPage();
                }
            }
            FlushStyled();

            if (inColumns)
                flow.EndColumns();
        }
        else if (fbox.PositioningMode != ParagraphPositioningMode.Default
                 || fbox.Left != 0 || fbox.Top != 0)
        {
            if (fbox.PositioningMode == ParagraphPositioningMode.Default)
            {
                // Default-positioned box with a Left/Top offset: the
                // offsets are relative to the page CONTENT area and the
                // box top anchors at the current flow position
                // (left margin + Left, top at the
                // cursor), so translate before the absolute render.
                var savedFbMode = fbox.PositioningMode;
                var savedFbTop = fbox.Top;
                var savedFbLeft = fbox.Left;
                fbox.PositioningMode = ParagraphPositioningMode.Absolute;
                fbox.Top = page.Height - flow.CurrentY + fbox.Top;
                fbox.Left = marginLeft + fbox.Left;
                fbox.PageBottomMargin = marginBottom;
                page.AddFloatingBox(fbox);
                fbox.PositioningMode = savedFbMode;
                fbox.Top = savedFbTop;
                fbox.Left = savedFbLeft;
            }
            else
            {
                // Absolute box — render in place, doesn't affect flow cursor.
                fbox.PageBottomMargin = marginBottom;
                page.AddFloatingBox(fbox);
            }
        }
        else
        {
            // Flow-positioned visible box (background/border): render it at the
            // current cursor — not the page top — so a coloured header band
            // honours the page's top margin, then advance the flow past it.
            var targetPage = flow.CurrentPage;
            var savedMode = fbox.PositioningMode;
            var savedTop = fbox.Top;
            fbox.PositioningMode = ParagraphPositioningMode.Absolute;
            fbox.Top = targetPage.Height - flow.CurrentY;
            targetPage.AddFloatingBox(fbox);
            fbox.PositioningMode = savedMode;
            fbox.Top = savedTop;
            flow.AdvanceY(fbox.Height);
        }
    }

    private void LayoutHtmlFragmentParagraph(HtmlFragment html, FlowLayout flow, Page page, Text.TextBuilder tb, HashSet<Table> renderedTables, List<(byte[] content, double width, double height)> overflowPages, Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages, double marginLeft, double marginRight, double marginTop, double marginBottom)
    {
        // Inline <svg> elements become <img src="inline-svg:i"> placeholders
        // rendered through the SVG engine by RenderHtmlImages.
        // A meeting-agenda fragment draws straight onto this page: its levels
        // carry their own indents and numbering boxes, which the block flow
        // below has no model for (see Agenda.cs).
        if (Converters.HtmlToPdfConverter.TryRenderAgendaOutline(
                html.HtmlContent ?? "", page, marginLeft, marginRight, marginTop))
            return;
        var htmlContent = Converters.HtmlToPdfConverter.ExtractInlineSvgs(
            Converters.HtmlToPdfConverter.ApplyKnockoutTextBindings(html.HtmlContent ?? ""),
            out var inlineSvgs);
        var htmlColor = html.TextState?.ForegroundColor;
        // One Link annotation per hyperlinked HtmlFragment (see below).
        var htmlFragmentLinkEmitted = false;

        // A layout table places blocks rather than drawing a grid: each
        // row lays its cells out side by side at their own widths, and a
        // cell's own table renders at that position. Laying out returns the



        // Render a run of block-structured HTML (paragraphs/headings/lists)
        // through the flow at the current cursor, then any <img> in that chunk.

        // Left border+padding of every framed block currently open around
        // the content being rendered (see the frame bookkeeping below).
        var htmlFrameIndent = 0.0;

        // Framed blocks: a block element whose CSS declares a border draws
        // a box round everything it contains — its own text and any table
        // inside it — over as many pages as that content takes. The spans
        // are in the SOURCE's coordinates, so the chunked render below can
        // say when each frame opens and closes by where the chunk sits.
        var htmlFrames = Converters.HtmlToPdfConverter.FramedBlockSpans(htmlContent);
        // The frames open so far, each with the slot and Y it opened at.
        var htmlOpenFrames = new List<(int Index, int Slot, double Top)>();
        // Open every frame that starts inside [from, to) and close every one
        // that ends there. A frame is honoured only when nothing renderable
        // precedes its open tag in its own chunk — the box top is the flow
        // cursor at the chunk boundary, so text before it would sit inside a
        // box that has not begun.
        void HtmlFramesOpening(int from, int to, string chunkText)
        {
            for (var fi = 0; fi < htmlFrames.Count; fi++)
            {
                var f = htmlFrames[fi];
                if (f.Start < from || f.Start >= to) continue;
                var before = htmlContent.Substring(from, f.Start - from);
                if (HtmlFragment.StripHtmlTags(before).Trim().Length > 0) continue;
                _ = chunkText;
                htmlOpenFrames.Add((fi, flow.CurrentSlot, flow.CurrentY));
                // The content starts below the top border and its padding,
                // and clear of the left border.
                flow.AdvanceY(f.BorderWidthPt + f.PadTopPt);
                htmlFrameIndent += f.BorderWidthPt;
            }
        }
        void HtmlFramesClosing(int from, int to)
        {
            for (var oi = htmlOpenFrames.Count - 1; oi >= 0; oi--)
            {
                var open = htmlOpenFrames[oi];
                var f = htmlFrames[open.Index];
                if (f.End <= from || f.End > to) continue;
                flow.DrawFrameBox(open.Slot, open.Top, flow.CurrentY,
                    f.BorderWidthPt, f.BorderColor);
                htmlFrameIndent -= f.BorderWidthPt;
                htmlOpenFrames.RemoveAt(oi);
            }
        }

        if (html.HtmlLoadOptions?.PageInfo is { MarginAssigned: true } mfPi)
        {
            // A fragment whose load options declare page margins lays
            // out in its own box INSIDE the page's content box: the
            // declared margins add to the page's. The first page takes
            // PageInfo.Margin, every generated page after it takes
            // AnyMargin. Vertical rhythm is the CSS half-leading model
            // on an integer-pixel "normal" line height, and a line's
            // ascent/descent is the max over the fragment's own font
            // (the block strut) and every run on the line.
            var mfSize = html.TextState is { } mfTs && mfTs.FontSize > 0 ? (double)mfTs.FontSize : 10.0;
            var mfFirst = mfPi.Margin;
            var mfRest = mfPi.AnyMarginAssigned ? mfPi.AnyMargin : mfPi.Margin;
            var mfLeft = marginLeft + mfFirst.Left;
            var mfRight = page.Width - marginRight - mfFirst.Right;
            var mfWidth = Math.Max(1, mfRight - mfLeft);
            var mfBottom = page.Height - marginBottom - mfFirst.Bottom;   // from top
            var mfTopFirst = marginTop + mfFirst.Top;                     // from top
            var mfTopRest = marginTop + mfRest.Top;

            // The strut: the fragment's own face, mapped onto the
            // Standard-14 metric twin the renderer can draw with.
            var mfStrut = Converters.HtmlToPdfConverter.Std14Face(
                html.TextState?.Font?.FontName, false, false);

            string MfFace(Converters.HtmlToPdfConverter.FlowRun r)
                => Converters.HtmlToPdfConverter.Std14Face(
                    r.Family ?? html.TextState?.Font?.FontName, r.Bold, r.Italic);

            double MfWidthOf(string t, string face)
            {
                if (t.Length == 0) return 0;
                try
                {
                    return Text.FontRepository.FindFont(face)?.MeasureString(t, mfSize)
                           ?? t.Length * mfSize * 0.5;
                }
                catch { return t.Length * mfSize * 0.5; }
            }

            // one laid-out piece of a line
            var mfLines = new List<(List<(string Text, Converters.HtmlToPdfConverter.FlowRun Run, string Face, double X, double W)> Pieces,
                double Above, double Below)>();

            foreach (var mfPara in Converters.HtmlToPdfConverter.ParseFlowParagraphs(htmlContent))
            {
                var pieces = new List<(string Text, Converters.HtmlToPdfConverter.FlowRun Run, string Face, double X, double W)>();
                var x = 0.0;
                var above = Converters.HtmlToPdfConverter.FaceAbove(mfStrut, mfSize);
                var below = Converters.HtmlToPdfConverter.FaceBelow(mfStrut, mfSize);
                var lineStarted = false;

                void MfFlush()
                {
                    // trailing space never holds a line open
                    while (pieces.Count > 0 && pieces[^1].Item1.Trim().Length == 0)
                        pieces.RemoveAt(pieces.Count - 1);
                    mfLines.Add((new List<(string Text, Converters.HtmlToPdfConverter.FlowRun Run, string Face, double X, double W)>(pieces),
                        above, below));
                    pieces.Clear();
                    x = 0;
                    above = Converters.HtmlToPdfConverter.FaceAbove(mfStrut, mfSize);
                    below = Converters.HtmlToPdfConverter.FaceBelow(mfStrut, mfSize);
                    lineStarted = false;
                }


                foreach (var run in mfPara.Runs)
                {
                    if (run.HardBreak) { MfFlush(); continue; }
                    var face = MfFace(run);
                    var runAbove = Converters.HtmlToPdfConverter.FaceAbove(face, mfSize);
                    var runBelow = Converters.HtmlToPdfConverter.FaceBelow(face, mfSize);
                    // split into words, keeping each word's leading space
                    foreach (System.Text.RegularExpressions.Match wm in System.Text.RegularExpressions.Regex.Matches(run.Text, @" *[^ ]+| +"))
                    {
                        var word = wm.Value;
                        var atLineStart = !lineStarted;
                        var draw = atLineStart ? word.TrimStart(' ') : word;
                        if (draw.Length == 0) continue;
                        var w = MfWidthOf(draw, face);
                        if (lineStarted && x + w > mfWidth + 0.01 && draw.Trim().Length > 0)
                        {
                            MfFlush();
                            draw = word.TrimStart(' ');
                            if (draw.Length == 0) continue;
                            w = MfWidthOf(draw, face);
                        }
                        pieces.Add((draw, run, face, x, w));
                        x += w;
                        lineStarted = true;
                        above = Math.Max(above, runAbove);
                        below = Math.Max(below, runBelow);
                    }
                }
                MfFlush();
            }

            // place: greedy fill while the line box stays inside the
            // bottom limit; a block moves whole unless two of its
            // lines still fit on the page
            var mfY = mfTopFirst;      // from top, line-box top
            var mfPageTop = mfTopFirst;
            var mfBuilder = new Content.ContentStreamBuilder();
            mfBuilder.SaveState();
            var mfDrew = false;

            void MfNewPage()
            {
                mfBuilder.RestoreState();
                if (mfDrew) flow.InjectContentAtCursor(mfBuilder.Build());
                flow.ForceNewPage();
                mfBuilder = new Content.ContentStreamBuilder();
                mfBuilder.SaveState();
                mfDrew = false;
                mfPageTop = mfTopRest;
                mfY = mfTopRest;
            }

            var mfPrevBelow = 0.0;
            var mfOnPage = 0;
            for (var li = 0; li < mfLines.Count; li++)
            {
                var (pieces, above, below) = mfLines[li];
                var baseline = mfOnPage == 0 ? mfPageTop + above : mfY + mfPrevBelow + above;
                if (baseline + below > mfBottom + 0.01)
                {
                    MfNewPage();
                    mfOnPage = 0;
                    baseline = mfPageTop + above;
                }
                foreach (var (text, run, face, x, w) in pieces)
                {
                    if (text.Trim().Length == 0) continue;
                    var res = Table.RegisterFont(flow.CurrentPage, face);
                    var py = page.Height - baseline;
                    if (run.Back is { } bg)
                    {
                        // the highlight fills the run's content area:
                        // no half-leading, just ascent+descent
                        var hTop = baseline - Converters.HtmlToPdfConverter.FaceAscent(face, mfSize);
                        var hH = Converters.HtmlToPdfConverter.FaceAscent(face, mfSize)
                                 + Converters.HtmlToPdfConverter.FaceDescent(face, mfSize);
                        mfBuilder.SetFillColor(bg)
                                 .Rectangle(mfLeft + x, page.Height - hTop - hH, w, hH)
                                 .Fill();
                    }
                    if (run.Fore is { } fg) mfBuilder.SetFillColor(fg);
                    else mfBuilder.SetFillGray(0);
                    mfBuilder.BeginText().SetFont(res, mfSize)
                             .MoveTextPosition(mfLeft + x, py)
                             .ShowText(text).EndText();
                    mfDrew = true;
                }
                mfY = baseline - above;
                mfPrevBelow = below;
                mfY = baseline;      // track the baseline for the next step
                mfOnPage++;
            }
            mfBuilder.RestoreState();
            if (mfDrew) flow.InjectContentAtCursor(mfBuilder.Build());
            var mfEnd = page.Height - (mfY + mfPrevBelow);
            if (flow.CurrentY > mfEnd) flow.AdvanceY(flow.CurrentY - mfEnd);
        }
        else if (Converters.HtmlToPdfConverter.TryParseFilingLetter(htmlContent, out var flItems))

        {
            // Centered filing-letter dialect: the letterhead image at
            // natural size on the page center, then Times lines on the
            // letter's 4 em rhythm — a hard break holds a full blank
            // line, paragraph wrappers keep their 1 em margins, and
            // the marked section sets left with its 1 cm indents.
            const double flPitch = 48.0, flFs = 12.0, flDrop = 48.4;
            double FlMeasure(string t)
            {
                try
                {
                    return Text.FontRepository.FindFont("Times-Roman")?.MeasureString(t, flFs)
                           ?? t.Length * flFs * 0.5;
                }
                catch { return t.Length * flFs * 0.5; }
            }
            foreach (var fl in flItems)
            {
                if (fl.ExtraGap > 0) flow.AdvanceY(fl.ExtraGap);
                if (fl.ImgSrc is not null)
                {
                    var fbytes = LoadHtmlImageBytes(fl.ImgSrc);
                    if (fbytes is not null)
                    {
                        // css-pixel sizing: the letter scales its
                        // letterhead at 0.75 pt per image pixel
                        TryGetImageNaturalSizePt(fbytes, false, out var fw, out var fh);
                        fw *= 0.75;
                        fh *= 0.75;
                        if (fw <= 0 || fh <= 0) { fw = 187.5; fh = 75; }
                        var fx = (page.Width - fw) / 2;
                        var fTop = flow.CurrentY;
                        flow.CurrentPage.AddImage(fbytes,
                            new Rectangle(fx, fTop - fh, fx + fw, fTop));
                        flow.AdvanceY(fh);
                    }
                    continue;
                }
                if (fl.Blank) { flow.AdvanceY(flPitch); continue; }
                if (fl.Text is not { } ftext) continue;
                var fres = Table.RegisterFont(flow.CurrentPage, "Times-Roman");
                var maxW = page.Width - 144 - fl.IndentPt;
                var linesOut = new List<string>();
                var rem2 = ftext;
                while (rem2.Length > 0 && FlMeasure(rem2) > maxW)
                {
                    var cut = rem2.Length;
                    while (cut > 0 && (cut >= rem2.Length || rem2[cut] != ' '
                           || FlMeasure(rem2[..cut]) > maxW))
                        cut--;
                    if (cut <= 0) { cut = rem2.Length; }
                    linesOut.Add(rem2[..cut]);
                    rem2 = rem2[cut..].TrimStart();
                }
                if (rem2.Length > 0) linesOut.Add(rem2);
                foreach (var lt in linesOut)
                {
                    if (flow.CurrentY - flDrop < flow.BottomMargin) flow.ForceNewPage();
                    var lw = FlMeasure(lt);
                    var lx = fl.AlignLeft ? 72 + fl.IndentPt
                        : Math.Max(72, (page.Width - lw) / 2);
                    var fb = new Content.ContentStreamBuilder();
                    fb.SaveState();
                    fb.BeginText().SetFont(fres, flFs)
                      .MoveTextPosition(lx, flow.CurrentY - flDrop)
                      .ShowText(lt).EndText();
                    fb.RestoreState();
                    flow.InjectContentAtCursor(fb.Build());
                    flow.AdvanceY(flPitch);
                }
            }
        }
        else if (Converters.HtmlToPdfConverter.TryParseProcedureStepRows(htmlContent, out var psRows,
                     html.IsParagraphHasMargin, html.HtmlLoadOptions))
        {
            LayoutProcedureStepRows(psRows, html, flow, page, marginLeft, marginRight, marginTop, marginBottom);
        }
        else if (Converters.HtmlToPdfConverter.ContainsTable(htmlContent))
        {
            // Mixed content (text blocks + real column tables): render each
            // top-level segment in document order so an HTML <table> flows as
            // columns instead of a flat tag-stripped stack.
            // A full <HTML> document with no fragment font and a table
            // declaring an absolute pixel width is the UA-serif wide-box
            // shape: its text chunks set in the serif writer above, and
            // its tables use the widest declared box.
            var uaWideBoxPt = 0.0;
            var uaSerifFrag = System.Text.RegularExpressions.Regex.IsMatch(
                    htmlContent, @"<html[\s>]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && html.TextState?.Font is null
                && string.IsNullOrEmpty(html.TextState?.FontName);
            if (uaSerifFrag)
                foreach (System.Text.RegularExpressions.Match uwm in
                    System.Text.RegularExpressions.Regex.Matches(htmlContent,
                        @"<table\b[^>]*\bwidth\s*=\s*[""']?(\d+)(?![\d%])",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    uaWideBoxPt = Math.Max(uaWideBoxPt,
                        double.Parse(uwm.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) * 0.75);
            uaSerifFrag &= uaWideBoxPt > page.Width - marginLeft - marginRight;
            // The segments concatenate back to htmlContent, so a running
            // offset places each one in the source — which is how the
            // framed-block spans above are expressed.
            // Verdana form-grid document: a width-percent wrapper div
            // whose cells declare inline Verdana spans throughout —
            // every table chunk below takes the dialect.
            var vgDoc = System.Text.RegularExpressions.Regex.IsMatch(
                    htmlContent, @"^\s*<div[^>]*style\s*=\s*'[^']*width:\s*\d+%",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && System.Text.RegularExpressions.Regex.Matches(
                    htmlContent, @"font-family:\s*Verdana",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count >= 4;
            // The width-percent wrapper scopes only its OWN subtree: a
            // section table after its </div> spans the full content box
            // (the owner/member/field grids run 90..505 while
            // the label grid keeps the wrapper's 92%). Depth-walk the div
            // tags to find the wrapper's matching close.
            var vgWrapClose = int.MaxValue;
            if (vgDoc)
            {
                var vgDepth = 0;
                foreach (System.Text.RegularExpressions.Match dm in
                    System.Text.RegularExpressions.Regex.Matches(htmlContent,
                        @"<\s*(/?)div\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    vgDepth += dm.Groups[1].Value.Length > 0 ? -1 : 1;
                    if (vgDepth == 0) { vgWrapClose = dm.Index; break; }
                }
            }
            var chunkAt = 0;
            foreach (var (isTable, chunk) in Converters.HtmlToPdfConverter.SegmentHtmlTables(htmlContent))
            {
                if (vgDoc && Environment.GetEnvironmentVariable("ASPOSE_HTML_DEBUG_FG") is not null)
                    Console.WriteLine($"[chunk] table={isTable} len={chunk.Length} " +
                        $"'{System.Text.RegularExpressions.Regex.Replace(chunk.Length > 70 ? chunk[..70] : chunk, @"\s+", " ")}'");
                var chunkEnd = chunkAt + chunk.Length;
                HtmlFramesOpening(chunkAt, chunkEnd, chunk);
                if (isTable && uaSerifFrag)
                {
                    RenderUaSerifTable(chunk, uaWideBoxPt, flow, page, marginLeft);
                }
                else if (isTable)
                {
                    var isLayout = Converters.HtmlToPdfConverter.IsLayoutTableHtml(chunk);
                    var chunkCss = isLayout
                        ? Converters.HtmlToPdfConverter.ParseStyleSheet(htmlContent)
                        : null;
                    // a percentage width resolves against the body's own
                    // declared width when the document states one
                    var layoutAvail = page.Width - marginLeft - marginRight;
                    if (uaSerifFrag) layoutAvail = uaWideBoxPt;
                    if (isLayout
                        && Converters.HtmlToPdfConverter.DeclaredBodyWidthPt(htmlContent) is > 0 and var bw)
                        layoutAvail = bw;
                    // The document's own `body { }` type is the grid's base
                    // too: a table inherits the page's face and size rather
                    // than falling back to the 11 pt Standard-14 default
                    // while the prose around it sets in the declared face.
                    var tblBodyCss = Converters.HtmlToPdfConverter.BodyCssFont(htmlContent);
                    // Verdana form-grid fragment: a report grid whose
                    // cells each declare an inline `font-family:
                    // Verdana; font-size: Npt` span, wrapped in a
                    // width-percent div. The dialect sets it
                    // in REAL Verdana metrics with 19px (14.25pt @8pt,
                    // scaling with the size) line boxes, the grid sized to
                    // the wrapper's percent of the content box.
                    // Doc-level gate (vgDoc): EVERY table of the form
                    // grid takes the dialect — the one-span section
                    // bands and the spacer table included, not just
                    // the span-heavy label grid.
                    var vgWrap = System.Text.RegularExpressions.Regex.Match(
                        htmlContent, @"^\s*<div[^>]*style\s*=\s*'[^']*width:\s*(\d+)%",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var vgSize = System.Text.RegularExpressions.Regex.Match(
                        chunk, @"font-size:\s*([\d.]+)\s*pt",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!vgSize.Success)
                        vgSize = System.Text.RegularExpressions.Regex.Match(
                            htmlContent, @"font-size:\s*([\d.]+)\s*pt",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var verdanaGrid = !isLayout && vgDoc && vgWrap.Success
                        && vgSize.Success;
                    var vgPt = verdanaGrid
                        ? double.Parse(vgSize.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture)
                        : 0;
                    var vgInWrap = chunkAt < vgWrapClose;
                    if (verdanaGrid && vgInWrap)
                        layoutAvail *= double.Parse(vgWrap.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) / 100.0;
                    // The cell strut: the ambient font's line box at the
                    // default size — Verdana-12 (14.25) inside the
                    // wrapper's <font face='Verdana'>, the serif
                    // default's 13.5 for the top-level section tables.
                    var vgStrutPt = !verdanaGrid ? 0
                        : vgInWrap
                            ? Converters.HtmlToPdfConverter.PxLinePt(
                                Converters.HtmlToPdfConverter.FormGridBasePt,
                                Converters.HtmlToPdfConverter.VerdanaWinLineRatio)
                            : Converters.HtmlToPdfConverter.PxLinePt(
                                Converters.HtmlToPdfConverter.FormGridBasePt,
                                Converters.HtmlToPdfConverter.SerifWinLineRatio);
                    // The strut's baseline drop: half-leading + winAscent
                    // within the strut box, in the ambient face.
                    var vgStrutDropPt = !verdanaGrid ? 0
                        : vgInWrap
                            ? (vgStrutPt - Converters.HtmlToPdfConverter.FormGridBasePt
                                    * Converters.HtmlToPdfConverter.VerdanaWinLineRatio) / 2
                                + Converters.HtmlToPdfConverter.FormGridBasePt
                                    * Converters.HtmlToPdfConverter.VerdanaWinAscent
                            : (vgStrutPt - Converters.HtmlToPdfConverter.FormGridBasePt
                                    * Converters.HtmlToPdfConverter.SerifWinLineRatio) / 2
                                + Converters.HtmlToPdfConverter.FormGridBasePt
                                    * Converters.HtmlToPdfConverter.SerifWinAscent;
                    var t = Converters.HtmlToPdfConverter.BuildTableFromHtml(
                        chunk, layoutAvail, out _, html.HtmlLoadOptions,
                        inlineSvgs, chunkCss, false, false,
                        verdanaGrid ? vgStrutPt : 0,
                        verdanaGrid ? vgPt : tblBodyCss.SizePt, false, isLayout,
                        // …and the face's own `line-height: normal` box,
                        // so a cell line steps on the same rhythm the
                        // prose does (Arial 12 → 13.5, not a bare 12).
                        cssRunFace: verdanaGrid ? null : tblBodyCss.Face,
                        defaultCellFace: verdanaGrid ? null : tblBodyCss.Face,
                        formGridDialect: verdanaGrid,
                        formGridStrutPt: vgStrutPt,
                        formGridStrutDropPt: vgStrutDropPt);
                    if (verdanaGrid && t is not null)
                    {
                        t.HonorCellTtfFaces = true;
                        t.FormGridCells = true;
                        // border=1 draws the table's OWN box border too:
                        // the cell grid sits one border-width inside the
                        // table box (outer stroke centre at
                        // 90.38 = box edge + half the 0.75 width, first
                        // cell content at 91.5).
                        if (t.HtmlCellBorderPt > 0 && t.Border is null)
                            t.Border = new BorderInfo(BorderSide.Box,
                                t.HtmlCellBorderPt,
                                t.DefaultCellBorder?.Color ?? Color.Black);
                        // The cell grid's box: the declared table width
                        // less the table border pair — the base every
                        // percent below resolves against (measured exact:
                        // member columns = 20/16/…% of 413.5 = 415 − 1.5).
                        var vgGridBox = layoutAvail - 2 * (t.Border?.Width ?? 0);
                        // A width='100%' section table FILLS the box —
                        // the band rows paint edge to edge; the
                        // content-sized column would shrink the fill
                        // to the caption's width.
                        if (System.Text.RegularExpressions.Regex.IsMatch(
                                chunk, @"<table[^>]*\bwidth\s*=\s*'100%'",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                            && !System.Text.RegularExpressions.Regex.IsMatch(
                                chunk, @"<td[^>]*<td",
                                System.Text.RegularExpressions.RegexOptions.Singleline))
                        {
                            var vgMaxTds = 0;
                            foreach (System.Text.RegularExpressions.Match rm in
                                System.Text.RegularExpressions.Regex.Matches(
                                    chunk, @"<tr[^>]*>(.*?)</tr>",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                                    | System.Text.RegularExpressions.RegexOptions.Singleline))
                                vgMaxTds = Math.Max(vgMaxTds,
                                    System.Text.RegularExpressions.Regex.Matches(
                                        rm.Groups[1].Value, @"<td\b",
                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count);
                            if (vgMaxTds == 1)
                                t.ColumnWidths = vgGridBox.ToString(
                                    "0.##", System.Globalization.CultureInfo.InvariantCulture);
                            else
                            {
                                // Multi-column: the first row whose EVERY td
                                // declares a percent width owns the grid
                                // (a band row with one colspan cell above
                                // it doesn't) — hard shares of the bordered
                                // box, no content floors (boundaries land
                                // to 0.01 pt).
                                foreach (System.Text.RegularExpressions.Match rm in
                                    System.Text.RegularExpressions.Regex.Matches(
                                        chunk, @"<tr[^>]*>(.*?)</tr>",
                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                                        | System.Text.RegularExpressions.RegexOptions.Singleline))
                                {
                                    var vgTds = System.Text.RegularExpressions.Regex.Matches(
                                        rm.Groups[1].Value, @"<td\b[^>]*>",
                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    if (vgTds.Count < vgMaxTds) continue;
                                    var vgPcts = new List<double>();
                                    foreach (System.Text.RegularExpressions.Match tdm in vgTds)
                                    {
                                        var pw = System.Text.RegularExpressions.Regex.Match(
                                            tdm.Value, @"width\s*=\s*['""]?([\d.]+)%",
                                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                        if (!pw.Success) { vgPcts.Clear(); break; }
                                        vgPcts.Add(double.Parse(pw.Groups[1].Value,
                                            System.Globalization.CultureInfo.InvariantCulture));
                                    }
                                    if (vgPcts.Count >= 2)
                                        t.ColumnWidths = string.Join(" ",
                                            vgPcts.Select(p => (p / 100.0 * vgGridBox).ToString(
                                                "0.###", System.Globalization.CultureInfo.InvariantCulture)));
                                    break;
                                }
                            }
                        }
                        // The declared widths OWN the grid: the ingest's
                        // draw-time min/max fields would re-derive
                        // content columns over the ColumnWidths.
                        if (t.ColumnWidths is not null)
                        {
                            t.HtmlColMinPt = null;
                            t.HtmlColMaxPt = null;
                        }
                    }
                    if (t is not null)
                    {
                        if (isLayout)
                        {
                            if (html.Margin is { Top: > 0 } lmt) flow.AdvanceY(lmt.Top);
                            // breaks written between rows sit above the table
                            var fostered = Converters.HtmlToPdfConverter.FosterParentedBreaks(chunk);
                            if (fostered > 0) flow.AdvanceY(fostered * 11.25);
                            var boxFloor = layoutAvail;
                            foreach (var lt2 in LeafTables(t))
                            {
                                Converters.HtmlToPdfConverter.ApplyAutoWidths(lt2, 0);   // measure at its minimum
                                var floorSum = 0.0;
                                foreach (var cw in (lt2.ColumnWidths ?? "").Split(
                                             ' ', StringSplitOptions.RemoveEmptyEntries))
                                    if (double.TryParse(cw, System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var cv))
                                        floorSum += cv;
                                boxFloor = Math.Max(boxFloor, floorSum);
                            }
                            var usedH = RenderLayoutTable(t, -1, boxFloor, flow.CurrentY, flow, marginLeft, renderedTables);
                            flow.AdvanceY(usedH);
                        }
                        else RenderHtmlTable(t, flow, page, marginLeft, marginTop, overflowPages, overflowImages);
                    }
                }
                else if (uaSerifFrag) RenderUaSerifChunk(chunk, uaWideBoxPt, html, flow, marginLeft);
                // Form-grid document: a bare-<br> stretch between two
                // section tables is one ambient line box per break —
                // the serif default's 13.5 outside the wrapper div
                // (622.1+13.5 = 635.6), the wrapper
                // font's UNROUNDED Verdana-12 line inside it
                // (414.0+14.58 = 428.6; the rounded 19px box
                // measures 0.35 short there).
                else if (vgDoc && System.Text.RegularExpressions.Regex.IsMatch(
                             chunk, @"^\s*(<br\s*/?>\s*)+$",
                             System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    foreach (System.Text.RegularExpressions.Match brm in
                        System.Text.RegularExpressions.Regex.Matches(
                            chunk, @"<br\s*/?>",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        flow.AdvanceY(VgBrBoxPt(chunkAt + brm.Index));
                else if (vgDoc && TryVgFlowText(chunk, chunkAt))
                {
                    // Form-grid document: a bare top-level <div>text</div>
                    // between section tables rendered as one serif flow
                    // line (see TryVgFlowText).
                }
                else
                {
                    // Form-grid document: a <br> standing BETWEEN element
                    // tags in a mixed chunk (`</div><br><div>…`) is the
                    // same one-line-box space as the bare-<br> chunks —
                    // the blocks renderer collapses it otherwise. A chunk-
                    // final <br> (the chunker split just before the next
                    // table tag) counts too.
                    if (vgDoc)
                        foreach (System.Text.RegularExpressions.Match brm in
                            System.Text.RegularExpressions.Regex.Matches(
                                chunk, @"(?<=>)\s*<br\s*/?>\s*(?=<|$)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            flow.AdvanceY(VgBrBoxPt(chunkAt + brm.Index));
                    RenderHtmlBlocks(chunk, html, flow, page, tb, htmlColor, inlineSvgs, ref htmlFragmentLinkEmitted, htmlFrameIndent, marginLeft, marginRight, marginTop);
                }
                // The break's own line box, by ITS position: the wrapper
                // font's UNROUNDED Verdana-12 line inside the div, the
                // serif default's rounded 13.5 outside it.
                double VgBrBoxPt(int at) => at < vgWrapClose
                    ? Converters.HtmlToPdfConverter.FormGridBasePt
                        * Converters.HtmlToPdfConverter.VerdanaWinLineRatio
                    : Converters.HtmlToPdfConverter.PxLinePt(
                        Converters.HtmlToPdfConverter.FormGridBasePt,
                        Converters.HtmlToPdfConverter.SerifWinLineRatio);
                // A structure-only chunk holding one bare <div>text</div>
                // (plus breaks and closing tags): the text is a top-level
                // flow line in the serif default — Times-12 on its 18px
                // box, Arabic shaped, seated at half-leading + ascent
                // (bbox 505.21..518.49 inside the 505.08 line).
                bool TryVgFlowText(string chunkV, int at)
                {
                    var dm = System.Text.RegularExpressions.Regex.Match(
                        chunkV, @"<div[^>]*>([^<]+)</div>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!dm.Success || dm.Groups[1].Value.Trim().Length == 0) return false;
                    var restV = chunkV.Remove(dm.Index, dm.Length);
                    if (System.Text.RegularExpressions.Regex.Replace(restV,
                            @"<br\s*/?>|</\s*(?:font|div)\s*>|\s+", "",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Length != 0)
                        return false;
                    var serifFont = Text.FontRepository.FindFont("Times New Roman");
                    var serifTtf = serifFont?.SourceFontData?.TtfData;
                    if (serifTtf is null) return false;
                    foreach (System.Text.RegularExpressions.Match brm in
                        System.Text.RegularExpressions.Regex.Matches(chunkV, @"<br\s*/?>",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        if (brm.Index < dm.Index) flow.AdvanceY(VgBrBoxPt(at + brm.Index));
                    var vgfPt = Converters.HtmlToPdfConverter.FormGridBasePt;
                    var vgfBox = Converters.HtmlToPdfConverter.PxLinePt(vgfPt,
                        Converters.HtmlToPdfConverter.SerifWinLineRatio);
                    var vgfText = System.Text.RegularExpressions.Regex.Replace(
                        dm.Groups[1].Value, @"\s+", " ").Trim();
                    var vgfShaped = Text.ArabicTextShaper.ContainsArabic(vgfText)
                        ? Text.ArabicTextShaper.Shape(vgfText) : vgfText;
                    var vgfDict = Table.ResolvePageFontDict(flow.CurrentPage);
                    var (vgfRes, vgfHex) = Text.Type0FontEmbedder.Embed(
                        vgfDict, serifTtf, serifFont!.FontName, vgfShaped,
                        stripSpacesInBaseFont: true);
                    var vgfAsc = vgfPt * Converters.HtmlToPdfConverter.SerifWinAscent;
                    var vgfDesc = vgfPt * Converters.HtmlToPdfConverter.SerifWinDescent;
                    var vgfBase = flow.CurrentY - (vgfBox - vgfAsc - vgfDesc) / 2 - vgfAsc;
                    var vgfNb = new Content.ContentStreamBuilder();
                    vgfNb.SaveState();
                    vgfNb.BeginText().SetFont(vgfRes, vgfPt)
                        .MoveTextPosition(marginLeft, vgfBase);
                    vgfNb.ShowTextHex(vgfHex);
                    vgfNb.EndText();
                    vgfNb.RestoreState();
                    flow.InjectContentAtCursor(vgfNb.Build());
                    flow.AdvanceY(vgfBox);
                    foreach (System.Text.RegularExpressions.Match brm in
                        System.Text.RegularExpressions.Regex.Matches(chunkV, @"<br\s*/?>",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        if (brm.Index > dm.Index) flow.AdvanceY(VgBrBoxPt(at + brm.Index));
                    return true;
                }
                HtmlFramesClosing(chunkAt, chunkEnd);
                chunkAt = chunkEnd;
            }
        }
        else if (Converters.HtmlToPdfConverter.TryParseHtmlStepList(htmlContent, out var stepItems)
                 && RenderHtmlStepList(stepItems, flow, marginLeft, marginRight, htmlColor))
        {
            // Step-list dialect (ul > li with heading blocks) — rendered
            // through the HTML-engine metrics path above.
        }
        else if (Converters.HtmlToPdfConverter.TryParseInlineEmphasisFont(htmlContent,
                     out var iefFace, out var iefPt, out var iefRuns)
                 && RenderInlineEmphasisRuns(iefFace, iefPt, iefRuns, flow, page, marginLeft, marginRight))
        {
            // Single-font emphasis dialect rendered as styled runs.
        }
        else if (Converters.HtmlToPdfConverter.TryParseNestedStyledSpans(htmlContent,
                     out var nsRuns))
        {
            // Nested styled spans: one styled run per style boundary —
            // the canonical renderer emits one text fragment per run
            // (sizes inherit down the chain, a background paints its
            // own span's run), and the split survives to the absorber
            // through the deferred styled-run writer.
            var nsStyled = new List<FlowLayout.StyledRun>();
            double nsMax = 0;
            foreach (var (nsText, nsSize, nsBg) in nsRuns)
            {
                var nsState = new Text.TextState();
                if (nsBg is { } nsB) nsState.BackgroundColor = nsB;
                if (html.TextState?.Font is { } nsFont) nsState.Font = nsFont;
                var sz = nsSize > 0 ? nsSize : 10;
                if (sz > nsMax) nsMax = sz;
                nsStyled.Add(new FlowLayout.StyledRun
                {
                    Text = nsText, Size = sz, State = nsState,
                });
            }
            flow.WriteStyledParagraph(nsStyled, nsMax * 0.12);
        }
        else if (Converters.HtmlToPdfConverter.TryParseMonoFontLineBoxes(htmlContent,
                     out var mfPt, out var mfLines))
        {
            // Monospace pre-formatted report: verbatim Courier line boxes
            // (every &nbsp; a real column space, every <br/> a hard line)
            // on the dialect's 1.377 em line pitch. The report's content
            // box starts 90 pt from the page top (the dialect's own top
            // margin), below the ambient flow top when that sits higher.
            var mfPitch = mfPt * 1.377;
            var mfAscent = 0.562 * mfPt; // Courier cap ascent
            if (flow.CurrentY > page.Height - 90)
                flow.AdvanceY(flow.CurrentY - (page.Height - 90));
            foreach (var mline in mfLines)
            {
                if (flow.CurrentY - mfPitch < flow.BottomMargin) flow.ForceNewPage();
                if (mline.Count > 0)
                {
                    var mb = new Content.ContentStreamBuilder();
                    mb.SaveState();
                    double mx = marginLeft;
                    foreach (var (mtext, mbold) in mline)
                    {
                        if (mtext.Length > 0 && !string.IsNullOrWhiteSpace(mtext))
                        {
                            var mres = Table.RegisterFont(flow.CurrentPage,
                                mbold ? "Courier-Bold" : "Courier");
                            mb.BeginText().SetFont(mres, mfPt)
                              .MoveTextPosition(mx, flow.CurrentY - mfAscent)
                              .ShowText(mtext).EndText();
                        }
                        mx += mtext.Length * 0.6 * mfPt; // fixed-pitch advance
                    }
                    mb.RestoreState();
                    flow.InjectContentAtCursor(mb.Build());
                }
                flow.AdvanceY(mfPitch);
            }
        }
        else if (Converters.HtmlToPdfConverter.HasBlockStructure(htmlContent))
        {
            HtmlFramesOpening(0, htmlContent.Length, htmlContent);
            RenderHtmlBlocks(htmlContent, html, flow, page, tb, htmlColor, inlineSvgs, ref htmlFragmentLinkEmitted, htmlFrameIndent, marginLeft, marginRight, marginTop);
            HtmlFramesClosing(0, htmlContent.Length);
        }
        else
        {
            var plainText = HtmlFragment.StripHtmlTags(htmlContent);
            if (!string.IsNullOrWhiteSpace(plainText))
            {
                var frag = new Text.TextFragment(plainText);
                if (html.TextState is { } htmlTs)
                {
                    if (htmlTs.Font is not null) frag.TextState.Font = htmlTs.Font;
                    if (htmlTs.FontData is not null) frag.TextState.FontData = htmlTs.FontData;
                    if (htmlTs.FontSize > 0) frag.TextState.FontSize = htmlTs.FontSize;
                    if (htmlTs.ForegroundColor is not null) frag.TextState.ForegroundColor = htmlTs.ForegroundColor;
                    frag.TextState.IsBold = htmlTs.IsBold;
                    frag.TextState.IsItalic = htmlTs.IsItalic;
                }
                // Inline <a href> runs survive tag stripping as plain
                // text; find each anchor's text in the stripped output
                // and re-attach its hyperlink so the flow writer emits
                // a Link annotation over the rendered run.
                System.Collections.Generic.List<(int Start, int Length, string Url)>? plainAnchors = null;
                foreach (System.Text.RegularExpressions.Match am in
                    System.Text.RegularExpressions.Regex.Matches(htmlContent,
                        "<a\\b[^>]*href=[\"']([^\"']+)[\"'][^>]*>(.*?)</a>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        | System.Text.RegularExpressions.RegexOptions.Singleline))
                {
                    var aText = HtmlFragment.StripHtmlTags(am.Groups[2].Value);
                    if (aText.Length == 0) continue;
                    var aAt = plainText.IndexOf(aText, StringComparison.Ordinal);
                    if (aAt >= 0)
                        (plainAnchors ??= new()).Add((aAt, aText.Length, am.Groups[1].Value));
                }
                if (plainAnchors is not null)
                    ApplyHtmlAnchorSegments(frag, plainText, plainAnchors);
                if (!flow.WriteTextFragment(frag))
                {
                    frag.Position = new Text.Position(marginLeft, page.Height - marginTop - frag.TextState.FontSize);
                    tb.AppendText(frag);
                }
                if (frag.Rectangle is { } r)
                    html.Rectangle = new System.Drawing.RectangleF(
                        (float)r.LLX, (float)r.LLY, (float)r.Width, (float)r.Height);
            }
            RenderHtmlImages(htmlContent, flow, marginLeft, marginRight, inlineSvgs);
        }
    }

    private bool TryLayoutInlineJoinedRun(List<BaseParagraph> paraList, ref int paraIdx,
        BaseParagraph para, FlowLayout flow)
    {
        // Consecutive paragraphs chained by IsInLineParagraph render as ONE
        // line: a fragment followed by inline members ("MyBrand" +
        // inline HtmlFragment("tm") + inline TextFragment(" New features!"))
        // must not stack one per line. Joinable members become per-segment
        // styled runs of a single composite fragment — HTML members take the
        // serif HTML body face; text members keep their own state.
        if (para is Text.TextFragment or HtmlFragment
            && paraIdx + 1 < paraList.Count
            && ParagraphInlineFlag(paraList[paraIdx + 1])
            && InlineJoinable(para, out _, out _))
        {
            var members = new List<BaseParagraph> { para };
            var k = paraIdx + 1;
            for (; k < paraList.Count && ParagraphInlineFlag(paraList[k])
                   && InlineJoinable(paraList[k], out _, out _); k++)
                members.Add(paraList[k]);
            if (members.Count > 1)
            {
                var joined = new Text.TextFragment();
                var anyHtmlStyled = false;
                foreach (var member in members)
                {
                    InlineJoinable(member, out var mText, out var mSerif);
                    var seg = new Text.TextSegment(mText);
                    if (member is Text.TextFragment mf)
                        seg.TextState.ApplyChangesFrom(mf.TextState);
                    else if (member is HtmlFragment mh
                        && System.Text.RegularExpressions.Regex.Match(mh.HtmlContent ?? "",
                            @"<span\b[^>]*style\s*=\s*(['""])(?<s>[^'""]*)\1",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                            is { Success: true } mSty)
                    {
                        // an inline HTML member styles its run from its
                        // outermost span's own CSS
                        anyHtmlStyled = true;
                        var css = mSty.Groups["s"].Value;
                        var fsm2 = System.Text.RegularExpressions.Regex.Match(css,
                            @"font-size\s*:\s*([\d.]+)\s*(pt|px)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (fsm2.Success)
                        {
                            var v = double.Parse(fsm2.Groups[1].Value,
                                System.Globalization.CultureInfo.InvariantCulture);
                            seg.TextState.FontSize = (float)(fsm2.Groups[2].Value
                                .Equals("px", StringComparison.OrdinalIgnoreCase) ? v * 0.75 : v);
                        }
                        var fam = System.Text.RegularExpressions.Regex.Match(css,
                            @"font-family\s*:\s*['""]?([^;'""]+)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (fam.Success) seg.TextState.FontName = fam.Groups[1].Value.Trim();
                        var col = System.Text.RegularExpressions.Regex.Match(css,
                            @"(?<![-\w])color\s*:\s*([^;]+)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (col.Success && Converters.HtmlToPdfConverter
                                .ParseCssColor(col.Groups[1].Value.Trim()) is { } cc)
                            seg.TextState.ForegroundColor = cc;
                        if (System.Text.RegularExpressions.Regex.IsMatch(css,
                                @"font-style\s*:\s*italic",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            seg.TextState.IsItalic = true;
                        if (System.Text.RegularExpressions.Regex.IsMatch(css,
                                @"text-decoration\s*:\s*line-through",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            seg.TextState.IsStrikeOut = true;
                    }
                    else if (mSerif)
                        seg.TextState.FontName = "TimesNewRoman";
                    joined.Segments.Add(seg);
                }
                if (flow.TryWriteStyledSegmentsLine(joined))
                {
                    paraIdx = k - 1;
                    return true;
                }
                // Too wide for one line: CSS-styled inline members flow as
                // ONE wrapped paragraph. The first line sets on the leading
                // the opening fragment declares, the rest on the HTML
                // 1.12-em rhythm.
                if (anyHtmlStyled)
                {
                    var styRuns2 = new List<FlowLayout.StyledRun>();
                    double maxFs2 = 0, introLs = 0;
                    foreach (var member in members)
                        if (member is Text.TextFragment lsf)
                        {
                            if (lsf.TextState.LineSpacing > introLs) introLs = lsf.TextState.LineSpacing;
                            foreach (Text.TextSegment lss in lsf.Segments)
                                if (lss.TextState.LineSpacing > introLs) introLs = lss.TextState.LineSpacing;
                        }
                    foreach (var seg in joined.Segments)
                    {
                        if (string.IsNullOrEmpty(seg.Text)) continue;
                        var sz = seg.TextState.FontSizeTouched ? (double)seg.TextState.FontSize : 12.0;
                        if (sz > maxFs2) maxFs2 = sz;
                        styRuns2.Add(new FlowLayout.StyledRun
                        {
                            Text = seg.Text, Size = sz, State = seg.TextState,
                        });
                    }
                    if (styRuns2.Count > 0 && maxFs2 > 0)
                    {
                        // members wrap ATOMICALLY: one joins the current line
                        // only when it fits whole, else it opens the next —
                        // and a member longer than a full line word-wraps
                        // alone. Greedy grouping, then one write per line.
                        double RunWidth(FlowLayout.StyledRun r)
                        {
                            var f = r.State.IsItalic ? "Helvetica-Oblique" : "Helvetica";
                            try
                            {
                                return Text.FontRepository.FindFont(f)
                                    ?.MeasureString(r.Text, r.Size) ?? r.Text.Length * r.Size * 0.5;
                            }
                            catch { return r.Text.Length * r.Size * 0.5; }
                        }
                        var lineGroups = new List<List<FlowLayout.StyledRun>> { new() };
                        var lw = 0.0;
                        foreach (var r in styRuns2)
                        {
                            var w = RunWidth(r);
                            if (lineGroups[^1].Count > 0 && lw + w > flow.CurWidth + 0.5)
                            { lineGroups.Add(new()); lw = 0; }
                            lineGroups[^1].Add(r);
                            lw += w;
                        }
                        // the first line sets on the leading the opening
                        // fragment declares (its box closes at the baseline);
                        // the rest keep the HTML 1.12-em rhythm
                        var htmlLead = maxFs2 * 0.12;
                        for (var lg = 0; lg < lineGroups.Count; lg++)
                            flow.WriteStyledParagraph(lineGroups[lg],
                                lg == 0 && introLs > 0
                                    ? introLs - 0.2075 * maxFs2 : htmlLead);
                        paraIdx = k - 1;
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
