using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class SquareAnnotation : CommonFigureAnnotation
{
    internal SquareAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public SquareAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("Square"));
    }

    public SquareAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Square"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Square;
}
