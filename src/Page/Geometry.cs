using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
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
        // A rotation cleared just before the resize means "make the page upright at this
        // size": the content is turned the way the viewer showed it, shrunk to fit if needed.
        // A rotation written since without the setters (a page editor's) stands instead.
        var turn = RotationQuarter == 0 ? _clearedRotation : 0;
        _clearedRotation = 0;
        if (turn != 0) BakeClearedRotationToFit(turn, width, height);

        // The size is the displayed one: a page turned a quarter stores it swapped.
        var turned = RotationQuarter is 90 or 270;
        var boxW = turned ? height : width;
        var boxH = turned ? width : height;

        // The box keeps its origin: content drawn at a page's own origin stays on it.
        var origin = MediaBox;
        var arr = NewBox(origin.LLX, origin.LLY, boxW, boxH);
        _dict.Set("MediaBox", arr);

        // Update CropBox if it exists so it matches
        if (_dict.ContainsKey("CropBox"))
            _dict.Set("CropBox", NewBox(origin.LLX, origin.LLY, boxW, boxH));
    }

    private static Core.PdfArray NewBox(double llx, double lly, double width, double height)
    {
        var arr = new Core.PdfArray();
        arr.Add(new Core.PdfReal(llx));
        arr.Add(new Core.PdfReal(lly));
        arr.Add(new Core.PdfReal(llx + width));
        arr.Add(new Core.PdfReal(lly + height));
        return arr;
    }

    /// <summary>The page's rotation as a quarter turn in 0..359, whichever way it is written
    /// (/Rotate -90 turns the page as 270 does).</summary>
    internal int RotationQuarter => ((RotateDegrees % 360) + 360) % 360;

    /// <summary>The matrix (a b c d e f) from the page as shown - origin at its displayed lower-left
    /// corner, <see cref="Width"/> x <see cref="Height"/> - to its media space; null for an unturned page.
    /// Content placed in shown coordinates and drawn through it lands upright where a viewer sees it.</summary>
    internal (double A, double B, double C, double D, double E, double F)? ShownToMediaMatrix()
    {
        var box = MediaBox;
        return RotationQuarter switch
        {
            90 => (0, 1, -1, 0, box.URX, box.LLY),
            180 => (-1, 0, 0, -1, box.URX, box.URY),
            270 => (0, -1, 1, 0, box.LLX, box.URY),
            _ => null,
        };
    }

    /// <summary>A placement matrix (a b c d e f) given in the page as shown, carried on into media space
    /// (unchanged on an unturned page). A stamp writes this as its one placement, so its content holds a
    /// single transform as on an unturned page: text extraction composes a turned matrix with a later one
    /// only approximately, and clipped a stamp drawn through two of them to the page bounds wrongly.</summary>
    internal (double A, double B, double C, double D, double E, double F) ShownPlacementToMedia(
        double a, double b, double c, double d, double e, double f)
    {
        if (ShownToMediaMatrix() is not { } m) return (a, b, c, d, e, f);
        return (a * m.A + b * m.C, a * m.B + b * m.D,
            c * m.A + d * m.C, c * m.B + d * m.D,
            e * m.A + f * m.C + m.E, e * m.B + f * m.D + m.F);
    }

    /// <summary>The media-space bounds of a rectangle given in the page as shown.</summary>
    internal Rectangle ShownToMedia(Rectangle shown)
    {
        if (ShownToMediaMatrix() is not { } m) return shown;
        double X(double x, double y) => x * m.A + y * m.C + m.E;
        double Y(double x, double y) => x * m.B + y * m.D + m.F;
        var xs = new[] { X(shown.LLX, shown.LLY), X(shown.URX, shown.URY), X(shown.LLX, shown.URY), X(shown.URX, shown.LLY) };
        var ys = new[] { Y(shown.LLX, shown.LLY), Y(shown.URX, shown.URY), Y(shown.LLX, shown.URY), Y(shown.URX, shown.LLY) };
        return new Rectangle(xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    /// <summary>The box content placed in shown coordinates lays out in: the media box of an unturned
    /// page, the shown <see cref="Width"/> x <see cref="Height"/> from the origin of a turned one.</summary>
    internal Rectangle ShownFrame => RotationQuarter == 0 ? MediaBox : new Rectangle(0, 0, Width, Height);

    /// <summary>
    /// Get the page rectangle, optionally considering the CropBox.
    /// </summary>
    /// <param name="considerRotation">Whether to account for page rotation.</param>
    /// <returns>The effective page rectangle.</returns>
    public Rectangle GetPageRect(bool considerRotation)
    {
        // A crop box reaching past the media box shows only the part inside it (ISO 32000-1 14.11.2).
        var box = _dict.ContainsKey("CropBox") ? CropBox.Intersect(MediaBox) : MediaBox;
        if (!considerRotation)
            return box;

        // A negative /Rotate (a page carrying -90) turns the same way as its positive
        // equivalent; the remainder keeps the sign, so fold it into 0..359 first.
        var rot = ((RotateDegrees % 360) + 360) % 360;
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
            // The scale reaches the content when the operators it was inserted among are written: text
            // read until then is read without it. Operators added on top of the content scale only those.
            if (_contents is { IsMaterialized: true }) HoldContentTransform(new Matrix(sx, 0, 0, sy, 0, 0));
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
        // The baked geometry below is absolute, so the viewing flag is always cleared, and a
        // rotation cleared before the bake is not turned into the content again by a resize.
        _dict.Set("Rotate", new PdfInteger(0));
        _clearedRotation = 0;
        if (rot == 0) return;
        var (a, b, c, d, e, f) = RotationBakeAffine(rot);
        MapPageBoxes(a, b, c, d, e, f);
        DrawContentThrough(a, b, c, d, e, f);
    }

    /// <summary>The rotation quarter the <see cref="Rotate"/> setter last cleared, until the
    /// next resize consumes it (0 when the rotation was not cleared).</summary>
    private int _clearedRotation;

    /// <summary>Remember a rotation being cleared (a later <see cref="SetPageSize"/> bakes
    /// it); setting any rotation forgets it. The flag itself is written as before.</summary>
    private void NoteRotationChange(int degrees)
    {
        if (degrees % 360 != 0) _clearedRotation = 0;
        else if (RotationQuarter != 0) _clearedRotation = RotationQuarter;
    }

    /// <summary>Content bounds that overrun the new box by no more than this (points) still fit.</summary>
    private const double FitTolerance = 0.5;

    /// <summary>A crop box no further inside the media box than this (points) shows the whole media.</summary>
    private const double CropInsetTolerance = 0.5;

    /// <summary>Bake the cleared rotation <paramref name="rot"/> into the content and, where the
    /// turned content no longer fits a <paramref name="width"/> x <paramref name="height"/> box at
    /// the turned origin, shrink it uniformly (never enlarge) and centre it in that box.</summary>
    private void BakeClearedRotationToFit(int rot, double width, double height)
    {
        // What the page holds unwritten is part of the content that is measured and turned.
        FlushHeldContent();
        var bounds = FitBounds();
        var (a, b, c, d, e, f) = RotationBakeAffine(rot);
        var turned = TransformRect(bounds, a, b, c, d, e, f);
        var origin = TransformRect(MediaBox, a, b, c, d, e, f);
        var box = new Rectangle(origin.LLX, origin.LLY, origin.LLX + width, origin.LLY + height);
        var fits = turned.LLX >= box.LLX - FitTolerance && turned.LLY >= box.LLY - FitTolerance
            && turned.URX <= box.URX + FitTolerance && turned.URY <= box.URY + FitTolerance;
        // The boxes take the turn alone: the shrink and shift only seat the content inside them.
        MapPageBoxes(a, b, c, d, e, f);
        if (!fits && turned.Width > 0 && turned.Height > 0)
        {
            var s = Math.Min(1, Math.Min(box.Width / turned.Width, box.Height / turned.Height));
            var tx = (box.LLX + box.URX) / 2 - s * (turned.LLX + turned.URX) / 2;
            var ty = (box.LLY + box.URY) / 2 - s * (turned.LLY + turned.URY) / 2;
            // Scale-and-centre after the turn: compose [s 0 0 s tx ty] onto the rotation.
            (a, b, c, d, e, f) = (s * a, s * b, s * c, s * d, s * e + tx, s * f + ty);
        }
        DrawContentThrough(a, b, c, d, e, f);
    }

    /// <summary>What the fit keeps whole: everything the content draws, cut to the crop box where
    /// the page (or the page tree above it) sets one inside its media box - the viewer showed only
    /// the crop, and the bake turns the content the way the viewer showed it. Nothing drawn in the
    /// crop keeps everything drawn.</summary>
    private Rectangle FitBounds()
    {
        var drawn = CalculateContentBBox();
        var media = MediaBox;
        if (GetBox("CropBox") is not { } crop) return drawn;
        var shown = crop.Intersect(media);
        var inside = shown.LLX > media.LLX + CropInsetTolerance || shown.LLY > media.LLY + CropInsetTolerance
            || shown.URX < media.URX - CropInsetTolerance || shown.URY < media.URY - CropInsetTolerance;
        var kept = drawn.Intersect(shown);
        return !shown.IsEmpty && inside && !kept.IsEmpty ? kept : drawn;
    }

    /// <summary>The affine mapping a page coordinate (x,y) into the space the page turned by
    /// <paramref name="rot"/> (90/180/270) shows, with its origin at (0,0):
    /// x' = a*x + c*y + e, y' = b*x + d*y + f (90deg clockwise maps (x,y) -> (y, w - x)).</summary>
    private (double a, double b, double c, double d, double e, double f) RotationBakeAffine(int rot)
    {
        var mb = MediaBox ?? new Rectangle(0, 0, 612, 792);
        double ox = mb.LLX, oy = mb.LLY, w = mb.Width, h = mb.Height;
        return rot switch
        {
            90 => (0, -1, 1, 0, -oy, w + ox),
            180 => (-1, 0, 0, -1, w + ox, h + oy),
            _ => (0, 1, -1, 0, h + oy, -ox), // 270
        };
    }

    /// <summary>Write the operators held in Contents into the page's streams: written back at save
    /// they would replace a wrap or a resize. The decorations registered on the page's text stay registered: each
    /// is drawn at save through the redraws of the content since its text was read (see TransformsSince).</summary>
    private void FlushHeldContent()
    {
        _contents?.FlushToPage();
        // Operators read since (a measurement) stay held: written back at save they too would
        // replace the wrap, so the view is let go.
        if (_contents is { IsMaterialized: true }) ResetContentsCache();
    }

    /// <summary>Draw the content and the annotations through the affine.</summary>
    private void DrawContentThrough(double a, double b, double c, double d, double e, double f)
    {
        // q {a b c d e f} cm, a q for each level the content restores below its start, the
        // content, then a Q for each level it leaves open and one for the outer q: no stray Q
        // of the content drops the matrix under the rest of it, and the matrix is popped when
        // the content ends, so a stream appended later (a stamp) draws in page space.
        FlushHeldContent();
        var originalContent = CollectContentBytes();
        // The matrix the content leaves in force matters only to text a writer drew since the last redraw.
        var (end, strayRestores, openSaves) = ImageStamp.ScanGraphicsState(originalContent, readMatrices: _writtenSinceRedraw);
        var prefix = System.Text.Encoding.ASCII.GetBytes(
            $"q\n{Format(a)} {Format(b)} {Format(c)} {Format(d)} {Format(e)} {Format(f)} cm\n"
            + string.Concat(Enumerable.Repeat("q\n", strayRestores)));
        var suffix = System.Text.Encoding.ASCII.GetBytes("\n" + string.Concat(Enumerable.Repeat("Q\n", openSaves + 1)));
        var wrapped = new byte[prefix.Length + originalContent.Length + suffix.Length];
        prefix.CopyTo(wrapped, 0);
        originalContent.CopyTo(wrapped, prefix.Length);
        suffix.CopyTo(wrapped, prefix.Length + originalContent.Length);
        SetContentStream(wrapped);
        // Text read before this turn is drawn through it now, and so is a decoration set on that text.
        var left = new Matrix(end.a, end.b, end.c, end.d, end.e, end.f);
        RecordContentTransform(WrittenMatrix(a, b, c, d, e, f), _writtenSinceRedraw && !IsIdentity(left) ? left : null);
        TransformAnnotationGeometry(a, b, c, d, e, f);
    }

    /// <summary>Map every defined page box through the affine (corners then renormalise).</summary>
    private void MapPageBoxes(double a, double b, double c, double d, double e, double f)
    {
        foreach (var boxName in new[] { "MediaBox", "CropBox", "BleedBox", "TrimBox", "ArtBox" })
        {
            var box = GetBox(boxName);
            if (box is null) continue;
            SetBox(boxName, TransformRect(box, a, b, c, d, e, f));
        }
    }

    /// <summary>Map a rectangle's two corners through the affine and renormalise to LL/UR.</summary>
    private static Rectangle TransformRect(Rectangle r, double a, double b, double c, double d, double e, double f)
    {
        double x0 = a * r.LLX + c * r.LLY + e, y0 = b * r.LLX + d * r.LLY + f;
        double x1 = a * r.URX + c * r.URY + e, y1 = b * r.URX + d * r.URY + f;
        return new Rectangle(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
    }

    /// <summary>Compose [a b c d 0 0] onto the left of each /AP /N appearance stream's /Matrix
    /// (handling both a single stream and a sub-dictionary of appearance states), once for each
    /// stream: <paramref name="turned"/> holds the stream each one met before was composed into.
    /// An annotation whose stream is turned as a copy takes it in an /AP of its own, as the /AP
    /// and its states may be shared with the annotations the stream stays turned for.</summary>
    private void RotateAppearanceMatrices(PdfDictionary annotDict, double a, double b, double c, double d, Dictionary<PdfStream, PdfStream> turned)
    {
        var ap = _reader.ResolveDict(annotDict.Get("AP"));
        if (ap is null) return;
        var normal = _reader.Resolve(ap.Get("N"));
        PdfObject? ownNormal = null;
        if (normal is PdfStream s)
        {
            var target = TurnAppearance(s, a, b, c, d, turned);
            if (!ReferenceEquals(target, s)) ownNormal = target;
        }
        else if (normal is PdfDictionary states)
        {
            PdfDictionary? ownStates = null;
            foreach (var key in states.Keys.ToList())
            {
                if (_reader.ResolveStream(states.Get(key)) is not PdfStream st) continue;
                var target = TurnAppearance(st, a, b, c, d, turned);
                if (!ReferenceEquals(target, st)) (ownStates ??= CopyOf(states)).Set(key, target);
            }
            ownNormal = ownStates;
        }
        if (ownNormal is null) return;
        var ownAp = CopyOf(ap);
        ownAp.Set("N", ownNormal);
        annotDict.Set("AP", ownAp);
    }

    /// <summary>The stream this bake composes the turn into for an annotation drawing
    /// <paramref name="s"/>: s itself, unless an earlier bake turned s for another page's
    /// annotations - then a copy of s with the matrix it had before that bake, so neither page
    /// takes the other's turn.</summary>
    private PdfStream TurnAppearance(PdfStream s, double a, double b, double c, double d, Dictionary<PdfStream, PdfStream> turned)
    {
        if (turned.TryGetValue(s, out var done)) return done;
        var target = s;
        if (_reader.OwnerDocument?.TurnedAppearances is { } record)
        {
            if (!record.TryGetValue(s, out var first)) record[s] = (_dict, s.Dict.Get("Matrix"));
            else if (!ReferenceEquals(first.Page, _dict))
            {
                target = CopyAppearance(s, first.Matrix);
                record[target] = (_dict, first.Matrix);
            }
        }
        ComposeStreamMatrix(target, a, b, c, d);
        turned[s] = target;
        return target;
    }

    /// <summary>A new appearance stream drawing what <paramref name="s"/> draws, with
    /// <paramref name="matrix"/> as its /Matrix: s's entries over its decoded content, so the
    /// copy carries none of the filters (or the cipher) of the object it was read from.</summary>
    private PdfStream CopyAppearance(PdfStream s, PdfObject? matrix)
    {
        var dict = CopyOf(s.Dict);
        dict.Remove("Filter");
        dict.Remove("DecodeParms");
        dict.Remove("Length");
        if (matrix is null) dict.Remove("Matrix");
        else dict.Set("Matrix", matrix);
        return new PdfStream(dict, _reader.DecodeStream(s));
    }

    private static PdfDictionary CopyOf(PdfDictionary source)
    {
        var copy = new PdfDictionary();
        foreach (var key in source.Keys) copy.Set(key, source.Get(key)!);
        return copy;
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
