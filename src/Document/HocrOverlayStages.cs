using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Document
{
// The stages of the hOCR overlay: the raw/visual coordinate swaps, one line-or-word match, and one word's invisible text.
    private static (double x, double y) RawToVisual(HocrOverlayState ho, double x, double y) => ho.rotation switch
    {
        90 => (y, ho.xSum - x),
        180 => (ho.xSum - x, ho.ySum - y),
        270 => (ho.ySum - y, x),
        _ => (x, y),
    };

    private static (double x, double y) VisualToRaw(HocrOverlayState ho, double x, double y) => ho.rotation switch
    {
        90 => (ho.xSum - y, x),
        180 => (ho.xSum - x, ho.ySum - y),
        270 => (y, ho.ySum - x),
        _ => (x, y),
    };

    /// <summary></summary>
    private static void OverlayHocrWords(HocrOverlayState ho)
    {
        foreach (var w in ho.words)
        {
            // The word's visual-space anchor point (bbox bottom-left, y top-down in
            // hOCR pixels). The baseline is stood ON the bbox bottom, lifted by the
            // page's dominant Helvetica descent. The
            // point is then converted back to raw page coordinates; the rotation is
            // carried by the text matrix (TextState.Rotation) so the overlay reads
            // upright on rotated pages.
            var lift = ho.mendModel
                ? -Text.Standard14Fonts.GetDescent("Helvetica") * w.fontSize / 1000.0
                : ho.lineLift[w.line];
            var (wx, wy) = VisualToRaw(ho, w.x, w.bottom + lift);
            ho.tb.AppendText(new TextFragment(w.display, textState: new TextState
            {
                FontName = "Helvetica",
                FontSize = (float)w.fontSize,
                RenderingMode = TextRenderingMode.Invisible,
                Rotation = ho.rotation,
                EmitStandard14Descriptor = true,
            })
            {
                Position = new Position(wx, wy),
            });
            ho.overlaid++;
        }
    }

    /// <summary></summary>
    private static void CollectHocrWords(HocrOverlayState ho)
    {
        foreach (Match m in HocrLineOrWordRegex.Matches(ho.hocr))
        {
            if (m.Groups[1].Success) { ho.lineId++; continue; } // ocr_line marker — geometric grouping

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
            var fontSize = WidthFitFontSize(display, (bx1 - bx0) * ho.sx);
            ho.words.Add((ho.anchorX + bx0 * ho.sx, ho.anchorY + ho.anchorH - by1 * ho.sy, fontSize, display, ho.lineId));
        }
    }
}
