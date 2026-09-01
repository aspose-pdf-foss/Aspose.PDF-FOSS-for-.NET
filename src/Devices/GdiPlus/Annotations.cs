using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer
{
    private void DrawAnnotations(PdfDictionary pageDict)
    {
        if (_reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots) return;
        foreach (var item in annots)
        {
            var annot = _reader.ResolveDict(item);
            if (annot is null) continue;
            var flags = (int)annot.GetInt("F");
            if ((flags & 0x02) != 0) continue; // Hidden
            var subtype = annot.GetName("Subtype");

            if (_reader.ResolveDict(annot.Get("AP")) is not null)
            {
                SafeDraw(() => DrawAppearanceAnnotation(annot));
                continue;
            }
            if (subtype == "Square" || subtype == "Circle")
                SafeDraw(() => DrawSquareCircleDefault(annot, subtype == "Circle"));
            else if (subtype == "Highlight")
                SafeDraw(() => DrawHighlightDefault(annot));
            // A /Text (sticky-note) annotation without /AP renders as its standard
            // note icon (PDF 32000 §12.5.6.4): NoZoom notes stretch the icon into
            // /Rect; otherwise the icon paints at its natural size anchored at the
            // rectangle's top-left corner.
            else if (subtype == "Text")
            {
                var form = Aspose.Pdf.Annotations.TextAnnotationIcons.BuildIconForm(annot, _reader);
                var target = (flags & 0x08) != 0 ? null : TextIconNaturalRect(annot);
                SafeDraw(() => DrawAppearanceForm(annot, form, target));
            }
            else if (subtype == "Popup")
            {
                var form = Aspose.Pdf.Annotations.PopupAppearance.BuildOpenPopupForm(annot, _reader);
                if (form is not null) SafeDraw(() => DrawAppearanceForm(annot, form));
            }
        }
    }

    private void DrawAppearanceAnnotation(PdfDictionary annot)
    {
        var ap = _reader.ResolveDict(annot.Get("AP"));
        if (ap is null) return;
        var nEntry = _reader.Resolve(ap.Get("N"));
        PdfStream? formStream = null;
        if (nEntry is PdfStream direct) formStream = direct;
        else if (nEntry is PdfDictionary stateDict)
        {
            var asName = annot.GetName("AS");
            if (!string.IsNullOrEmpty(asName)) formStream = _reader.ResolveStream(stateDict.Get(asName));
        }
        if (formStream is null) return;
        DrawAppearanceForm(annot, formStream);
    }

    /// <summary>Paint a Form XObject appearance into the annotation's /Rect, mapping the
    /// form's transformed /BBox onto the rectangle (PDF 32000 §12.5.5). Shared by the
    /// /AP path and by synthesised appearances (e.g. open popups). An explicit
    /// <paramref name="targetRect"/> paints the form there instead of at /Rect.</summary>
    private void DrawAppearanceForm(PdfDictionary annot, PdfStream formStream,
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
            double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]), rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
            rMinX = Math.Min(rx1, rx2); rMaxX = Math.Max(rx1, rx2);
            rMinY = Math.Min(ry1, ry2); rMaxY = Math.Max(ry1, ry2);
        }
        double rW = rMaxX - rMinX, rH = rMaxY - rMinY;
        if (rW <= 0 || rH <= 0) return;

        if (formStream.Dict.Get("BBox") is not PdfArray bbox || bbox.Count < 4) return;
        double bx1 = NumFrom(bbox[0]), by1 = NumFrom(bbox[1]), bx2 = NumFrom(bbox[2]), by2 = NumFrom(bbox[3]);
        var fm = ExtractFormMatrix(formStream.Dict) ?? new double[] { 1, 0, 0, 1, 0, 0 };
        double tMinX = double.PositiveInfinity, tMinY = double.PositiveInfinity, tMaxX = double.NegativeInfinity, tMaxY = double.NegativeInfinity;
        foreach (var (cx, cy) in new[] { (bx1, by1), (bx2, by1), (bx2, by2), (bx1, by2) })
        {
            var tx = fm[0] * cx + fm[2] * cy + fm[4];
            var ty = fm[1] * cx + fm[3] * cy + fm[5];
            if (tx < tMinX) tMinX = tx; if (tx > tMaxX) tMaxX = tx;
            if (ty < tMinY) tMinY = ty; if (ty > tMaxY) tMaxY = ty;
        }
        double tW = tMaxX - tMinX, tH = tMaxY - tMinY;
        if (tW <= 0 || tH <= 0) return;
        double sx = rW / tW, sy = rH / tH;
        var outerCtm = new double[] { sx, 0, 0, sy, rMinX - tMinX * sx, rMinY - tMinY * sy };

        // Annotation constant alpha (/CA, PDF 32000 §12.5.2): the whole appearance is
        // composited at this opacity, as if it were an isolated transparency group.
        var ca = annot.Get("CA") is { } caObj ? NumFrom(caObj) : 1.0;
        if (ca <= 0) return; // fully transparent: nothing to paint
        var state = new GraphicsState { Ctm = outerCtm };
        if (ca < 1) { state.FillAlpha = ca; state.StrokeAlpha = ca; }

        DrawFormXObject(formStream, state, forceComposite: ca < 0.999);
    }

    private void DrawSquareCircleDefault(PdfDictionary annot, bool isCircle)
    {
        if (annot.Get("Rect") is not PdfArray rect || rect.Count < 4) return;
        double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]), rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
        float x = (float)Math.Min(rx1, rx2), y = (float)Math.Min(ry1, ry2);
        float w = (float)Math.Abs(rx2 - rx1), h = (float)Math.Abs(ry2 - ry1);
        if (w <= 0 || h <= 0) return;

        var saved = _g.Transform;
        using var world = WorldMatrix(new double[] { 1, 0, 0, 1, 0, 0 });
        _g.Transform = world;
        try
        {
            var ic = ParseAnnotColor(annot.Get("IC"));
            var bc = ParseAnnotColor(annot.Get("C"));
            if (ic is { } fillCol)
            {
                using var b = new SolidBrush(fillCol);
                if (isCircle) _g.FillEllipse(b, x, y, w, h); else _g.FillRectangle(b, x, y, w, h);
            }
            float bw = 1f;
            if (_reader.ResolveDict(annot.Get("BS")) is { } bs) bw = (float)NumFrom(bs.Get("W"));
            if (bw > 0 && bc is { } borderCol)
            {
                using var p = new Pen(borderCol, bw);
                if (isCircle) _g.DrawEllipse(p, x, y, w, h); else _g.DrawRectangle(p, x, y, w, h);
            }
        }
        finally { _g.Transform = saved; }
    }

    /// <summary>Default appearance for a text-markup Highlight annotation that
    /// carries no /AP stream: paint each QuadPoints quadrilateral (or the /Rect when
    /// QuadPoints is absent) in the annotation colour using the Multiply blend mode
    /// (PDF 32000 §12.5.6.10), so underlying text shows through.</summary>
    private void DrawHighlightDefault(PdfDictionary annot)
    {
        var col = ParseAnnotColor(annot.Get("C"));
        if (col is not { } c) return;

        using var path = new GraphicsPath { FillMode = FillMode.Winding };
        if (_reader.Resolve(annot.Get("QuadPoints")) is PdfArray qp && qp.Count >= 8 && qp.Count % 8 == 0)
        {
            for (int i = 0; i + 7 < qp.Count; i += 8)
            {
                // Each quad: (x1,y1)(x2,y2)(x3,y3)(x4,y4). Use the quad's bounding box.
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                for (int j = 0; j < 8; j += 2)
                {
                    double px = NumFrom(qp[i + j]), py = NumFrom(qp[i + j + 1]);
                    if (px < minX) minX = px; if (px > maxX) maxX = px;
                    if (py < minY) minY = py; if (py > maxY) maxY = py;
                }
                float w = (float)(maxX - minX), h = (float)(maxY - minY);
                if (w > 0 && h > 0) path.AddRectangle(new RectangleF((float)minX, (float)minY, w, h));
            }
        }
        else if (annot.Get("Rect") is PdfArray rect && rect.Count >= 4)
        {
            double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]), rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
            float x = (float)Math.Min(rx1, rx2), y = (float)Math.Min(ry1, ry2);
            float w = (float)Math.Abs(rx2 - rx1), h = (float)Math.Abs(ry2 - ry1);
            if (w <= 0 || h <= 0) return;
            path.AddRectangle(new RectangleF(x, y, w, h));
        }
        if (path.PointCount == 0) return;

        using var world = WorldMatrix(new double[] { 1, 0, 0, 1, 0, 0 });
        FillPathBlended(path, world, Rasterizer.BlendMode.Multiply,
            new GraphicsState { FillR = c.R / 255.0, FillG = c.G / 255.0, FillB = c.B / 255.0, FillAlpha = 1.0 });
    }

    private GdiColor? ParseAnnotColor(PdfObject? o)
    {
        if (_reader.Resolve(o) is not PdfArray arr) return null;
        switch (arr.Count)
        {
            case 1: { int v = Clamp255(NumFrom(arr[0])); return GdiColor.FromArgb(v, v, v); }
            case 3: return GdiColor.FromArgb(Clamp255(NumFrom(arr[0])), Clamp255(NumFrom(arr[1])), Clamp255(NumFrom(arr[2])));
            case 4:
                double c = NumFrom(arr[0]), m = NumFrom(arr[1]), yv = NumFrom(arr[2]), k = NumFrom(arr[3]);
                return GdiColor.FromArgb(Clamp255((1 - c) * (1 - k)), Clamp255((1 - m) * (1 - k)), Clamp255((1 - yv) * (1 - k)));
            default: return null; // empty array = transparent / no colour
        }
    }

    /// <summary>Convert a 32bpp ARGB GDI+ bitmap (BGRA byte order) to an RGBA buffer.</summary>
    private static RgbaBuffer ToRgbaBuffer(Bitmap bmp, int w, int h)
    {
        var data = new byte[w * h * 4];
        var rect = new System.Drawing.Rectangle(0, 0, w, h);
        var bits = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = bits.Stride;
            var scan = bits.Scan0;
            var row = new byte[stride];
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(scan + y * stride, row, 0, stride);
                int di = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int si = x * 4;
                    // GDI+ Format32bppArgb is little-endian BGRA in memory.
                    data[di + 0] = row[si + 2]; // R
                    data[di + 1] = row[si + 1]; // G
                    data[di + 2] = row[si + 0]; // B
                    data[di + 3] = row[si + 3]; // A
                    di += 4;
                }
            }
        }
        finally { bmp.UnlockBits(bits); }
        return new RgbaBuffer(data, w, h);
    }
}
