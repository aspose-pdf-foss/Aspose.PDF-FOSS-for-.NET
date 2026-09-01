using System.Collections;
using System.Globalization;
using System.Text;
namespace Aspose.Pdf.Core;

internal sealed class PdfString : PdfObject
{
    public byte[] Value { get; }
    public bool IsHex { get; }

    public PdfString(byte[] value, bool isHex = false)
    {
        Value = value;
        IsHex = isHex;
    }

    public string ToText()
    {
        // PDF text strings may be UTF-16BE (with BOM \xFE\xFF) or PDFDocEncoding (PDF 32000:2008 D.2)
        if (Value.Length >= 2 && Value[0] == 0xFE && Value[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(Value, 2, Value.Length - 2);
        // UTF-16LE (BOM \xFF\xFE) — non-standard for PDF but emitted by many
        // Windows/Word producers; decode it rather than returning raw bytes.
        if (Value.Length >= 2 && Value[0] == 0xFF && Value[1] == 0xFE)
            return Encoding.Unicode.GetString(Value, 2, Value.Length - 2);
        // UTF-8 BOM
        if (Value.Length >= 3 && Value[0] == 0xEF && Value[1] == 0xBB && Value[2] == 0xBF)
            return Encoding.UTF8.GetString(Value, 3, Value.Length - 3);
        return DecodePdfDocEncoding(Value);
    }

    // PDFDocEncoding → Unicode (PDF 32000:2008 Annex D.2). Bytes 0x00-0x17 and
    // 0x20-0x7E match ASCII; 0x18-0x1F and 0x80-0xA0 are PDFDocEncoding-specific;
    // 0xA1-0xFF match ISO Latin-1 (with 0xAD undefined, mapped to U+00AD for round-trip safety).
    private static readonly char[] PdfDocEncodingTable = BuildPdfDocEncoding();

    private static char[] BuildPdfDocEncoding()
    {
        var t = new char[256];
        for (int i = 0; i < 256; i++) t[i] = (char)i;
        t[0x18] = '˘'; t[0x19] = 'ˇ'; t[0x1A] = 'ˆ'; t[0x1B] = '˙';
        t[0x1C] = '˝'; t[0x1D] = '˛'; t[0x1E] = '˚'; t[0x1F] = '˜';
        t[0x80] = '•'; t[0x81] = '†'; t[0x82] = '‡'; t[0x83] = '…';
        t[0x84] = '—'; t[0x85] = '–'; t[0x86] = 'ƒ'; t[0x87] = '⁄';
        t[0x88] = '‹'; t[0x89] = '›'; t[0x8A] = '−'; t[0x8B] = '‰';
        t[0x8C] = '„'; t[0x8D] = '“'; t[0x8E] = '”'; t[0x8F] = '‘';
        t[0x90] = '’'; t[0x91] = '‚'; t[0x92] = '™'; t[0x93] = 'ﬁ';
        t[0x94] = 'ﬂ'; t[0x95] = 'Ł'; t[0x96] = 'Œ'; t[0x97] = 'Š';
        t[0x98] = 'Ÿ'; t[0x99] = 'Ž'; t[0x9A] = 'ı'; t[0x9B] = 'ł';
        t[0x9C] = 'œ'; t[0x9D] = 'š'; t[0x9E] = 'ž'; t[0xA0] = '€';
        // 0x9F and 0xAD are undefined in PDFDocEncoding; leave as raw byte for round-trip.
        return t;
    }

    private static string DecodePdfDocEncoding(byte[] bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) chars[i] = PdfDocEncodingTable[bytes[i]];
        return new string(chars);
    }

    public override string ToString()
    {
        if (IsHex)
            return $"<{Convert.ToHexString(Value)}>";
        return $"({Encoding.Latin1.GetString(Value)})";
    }
}
