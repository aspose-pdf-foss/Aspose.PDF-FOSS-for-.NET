using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>A square annotation: a rectangle drawn at the annotation rectangle.</summary>
public partial class SquareAnnotation : CommonFigureAnnotation
{
    internal SquareAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Creates a square annotation for <c>document</c> with an empty rectangle; set its rectangle and add it to a page.</summary>
    public SquareAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("Square"));
    }

    /// <summary>Creates a square annotation on <c>page</c>: a rectangle drawn at <c>rect</c>.</summary>
    public SquareAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Square"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Square;
}
