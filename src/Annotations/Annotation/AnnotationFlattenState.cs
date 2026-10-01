using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class Annotation
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class AnnotationFlattenState
{
    // Get the page this annotation belongs to
    public Aspose.Pdf.Core.PdfDictionary? pageDict;
    public string? subtype;
    public Aspose.Pdf.Core.PdfStream? appearanceStream;
}
}
