using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Underline text markup annotation.</summary>
public partial class UnderlineAnnotation : TextMarkupAnnotation
{
    internal UnderlineAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public UnderlineAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Underline"));
        SetDefaultQuadPoints(rect);
    }
    public new AnnotationType AnnotationType => AnnotationType.Underline;
}
