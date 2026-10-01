using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.IO;

internal sealed partial class PdfWriter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class XRefStreamWriteState
{
    // The xref stream object gets its own object number
    public int xrefObjNum;
    public long xrefOffset;
    // Find the range of object numbers
    public int maxObjNum;
    public int size;
    // Determine field widths: type=1 byte, offset needs enough bytes for max offset,
    // gen/index needs enough bytes
    public long maxOffset;
    // Field 2 of a TYPE-2 entry holds the containing object STREAM's number, so /W[1]
    // must cover the largest such number as well as the largest byte offset — a small
    // file that keeps large inherited object numbers (e.g. a 5 KB save carrying object
    // 100003) otherwise writes the stream number truncated to the offset width.
    public long maxField2;
    public int w2;
    // For generation/index: typically small, but check compressed entries too
    public long maxField3;
    public int w3;
    // Build binary xref data
    // Entry layout: [type:1] [field2:w2] [field3:w3]
    public int entrySize;
    public byte[] streamData = null!;
    // Compress the xref stream data
    public byte[] compressedData = null!;
    // Build the xref stream dictionary (which also serves as the trailer)
    public Aspose.Pdf.Core.PdfDictionary xrefDict = null!;
    public Aspose.Pdf.Core.PdfArray wArray = null!;
    public PdfDictionary trailerEntries = default!;
    public Dictionary<int, (int streamObjNum, int indexInStream)>? compressedEntries = null;
}
}
