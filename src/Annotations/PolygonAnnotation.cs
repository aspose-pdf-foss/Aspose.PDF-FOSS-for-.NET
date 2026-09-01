using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class PolygonAnnotation : PolyAnnotation
{
    internal PolygonAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public PolygonAnnotation(Page page, Rectangle rect, Point[] vertices) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Polygon"));
        Vertices = vertices;
    }

    public PolygonAnnotation(Document document, Point[] vertices) : base(document, BoundingRect(vertices))
    {
        Dict.Set("Subtype", new PdfName("Polygon"));
        Vertices = vertices;
    }

    public new AnnotationType AnnotationType => AnnotationType.Polygon;
}
