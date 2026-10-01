using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

internal sealed partial class XRefTable
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TraditionalXrefState
{
    public long pos;
    public bool firstSubsection;
    public IO.PdfParser parser = null!;
    public Aspose.Pdf.Core.PdfObject trailerObj = null!;
    public byte[] data = default!;
    public long offset = 0;
    public HashSet<long> visited = default!;
}
}
