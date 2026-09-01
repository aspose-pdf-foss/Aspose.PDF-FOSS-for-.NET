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
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Document.Convert(CallBackGetHocr) hands each page to the callback as a "
                + "System.Drawing.Image, which requires GDI+ and is available on Windows only.");
        var ok = true;
        // Track whether the callback's hOCR actually yielded recognised text that
        // got overlaid. Convert reports success only when real OCR text was applied;
        // an empty / wordless hOCR result (e.g. "<test/>") makes it return false so
        // callers can tell that nothing was recognised.
        var appliedAny = false;
        foreach (var page in Pages)
        {
            try
            {
                // Render each page and hand it to the callback to OCR; the returned
                // hOCR text is overlaid as invisible text. Pages without rasterised
                // images still render (to a blank raster) and receive the callback so
                // the caller can supply text for any page.
                if (page is null)
                {
                    continue;
                }

                // The image handed to the OCR callback is the page's own scan
                // (its dominant raster image) when one exists; a page render is
                // only the fallback for pages without images. A clean JPEG is
                // handed stream-backed so its Save round-trips the original
                // bytes; one carrying EXIF metadata is normalised first (the
                // metadata does not describe the overlay geometry), which hands a
                // decoded bitmap whose Save re-encodes the pixels.
                var handImg = TryGetDominantImage(page);

                byte[]? imageBytes = null;
                if (flattenImages || handImg is null)
                {
                    using var ms = new MemoryStream();
                    var device = new PngDevice();
                    device.Process(page, ms);
                    imageBytes = ms.ToArray();
                }

                System.Drawing.Image img;
                if (handImg is not null)
                {
                    img = handImg;
                }
                else
                {
                    try
                    {
                        img = System.Drawing.Image.FromStream(new MemoryStream(imageBytes!));
                    }
                    catch
                    {
                        ok = false;
                        continue;
                    }
                }

                string hocr;
                using (img)
                {
                    hocr = invoke(img, page) ?? string.Empty;
                }

                if (flattenImages && imageBytes is not null)
                {
                    ReplacePageWithImage(page, imageBytes);
                }

                if (OverlayHocrAsInvisibleText(page, hocr) > 0)
                    appliedAny = true;
            }
            catch
            {
                ok = false;
            }
        }
        return ok && appliedAny;
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
            var ms = new MemoryStream();
            placement.Image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            return System.Drawing.Image.FromStream(ms);
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
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex HocrInlineTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    // Combined scan over the hOCR body: each match is either an ocr_line container
    // (group 1 = "ocr_line", groups 2-5 = its bbox) or an ocrx_word (groups 6-9 =
    // bbox, group 10 = word markup). Iterating in document order lets us assign each
    // word to its enclosing ocr_line, so the overlay preserves the OCR's own line
    // structure (words on one visual line share a baseline) rather than scattering
    // every word onto its own jittered baseline.
    private static readonly Regex HocrLineOrWordRegex = new(
        @"class=['""](ocr_line)['""][^>]*title=['""][^'""]*bbox\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)" +
        @"|<span[^>]+class=['""]ocrx?_word['""][^>]*title=['""][^'""]*bbox\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)[^'""]*['""][^>]*>(.*?)</span>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>The <c>ocr_page</c> element carries the source raster's pixel
    /// dimensions as <c>bbox 0 0 W H</c>. These are the correct denominators for
    /// mapping word boxes onto the PDF page; using the rightmost/bottommost word
    /// edge instead would stretch the overlay so the last word touches the page
    /// edge. The page title is single-quoted but embeds double quotes
    /// (<c>image ""; bbox …</c>), so a <c>[^'"]</c> scan would stop short of
    /// bbox — match within the tag (up to '&gt;') instead.</summary>
    private static readonly Regex HocrPageBBoxRegex = new(
        @"class=['""]ocr_page['""][^>]*?bbox\s+\d+\s+\d+\s+(\d+)\s+(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        if (string.IsNullOrWhiteSpace(hocr)) return 0;
        var matches = HocrWordRegex.Matches(hocr);
        if (matches.Count == 0) return 0;

        var rect = page.GetPageRect(considerRotation: false);
        var pageWidth = rect.Width;
        var pageHeight = rect.Height;
        if (pageWidth <= 0 || pageHeight <= 0) return 0;

        // The OCR raster the callback saw is the page image normalised UPRIGHT —
        // its word boxes live in the page's VISUAL (rotated) space. Map the anchor
        // into visual space, place each word there, then convert the point back to
        // raw page coordinates and carry the page rotation in the text matrix.
        var rotation = page.Rotate switch
        {
            Rotation.on90 => 90,
            Rotation.on180 => 180,
            Rotation.on270 => 270,
            _ => 0,
        };
        double xSum = rect.LLX + rect.URX, ySum = rect.LLY + rect.URY;
        (double x, double y) RawToVisual(double x, double y) => rotation switch
        {
            90 => (y, xSum - x),
            180 => (xSum - x, ySum - y),
            270 => (ySum - y, x),
            _ => (x, y),
        };
        (double x, double y) VisualToRaw(double x, double y) => rotation switch
        {
            90 => (xSum - y, x),
            180 => (xSum - x, ySum - y),
            270 => (y, ySum - x),
            _ => (x, y),
        };

        // Anchor the overlay to the scan image's placement rectangle: the OCR
        // raster is the page's image, so hOCR pixel coordinates map into where
        // that image is drawn (which may cover only part of the page — e.g. a
        // photo at natural size). Pages without images map to the page box.
        var anchorRaw = rect;
        var dominant = mendModel ? null : FindDominantImagePlacement(page);
        if (dominant is not null && dominant.Rectangle.Width > 1 && dominant.Rectangle.Height > 1)
            anchorRaw = dominant.Rectangle;
        var (ax0, ay0) = RawToVisual(anchorRaw.LLX, anchorRaw.LLY);
        var (ax1, ay1) = RawToVisual(anchorRaw.URX, anchorRaw.URY);
        double anchorX = Math.Min(ax0, ax1), anchorY = Math.Min(ay0, ay1);
        double anchorW = Math.Abs(ax1 - ax0), anchorH = Math.Abs(ay1 - ay0);

        // Prefer the OCR raster's true pixel dimensions from the ocr_page bbox.
        // Fall back to the extent of the recognised words only when the page
        // element is absent or malformed.
        double imgW = 0, imgH = 0;
        var pageMatch = HocrPageBBoxRegex.Match(hocr);
        if (pageMatch.Success)
        {
            int.TryParse(pageMatch.Groups[1].Value, out var pw);
            int.TryParse(pageMatch.Groups[2].Value, out var ph);
            imgW = pw; imgH = ph;
        }
        if (imgW <= 0 || imgH <= 0)
        {
            var maxX = 0; var maxY = 0;
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Groups[3].Value, out var x1) && x1 > maxX) maxX = x1;
                if (int.TryParse(m.Groups[4].Value, out var y1) && y1 > maxY) maxY = y1;
            }
            imgW = maxX > 0 ? maxX : 1;
            imgH = maxY > 0 ? maxY : 1;
        }

        var sx = anchorW / imgW;
        var sy = anchorH / imgH;

        // First pass: collect the words with their fitted font sizes. Word bottoms
        // stay per-word (the extractor's vertical-gap rule needs the deepest-glyph
        // bottoms to survive); the descent lift below is computed once for the page.
        var words = new List<(double x, double bottom, int fontSize, string display, int line)>();
        var lineId = 0;
        foreach (Match m in HocrLineOrWordRegex.Matches(hocr))
        {
            if (m.Groups[1].Success) { lineId++; continue; } // ocr_line marker — geometric grouping

            if (!int.TryParse(m.Groups[6].Value, out var bx0) ||
                !int.TryParse(m.Groups[7].Value, out var by0) ||
                !int.TryParse(m.Groups[8].Value, out var bx1) ||
                !int.TryParse(m.Groups[9].Value, out var by1))
                continue;
            var raw = HocrInlineTagRegex.Replace(m.Groups[10].Value, string.Empty);
            var word = System.Net.WebUtility.HtmlDecode(raw)?.Trim();
            if (string.IsNullOrEmpty(word)) continue;

            // Size each word to FILL its bbox width (not height): fontSize =
            // round(bboxWidthPts / wordEmWidth), the same integer-per-word rule the
            // OCR layout uses, so the extractor's dominant-font grid cell matches.
            // Measure the FOLDED text so the rendered advance matches the box (the fi/fl
            // fold changes glyph widths); otherwise a folded word overshoots into the next.
            var display = FoldLigatures(word!);
            var fontSize = WidthFitFontSize(display, (bx1 - bx0) * sx);
            words.Add((anchorX + bx0 * sx, anchorY + anchorH - by1 * sy, fontSize, display, lineId));
        }

        // Per-LINE descent lift: every word of an OCR line shares its line's lift
        // (the line's modal fitted size), so baselines inside a row stay level —
        // the extractor's line grouping survives — while each row's glyph rect
        // lands on the row's bbox bottom.
        var lineLift = new Dictionary<int, double>();
        if (!mendModel)
        foreach (var lineGroup in System.Linq.Enumerable.GroupBy(words, w => w.line))
        {
            var counts = new Dictionary<int, int>();
            foreach (var w in lineGroup)
                counts[w.fontSize] = counts.TryGetValue(w.fontSize, out var c) ? c + 1 : 1;
            var modal = 0; var best = 0;
            foreach (var kv in counts)
                if (kv.Value > best || (kv.Value == best && kv.Key > modal)) { modal = kv.Key; best = kv.Value; }
            lineLift[lineGroup.Key] = -Text.Standard14Fonts.GetDescent("Helvetica") * modal / 1000.0;
        }

        // Per-word descent lift: the drawn baseline sits one descent ABOVE the
        // word's bbox bottom, so the glyph rect (baseline minus descent) lands
        // exactly ON the box bottom — the row position the OCR reported.
        var tb = new TextBuilder(page);
        var overlaid = 0;
        foreach (var w in words)
        {
            // The word's visual-space anchor point (bbox bottom-left, y top-down in
            // hOCR pixels). The baseline is stood ON the bbox bottom, lifted by the
            // page's dominant Helvetica descent. The
            // point is then converted back to raw page coordinates; the rotation is
            // carried by the text matrix (TextState.Rotation) so the overlay reads
            // upright on rotated pages.
            var lift = mendModel
                ? -Text.Standard14Fonts.GetDescent("Helvetica") * w.fontSize / 1000.0
                : lineLift[w.line];
            var (wx, wy) = VisualToRaw(w.x, w.bottom + lift);
            tb.AppendText(new TextFragment(w.display, textState: new TextState
            {
                FontName = "Helvetica",
                FontSize = (float)w.fontSize,
                RenderingMode = TextRenderingMode.Invisible,
                Rotation = rotation,
                EmitStandard14Descriptor = true,
            })
            {
                Position = new Position(wx, wy),
            });
            overlaid++;
        }
        return overlaid;
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

    private void ReplacePageWithImage(Page page, byte[] pngBytes)
    {
        var rect = page.GetPageRect(considerRotation: false);
        var width = rect.Width;
        var height = rect.Height;
        if (width <= 0 || height <= 0) return;

        page.Resources.Images.Add(new MemoryStream(pngBytes));
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
