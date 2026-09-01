using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>Map a rectangle from the page's displayed (rotation-applied) coordinate frame
    /// back to unrotated page space, where annotation /Rect values live.</summary>
    private static Rectangle MapDisplayedRectToUnrotated(Rectangle d, int rotate, Rectangle mb)
    {
        double wu = mb.Width, hu = mb.Height;
        (double x, double y) Map(double x, double y) => (((rotate % 360) + 360) % 360) switch
        {
            90 => (wu - y, x),
            180 => (wu - x, hu - y),
            270 => (y, hu - x),
            _ => (x, y),
        };
        var (x1, y1) = Map(d.LLX, d.LLY);
        var (x2, y2) = Map(d.URX, d.URY);
        return new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    /// <summary>
    /// Computes the background rectangle height for a text segment/fragment.
    /// Uses the system font WinLineHeight for Standard-14 equivalents (most accurate),
    /// falls back to CapHeight+|Descent| from the font descriptor, then BBox, then 1.16×fontSize.
    /// </summary>
    private static double ComputeBgRectHeight(string fontName, Text.FontInfo? font, double rawFs, double tmD)
    {
        // A text-highlight background box is sized at a flat 1.1×
        // the font size, independent of the font's own line-height metrics
        // (a 72pt run yields 79.2, a 12pt run 13.2, an 8pt run 8.8).
        _ = fontName; _ = font;
        return rawFs * tmD * 1.1;
    }

    /// <summary>Set the media box for this page.</summary>
    public void SetMediaBox(Rectangle rect) => SetBox("MediaBox", rect);

    /// <summary>Set the crop box for this page.</summary>
    public void SetCropBox(Rectangle rect) => SetBox("CropBox", rect);

    /// <summary>Set the bleed box for this page.</summary>
    public void SetBleedBox(Rectangle rect) => SetBox("BleedBox", rect);

    /// <summary>Set the trim box for this page.</summary>
    public void SetTrimBox(Rectangle rect) => SetBox("TrimBox", rect);

    /// <summary>Set the art box for this page.</summary>
    public void SetArtBox(Rectangle rect) => SetBox("ArtBox", rect);

    /// <summary>
    /// Set the page size by updating the MediaBox.
    /// Width and height are in points.
    /// </summary>
    public void SetPageSize(double width, double height)
    {
        var rot = RotateDegrees % 360;
        double boxW, boxH;
        if (rot == 90 || rot == 270)
        {
            boxW = height;
            boxH = width;
        }
        else
        {
            boxW = width;
            boxH = height;
        }

        var arr = new Core.PdfArray();
        arr.Add(new Core.PdfReal(0));
        arr.Add(new Core.PdfReal(0));
        arr.Add(new Core.PdfReal(boxW));
        arr.Add(new Core.PdfReal(boxH));
        _dict.Set("MediaBox", arr);

        // Update CropBox if it exists so it matches
        if (_dict.ContainsKey("CropBox"))
        {
            var cropArr = new Core.PdfArray();
            cropArr.Add(new Core.PdfReal(0));
            cropArr.Add(new Core.PdfReal(0));
            cropArr.Add(new Core.PdfReal(boxW));
            cropArr.Add(new Core.PdfReal(boxH));
            _dict.Set("CropBox", cropArr);
        }
    }

    /// <summary>
    /// Get the page rectangle, optionally considering the CropBox.
    /// </summary>
    /// <param name="considerRotation">Whether to account for page rotation.</param>
    /// <returns>The effective page rectangle.</returns>
    public Rectangle GetPageRect(bool considerRotation)
    {
        var box = _dict.ContainsKey("CropBox") ? CropBox : MediaBox;
        if (!considerRotation)
            return box;

        var rot = RotateDegrees % 360;
        if (rot == 90 || rot == 270)
            return new Rectangle(box.LLX, box.LLY, box.LLX + box.Height, box.LLY + box.Width);
        return box;
    }

    /// <summary>Resize this page to <paramref name="targetSize"/> via media-box update.</summary>
    public void Resize(Aspose.Pdf.PageSize targetSize)
    {
        if (targetSize is null) return;
        // Scale the existing content to the new page box, not just the box itself —
        // otherwise the content keeps its original size and appears zoomed relative to
        // the resized page. Prepend a `cm` that maps the current media box
        // onto the target size; it precedes any q/Q so the whole content is scaled.
        var mb = MediaBox;
        double curW = mb.Width, curH = mb.Height;
        if (curW > 0 && curH > 0)
        {
            double sx = targetSize.Width / curW;
            double sy = targetSize.Height / curH;
            Contents.Insert(1, new Aspose.Pdf.Operators.ConcatenateMatrix(new[] { sx, 0, 0, sy, 0, 0 }));
        }
        MediaBox = new Rectangle(0, 0, targetSize.Width, targetSize.Height);
    }

    /// <summary>
    /// Physically bake a page rotation of <paramref name="degrees"/> (0/90/180/270) into the
    /// page geometry: wrap the content stream in the rotation CTM, map every page box and the
    /// annotation /Rect, /QuadPoints and appearance /Matrix into the rotated space, and clear
    /// the /Rotate viewing flag. Unlike the <see cref="Rotate"/> flag (which leaves the stored
    /// geometry untouched and only rotates the view), this stores the rotation as content
    /// geometry so the annotation rectangles report their rotated positions — the
    /// <c>PdfPageEditor.PageRotations</c> semantics.
    /// </summary>
    internal void BakeRotation(int degrees)
    {
        int rot = ((degrees % 360) + 360) % 360;
        // The baked geometry below is absolute, so the viewing flag is always cleared.
        _dict.Set("Rotate", new PdfInteger(0));
        if (rot == 0) return;

        var mb = MediaBox ?? new Rectangle(0, 0, 612, 792);
        double ox = mb.LLX, oy = mb.LLY, w = mb.Width, h = mb.Height;

        // Affine that maps an old page coordinate (x,y) to the rotated space whose origin is
        // (0,0): x' = a*x + c*y + e, y' = b*x + d*y + f
        // (e.g. 90deg clockwise maps (x,y) -> (y, w - x)).
        double a, b, c, d, e, f;
        switch (rot)
        {
            case 90:  a = 0; b = -1; c = 1; d = 0;  e = -oy;    f = w + ox; break;
            case 180: a = -1; b = 0; c = 0; d = -1; e = w + ox; f = h + oy; break;
            default:  a = 0; b = 1; c = -1; d = 0;  e = h + oy; f = -ox;    break; // 270
        }

        // Wrap the original content in the rotation CTM, isolating it in q…Q just as
        // ApplyContentResize does: {a b c d e f} cm  q  … original …  Q.
        var originalContent = CollectContentBytes();
        var prefix = System.Text.Encoding.ASCII.GetBytes(
            $"{Format(a)} {Format(b)} {Format(c)} {Format(d)} {Format(e)} {Format(f)} cm\nq\n");
        var suffix = System.Text.Encoding.ASCII.GetBytes("\nQ\n");
        var wrapped = new byte[prefix.Length + originalContent.Length + suffix.Length];
        prefix.CopyTo(wrapped, 0);
        originalContent.CopyTo(wrapped, prefix.Length);
        suffix.CopyTo(wrapped, prefix.Length + originalContent.Length);
        SetContentStream(wrapped);

        // Map every defined page box through the same affine (corners then renormalise).
        foreach (var boxName in new[] { "MediaBox", "CropBox", "BleedBox", "TrimBox", "ArtBox" })
        {
            var box = GetBox(boxName);
            if (box is null) continue;
            SetBox(boxName, TransformRect(box, a, b, c, d, e, f));
        }

        TransformAnnotationGeometry(a, b, c, d, e, f);
    }

    /// <summary>Map a rectangle's two corners through the affine and renormalise to LL/UR.</summary>
    private static Rectangle TransformRect(Rectangle r, double a, double b, double c, double d, double e, double f)
    {
        double x0 = a * r.LLX + c * r.LLY + e, y0 = b * r.LLX + d * r.LLY + f;
        double x1 = a * r.URX + c * r.URY + e, y1 = b * r.URX + d * r.URY + f;
        return new Rectangle(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
    }

    /// <summary>Compose [a b c d 0 0] onto the left of each /AP /N appearance stream's /Matrix
    /// (handling both a single stream and a sub-dictionary of appearance states).</summary>
    private void RotateAppearanceMatrices(PdfDictionary annotDict, double a, double b, double c, double d)
    {
        var ap = _reader.ResolveDict(annotDict.Get("AP"));
        if (ap is null) return;
        var normal = _reader.Resolve(ap.Get("N"));
        if (normal is PdfStream s)
            ComposeStreamMatrix(s, a, b, c, d);
        else if (normal is PdfDictionary states)
        {
            foreach (var key in states.Keys)
                if (_reader.ResolveStream(states.Get(key)) is PdfStream st)
                    ComposeStreamMatrix(st, a, b, c, d);
        }
    }

    /// <summary>True when this page's media box was merely INHERITED from the
    /// document (no-size Pages.Insert) rather than set explicitly. A landscape
    /// request on such a page resolves to the A4-landscape default at layout,
    /// replacing the inherited box.</summary>
    internal bool SizeInherited { get; set; }

    private Rectangle? InheritBox(string name)
    {
        // Walk up /Parent chain for inherited attributes
        var parentObj = _dict.Get("Parent");
        var visited = new HashSet<int>();

        while (parentObj is not null)
        {
            var parent = _reader.ResolveDict(parentObj);
            if (parent is null) break;

            // Prevent infinite loops
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                break;

            var boxObj = _reader.Resolve(parent.Get(name));
            if (boxObj is PdfArray arr && arr.Count >= 4)
                return Rectangle.FromPdfArray(ResolveArrayElements(arr));

            parentObj = parent.Get("Parent");
        }

        return null;
    }
}
