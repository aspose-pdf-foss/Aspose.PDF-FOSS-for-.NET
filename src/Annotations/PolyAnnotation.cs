using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Common base for polygon and polyline annotations — a chain of
/// connected vertices (PDF 32000 §12.5.6.9).</summary>
public abstract partial class PolyAnnotation : MarkupAnnotation
{
    internal PolyAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    protected PolyAnnotation(Page page, Rectangle rect) : base(page, rect) { }
    protected PolyAnnotation(Document document, Rectangle rect) : base(document, rect) { }

    /// <summary>The vertices of the path (/Vertices entry).</summary>
    public Point[] Vertices
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("Vertices")) as PdfArray;
            if (arr is null) return System.Array.Empty<Point>();
            var pts = new Point[arr.Count / 2];
            for (int i = 0; i + 1 < arr.Count; i += 2)
            {
                double x = arr[i] is PdfReal rx ? rx.Value : arr[i] is PdfInteger ix ? ix.Value : 0;
                double y = arr[i + 1] is PdfReal ry ? ry.Value : arr[i + 1] is PdfInteger iy ? iy.Value : 0;
                pts[i / 2] = new Point(x, y);
            }
            return pts;
        }
        set
        {
            var arr = new PdfArray();
            if (value is not null)
                foreach (var p in value) { arr.Add(new PdfReal(p.X)); arr.Add(new PdfReal(p.Y)); }
            Dict.Set("Vertices", arr);
        }
    }

    /// <summary>The intent of the annotation (/IT entry).</summary>
    public PolyIntent Intent
    {
        get => Dict.GetName("IT") switch
        {
            "PolygonCloud" => PolyIntent.PolygonCloud,
            "PolygonDimension" => PolyIntent.PolygonDimension,
            "PolyLineDimension" => PolyIntent.PolyLineDimension,
            _ => PolyIntent.Undefined,
        };
        set
        {
            if (value == PolyIntent.Undefined) Dict.Remove("IT");
            else Dict.Set("IT", new PdfName(value.ToString()));
        }
    }

    /// <summary>Regenerate the normal appearance (/AP /N) by stroking the
    /// vertex path (and filling it with <see cref="Annotation.InteriorColor"/>
    /// for a closed polygon).</summary>
    public override void UpdateAppearances()
    {
        var verts = Vertices;
        var r = Rect;
        if (verts.Length == 0 || r is null) { base.UpdateAppearances(); return; }
        bool polygon = Dict.GetName("Subtype") == "Polygon";
        // The appearance itself stays OPAQUE: the annotation's /CA is applied by the
        // renderer (and by viewers) when the appearance is drawn — baking it here
        // would apply the opacity twice. The flatten path, which removes the
        // annotation, wraps the stamped appearance in its own /CA graphics state.
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(Color);
        var width = GetBorderWidthValue();
        if (width != 1) b.SetLineWidth(width);
        var ic = InteriorColor;
        if (polygon && ic is not null) b.SetFillColor(ic);
        b.MoveTo(verts[0].X, verts[0].Y);
        for (int i = 1; i < verts.Length; i++) b.LineTo(verts[i].X, verts[i].Y);
        if (polygon)
        {
            b.ClosePath();
            if (ic is not null) b.FillAndStroke(); else b.Stroke();
        }
        else b.Stroke();
        b.RestoreState();
        SetNormalAppearance(b.Build(), r);
    }

    private protected static Rectangle BoundingRect(Point[] vertices)
    {
        if (vertices is null || vertices.Length == 0) return new Rectangle(0, 0, 0, 0);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var p in vertices)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return new Rectangle(minX, minY, maxX, maxY);
    }
}
