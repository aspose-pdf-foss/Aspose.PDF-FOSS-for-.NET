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

                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    var device = new PngDevice();
                    device.Process(page, ms);
                    imageBytes = ms.ToArray();
                }

                System.Drawing.Image img;
                try
                {
                    using var imgMs = new MemoryStream(imageBytes);
                    img = System.Drawing.Image.FromStream(imgMs);
                }
                catch
                {
                    ok = false;
                    continue;
                }

                string hocr;
                using (img)
                {
                    hocr = invoke(img, page) ?? string.Empty;
                }

                if (flattenImages)
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
    /// is empty, malformed, or contains no word spans).</summary>
    internal static int OverlayHocrAsInvisibleText(Page page, string hocr)
    {
        if (string.IsNullOrWhiteSpace(hocr)) return 0;
        var matches = HocrWordRegex.Matches(hocr);
        if (matches.Count == 0) return 0;

        var rect = page.GetPageRect(considerRotation: false);
        var pageWidth = rect.Width;
        var pageHeight = rect.Height;
        if (pageWidth <= 0 || pageHeight <= 0) return 0;

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

        var sx = pageWidth / imgW;
        var sy = pageHeight / imgH;

        var tb = new TextBuilder(page);
        var overlaid = 0;
        // Emit each word at its OWN baseline (bbox bottom) and font. The extractor groups
        // words into lines and derives each line's baseline (median) and bottom (deepest
        // glyph) — the vertical-gap rule needs both, so the per-word bottoms must survive.
        foreach (Match m in HocrLineOrWordRegex.Matches(hocr))
        {
            if (m.Groups[1].Success) continue; // ocr_line marker — grouping is geometric

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
            tb.AppendText(new TextFragment(display, textState: new TextState
            {
                FontSize = (float)fontSize,
                RenderingMode = TextRenderingMode.Invisible,
            })
            {
                Position = new Position(bx0 * sx, pageHeight - by1 * sy),
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
            em += Text.Standard14Fonts.GetWidth("Helvetica", ch);
        em /= 1000.0;
        if (em <= 0 || targetWidthPts <= 0) return 1;
        return Math.Max(1, (int)Math.Round(targetWidthPts / em, MidpointRounding.AwayFromZero));
    }

    /// <summary>The reference overlay font cannot encode the fi/fl ligatures, so the
    /// extracted text carries a NUL where they occur; mirror that so extraction is
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
