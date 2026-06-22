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

    private static readonly Regex HocrWordRegex = new(
        @"<span[^>]+class=['""]ocrx?_word['""][^>]*title=['""][^'""]*bbox\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)[^'""]*['""][^>]*>([^<]*)</span>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Overlay the recognised hOCR words on the page as an invisible
    /// text layer. Returns the number of words actually overlaid (0 when the hOCR
    /// is empty, malformed, or contains no word spans).</summary>
    private static int OverlayHocrAsInvisibleText(Page page, string hocr)
    {
        if (string.IsNullOrWhiteSpace(hocr)) return 0;
        var matches = HocrWordRegex.Matches(hocr);
        if (matches.Count == 0) return 0;

        var rect = page.GetPageRect(considerRotation: false);
        var pageWidth = rect.Width;
        var pageHeight = rect.Height;
        if (pageWidth <= 0 || pageHeight <= 0) return 0;

        var maxX = 0; var maxY = 0;
        foreach (Match m in matches)
        {
            if (int.TryParse(m.Groups[3].Value, out var x1) && x1 > maxX) maxX = x1;
            if (int.TryParse(m.Groups[4].Value, out var y1) && y1 > maxY) maxY = y1;
        }
        if (maxX <= 0) maxX = 1;
        if (maxY <= 0) maxY = 1;

        var sx = pageWidth / maxX;
        var sy = pageHeight / maxY;

        var tb = new TextBuilder(page);
        var overlaid = 0;
        foreach (Match m in matches)
        {
            if (!int.TryParse(m.Groups[1].Value, out var bx0) ||
                !int.TryParse(m.Groups[2].Value, out var by0) ||
                !int.TryParse(m.Groups[3].Value, out var bx1) ||
                !int.TryParse(m.Groups[4].Value, out var by1))
                continue;
            var word = System.Net.WebUtility.HtmlDecode(m.Groups[5].Value)?.Trim();
            if (string.IsNullOrEmpty(word)) continue;

            var pdfX = bx0 * sx;
            // hOCR origin is top-left; PDF is bottom-left.
            var pdfY = pageHeight - (by1 * sy);
            var fontSize = Math.Max(1.0, (by1 - by0) * sy);

            var fragment = new TextFragment(word!, textState: new TextState
            {
                FontSize = (float)fontSize,
                RenderingMode = TextRenderingMode.Invisible,
            })
            {
                Position = new Position(pdfX, pdfY),
            };
            tb.AppendText(fragment);
            overlaid++;
        }
        return overlaid;
    }

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
