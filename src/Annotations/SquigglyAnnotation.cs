using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Squiggly text markup annotation.</summary>
public partial class SquigglyAnnotation : TextMarkupAnnotation
{
    internal SquigglyAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    /// <summary>Creates a squiggly-underline annotation on <c>page</c> covering <c>rect</c>. The quad points default to the four corners of <c>rect</c>.</summary>
    public SquigglyAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Squiggly"));
        SetDefaultQuadPoints(rect);
    }
    public new AnnotationType AnnotationType => AnnotationType.Squiggly;
}
