using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>
    /// Paint annotations on top of the page. Currently supports text-markup annotations
    /// (/Highlight, /Underline, /StrikeOut, /Squiggly) because they have a simple geometric
    /// appearance derived from /QuadPoints. Other subtypes carry an /AP appearance stream
    /// which we don't yet rasterise.
    /// </summary>
    private static void DrawAnnotations(RenderContext ctx, PdfDictionary pageDict)
    {
        var annots = ctx.Reader.Resolve(pageDict.Get("Annots")) as PdfArray;
        if (annots is null) return;
        foreach (var item in annots)
        {
            var annot = ctx.Reader.ResolveDict(item);
            if (annot is null) continue;
            // Skip annotations that are hidden (bit 2 of /F). Bits are 1-indexed in the spec.
            var flags = (int)annot.GetInt("F");
            if ((flags & 0x02) != 0) continue;
            var subtype = annot.GetName("Subtype");
            if (subtype == "Highlight")
            {
                DrawHighlightAnnotation(ctx, annot);
                continue;
            }
            // For everything else, first try the /AP /N appearance stream per
            // PDF 32000-1:2008 §12.5.5. Covers /FreeText, /Stamp, /Widget, /Popup,
            // /Ink, /Line, /Polygon and any subtype that ships its appearance
            // as a Form XObject.
            var hasAppearance = ctx.Reader.ResolveDict(annot.Get("AP")) is not null;
            if (hasAppearance)
            {
                DrawAppearanceAnnotation(ctx, annot);
                continue;
            }
            // No /AP → the spec requires we generate a default appearance from
            // the subtype's attributes. /Square and /Circle have a simple
            // default (filled/stroked rectangle or ellipse in page space).
            // PDF 32000-1:2008 §12.5.6.8.
            if (subtype == "Square" || subtype == "Circle")
            {
                DrawSquareCircleDefault(ctx, annot, isCircle: subtype == "Circle");
            }
            // A /Text (sticky-note) annotation without /AP renders as its standard
            // note icon (PDF 32000 §12.5.6.4): NoZoom notes stretch the icon into
            // /Rect; otherwise the icon paints at its natural size anchored at the
            // rectangle's top-left corner.
            else if (subtype == "Text")
            {
                var form = Aspose.Pdf.Annotations.TextAnnotationIcons.BuildIconForm(annot, ctx.Reader);
                DrawAppearanceForm(ctx, annot, form, (flags & 0x08) != 0 ? null : TextIconNaturalRect(annot));
            }
            // Open /Popup notes carry no /AP (they are interactive UI); synthesise the
            // comment box so "render comments to image" bakes it in. PDF 32000 §12.5.6.14.
            else if (subtype == "Popup")
            {
                var form = Aspose.Pdf.Annotations.PopupAppearance.BuildOpenPopupForm(annot, ctx.Reader);
                if (form is not null) DrawAppearanceForm(ctx, annot, form);
            }
        }
    }

    /// <summary>
    /// Compose the page's own /Rotate (and pinned-canvas stretch) CTM onto an annotation's
    /// page-space transform. Annotations are authored in UNROTATED page space, so without
    /// this they stay upright while the content around them swings into the rotated canvas -
    /// a comment whose appearance stream turns its own text ended up at right angles to the
    /// page and clipped off its edge. Mirrors the GDI+ renderer's annotation base CTM.
    /// </summary>
    private static double[] ComposeAnnotBase(RenderContext ctx, double[] ctm)
        => ctx.PageCtm is null ? ctm : GraphicsState.MultiplyMatrices(ctm, ctx.PageCtm);

    /// <summary>
    /// Paint an annotation whose visual is expressed as an /AP /N Form XObject.
    /// Computes the CTM that maps the appearance stream's transformed /BBox into the
    /// annotation's page-space /Rect (PDF 32000 §12.5.5), then dispatches to the
    /// regular Form XObject renderer so all normal content-stream operators work
    /// (text, images, paths, nested XObjects).
    /// </summary>
    private static void DrawAppearanceAnnotation(RenderContext ctx, PdfDictionary annot)
    {
        var ap = ctx.Reader.ResolveDict(annot.Get("AP"));
        if (ap is null) return;
        // /N can be either a stream directly (simple markup annotations) or a dict
        // of appearance-state → stream (checkboxes, radio buttons, multi-state Widgets)
        // selected by the annotation's /AS entry (PDF 32000 §12.5.5).
        var nEntry = ctx.Reader.Resolve(ap.Get("N"));
        PdfStream? formStream = null;
        if (nEntry is PdfStream direct)
        {
            formStream = direct;
        }
        else if (nEntry is PdfDictionary stateDict)
        {
            var asName = annot.GetName("AS");
            if (!string.IsNullOrEmpty(asName))
                formStream = ctx.Reader.ResolveStream(stateDict.Get(asName));
        }
        if (formStream is null) return;
        DrawAppearanceForm(ctx, annot, formStream);
    }

    private static void DrawAppearanceForm(RenderContext ctx, PdfDictionary annot, PdfStream formStream,
        (double MinX, double MinY, double MaxX, double MaxY)? targetRect = null)
    {
        double rMinX, rMinY, rMaxX, rMaxY;
        if (targetRect is { } tr)
        {
            (rMinX, rMinY, rMaxX, rMaxY) = tr;
        }
        else
        {
            if (annot.Get("Rect") is not PdfArray rect || rect.Count < 4) return;

            // Normalise /Rect (some PDFs emit it with corners out of order).
            double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]);
            double rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
            rMinX = Math.Min(rx1, rx2); rMaxX = Math.Max(rx1, rx2);
            rMinY = Math.Min(ry1, ry2); rMaxY = Math.Max(ry1, ry2);
        }
        var rW = rMaxX - rMinX; var rH = rMaxY - rMinY;
        if (rW <= 0 || rH <= 0) return;

        // Transform the form's /BBox through its /Matrix to get the source rectangle
        // in the form's post-matrix space. DrawFormXObject concatenates /Matrix again,
        // so the outer CTM we build here must operate on the *post-matrix* bbox.
        if (formStream.Dict.Get("BBox") is not PdfArray bbox || bbox.Count < 4) return;
        double bx1 = NumFrom(bbox[0]), by1 = NumFrom(bbox[1]);
        double bx2 = NumFrom(bbox[2]), by2 = NumFrom(bbox[3]);
        var formMatrix = ExtractFormMatrix(formStream.Dict) ?? new double[] { 1, 0, 0, 1, 0, 0 };
        double tMinX = double.PositiveInfinity, tMinY = double.PositiveInfinity;
        double tMaxX = double.NegativeInfinity, tMaxY = double.NegativeInfinity;
        foreach (var (cx, cy) in new[] { (bx1, by1), (bx2, by1), (bx2, by2), (bx1, by2) })
        {
            var tx = formMatrix[0] * cx + formMatrix[2] * cy + formMatrix[4];
            var ty = formMatrix[1] * cx + formMatrix[3] * cy + formMatrix[5];
            if (tx < tMinX) tMinX = tx;
            if (tx > tMaxX) tMaxX = tx;
            if (ty < tMinY) tMinY = ty;
            if (ty > tMaxY) tMaxY = ty;
        }
        var tW = tMaxX - tMinX; var tH = tMaxY - tMinY;
        if (tW <= 0 || tH <= 0) return;

        var sx = rW / tW;
        var sy = rH / tH;
        // Outer CTM maps the form's transformed bbox origin to /Rect's lower-left.
        // DrawFormXObject will left-multiply this with /Matrix internally.
        var outerCtm = new double[]
        {
            sx, 0, 0, sy,
            rMinX - tMinX * sx,
            rMinY - tMinY * sy,
        };
        var state = new GraphicsState();
        state.Ctm = ComposeAnnotBase(ctx, outerCtm);
        // Annotations are painted after page content; any clip mask left over from the
        // content stream would wrongly clip the annotation. Clear before rendering,
        // restore after so subsequent annotations start from a clean state too.
        var savedClip = ctx.ClipMask;
        ctx.ClipMask = null;
        DrawFormXObject(ctx, formStream, state, null);
        ctx.ClipMask = savedClip;
    }

    /// <summary>
    /// Render a /Highlight annotation as a multiply-blended coloured rectangle per QuadPoint
    /// quad, falling back to the single /Rect if QuadPoints is absent. Multiply blending
    /// (PDF §11.3.5.3) preserves any text underneath — which is exactly why Acrobat uses it
    /// for highlights.
    /// </summary>
    private static void DrawHighlightAnnotation(RenderContext ctx, PdfDictionary annot)
    {
        // /C is the annotation colour in the current colour space (1, 3, or 4 components).
        // Default to yellow — Acrobat's factory default for the highlight tool.
        var color = ResolveColorArray(annot.Get("C")) ?? new[] { 1.0, 1.0, 0.0 };
        var (hr, hg, hb) = ColorToRgb(color);

        var quads = ctx.Reader.Resolve(annot.Get("QuadPoints")) as PdfArray;
        if (quads is not null && quads.Count >= 8 && quads.Count % 8 == 0)
        {
            // Each quad is 8 numbers: x1 y1 x2 y2 x3 y3 x4 y4 — the four corners in an
            // implementation-specific order. We compute the axis-aligned bounding box of
            // the four corners and fill it; that matches what Acrobat does visually for
            // axis-aligned text lines (the usual case).
            for (var i = 0; i + 8 <= quads.Count; i += 8)
            {
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                for (var k = 0; k < 4; k++)
                {
                    var qx = ToDouble(quads[i + k * 2]);
                    var qy = ToDouble(quads[i + k * 2 + 1]);
                    if (qx < minX) minX = qx;
                    if (qx > maxX) maxX = qx;
                    if (qy < minY) minY = qy;
                    if (qy > maxY) maxY = qy;
                }
                // The quad is filled as written. An earlier heuristic trimmed 28% off its
                // bottom on the theory that the box runs past the baseline into the leading,
                // but the GDI+ rasteriser fills the whole quad, as the expected render does: a
                // highlight authored as a plain 200pt square came out 28% short.
                var q = MapAnnotRect(ctx, minX, minY, maxX, maxY);
                FillMultiplyRect(ctx, q.MinX, q.MinY, q.MaxX, q.MaxY, hr, hg, hb);
            }
            return;
        }

        var rect = ctx.Reader.Resolve(annot.Get("Rect")) as PdfArray;
        if (rect is not null && rect.Count >= 4)
        {
            var x1 = ToDouble(rect[0]);
            var y1 = ToDouble(rect[1]);
            var x2 = ToDouble(rect[2]);
            var y2 = ToDouble(rect[3]);
            var hq = MapAnnotRect(ctx, Math.Min(x1, x2), Math.Min(y1, y2),
                Math.Max(x1, x2), Math.Max(y1, y2));
            FillMultiplyRect(ctx, hq.MinX, hq.MinY, hq.MaxX, hq.MaxY, hr, hg, hb);
        }
    }

    /// <summary>
    /// Render the default appearance of a /Square or /Circle annotation when
    /// it lacks an /AP /N stream. PDF 32000 §12.5.6.8: a Square's default
    /// appearance is the /Rect filled with the interior colour /IC and stroked
    /// with /C using /BS's line width (default 1 pt). /Circle is the same
    /// shape fit inside /Rect.
    /// </summary>
    private static void DrawSquareCircleDefault(RenderContext ctx, PdfDictionary annot, bool isCircle)
    {
        if (ctx.Reader.Resolve(annot.Get("Rect")) is not PdfArray rect || rect.Count < 4) return;

        double x1 = ToDouble(rect[0]);
        double y1 = ToDouble(rect[1]);
        double x2 = ToDouble(rect[2]);
        double y2 = ToDouble(rect[3]);
        var minX = Math.Min(x1, x2); var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2); var maxY = Math.Max(y1, y2);
        if (maxX <= minX || maxY <= minY) return;
        (minX, minY, maxX, maxY) = MapAnnotRect(ctx, minX, minY, maxX, maxY);

        // Interior colour (/IC). Optional — no fill if absent.
        var icColor = ResolveColorArray(annot.Get("IC"));

        // Border colour (/C). Optional — default black when a border is drawn.
        var borderArr = ResolveColorArray(annot.Get("C"));
        byte br = 0, bg = 0, bb = 0;
        var borderColorSet = borderArr is not null;
        if (borderArr is not null)
        {
            var (r, g, b) = ColorToRgb(borderArr);
            br = r; bg = g; bb = b;
        }

        // Border width from /BS /W (preferred) or legacy /Border [hr vr w].
        // Default 1pt per spec.
        double borderW = 1.0;
        if (ctx.Reader.ResolveDict(annot.Get("BS")) is PdfDictionary bs)
        {
            var w = ctx.Reader.Resolve(bs.Get("W"));
            if (w is not null) borderW = ToDouble(w);
        }
        else if (ctx.Reader.Resolve(annot.Get("Border")) is PdfArray legacyBorder && legacyBorder.Count >= 3)
        {
            borderW = ToDouble(legacyBorder[2]);
        }

        // Convert page-space rect to pixel-space. CTM for base-page rendering
        // is identity apart from the DPI scale + the y-flip.
        var pxMinX = (int)Math.Round((minX - ctx.MediaBox.LLX) * ctx.Scale);
        var pxMaxX = (int)Math.Round((maxX - ctx.MediaBox.LLX) * ctx.Scale);
        var pxMinY = (int)Math.Round(ctx.PixelH - (maxY - ctx.MediaBox.LLY) * ctx.Scale);
        var pxMaxY = (int)Math.Round(ctx.PixelH - (minY - ctx.MediaBox.LLY) * ctx.Scale);

        // Fill interior.
        if (icColor is not null)
        {
            var (ir, ig, ib) = ColorToRgb(icColor);
            if (isCircle)
                FillEllipse(ctx, pxMinX, pxMinY, pxMaxX, pxMaxY, ir, ig, ib, 255);
            else
                FillRect(ctx, pxMinX, pxMinY, pxMaxX - pxMinX, pxMaxY - pxMinY, ir, ig, ib, 255);
        }

        // Stroke border only when /C is explicitly specified. Acrobat's
        // default-appearance behavior is:
        // no /C → no visible outline, regardless of /IC or /BS. Without /IC
        // and without /C the annotation collapses to nothing on the page.
        if (borderW > 0 && borderColorSet)
        {
            var lw = (float)(borderW * ctx.Scale);
            if (lw < 1) lw = 1;
            var clip = ctx.ClipMask;
            // Draw 4 line segments forming the rect outline. Circle fallback
            // also uses the rect outline; a proper elliptical stroke can come
            // later. Callers only see Circle here when /IC is set too.
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMinX, pxMinY, pxMaxX, pxMinY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMaxX, pxMinY, pxMaxX, pxMaxY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMaxX, pxMaxY, pxMinX, pxMaxY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
            ScanlineFiller.StrokeLine(ctx.Pixels, ctx.PixelW, ctx.PixelH,
                pxMinX, pxMaxY, pxMinX, pxMinY, br, bg, bb, 255, lw, clip,
                blendMode: ctx.CurrentBlendMode, knockout: ctx.IsKnockoutGroup, softMask: ctx.SoftMaskAlpha);
        }
    }

    // Simple axis-aligned ellipse fill using the implicit equation — good
    // enough for /Circle annotation defaults. Pixel-space coordinates.
    private static void FillEllipse(RenderContext ctx, int minX, int minY, int maxX, int maxY,
        byte r, byte g, byte b, byte a)
    {
        var cx = (minX + maxX) / 2.0;
        var cy = (minY + maxY) / 2.0;
        var rx = (maxX - minX) / 2.0;
        var ry = (maxY - minY) / 2.0;
        if (rx <= 0 || ry <= 0) return;

        var y0 = Math.Max(0, minY);
        var y1 = Math.Min(ctx.PixelH, maxY);
        var x0 = Math.Max(0, minX);
        var x1 = Math.Min(ctx.PixelW, maxX);
        for (var y = y0; y < y1; y++)
        {
            var dy = (y - cy) / ry;
            for (var x = x0; x < x1; x++)
            {
                var dx = (x - cx) / rx;
                if (dx * dx + dy * dy <= 1.0)
                    SetPixel(ctx, x, y, r, g, b, a);
            }
        }
    }

    private static double[]? ResolveColorArray(PdfObject? obj)
    {
        if (obj is not PdfArray arr) return null;
        var result = new double[arr.Count];
        for (var i = 0; i < arr.Count; i++) result[i] = ToDouble(arr[i]);
        return result;
    }

    private static double ToDouble(PdfObject? obj) => obj switch
    {
        PdfInteger pi => pi.Value,
        PdfReal pr => pr.Value,
        _ => 0,
    };

    /// <summary>
    /// Fill a user-space rectangle into the pixel buffer using PDF Multiply blending
    /// (result = dest × src). Used for translucent text-markup annotations such as
    /// /Highlight, where the colour must not obscure the text it covers.
    /// </summary>
    private static void FillMultiplyRect(RenderContext ctx, double x1, double y1,
        double x2, double y2, byte sr, byte sg, byte sb)
    {
        // 2-pixel outward dilation on each edge: an anti-aliased rasterisation of
        // the rectangle carries a couple of rows/columns of partial-coverage
        // edge pixels (half-yellow). Our hard-edged multiply produces no such
        // fringe, so we paint those edge pixels fully — within a 6-pixel
        // neighbourhood the edge colouring then comes out close either way.
        var px1 = (int)Math.Floor((x1 - ctx.MediaBox.LLX) * ctx.Scale) - 2;
        var px2 = (int)Math.Ceiling((x2 - ctx.MediaBox.LLX) * ctx.Scale) + 2;
        // PDF y grows upward; pixel y grows downward, so the lower-left corner becomes the
        // higher pixel-y and vice versa.
        var py1 = (int)Math.Floor(ctx.PixelH - (y2 - ctx.MediaBox.LLY) * ctx.Scale) - 2;
        var py2 = (int)Math.Ceiling(ctx.PixelH - (y1 - ctx.MediaBox.LLY) * ctx.Scale) + 2;

        if (px1 < 0) px1 = 0;
        if (py1 < 0) py1 = 0;
        if (px2 > ctx.PixelW) px2 = ctx.PixelW;
        if (py2 > ctx.PixelH) py2 = ctx.PixelH;
        if (px1 >= px2 || py1 >= py2) return;

        var pix = ctx.Pixels;
        for (var y = py1; y < py2; y++)
        {
            var rowBase = y * ctx.PixelW * 4;
            for (var x = px1; x < px2; x++)
            {
                var p = rowBase + x * 4;
                pix[p]     = (byte)(pix[p]     * sr / 255);
                pix[p + 1] = (byte)(pix[p + 1] * sg / 255);
                pix[p + 2] = (byte)(pix[p + 2] * sb / 255);
                // Alpha stays fully opaque — the highlight doesn't punch through the page.
            }
        }
    }
}
