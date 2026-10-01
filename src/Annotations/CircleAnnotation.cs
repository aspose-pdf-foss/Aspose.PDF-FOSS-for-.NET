using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>A circle annotation: an ellipse drawn inside the annotation rectangle.</summary>
public partial class CircleAnnotation : CommonFigureAnnotation
{
    internal CircleAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Creates a circle annotation for <c>document</c> with an empty rectangle; set its rectangle and add it to a page.</summary>
    public CircleAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("Circle"));
    }

    /// <summary>Creates a circle annotation on <c>page</c>: an ellipse fitted inside <c>rect</c>.</summary>
    public CircleAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Circle"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Circle;
}
