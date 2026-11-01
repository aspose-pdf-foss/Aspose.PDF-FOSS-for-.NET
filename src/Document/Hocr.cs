using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>External OCR callback. Receives a rasterised page image
    /// and returns the page's recognised text as hOCR-formatted XML.</summary>
    public delegate string CallBackGetHocr(System.Drawing.Image img);

    /// <summary>External OCR callback variant that also passes the source
    /// <see cref="Page"/> so the implementation can inspect existing
    /// content (e.g. /Rotate) before producing hOCR.</summary>
    public delegate string CallBackGetHocrWithPage(System.Drawing.Image img, Page page);

    /// <summary>
    /// Render each page to an image, hand it to the OCR <paramref name="callback"/>,
    /// then overlay the returned hOCR text on the page as an invisible
    /// text layer (text rendering mode 3, /Tr 3) so the PDF becomes
    /// searchable / copy-pasteable.
    /// </summary>
    /// <param name="callback">OCR engine callback.</param>
    /// <param name="flattenImages">When true, the original page content is
    /// replaced by the rasterised image so the visible appearance is the
    /// rendered output (typical "OCR'd from scan" workflow).</param>
    /// <returns>True if every page successfully processed; false otherwise.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public bool Convert(CallBackGetHocr callback, bool flattenImages)
    {
        if (callback is null) return false;
        return ApplyHocrCallback((img, _) => callback(img), flattenImages);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public bool Convert(CallBackGetHocrWithPage callback, bool flattenImages)
    {
        if (callback is null) return false;
        return ApplyHocrCallback((img, page) => callback(img, page), flattenImages);
    }

    /// <summary>Convenience overload: same as
    /// <see cref="Convert(CallBackGetHocr, bool)"/> with
    /// <c>flattenImages = false</c>. FOSS-extra for the legacy test
    /// surface that calls <c>doc.Convert(callback)</c> without the
    /// flatten flag.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public bool Convert(CallBackGetHocr callback) => Convert(callback, false);

    /// <summary>Convenience overload of
    /// <see cref="Convert(CallBackGetHocrWithPage, bool)"/> with
    /// <c>flattenImages = false</c>.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public bool Convert(CallBackGetHocrWithPage callback) => Convert(callback, false);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private bool ApplyHocrCallback(Func<System.Drawing.Image, Page, string> invoke, bool flattenImages)
    {
        // The callback signature hands the page over as a System.Drawing.Image, so this
        // API is Windows-only by its own contract - hence the SupportedOSPlatform above.
        // That attribute is an analyser hint and skips nothing at run time, so say it here
        // too. Off Windows every page would fail to materialise an Image and the loop
        // below would quietly return false, which reads as "nothing was recognised" - a
        // caller cannot tell that apart from the platform simply not having GDI+.
        if (!Compat.IsWindows())
            throw new PlatformNotSupportedException(
                "Document.Convert(CallBackGetHocr) hands each page to the callback as a "
                + "System.Drawing.Image, which requires GDI+ and is available on Windows only.");
        var ok = true;
        // Track whether the callback's hOCR actually yielded recognised text that
        // got overlaid. Convert reports success only when real OCR text was applied;
        // an empty / wordless hOCR result (e.g. "<test/>") makes it return false so
        // callers can tell that nothing was recognised.
        var appliedAny = false;
        // A page is finished - its flattened image set, its text overlaid - one page late:
        // the image stream deflates on another thread while the next page renders, and
        // the pages are still finished in order on this thread.
        RecognisedPage? pending = null;
        foreach (var page in Pages)
        {
            // Render each page and hand it to the callback to OCR; the returned
            // hOCR text is overlaid as invisible text. Pages without rasterised
            // images still render (to a blank raster) and receive the callback so
            // the caller can supply text for any page.
            if (page is null) continue;
            try
            {
                // The image handed to the OCR callback is the page's own scan
                // (its dominant raster image) when one exists; a page render is
                // only the fallback for pages without images. A clean JPEG is
                // handed stream-backed so its Save round-trips the original
                // bytes; one carrying EXIF metadata is normalised first (the
                // metadata does not describe the overlay geometry), which hands a
                // decoded bitmap whose Save re-encodes the pixels.
                var handImg = TryGetDominantImage(page);

                // One render serves both ends: the callback's image when the page has no
                // scan of its own, and the flattened page. The callback's PNG is transient,
                // so it is stored rather than deflated; the flattened page takes the pixels
                // as they are, which is what decoding that PNG would have given.
                var rendered = flattenImages || handImg is null ? new PngDevice().Render(page) : null;

                string hocr;
                using (var img = handImg ?? ImageOfRender(rendered!))
                {
                    hocr = invoke(img, page) ?? string.Empty;
                }

                var flattened = flattenImages && rendered is not null
                    ? Task.Run(() => ImageStamp.FromRgba(rendered.Data, rendered.Width, rendered.Height))
                    : null;
                var done = pending;
                pending = new RecognisedPage(page, flattened, hocr);
                if (Finish(done)) appliedAny = true;
            }
            catch
            {
                ok = false;
            }
        }
        try
        {
            if (Finish(pending)) appliedAny = true;
        }
        catch
        {
            ok = false;
        }
        return ok && appliedAny;
    }

    /// <summary>A page the callback has seen, waiting to be finished: its flattened image
    /// (deflating), and the hOCR to overlay.</summary>
    private sealed record RecognisedPage(Page Page, Task<ImageStamp>? Flattened, string Hocr);

    /// <summary>Set the flattened image and overlay the text; true when words were overlaid.</summary>
    private static bool Finish(RecognisedPage? done)
    {
        if (done is null) return false;
        if (done.Flattened is { } flattened) ReplacePageWithImage(done.Page, flattened.Result);
        return OverlayHocrAsInvisibleText(done.Page, done.Hocr) > 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static System.Drawing.Image ImageOfRender(Devices.RgbaBuffer rendered)
    {
        var png = IO.PngEncoder.Encode(rendered.Data, rendered.Width, rendered.Height,
            colorType: 6, level: IO.Filters.DeflateLevel.Store);
        return System.Drawing.Image.FromStream(new MemoryStream(png));
    }

    /// <summary>Find the page's dominant (largest-area) raster-image placement.
    /// The hOCR overlay anchors to it — the recognised raster IS the page's scan
    /// image, so word boxes map into the image's placement rectangle on the page
    /// rather than the page box. Null for pages without images.</summary>
    private static ImagePlacement? FindDominantImagePlacement(Page page)
    {
        try
        {
            var absorber = new ImagePlacementAbsorber();
            absorber.Visit(page);
            ImagePlacement? best = null;
            double bestArea = 0;
            foreach (var p in absorber.ImagePlacements)
            {
                var area = p.Rectangle.Width * p.Rectangle.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = p;
                }
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Build the System.Drawing image handed to the OCR callback from
    /// the page's dominant embedded image. A DCTDecode stream is loaded from its
    /// original bytes (undecoded — GDI+ then round-trips them on re-save); other
    /// formats decode through the image's Save path. Null when the page has no
    /// usable image.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static System.Drawing.Image? TryGetDominantImage(Page page)
    {
        var placement = FindDominantImagePlacement(page);
        if (placement?.Image is null) return null;
        try
        {
            var stream = placement.Image.Stream;
            if (stream.Dict.GetName("Filter") == "DCTDecode")
            {
                var raw = stream.RawData;
                // EXIF-carrying JPEG: normalise by decoding to a plain bitmap
                // (drops the APP1 metadata; a later Save re-encodes the pixels).
                // A metadata-free JPEG stays stream-backed and round-trips.
                if (HasExifApp1(raw))
                {
                    using var encoded = System.Drawing.Image.FromStream(new MemoryStream(raw));
                    return new System.Drawing.Bitmap(encoded);
                }
                return System.Drawing.Image.FromStream(new MemoryStream(raw));
            }
            // A bilevel scan goes over as the 1 bpp bitmap it is. Anything else goes over as
            // a PNG that only carries the pixels to GDI+ in this process, so it is stored,
            // not deflated.
            if (placement.Image.OneBitBitmap() is { } bilevel) return bilevel;
            return System.Drawing.Image.FromStream(new MemoryStream(placement.Image.ToPng(IO.Filters.DeflateLevel.Store)));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when the JPEG carries an EXIF APP1 (0xFFE1) segment in its
    /// pre-scan marker run.</summary>
    private static bool HasExifApp1(byte[] jpg)
    {
        var i = 2;
        while (i < jpg.Length - 4)
        {
            if (jpg[i] != 0xFF) return false;
            var marker = jpg[i + 1];
            if (marker == 0xE1) return true;
            if (marker == 0xDA) return false;   // start of scan — no more metadata
            var segLen = (jpg[i + 2] << 8) | jpg[i + 3];
            if (segLen < 2) return false;
            i += 2 + segLen;
        }
        return false;
    }

    // The word content is captured lazily as (.*?) rather than [^<]* so a word
    // wrapped in inline markup (e.g. <strong>is</strong>, <em>…</em>) is matched
    // too — its tags are stripped afterwards. [^<]* would stop at the inner '<'
    // and fail the </span> anchor, silently dropping every bold/italic word from
    // the searchable overlay.
    private static readonly Regex HocrWordRegex = new(
        @"<span[^>]+class=['""]ocrx?_word['""][^>]*title=['""][^'""]*bbox\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)[^'""]*['""][^>]*>(.*?)</span>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, RegexLimits.Timeout);

    private static readonly Regex HocrInlineTagRegex = new(@"<[^>]+>", RegexOptions.Compiled, RegexLimits.Timeout);

    // Combined scan over the hOCR body: each match is either an ocr_line container
    // (group 1 = "ocr_line", groups 2-5 = its bbox) or an ocrx_word (groups 6-9 =
    // bbox, group 10 = word markup). Iterating in document order lets us assign each
    // word to its enclosing ocr_line, so the overlay preserves the OCR's own line
    // structure (words on one visual line share a baseline) rather than scattering
    // every word onto its own jittered baseline.
    private static readonly Regex HocrLineOrWordRegex = new(
        @"class=['""](ocr_line)['""][^>]*title=['""][^'""]*bbox\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)" +
        @"|<span[^>]+class=['""]ocrx?_word['""][^>]*title=['""][^'""]*bbox\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)[^'""]*['""][^>]*>(.*?)</span>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, RegexLimits.Timeout);

    /// <summary>The <c>ocr_page</c> element carries the source raster's pixel
    /// dimensions as <c>bbox 0 0 W H</c>. These are the correct denominators for
    /// mapping word boxes onto the PDF page; using the rightmost/bottommost word
    /// edge instead would stretch the overlay so the last word touches the page
    /// edge. The page title is single-quoted but embeds double quotes
    /// (<c>image ""; bbox …</c>), so a <c>[^'"]</c> scan would stop short of
    /// bbox — match within the tag (up to '&gt;') instead.</summary>
    private static readonly Regex HocrPageBBoxRegex = new(
        @"class=['""]ocr_page['""][^>]*?bbox\s+\d+\s+\d+\s+(\d+)\s+(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);

    /// <summary>Overlay the recognised hOCR words on the page as an invisible
    /// text layer. Returns the number of words actually overlaid (0 when the hOCR
    /// is empty, malformed, or contains no word spans).
    /// <paramref name="mendModel"/> selects the legacy Page.AddImage(hocr, ...)
    /// writer model (measured): word boxes map by the PAGE
    /// box ratios (x by width, y by height - never the scan image's letterboxed
    /// placement) and every word takes its OWN descent lift; the OCR Convert
    /// pipeline keeps the image-anchored, per-line-lift model it was calibrated
    /// on.</summary>
    internal static int OverlayHocrAsInvisibleText(Page page, string hocr, bool mendModel = false)
    {
        var ho = new HocrOverlayState();
        ho.page = page;
        ho.hocr = hocr;
        ho.mendModel = mendModel;
        if (string.IsNullOrWhiteSpace(ho.hocr)) return 0;
        ho.matches = SafeRegex.Matches(HocrWordRegex, ho.hocr);
        if (ho.matches.Count == 0) return 0;

        ho.rect = ho.page.GetPageRect(considerRotation: false);
        ho.pageWidth = ho.rect.Width;
        ho.pageHeight = ho.rect.Height;
        if (ho.pageWidth <= 0 || ho.pageHeight <= 0) return 0;

        ho.rotation = ho.page.Rotate switch
        {
            Rotation.on90 => 90,
            Rotation.on180 => 180,
            Rotation.on270 => 270,
            _ => 0,
        };
        ho.xSum = ho.rect.LLX + ho.rect.URX;
        ho.ySum = ho.rect.LLY + ho.rect.URY;
        ho.anchorRaw = ho.rect;
        ho.dominant = ho.mendModel ? null : FindDominantImagePlacement(ho.page);
        if (ho.dominant is not null && ho.dominant.Rectangle.Width > 1 && ho.dominant.Rectangle.Height > 1)
            ho.anchorRaw = ho.dominant.Rectangle;
        var (ax0, ay0) = RawToVisual(ho, ho.anchorRaw.LLX, ho.anchorRaw.LLY);
        var (ax1, ay1) = RawToVisual(ho, ho.anchorRaw.URX, ho.anchorRaw.URY);
        ho.anchorX = Math.Min(ax0, ax1);
        ho.anchorY = Math.Min(ay0, ay1);
        ho.anchorW = Math.Abs(ax1 - ax0);
        ho.anchorH = Math.Abs(ay1 - ay0);

        ho.imgW = 0;
        ho.imgH = 0;
        ho.pageMatch = SafeRegex.Match(HocrPageBBoxRegex, ho.hocr);
        if (ho.pageMatch.Success)
        {
            int.TryParse(ho.pageMatch.Groups[1].Value, out var pw);
            int.TryParse(ho.pageMatch.Groups[2].Value, out var ph);
            ho.imgW = pw; ho.imgH = ph;
        }
        if (ho.imgW <= 0 || ho.imgH <= 0)
        {
            var maxX = 0; var maxY = 0;
            foreach (var groups in ho.matches.Select(m => m.Groups))
            {
                if (int.TryParse(groups[3].Value, out var x1) && x1 > maxX) maxX = x1;
                if (int.TryParse(groups[4].Value, out var y1) && y1 > maxY) maxY = y1;
            }
            ho.imgW = maxX > 0 ? maxX : 1;
            ho.imgH = maxY > 0 ? maxY : 1;
        }

        ho.sx = ho.anchorW / ho.imgW;
        ho.sy = ho.anchorH / ho.imgH;

        ho.words = new List<(double x, double bottom, int fontSize, string display, int line)>();
        ho.lineId = 0;
        CollectHocrWords(ho);

        ho.lineLift = new Dictionary<int, double>();
        if (!ho.mendModel)
            foreach (var lineGroup in System.Linq.Enumerable.GroupBy(ho.words, w => w.line))
            {
                var counts = new Dictionary<int, int>();
                foreach (var wfontSize in lineGroup.Select(w => w.fontSize))
                    counts[wfontSize] = counts.TryGetValue(wfontSize, out var c) ? c + 1 : 1;
                var modal = 0; var best = 0;
                foreach (var kv in counts)
                    if (kv.Value > best || (kv.Value == best && kv.Key > modal)) { modal = kv.Key; best = kv.Value; }
                ho.lineLift[lineGroup.Key] = -Text.Standard14Fonts.GetDescent("Helvetica") * modal / 1000.0;
            }

        ho.tb = new TextBuilder(ho.page) { SeatsInPageSpace = true };
        ho.overlaid = 0;
        OverlayHocrWords(ho);
        return ho.overlaid;
    }

    /// <summary>Fit a word to a target rendered width: fontSize = round(width /
    /// wordEmWidth) where wordEmWidth is the sum of the word's Helvetica advance
    /// widths (em units). Half-up rounding, floor of 1.</summary>
    private static int WidthFitFontSize(string word, double targetWidthPts)
    {
        double em = 0;
        foreach (var ch in word)
        {
            // A folded fi/fl ligature (NUL sentinel) measures as its f+i
            // decomposition (500) - "speci\0ed" is sized at the
            // ligature's own advance, not zero. Typographic punctuation above
            // U+00FF (quotes, dashes, bullet, ellipsis) measures through its
            // WinAnsi byte — the overlay writes it as that byte, so a giant
            // quote or em-dash word sizes to its true advance instead of
            // falling to the 1-pt floor.
            var code = Content.ContentStreamBuilder.ToWinAnsi(ch);
            var w = Text.Standard14Fonts.GetWidth("Helvetica", code);
            em += ch == '\0' && w <= 0 ? 500 : Math.Max(0, w);
        }
        em /= 1000.0;
        if (em <= 0 || targetWidthPts <= 0) return 1;
        return Math.Max(1, (int)Math.Round(targetWidthPts / em, MidpointRounding.AwayFromZero));
    }

    /// <summary>The overlay font cannot encode the fi/fl ligatures, so the
    /// extracted text carries a NUL where they occur, keeping extraction
    /// byte-faithful.</summary>
    private static string FoldLigatures(string s) =>
        (s.IndexOf('ﬁ') < 0 && s.IndexOf('ﬂ') < 0)
            ? s
            : s.Replace('ﬁ', '\0').Replace('ﬂ', '\0');

    private static void ReplacePageWithImage(Page page, ImageStamp flattened)
    {
        var rect = page.GetPageRect(considerRotation: false);
        var width = rect.Width;
        var height = rect.Height;
        if (width <= 0 || height <= 0) return;

        page.Resources.Images.Add(flattened);
        var imageName = $"Im{page.Resources.Images.Count}";

        var sb = new System.Text.StringBuilder();
        sb.Append("q\n");
        sb.Append(width.ToString(CultureInfo.InvariantCulture)).Append(" 0 0 ");
        sb.Append(height.ToString(CultureInfo.InvariantCulture));
        sb.Append(" 0 0 cm\n/").Append(imageName).Append(" Do\nQ\n");

        var streamBytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        page.Dict.Set("Contents", new PdfStream(new PdfDictionary(), streamBytes));
    }
}
