using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BlankCoverageState
{
    public Rectangle crop = null!;
    public double sum;
    public bool hard;
    public HashSet<string> counted = null!;
    public Aspose.Pdf.Core.PdfDictionary? resources0;
    public Aspose.Pdf.Core.PdfObject? contentsObj;
    public byte[] contents = null!;
    public double tolerance = 0;
}
}
