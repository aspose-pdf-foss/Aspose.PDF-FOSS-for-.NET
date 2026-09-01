using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class CircleAnnotation : CommonFigureAnnotation
{
    internal CircleAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public CircleAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("Circle"));
    }

    public CircleAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Circle"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Circle;
}
