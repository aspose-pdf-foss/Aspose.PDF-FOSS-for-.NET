using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

internal static partial class PdfAValidator
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TransparencyCheckState
{
    // Check ExtGState for transparency-related entries
    public Aspose.Pdf.Core.PdfDictionary? resources;
    public Aspose.Pdf.Core.PdfDictionary? extGStateDict;
    public Document document = default!;
    public Page page = default!;
    public bool isPdfA1 = false;
    public List<string> issues = default!;
    public List<PdfAViolation> violations = default!;
}
}
