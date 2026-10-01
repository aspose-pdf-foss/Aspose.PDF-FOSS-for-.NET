using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>A polygon annotation: a closed shape through a list of vertices.</summary>
public partial class PolygonAnnotation : PolyAnnotation
{
    internal PolygonAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Creates a polygon annotation on <c>page</c> inside <c>rect</c> with the given vertices (page coordinates in points).</summary>
    public PolygonAnnotation(Page page, Rectangle rect, Point[] vertices) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Polygon"));
        Vertices = vertices;
    }

    /// <summary>Creates a polygon annotation for <c>document</c> with the given vertices; the rectangle is the bounding box of the vertices.</summary>
    public PolygonAnnotation(Document document, Point[] vertices) : base(document, BoundingRect(vertices))
    {
        Dict.Set("Subtype", new PdfName("Polygon"));
        Vertices = vertices;
    }

    public new AnnotationType AnnotationType => AnnotationType.Polygon;
}
