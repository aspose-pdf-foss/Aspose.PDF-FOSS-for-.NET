using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Squiggly text markup annotation.</summary>
public partial class SquigglyAnnotation : TextMarkupAnnotation
{
    internal SquigglyAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public SquigglyAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Squiggly"));
        SetDefaultQuadPoints(rect);
    }
    public new AnnotationType AnnotationType => AnnotationType.Squiggly;
}
