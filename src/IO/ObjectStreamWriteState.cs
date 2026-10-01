using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.IO;

internal sealed partial class PdfWriter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ObjectStreamWriteState
{
    // Collect eligible objects: non-stream, non-encrypted, small enough, no inline streams
    public List<int> eligible = null!;
    // Build groups of eligible objects (up to MaxObjectsPerStream per group)
    public Dictionary<int, (int streamObjNum, int indexInStream)> compressedEntries = null!;
    public List<List<int>> groups = null!;
    public PdfDictionary trailerEntries = default!;
}
}
