
namespace Aspose.Pdf.Text;

internal sealed partial class CffParser
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CffWidthState
{
    public Dictionary<int, int> widths = null!;
    // Parse header
    // byte 0: major version
    // byte 1: minor version
    // byte 2: header size
    // byte 3: offSize (absolute offset size)
    public byte hdrSize;
    public int pos;
    // Parse Top DICT INDEX — extract charStrings offset and Private DICT location
    public Aspose.Pdf.Text.CffParser.IndexInfo topDictIndex;
    // Parse the first Top DICT
    public byte[] topDictData = null!;
    public Dictionary<int, List<double>> topDict = null!;
    // Get charStrings offset from Top DICT (operator 17)
    public int charStringsOffset;
    public Aspose.Pdf.Text.CffParser.IndexInfo charStringsIndex;
    // Get Private DICT location from Top DICT (operator 18 = size, offset pair).
    public int privateDictSize;
    public int privateDictOffset;
}
}
