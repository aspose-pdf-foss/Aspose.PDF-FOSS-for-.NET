using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class CidFontInfo
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CidFontBuildState
{
    // /Encoding can be a direct name OR an indirect reference to a CMap stream
    // (PDF 32000 §9.7.5.2). Resolve through the reference first; if the resolved
    // object is a stream, peek at its dict's /CMapName for the predefined-name
    // lookup, otherwise inspect the stream's `begincodespacerange ... endcode-
    // spacerange` declarations to decide 2-byte vs 1-byte. Without this,
    // sub-setted PDFs that emit `/Encoding 23 0 R` for an embedded custom CMap
    // (CMapName = subset-prefix+family, not a predefined name) get isTwoByte =
    // false and the renderer walks raw bytes one at a time — every 2-byte CID
    // is drawn as two .notdef glyphs, doubling visible letter-spacing.
    public string? encoding;
    public PdfStream? encStream;
    public bool isTwoByte;
    // If the CMap name didn't identify a known encoding, fall back to parsing
    // the stream's codespace ranges. Custom subset CMaps name themselves after
    // the font (e.g. "NQTMYA+Lucida Sans Unicode,Bold") which can't be guessed.
    public System.Collections.Generic.Dictionary<int, int>? cmapCodeToCid;
    public bool singleByteCMap;
    // Uni*-UCS2-* and Uni*-UTF16-* CMaps emit Unicode codepoints (not
    // Adobe CIDs) for each 2-byte code. Identity-H/V and the legacy
    // bytecode CMaps (GB-EUC, ETen-B5, KSC-EUC, etc.) emit CIDs.
    public bool isUnicodeEnc;
    // DescendantFonts[0] holds the CIDFont dictionary that owns /CIDToGIDMap
    // and /CIDSystemInfo.
    public int[]? cidToGid;
    public string? ordering;
    public double vertOriginY;
    public double vertAdvance;
    public Dictionary<int, (double, double, double)>? w2;
    public Aspose.Pdf.Core.PdfObject? descendantsObj = null!;
    public PdfDictionary fontDict = default!;
    public PdfReader reader = default!;
}
}
