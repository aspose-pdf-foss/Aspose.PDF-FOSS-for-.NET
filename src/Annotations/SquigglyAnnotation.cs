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

    // The wave rises and falls every sixth of the marked line's height, by half of that step.
    private const double StepsPerLineHeight = 6;

    /// <summary>Regenerate the normal appearance (/AP /N): a wave along the bottom of each quad, in the
    /// annotation colour.</summary>
    public override void UpdateAppearances() =>
        DrawLineMarks((b, quad, width) =>
        {
            var step = (quad.maxY - quad.minY) / StepsPerLineHeight;
            if (step <= 0) step = width;
            var low = quad.minY + width;
            var x = quad.minX;
            var up = true;
            b.MoveTo(x, low);
            while (x < quad.maxX)
            {
                x = Math.Min(x + step, quad.maxX);
                b.LineTo(x, up ? low + step / 2 : low);
                up = !up;
            }
        });
}
