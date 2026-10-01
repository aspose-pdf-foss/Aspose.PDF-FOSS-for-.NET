using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Underline text markup annotation.</summary>
public partial class UnderlineAnnotation : TextMarkupAnnotation
{
    internal UnderlineAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    /// <summary>Creates an underline annotation on <c>page</c> covering <c>rect</c>. The quad points default to the four corners of <c>rect</c>.</summary>
    public UnderlineAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Underline"));
        SetDefaultQuadPoints(rect);
    }
    public new AnnotationType AnnotationType => AnnotationType.Underline;
}
