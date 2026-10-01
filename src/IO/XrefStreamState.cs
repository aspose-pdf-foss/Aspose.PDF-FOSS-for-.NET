using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

internal sealed partial class XRefTable
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class XrefStreamState
{
    public IO.PdfParser parser = null!;
    // No strict header check here: /Prev chain nodes with shifted offsets rely
    // on the lexer's garbage-skipping recovery (a broken chain). The TOP-LEVEL
    // startxref target is vetted in Read() instead — only a target that names
    // no object at all routes to the tail-stream recovery.
    public Aspose.Pdf.Core.IndirectObject indirectObj;
    public Aspose.Pdf.Core.PdfDictionary dict = null!;
    // Decode the stream
    public byte[] decodedData = null!;
    // Parse W array
    public Aspose.Pdf.Core.PdfArray? wArray;
    public int w1;
    public int w2;
    public int w3;
    public int entrySize;
    // Parse Index array (default: [0 Size])
    public Aspose.Pdf.Core.PdfArray? indexArray;
    public int size;
    public List<(int start, int count)> subsections = null!;
    public int dataPos;
    // Follow /Prev
    public long prev;
    public byte[] data = default!;
    public long offset = 0;
    public HashSet<long> visited = default!;
}
}
