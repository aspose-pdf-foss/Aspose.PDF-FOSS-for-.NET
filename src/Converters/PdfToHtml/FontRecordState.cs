using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FontRecordState
{
    public Aspose.Pdf.Core.PdfObject? fontRef;
    // resolved before any stage reads it - a resource that resolves to no dictionary is skipped
    public Aspose.Pdf.Core.PdfDictionary font = null!;
    public string baseFont = null!;
    // Parse ToUnicode CMap
    public Dictionary<int, string>? toUnicodeMap;
    // Check if this is a CID font (Type0)
    public string? fontSubtype;
    public bool isCid;
    // FontEncodingRules.DecreaseToUnicodePriorityLevel: the font program's own
    // cmap subtable outranks the /ToUnicode CMap. Exporters that pre-compose
    // text sometimes map a combining-mark CID to a space (or another filler)
    // in /ToUnicode while the embedded cmap still carries the real codepoint
    // (e.g. Thai NIKHAHIT U+0E4D) — copy/paste from the HTML then loses the
    // character unless the cmap wins. Identity CIDs are glyph ids, so the
    // reverse cmap (gid → unicode) applies directly.
    public Dictionary<int, int>? reverseCmap;
    // The ligature/unmapped-code model needs the embedded font program's
    // cmap coverage: a multi-char ToUnicode sequence stays expanded only
    // when the font can actually render its component characters, and an
    // Identity-encoded code with no unicode mapping at all is a bare glyph
    // id whose text form must be minted (U+A880 upward), exactly one new
    // character per glyph, shared across the pages of one conversion.
    public bool isIdentity;
    public bool hasMultiDst;
    // With Identity ENCODING the 2-byte code is the CID, but the CID is the
    // glyph id only under an Identity /CIDToGIDMap. A CIDToGIDMap STREAM
    // (packed big-endian uint16 per CID) marks the codes as true CIDs.
    public PdfStream? c2gStream;
    public HashSet<int>? cmapChars;
    public Dictionary<int, int>? gidToUnicode;
    public Func<int, bool>? glyphMapped;
    public Func<int, double?>? embeddedAdvMilli;
    public Func<int, double?>? programAdvMilli;
    public Func<int, double?>? programCharAdvMilli;
    public LigatureSubstitutor? substitutor;
    public Func<byte[], string>? toUnicodeFunc;
    public double ascentFactor;
    public double lineHeightEm;
    public byte[]? ascSfnt;
    // Without a ToUnicode CMap the show bytes still decode through the
    // font's base encoding (MacRoman/WinAnsi/Standard, /Differences with
    // glyph names, embedded-program cmap/post) rather than raw Latin1 —
    // a MacRomanEncoding font's quotes (0xD2/0xD5) otherwise render as
    // Ò/Õ mojibake.
    public Aspose.Pdf.Core.PdfDictionary fontForDecode = null!;
    public Func<byte[], string>? baseDecode;
    // The EMBEDDED program's character coverage: a rendered char the
    // subset cannot map falls to the CSS fallback face, which cuts a
    // span and switches the measuring metrics.
    public Func<int, bool>? subsetHas;
    public byte[]? subsetSfnt;
    // A bare CFF (Type1C) — or an Adobe Type 1 program (FontFile) — is
    // shipped as a TrueType sfnt synthesized from the charstrings, so the
    // served face's advances are the program's own — the same values the
    // font dict's /Widths carries. Handing the browser model those
    // advances leaves each glyph's error as exactly its Tc/Tw and TJ
    // kern contribution, which is what the letter-spacing solves against.
    public System.Func<int, double>? advanceMap;
    // A substituted CJK font serves the substitute face's subset, so the
    // browser-model metrics are that face's own advances — the same
    // basis the shipped @font-face program carries, which is what the
    // em-compensation solve and the re-import both measure against.
    public bool substituteFace;
}
}
