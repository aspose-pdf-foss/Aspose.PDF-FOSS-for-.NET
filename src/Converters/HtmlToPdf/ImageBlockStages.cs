using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The escaped-attribute dialect's placeholder for an image that did not load.</summary>
    private static void PlaceEscapedMissingImage(HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth)
    {
        // A broken image renders the browser's 32×32 placeholder icon at
        // the content edge (the escaped float:/size styles can never
        // apply). Measured: the icon's top rides 9.47 pt above the flow
        // cursor — a heading's bottom margin does not span a replaced
        // box — and a following grid's top border lands 1.38 pt under
        // the icon (the cursor sits one 0.9em ascent below that edge).
        var iconTop = flow.y + 9.47;
        if (iconTop - 32 < marginBottom)
        {
            flow.page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(flow.page, docFontDict);
            flow.y = FreshPageTopY(profile, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
            iconTop = flow.y;
        }
        (var phName, flow.flowIconRef) = RegisterPlaceholderIcon(doc, flow.page, flow.flowIconRef, masked: true);
        flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
            $"q 32 0 0 32 {marginLeft + 1:0.##} {iconTop - 32:0.##} cm /{phName} Do Q\n")));
        // The browser frames a broken image with a 1 pt INSET border —
        // top/left #555, bottom/right #aaa — half a point outside the icon.
        var phDark = ParseCssColor("#555555");
        var phLite = ParseCssColor("#AAAAAA");
        DrawBox(flow.page, marginLeft, iconTop, 34, 1, null, 0, phDark);
        DrawBox(flow.page, marginLeft, iconTop - 33, 34, 1, null, 0, phLite);
        DrawBox(flow.page, marginLeft, iconTop - 33, 1, 34, null, 0, phDark);
        DrawBox(flow.page, marginLeft + 33, iconTop - 33, 1, 34, null, 0, phLite);
        flow.contentPage = flow.page;
        flow.y = iconTop - 33 - 0.9 * 12;
    }

    /// <summary>The MSO-filtered dialect's placeholder for an image that did not load.</summary>
    private static void PlaceMsoMissingImage(HtmlFlowCursor flow, Document doc, double marginLeft, double pageHeight, double lineHeight)
    {
        // Word-filtered pages: an unloadable image leaves the browser's
        // 32×32 placeholder while its paragraph keeps ONE empty UA line
        // of flow. Both lead placeholders ride 14.4 under the top margin
        // (measured); the banner in the absolutely positioned span seats
        // at 90 + (left 0 + margin-left −96px)·0.75 + the 1 pt frame
        // inset = 19, the inline one at the content edge.
        var mphX = flow.msoBrokenImgCount == 0 ? 90.0 - 72.0 + 1.0 : marginLeft + 0.75;
        flow.msoBrokenImgCount++;
        var mphTop = pageHeight - 72.0 - MsoBrokenImgDropPt;
        (var mphName, flow.flowIconRef) = RegisterPlaceholderIcon(doc, flow.page, flow.flowIconRef, masked: true);
        flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
            $"q 32 0 0 32 {mphX:0.##} {mphTop - 32:0.##} cm /{mphName} Do Q\n")));
        var mphDark = ParseCssColor("#555555");
        var mphLite = ParseCssColor("#AAAAAA");
        DrawBox(flow.page, mphX - 1, mphTop + 1, 34, 1, null, 0, mphDark);
        DrawBox(flow.page, mphX - 1, mphTop - 32, 34, 1, null, 0, mphLite);
        DrawBox(flow.page, mphX - 1, mphTop - 32, 1, 34, null, 0, mphDark);
        DrawBox(flow.page, mphX + 32, mphTop - 32, 1, 34, null, 0, mphLite);
        flow.contentPage = flow.page;
        // the paragraph's one empty UA text line (an image block
        // carries no font size, so the per-block line height is 0 here)
        flow.y -= lineHeight > 1 ? lineHeight : PpLineBoxPt;
    }

    /// <summary>The standard-serif dialect's placeholder box for an image that did not load.</summary>
    private static void PlaceUaMissingImage(Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth)
    {
        // UA flow: an unloadable image with a DECLARED box (class or
        // attribute size) reserves that box — it draws the
        // browser's bordered placeholder frame with the 32×32 icon at
        // its top-left, and the flow resumes below it (the licensing
        // letter's 453×271 px photo box).
        var phW = block.ImageWidth * 0.75;
        var phH = block.ImageHeight * 0.75;
        var phX = marginLeft + block.LeftIndent;
        var phTop = flow.y;
        if (phTop - phH < marginBottom)
        {
            flow.page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(flow.page, docFontDict);
            flow.y = FreshPageTopY(profile, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
            phTop = flow.y;
        }
        var uaPhDark = ParseCssColor("#555555");
        var uaPhLite = ParseCssColor("#AAAAAA");
        DrawBox(flow.page, phX, phTop, phW, 1, null, 0, uaPhDark);
        DrawBox(flow.page, phX, phTop - phH, phW, 1, null, 0, uaPhLite);
        DrawBox(flow.page, phX, phTop - phH, 1, phH, null, 0, uaPhDark);
        DrawBox(flow.page, phX + phW - 1, phTop - phH, 1, phH, null, 0, uaPhLite);
        (var uaPhName, flow.flowIconRef) = RegisterPlaceholderIcon(doc, flow.page, flow.flowIconRef, masked: true);
        flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
            $"q 32 0 0 32 {phX + 3:0.##} {phTop - 3 - 32:0.##} cm /{uaPhName} Do Q\n")));
        flow.contentPage = flow.page;
        flow.y -= phH;
    }

    // The descent of the Word mail's face under its baseline (Calibri's descender, 512/2048 em).
    private const double WordMailDescentEm = 0.25;

    /// <summary>Size the loaded image to its declared or natural box, break the page when it does not fit, and draw it at the flow position with its float, caption and margins.</summary>
    private static bool PlaceImage(byte[] bytes, double svgNatW, double svgNatH, Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, Page? vectorPage = null)
    {
        var ib = new ImageBlockState();
        ib.natW = 0;
        ib.natH = 0;
        if (!ResolveImageBox(ib, bytes, svgNatW, svgNatH, block, flow, profile, marginLeft, marginTop, pageHeight, vectorPage)) return false;
        if (!BreakPageForImage(ib, bytes, block, flow, profile, doc, docFontDict, bandStack, marginBottom, marginLeft, marginTop, pageHeight, pageWidth)) return false;
        DrawImageAtFlow(ib, bytes, block, flow, profile, marginLeft, marginTop, pageHeight, vectorPage);
        if (!SettleImageFloat(ib, block, flow, profile, bandStack)) return false;
        return true;
    }

    /// <summary>After the image: a floated image opens a float band beside itself, otherwise the flow drops below it and spends its bottom padding and dialect margin.</summary>
    private static bool SettleImageFloat(ImageBlockState ib, Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack)
    {
        if (profile.floatImageDoc && block.FloatLeft)
        {
            flow.floatBottomY = double.IsNegativeInfinity(flow.floatBottomY)
                ? flow.y - ib.h - ib.padBottom
                : System.Math.Min(flow.floatBottomY, flow.y - ib.h - ib.padBottom);
            // The float's occupied width is measured from the content edge,
            // so it counts the image's OWN margin as well as its box - and the
            // margin facing the flow, which is where wrapped text stops.
            flow.floatIndentPt = block.ImageIndentPt + ib.w
                + (block.ImageFloatGutterPt ?? FloatGutterPt);
            flow.floatPage = flow.page;
            flow.y = ib.imgFlowY;
            flow.lastWasHardBreak = false;
            return false;   // the block is laid out; the loop this came from would continue
        }
        // A RIGHT float leaves the flow the same way: the cursor stays put and
        // the blocks below keep their top, with their lines shortened on the
        // RIGHT until the flow has passed the image's bottom edge. Two floats
        // in a row share the deepest bottom.
        if (profile.floatImageDoc && block.FloatRight)
        {
            flow.floatRightTopY = flow.y;
            flow.floatRightBottomY = flow.y - ib.h - ib.padBottom;
            flow.floatRightInsetPt = block.ImageIndentPt + ib.w
                + (block.ImageFloatGutterPt ?? FloatGutterPt);
            // The float left the flow: the cursor never moved for it.
            flow.y = ib.imgFlowY;
            flow.lastWasHardBreak = false;
            return false;   // the block is laid out; the loop this came from would continue
        }
        flow.y -= ib.h + ib.padBottom;
        // Inline image in a band column: the image sits on a text line box,
        // so the line's tail (descent + leading) separates it from the next
        // paragraph — without it the following text's ascent rises to the
        // image's bottom edge (the legacy baseline-at-cursor model).
        if (profile.floatBandDoc && bandStack.Count > 0) flow.y -= 9;
        // Form dialect: same baseline-at-cursor problem in the main flow —
        // the following section heading's ascent plus the heading gap
        // kept below a block image.
        else if (profile.formHorizontalDoc) flow.y -= 25.5;
        return true;
    }

    /// <summary>Paint the picture in its box at (<paramref name="x"/>, <paramref name="top"/>): the SVG's
    /// vector content when there is one, else the raster; then the widget card's border around it.</summary>
    private static void PaintImageBox(ImageBlockState ib, byte[] bytes, Block block, HtmlFlowCursor flow, Page? vectorPage, double x, double top)
    {
        ib.imgX = x;
        if (vectorPage is null || !TryStampVectorImage(vectorPage, ib, flow.page, top))
            flow.page.AddImage(bytes, new Rectangle(x, top - ib.h, x + ib.w, top));
        // The widget card's border around the picture: its lines run through the border's
        // centre, an inset less half a border outside the picture on the left, right and top,
        // and half a border below it - the card closes on the picture's bottom edge.
        if (block.ImageCardFrame is { } frame && block.ImageCardFrameInsetPt > 0 && block.ImageCardFrameBorderPt > 0)
        {
            var half = block.ImageCardFrameBorderPt / 2;
            var outset = block.ImageCardFrameInsetPt - half;
            var fl = x - outset;
            var fr = x + ib.w + outset;
            var ft = top + outset;
            var fb = top - ib.h - half;
            var finv = System.Globalization.CultureInfo.InvariantCulture;
            flow.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(finv,
                $"q {frame.R / 255.0:0.###} {frame.G / 255.0:0.###} {frame.B / 255.0:0.###} RG {block.ImageCardFrameBorderPt:0.##} w " +
                $"{fl:0.##} {fb:0.##} {fr - fl:0.##} {ft - fb:0.##} re S Q\n")));
        }
    }

    /// <summary>Room outside the SVG viewport for a stroke drawn on its edge: the thinnest device
    /// line at any resolution the page is rendered at.</summary>
    private const double EdgeStrokeOutsetPt = 0.375;

    /// <summary>Draw an SVG's own vector content in the image's box - the reference renders an
    /// inline chart as real paths and text, not as a bitmap of them. The SVG's page is imported
    /// as a self-contained form XObject: its own resources, its box as the clip, so nothing it
    /// paints reaches past the image box, and nothing collides with the page's own resources.
    /// False when the import fails, and the caller draws the raster instead.</summary>
    private static bool TryStampVectorImage(Page vectorPage, ImageBlockState ib, Page page, double top)
    {
        try
        {
            var stamp = new PdfPageStamp(vectorPage)
            {
                XIndent = ib.imgX,
                YIndent = top - ib.h,
                Width = ib.w,
                Height = ib.h,
                CarryAnnotations = false,
                PromoteFontsToPage = false,
                BBoxOutsetPt = EdgeStrokeOutsetPt,
            };
            stamp.ApplyTo(page);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Draw the image at the flow position with its float offset, the chart-card shadow and the content page bookkeeping.</summary>
    private static void DrawImageAtFlow(ImageBlockState ib, byte[] bytes, Block block, HtmlFlowCursor flow, HtmlDocProfile profile, double marginLeft, double marginTop, double pageHeight, Page? vectorPage)
    {
        // Word mail: a block image after a text line starts at that line's BOTTOM edge (its
        // baseline plus the face's descent), not at the next baseline the legacy cursor rests
        // on (measured: the mail's table picture sat 11 pt too low).
        if (profile.wordMailDoc && flow.prevBlockWasText && flow.prevFlowLineHeight > 0 && flow.prevFlowFontSize > 0)
            flow.y += flow.prevFlowLineHeight - flow.prevFlowFontSize * WordMailDescentEm;
        flow.y -= ib.padTop;
        ib.imgX = profile.floatImageDoc && block.FloatRight
            ? marginLeft + flow.contentWidth - ib.w - block.ImageIndentPt
            : ib.rtlOverflow ? marginLeft + flow.contentWidth - ib.w
            : block.ImageCentered ? marginLeft + (flow.contentWidth - ib.w) / 2
            : marginLeft + block.ImageIndentPt;
        ib.floatDropY = flow.y;
        if (profile.floatImageDoc && block.FloatRight && flow.floatIndentPt > 0
            && ib.imgX < marginLeft + flow.floatIndentPt
            && !double.IsNegativeInfinity(flow.floatBottomY))
            ib.floatDropY = flow.floatBottomY;
        ib.imgFlowY = flow.y;
        flow.y = ib.floatDropY;
        try
        {
            if (block.ImageRotateDeg != 0)
            {
                // CSS transform: rotate(θ) spins the image about its layout
                // box centre and leaves the layout box (and the flow advance)
                // unrotated. CSS angles are clockwise on the page; the stamp
                // matrix is PDF counter-clockwise, hence the sign flip. The
                // stamp anchors at the ROTATED bounding box's bottom-left.
                var rad = block.ImageRotateDeg * Math.PI / 180.0;
                var bw = Math.Abs(ib.w * Math.Cos(rad)) + Math.Abs(ib.h * Math.Sin(rad));
                var bh = Math.Abs(ib.w * Math.Sin(rad)) + Math.Abs(ib.h * Math.Cos(rad));
                var cx = ib.imgX + ib.w / 2;
                var cy = flow.y - ib.h / 2;
                var stamp = ImageStamp.FromEncodedBytes(bytes);
                stamp.XIndent = cx - bw / 2;
                stamp.YIndent = cy - bh / 2;
                stamp.DisplayWidth = ib.w;
                stamp.DisplayHeight = ib.h;
                stamp.RotateAngle = -block.ImageRotateDeg;
                stamp.ApplyTo(flow.page);
            }
            else
                PaintImageBox(ib, bytes, block, flow, vectorPage, ib.imgX, flow.y);
        }
        catch { /* undecodable image: skip, keep the flow going */ }
        // box-shadow (2px offset, 2px blur — the only visible chrome, the
        // card's fill and border being white). Approximate the bitmap
        // a browser renders with the offset right/bottom bars plus a hairline
        // ring. Card box recovered from the content position: its left chrome
        // insets the image, its inner box is the col's content width, and it
        // closes one chrome below the chart.
        if (profile.chartCardDoc && block.ImageCardShadow is { } cardShadow
            && block.ImageCardChromePt > 0)
        {
            var chrome = block.ImageCardChromePt;
            var cardL = marginLeft + block.ImageIndentPt - chrome;
            var cardInnerW = flow.contentWidth - block.ImageWidenPadPt;
            var cardR = cardL + cardInnerW + 2 * chrome;
            var cardTopPdf = pageHeight - marginTop;
            var cardBottomPdf = flow.y - ib.h - chrome;
            const double ShadowOffPt = 1.5;   // 2px offset
            const double ShadowExtPt = 2.75;  // offset + blur extent, measured on the expected bitmap
            var inv2 = System.Globalization.CultureInfo.InvariantCulture;
            var sr = cardShadow.R / 255.0; var sg = cardShadow.G / 255.0; var sbv = cardShadow.B / 255.0;
            var ops = Compat.Format(inv2,
                $"q {sr:0.###} {sg:0.###} {sbv:0.###} rg " +
                $"{cardR:0.##} {cardBottomPdf - ShadowExtPt:0.##} {ShadowExtPt:0.##} {cardTopPdf - ShadowOffPt - (cardBottomPdf - ShadowExtPt):0.##} re f " +
                $"{cardL + ShadowOffPt:0.##} {cardBottomPdf - ShadowExtPt:0.##} {cardR - cardL - ShadowOffPt + ShadowExtPt:0.##} {ShadowExtPt:0.##} re f " +
                $"{sr:0.###} {sg:0.###} {sbv:0.###} RG 0.5 w " +
                $"{cardL:0.##} {cardBottomPdf:0.##} {cardR - cardL:0.##} {cardTopPdf - cardBottomPdf:0.##} re S Q\n");
            flow.page.AddContentStream(Encoding.ASCII.GetBytes(ops));
        }
        flow.contentPage = flow.page;
        // A LEFT-FLOATED image leaves the flow: the cursor stays where it
        // was and the block boxes below keep starting at the content top —
        // only their LINES are shortened, on the right of the image, until
        // the flow has passed its bottom edge.
    }

    /// <summary>An image that does not fit the page opens a new page, in the MSO dialect with its caption rules.</summary>
    /// <summary>How many content bands an image may cross and still be treated as a tall
    /// picture that overflows its sheet. Beyond this it is a degenerate size rather than a
    /// layout, and the flow takes the ordinary page break instead of repeating it per band.
    /// The real cases measured span two and three bands; the guard is set well clear of them.</summary>
    private const int OverflowImageMaxBands = 16;

    private static bool BreakPageForImage(ImageBlockState ib, byte[] bytes, Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth)
    {
        // An image TALLER THAN A WHOLE CONTENT BAND can never be made to fit by breaking the
        // page, so the reference does not try: it draws at the flow position and lets the image
        // run off the sheet, redrawing it on each band it reaches. Breaking instead loses the
        // part above the break and seats the rest at the top margin, which is a page short and a
        // band out (measured on the over-tall sample: the reference draws one 1902.8 pt image at
        // 91.5 on THREE sheets, 698 apart; we drew it once, on sheet two, at 72).
        var bandH = pageHeight - marginTop - marginBottom;
        // …but only for a height that is a LAYOUT. An image tens of bands tall is a sizing
        // defect upstream, not a tall picture, and repeating it per band turns one sheet into
        // hundreds (measured: a synthesised 124815 pt strip took a 2-page document to 179).
        // Such a height keeps the ordinary break, which bounds the damage to one sheet.
        var tallerThanAnyPage = bandH > 0 && ib.h > bandH
            && ib.h <= bandH * OverflowImageMaxBands;
        if ((profile.msoFilteredDoc || tallerThanAnyPage)
            && flow.y - ib.h - ib.padTop - ib.padBottom < marginBottom)
        {
            flow.y -= ib.padTop;
            var crossX = block.ImageCentered
                ? marginLeft + (flow.contentWidth - ib.w) / 2
                : marginLeft + block.ImageIndentPt;
            var crossBand = bandH;
            var crossInv = System.Globalization.CultureInfo.InvariantCulture;
            // Each sheet's content clips at the margin
            // band — the rows past the bottom margin appear only on
            // the continuation page (which clips above its top
            // margin in turn: no row repeats).
            void CrossDraw(Page cp, double topY, bool clipTop)
            {
                var clipLo = marginBottom;
                var clipHi = clipTop ? pageHeight - marginTop : pageHeight;
                cp.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(crossInv,
                    $"q 0 {clipLo:0.##} {pageWidth:0.##} {clipHi - clipLo:0.##} re W n\n")));
                try { cp.AddImage(bytes, new Rectangle(crossX, topY - ib.h, crossX + ib.w, topY)); }
                catch { /* undecodable image: keep the flow */ }
                cp.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
            }
            CrossDraw(flow.page, flow.y, clipTop: false);
            var crossTop = flow.y;
            while (crossBand > 0 && crossTop - ib.h < marginBottom - 0.01)
            {
                flow.page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(flow.page, docFontDict);
                crossTop += crossBand;
                CrossDraw(flow.page, crossTop, clipTop: true);
            }
            flow.y = crossTop - ib.h - ib.padBottom;
            flow.contentPage = flow.page;
            flow.lastWasHardBreak = false;
            return false;   // the block is laid out; the loop this came from would continue
        }
        if (flow.y - ib.h - ib.padTop - ib.padBottom < marginBottom)
        {
            // Inside a float column the overflow is clipped, not paginated.
            if (profile.floatBandDoc && bandStack.Count > 0)
            {
                flow.bandColClipped = true;
                flow.lastWasHardBreak = false;
                return false;   // the block is laid out; the loop this came from would continue
            }
            flow.page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(flow.page, docFontDict);
            flow.y = pageHeight - marginTop; flow.pendingTopDrop = profile.hasZeroTopMargin;
        }
        return true;
    }

    /// <summary>The image's natural size and its declared or fitted box, clamped to the available width and the max-width fraction.</summary>
    private static bool ResolveImageBox(ImageBlockState ib, byte[] bytes, double svgNatW, double svgNatH, Block block, HtmlFlowCursor flow, HtmlDocProfile profile, double marginLeft, double marginTop, double pageHeight, Page? vectorPage)
    {
        if (svgNatW > 0 && svgNatH > 0) { ib.natW = svgNatW; ib.natH = svgNatH; }
        else
        {
            if (TryReadImagePixelSize(bytes) is (var pxW, var pxH) && pxW > 0 && pxH > 0)
            {
                ib.natW = pxW * 0.75;
                ib.natH = pxH * 0.75;
            }
        }
        ib.w = block.ImageWidth > 0 ? block.ImageWidth * 0.75 : 0;
        ib.h = block.ImageHeight > 0 ? block.ImageHeight * 0.75 : 0;
        if (ib.w <= 0 && ib.h <= 0) { ib.w = ib.natW > 0 ? ib.natW : 72; ib.h = ib.natH > 0 ? ib.natH : 72; }
        else if (ib.h <= 0) ib.h = (ib.natW > 0 && ib.natH > 0) ? ib.w * ib.natH / ib.natW : ib.w;
        else if (ib.w <= 0) ib.w = (ib.natW > 0 && ib.natH > 0) ? ib.h * ib.natW / ib.natH : ib.h;
        // Absolutely positioned image: seats at the page's content origin plus its seat
        // offsets (the chrome of the containing block its nearest positioned ancestor
        // establishes, its own left/top and its own margins - see AbsoluteImageSeat.cs)
        // and leaves the flow - no width clamp, no cursor advance.
        if (block.ImageAbsPos)
        {
            var apX = marginLeft + block.ImageAbsLeftPt;
            var apTop = pageHeight - marginTop - block.ImageAbsTopPt;
            try
            {
                PaintImageBox(ib, bytes, block, flow, vectorPage, apX, apTop);
            }
            catch { /* undecodable image: skip, keep the flow going */ }
            flow.contentPage = flow.page;
            flow.lastWasHardBreak = false;
            return false;   // the block is laid out; the loop this came from would continue
        }
        ib.availW = flow.contentWidth;
        // an inline %-max-width caps the drawn box at its share of the
        // content width (aspect kept)
        if (block.ImageMaxWFrac > 0 && ib.availW > 0 && ib.w > ib.availW * block.ImageMaxWFrac)
        {
            ib.h *= ib.availW * block.ImageMaxWFrac / ib.w;
            ib.w = ib.availW * block.ImageMaxWFrac;
        }
        ib.rtlOverflow = profile.rtlDoc && ib.availW > 0 && ib.w > ib.availW;
        // Chart-card: the widened page fits the chart at NATURAL size; its
        // indented right edge may pass the content box by the container
        // chrome (it draws there, unclipped) — never downscale.
        // A sheet grown to this image's ink draws it at its declared size: it may
        // overrun the text box by the body inset, ending one page margin from the edge.
        if (ib.availW > 0 && ib.w > ib.availW && !ib.rtlOverflow && !profile.chartCardDoc
            && ib.w > profile.inkWidenPt + ImageInkAllowancePt)
        { ib.h *= ib.availW / ib.w; ib.w = ib.availW; }
        ib.padTop = block.ImagePadTopPx * 0.75;
        ib.padBottom = block.ImagePadBottomPx * 0.75;
        // Word-filtered pages: an over-tall image draws AT the flow
        // position and CROSSES the page boundary — each continuation
        // page redraws it shifted up by one content band (measured:
        // the snip capture runs 290..1076 across two sheets).
        return true;
    }
}
