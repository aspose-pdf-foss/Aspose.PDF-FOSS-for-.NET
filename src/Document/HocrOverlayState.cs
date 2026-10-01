using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class HocrOverlayState
{
    public System.Text.RegularExpressions.MatchCollection matches = null!;
    public Rectangle rect = null!;
    public double pageWidth;
    public double pageHeight;
    // The OCR raster the callback saw is the page image normalised UPRIGHT —
    // its word boxes live in the page's VISUAL (rotated) space. Map the anchor
    // into visual space, place each word there, then convert the point back to
    // raw page coordinates and carry the page rotation in the text matrix.
    public int rotation;
    public double xSum;
    public double ySum;
    // Anchor the overlay to the scan image's placement rectangle: the OCR
    // raster is the page's image, so hOCR pixel coordinates map into where
    // that image is drawn (which may cover only part of the page — e.g. a
    // photo at natural size). Pages without images map to the page box.
    public Rectangle anchorRaw = null!;
    public ImagePlacement? dominant = null!;
    public double anchorX;
    public double anchorY;
    public double anchorW;
    public double anchorH;
    // Prefer the OCR raster's true pixel dimensions from the ocr_page bbox.
    // Fall back to the extent of the recognised words only when the page
    // element is absent or malformed.
    public double imgW;
    public double imgH;
    public System.Text.RegularExpressions.Match pageMatch = null!;
    public double sx;
    public double sy;
    // First pass: collect the words with their fitted font sizes. Word bottoms
    // stay per-word (the extractor's vertical-gap rule needs the deepest-glyph
    // bottoms to survive); the descent lift below is computed once for the page.
    public List<(double x, double bottom, int fontSize, string display, int line)> words = null!;
    public int lineId;
    // Per-LINE descent lift: every word of an OCR line shares its line's lift
    // (the line's modal fitted size), so baselines inside a row stay level —
    // the extractor's line grouping survives — while each row's glyph rect
    // lands on the row's bbox bottom.
    public Dictionary<int, double> lineLift = null!;
    // Per-word descent lift: the drawn baseline sits one descent ABOVE the
    // word's bbox bottom, so the glyph rect (baseline minus descent) lands
    // exactly ON the box bottom — the row position the OCR reported.
    public Aspose.Pdf.Text.TextBuilder tb = null!;
    public int overlaid;
    public Page page = default!;
    public string hocr = default!;
    public bool mendModel = false;
}
}
