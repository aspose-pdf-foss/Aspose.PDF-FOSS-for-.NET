using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Backward-compatible alias for <see cref="RedactionAnnotation"/>.</summary>
public class RedactAnnotation : RedactionAnnotation
{
    internal RedactAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
}
