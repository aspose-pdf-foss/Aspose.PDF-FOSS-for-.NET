using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class CffParserTests
{
    /// <summary>
    /// Build a minimal valid CFF font with the given parameters.
    /// </summary>
    private static byte[] BuildMinimalCff(
        int defaultWidthX = 0,
        int nominalWidthX = 0,
        byte[][]? charstrings = null,
        string fontName = "Test")
    {
        using var ms = new MemoryStream();

        // We'll build the CFF in sections and patch offsets at the end.

        // ── Header (4 bytes) ──
        ms.WriteByte(1); // major
        ms.WriteByte(0); // minor
        ms.WriteByte(4); // hdrSize
        ms.WriteByte(1); // offSize

        // ── Name INDEX ──
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(fontName);
        WriteIndex(ms, new[] { nameBytes });

        // We need to know the Top DICT's position to calculate offsets.
        // Strategy: write a placeholder Top DICT INDEX, then String INDEX, Global Subr INDEX,
        // CharStrings INDEX, Private DICT, then patch the Top DICT offsets.

        // Prepare charstrings data
        charstrings ??= new[] { new byte[] { 139, 14 } }; // single glyph: width=defaultWidthX, endchar

        // Prepare Private DICT
        var privateDictBytes = BuildPrivateDict(defaultWidthX, nominalWidthX);

        // We need to calculate the offsets for the Top DICT.
        // Layout after Top DICT INDEX:
        //   String INDEX (empty: 2 bytes [0,0])
        //   Global Subr INDEX (empty: 2 bytes [0,0])
        //   CharStrings INDEX
        //   Private DICT

        // The Top DICT INDEX position is current position.
        // We'll calculate final offsets after knowing the Top DICT size.
        // Use a two-pass approach: first estimate, then adjust.

        // Build the Top DICT content with placeholder offsets, measure, then rebuild.
        // For simplicity, use 29 (4-byte int) encoding for offsets which is always 5 bytes.

        // Calculate: after Top DICT INDEX, we have:
        //   String INDEX: 2 bytes
        //   Global Subr INDEX: 2 bytes
        //   Then CharStrings INDEX
        //   Then Private DICT

        // Top DICT INDEX overhead: 2 (count) + 1 (offSize) + 2*offSize (offsets) + dictLen
        // We need to know dictLen to know the charStrings offset.

        // Pass 1: build Top DICT with dummy large offsets to get exact byte count
        var dummyTopDict = BuildTopDict(9999, privateDictBytes.Length, 9999);
        var topDictIndexOverhead = 2 + 1 + 2 * 1 + dummyTopDict.Length; // count=1, offSize=1, 2 offsets

        // Wait - offSize for the Top DICT INDEX could be larger if data > 255 bytes.
        // With offSize=1, max data is 255 bytes. Our Top DICT should be small enough.
        // Recalculate properly:
        var topDictDataLen = dummyTopDict.Length;
        var topDictIndexSize = 2 + 1 + 2 * 1 + topDictDataLen; // assuming offSize=1

        var stringIndexSize = 2; // empty: count=0
        var globalSubrIndexSize = 2; // empty: count=0

        var charStringsOffset = (int)ms.Position + topDictIndexSize + stringIndexSize + globalSubrIndexSize;

        // Calculate CharStrings INDEX size
        var csIndexSize = CalculateIndexSize(charstrings);

        var privateDictOffset = charStringsOffset + csIndexSize;

        // Now rebuild Top DICT with correct offsets
        var topDict = BuildTopDict(charStringsOffset, privateDictBytes.Length, privateDictOffset);

        // If the size changed, recalculate (unlikely with 4-byte int encoding)
        if (topDict.Length != dummyTopDict.Length)
        {
            topDictIndexSize = 2 + 1 + 2 * 1 + topDict.Length;
            charStringsOffset = (int)ms.Position + topDictIndexSize + stringIndexSize + globalSubrIndexSize;
            privateDictOffset = charStringsOffset + csIndexSize;
            topDict = BuildTopDict(charStringsOffset, privateDictBytes.Length, privateDictOffset);
        }

        // ── Top DICT INDEX ──
        WriteIndex(ms, new[] { topDict });

        // ── String INDEX (empty) ──
        ms.WriteByte(0);
        ms.WriteByte(0);

        // ── Global Subr INDEX (empty) ──
        ms.WriteByte(0);
        ms.WriteByte(0);

        // ── CharStrings INDEX ──
        WriteIndex(ms, charstrings);

        // ── Private DICT ──
        ms.Write(privateDictBytes);

        return ms.ToArray();
    }

    /// <summary>
    /// Build a Top DICT with charStrings offset (op 17) and Private DICT (op 18).
    /// </summary>
    private static byte[] BuildTopDict(int charStringsOffset, int privateDictSize, int privateDictOffset)
    {
        using var ms = new MemoryStream();

        // charStrings offset: operator 17
        EncodeDictInt(ms, charStringsOffset);
        ms.WriteByte(17);

        // Private DICT: size then offset, operator 18
        EncodeDictInt(ms, privateDictSize);
        EncodeDictInt(ms, privateDictOffset);
        ms.WriteByte(18);

        return ms.ToArray();
    }

    /// <summary>
    /// Build a Private DICT with defaultWidthX (op 20) and nominalWidthX (op 21).
    /// </summary>
    private static byte[] BuildPrivateDict(int defaultWidthX, int nominalWidthX)
    {
        using var ms = new MemoryStream();

        if (defaultWidthX != 0)
        {
            EncodeDictInt(ms, defaultWidthX);
            ms.WriteByte(20); // defaultWidthX operator
        }

        if (nominalWidthX != 0)
        {
            EncodeDictInt(ms, nominalWidthX);
            ms.WriteByte(21); // nominalWidthX operator
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Encode an integer as a DICT operand.
    /// Uses the 4-byte integer encoding (operator 29) for simplicity.
    /// </summary>
    private static void EncodeDictInt(MemoryStream ms, int value)
    {
        // Use 29 (4-byte integer) encoding for values outside single-byte range
        if (value >= -107 && value <= 107)
        {
            ms.WriteByte((byte)(value + 139));
        }
        else
        {
            ms.WriteByte(29); // 4-byte integer marker
            ms.WriteByte((byte)((value >> 24) & 0xFF));
            ms.WriteByte((byte)((value >> 16) & 0xFF));
            ms.WriteByte((byte)((value >> 8) & 0xFF));
            ms.WriteByte((byte)(value & 0xFF));
        }
    }

    /// <summary>
    /// Write a CFF INDEX structure to a stream.
    /// </summary>
    private static void WriteIndex(MemoryStream ms, byte[][] entries)
    {
        var count = (ushort)entries.Length;
        ms.WriteByte((byte)(count >> 8));
        ms.WriteByte((byte)(count & 0xFF));

        if (count == 0) return;

        // Calculate total data size to determine offSize
        var totalData = 0;
        foreach (var entry in entries)
            totalData += entry.Length;

        // Determine offSize (1-4 bytes)
        byte offSize;
        if (totalData + 1 <= 0xFF) offSize = 1;
        else if (totalData + 1 <= 0xFFFF) offSize = 2;
        else if (totalData + 1 <= 0xFFFFFF) offSize = 3;
        else offSize = 4;

        ms.WriteByte(offSize);

        // Write offsets (1-based)
        var offset = 1;
        WriteOffset(ms, offset, offSize);
        foreach (var entry in entries)
        {
            offset += entry.Length;
            WriteOffset(ms, offset, offSize);
        }

        // Write data
        foreach (var entry in entries)
            ms.Write(entry);
    }

    private static void WriteOffset(MemoryStream ms, int offset, byte offSize)
    {
        switch (offSize)
        {
            case 4: ms.WriteByte((byte)((offset >> 24) & 0xFF)); goto case 3;
            case 3: ms.WriteByte((byte)((offset >> 16) & 0xFF)); goto case 2;
            case 2: ms.WriteByte((byte)((offset >> 8) & 0xFF)); goto case 1;
            case 1: ms.WriteByte((byte)(offset & 0xFF)); break;
        }
    }

    private static int CalculateIndexSize(byte[][] entries)
    {
        if (entries.Length == 0) return 2;

        var totalData = 0;
        foreach (var entry in entries)
            totalData += entry.Length;

        byte offSize;
        if (totalData + 1 <= 0xFF) offSize = 1;
        else if (totalData + 1 <= 0xFFFF) offSize = 2;
        else if (totalData + 1 <= 0xFFFFFF) offSize = 3;
        else offSize = 4;

        return 2 + 1 + (entries.Length + 1) * offSize + totalData;
    }

    // ── Charstring building helpers ─────────────────────────────────────

    /// <summary>
    /// Build a Type 2 charstring with just endchar (no explicit width → uses defaultWidthX).
    /// </summary>
    private static byte[] BuildCharstringNoWidth()
    {
        // endchar only
        return new byte[] { 14 };
    }

    /// <summary>
    /// Build a Type 2 charstring with an explicit width value before vmoveto.
    /// Width = nominalWidthX + widthDelta.
    /// Format: widthDelta vmoveto(4) dy endchar(14)
    /// </summary>
    private static byte[] BuildCharstringWithWidth(int widthDelta, int dy = 100)
    {
        using var ms = new MemoryStream();
        EncodeCharstringInt(ms, widthDelta); // width delta
        EncodeCharstringInt(ms, dy);         // dy for vmoveto
        ms.WriteByte(4);                     // vmoveto
        ms.WriteByte(14);                    // endchar
        return ms.ToArray();
    }

    /// <summary>
    /// Build a charstring with width before endchar (simplest form).
    /// Format: widthDelta endchar(14)
    /// </summary>
    private static byte[] BuildCharstringWidthEndchar(int widthDelta)
    {
        using var ms = new MemoryStream();
        EncodeCharstringInt(ms, widthDelta);
        ms.WriteByte(14); // endchar
        return ms.ToArray();
    }

    private static void EncodeCharstringInt(MemoryStream ms, int value)
    {
        if (value >= -107 && value <= 107)
        {
            ms.WriteByte((byte)(value + 139));
        }
        else if (value >= 108 && value <= 1131)
        {
            var adjusted = value - 108;
            ms.WriteByte((byte)(247 + (adjusted >> 8)));
            ms.WriteByte((byte)(adjusted & 0xFF));
        }
        else if (value >= -1131 && value <= -108)
        {
            var adjusted = -value - 108;
            ms.WriteByte((byte)(251 + (adjusted >> 8)));
            ms.WriteByte((byte)(adjusted & 0xFF));
        }
        else
        {
            // Use 28 (2-byte int) for charstring context
            ms.WriteByte(28);
            ms.WriteByte((byte)((value >> 8) & 0xFF));
            ms.WriteByte((byte)(value & 0xFF));
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseMinimalCffHeader()
    {
        var cff = BuildMinimalCff();
        var parser = new CffParser(cff);
        var widths = parser.ExtractWidths();

        Assert.True(parser.GlyphCount > 0, "Should parse at least one glyph");
        Assert.NotNull(widths);
    }

    [Fact]
    public void ParseIndexWithSingleEntry()
    {
        // Build CFF with exactly one glyph (the .notdef)
        var cs = new[] { BuildCharstringNoWidth() };
        var cff = BuildMinimalCff(charstrings: cs);
        var parser = new CffParser(cff);
        var widths = parser.ExtractWidths();

        Assert.Equal(1, parser.GlyphCount);
        Assert.Single(widths);
    }

    [Fact]
    public void ParseIndexWithMultipleEntries()
    {
        var cs = new[]
        {
            BuildCharstringNoWidth(),                  // glyph 0 (.notdef)
            BuildCharstringWidthEndchar(200),          // glyph 1 with width
            BuildCharstringNoWidth(),                  // glyph 2 default width
        };

        var cff = BuildMinimalCff(charstrings: cs, defaultWidthX: 500, nominalWidthX: 300);
        var parser = new CffParser(cff);
        var widths = parser.ExtractWidths();

        Assert.Equal(3, parser.GlyphCount);
        Assert.Equal(3, widths.Count);
    }

    [Fact]
    public void DictOperandEncoding_SingleByte()
    {
        // Single-byte encoding: value 0 → byte 139; value 100 → byte 239; value -107 → byte 32
        // Test via DICT parsing directly
        var dictData = new byte[] { 139, 20 }; // operand 0, operator 20 (defaultWidthX)
        var dict = CffParser.ParseDict(dictData);

        Assert.True(dict.ContainsKey(20));
        Assert.Equal(0.0, dict[20][0]);
    }

    [Fact]
    public void DictOperandEncoding_SingleByte_Positive()
    {
        // value 100 → byte 239
        var dictData = new byte[] { 239, 20 };
        var dict = CffParser.ParseDict(dictData);

        Assert.True(dict.ContainsKey(20));
        Assert.Equal(100.0, dict[20][0]);
    }

    [Fact]
    public void DictOperandEncoding_MultiByte_TwoBytePositive()
    {
        // 247-250 range: (b0-247)*256 + b1 + 108
        // value 300: (b0-247)*256 + b1 + 108 = 300 → (b0-247)*256 = 192 - b1
        // b0=247: 0*256 + b1 + 108 = 300 → b1 = 192
        var dictData = new byte[] { 247, 192, 20 };
        var dict = CffParser.ParseDict(dictData);

        Assert.True(dict.ContainsKey(20));
        Assert.Equal(300.0, dict[20][0]);
    }

    [Fact]
    public void DictOperandEncoding_MultiByte_Negative()
    {
        // 251-254 range: -(b0-251)*256 - b1 - 108
        // value -300: -(b0-251)*256 - b1 - 108 = -300 → (b0-251)*256 + b1 = 192
        // b0=251: 0*256 + b1 + 108 = 300 → b1 = 192
        var dictData = new byte[] { 251, 192, 20 };
        var dict = CffParser.ParseDict(dictData);

        Assert.True(dict.ContainsKey(20));
        Assert.Equal(-300.0, dict[20][0]);
    }

    [Fact]
    public void DictOperandEncoding_FourByteInt()
    {
        // 29 followed by 4 bytes: big-endian int32
        // value 1000 = 0x000003E8
        var dictData = new byte[] { 29, 0, 0, 3, 0xE8, 20 };
        var dict = CffParser.ParseDict(dictData);

        Assert.True(dict.ContainsKey(20));
        Assert.Equal(1000.0, dict[20][0]);
    }

    [Fact]
    public void DictOperandEncoding_TwoByteInt()
    {
        // 28 followed by 2 bytes: big-endian int16
        // value 500 = 0x01F4
        var dictData = new byte[] { 28, 1, 0xF4, 20 };
        var dict = CffParser.ParseDict(dictData);

        Assert.True(dict.ContainsKey(20));
        Assert.Equal(500.0, dict[20][0]);
    }

    [Fact]
    public void ExtractDefaultWidth_FromPrivateDict()
    {
        var cs = new[] { BuildCharstringNoWidth() };
        var cff = BuildMinimalCff(defaultWidthX: 750, charstrings: cs);
        var parser = new CffParser(cff);
        var widths = parser.ExtractWidths();

        Assert.Equal(750, parser.DefaultWidth);
        Assert.Equal(750, widths[0]);
    }

    [Fact]
    public void ExtractGlyphCount_FromCharStringsIndex()
    {
        var cs = new[]
        {
            BuildCharstringNoWidth(),
            BuildCharstringNoWidth(),
            BuildCharstringNoWidth(),
            BuildCharstringNoWidth(),
            BuildCharstringNoWidth(),
        };

        var cff = BuildMinimalCff(charstrings: cs);
        var parser = new CffParser(cff);
        parser.ExtractWidths();

        Assert.Equal(5, parser.GlyphCount);
    }

    [Fact]
    public void HandleEmptyCffData_Gracefully()
    {
        var parser = new CffParser(Array.Empty<byte>());
        var widths = parser.ExtractWidths();

        Assert.Empty(widths);
        Assert.Equal(0, parser.GlyphCount);
    }

    [Fact]
    public void HandleTruncatedCffData_Gracefully()
    {
        // Just a header, no INDEX structures
        var cff = new byte[] { 1, 0, 4, 1 };
        var parser = new CffParser(cff);
        var widths = parser.ExtractWidths();

        Assert.Empty(widths);
    }

    [Fact]
    public void ParseBcdRealNumber()
    {
        // BCD encoding for 1.5: nibbles 1, A(.), 5, F(end)
        // byte 1: 0x1A, byte 2: 0x5F
        var data = new byte[] { 0x1A, 0x5F };
        var pos = 0;
        var result = CffParser.ParseBcdReal(data, ref pos);

        Assert.Equal(1.5, result, precision: 10);
    }

    [Fact]
    public void ParseBcdRealNumber_Negative()
    {
        // BCD encoding for -2.5: nibbles E(-), 2, A(.), 5, F(end)
        // byte 1: 0xE2, byte 2: 0xA5, byte 3: 0xFF (end nibble in first position of byte)
        var data = new byte[] { 0xE2, 0xA5, 0xFF };
        var pos = 0;
        var result = CffParser.ParseBcdReal(data, ref pos);

        Assert.Equal(-2.5, result, precision: 10);
    }

    [Fact]
    public void ParseBcdRealNumber_WithExponent()
    {
        // BCD encoding for 1E3 (= 1000): nibbles 1, B(E), 3, F(end)
        // byte 1: 0x1B, byte 2: 0x3F
        var data = new byte[] { 0x1B, 0x3F };
        var pos = 0;
        var result = CffParser.ParseBcdReal(data, ref pos);

        Assert.Equal(1000.0, result, precision: 10);
    }

    [Fact]
    public void BcdRealInDict()
    {
        // operator 30 (real), then BCD for 1.5: 0x1A 0x5F, then operator 20
        var dictData = new byte[] { 30, 0x1A, 0x5F, 20 };
        var dict = CffParser.ParseDict(dictData);

        Assert.True(dict.ContainsKey(20));
        Assert.Equal(1.5, dict[20][0], precision: 10);
    }

    [Fact]
    public void ExtractWidths_WithExplicitWidth()
    {
        // Glyph 0: no explicit width (uses defaultWidthX)
        // Glyph 1: explicit width delta of 200 → width = nominalWidthX + 200 = 300 + 200 = 500
        var cs = new[]
        {
            BuildCharstringNoWidth(),
            BuildCharstringWidthEndchar(200),
        };

        var cff = BuildMinimalCff(defaultWidthX: 600, nominalWidthX: 300, charstrings: cs);
        var parser = new CffParser(cff);
        var widths = parser.ExtractWidths();

        Assert.Equal(2, widths.Count);
        Assert.Equal(600, widths[0]); // default
        Assert.Equal(500, widths[1]); // 300 + 200
    }

    [Fact]
    public void ExtractWidths_VmovetoWithWidth()
    {
        // Charstring: widthDelta dy vmoveto endchar
        // widthDelta=100, dy=50 → width = nominalWidthX + 100
        var cs = new[]
        {
            BuildCharstringNoWidth(),
            BuildCharstringWithWidth(widthDelta: 100, dy: 50),
        };

        var cff = BuildMinimalCff(defaultWidthX: 0, nominalWidthX: 400, charstrings: cs);
        var parser = new CffParser(cff);
        var widths = parser.ExtractWidths();

        Assert.Equal(0, widths[0]);   // defaultWidthX
        Assert.Equal(500, widths[1]); // 400 + 100
    }

    [Fact]
    public void ExtractWidths_NegativeWidthDelta()
    {
        var cs = new[]
        {
            BuildCharstringNoWidth(),
            BuildCharstringWidthEndchar(-50),
        };

        var cff = BuildMinimalCff(defaultWidthX: 1000, nominalWidthX: 500, charstrings: cs);
        var parser = new CffParser(cff);
        var widths = parser.ExtractWidths();

        Assert.Equal(1000, widths[0]); // default
        Assert.Equal(450, widths[1]);  // 500 + (-50)
    }

    [Fact]
    public void ExtractWidths_MixedGlyphs()
    {
        // Mix of glyphs with and without explicit widths
        var cs = new[]
        {
            BuildCharstringNoWidth(),        // glyph 0: default (600)
            BuildCharstringWidthEndchar(0),  // glyph 1: nominal + 0 = 300
            BuildCharstringNoWidth(),        // glyph 2: default (600)
            BuildCharstringWidthEndchar(50), // glyph 3: nominal + 50 = 350
        };

        var cff = BuildMinimalCff(defaultWidthX: 600, nominalWidthX: 300, charstrings: cs);
        var parser = new CffParser(cff);
        var widths = parser.ExtractWidths();

        Assert.Equal(4, widths.Count);
        Assert.Equal(600, widths[0]);
        Assert.Equal(300, widths[1]);
        Assert.Equal(600, widths[2]);
        Assert.Equal(350, widths[3]);
    }

    [Fact]
    public void NullCffData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CffParser(null!));
    }

    [Fact]
    public void TwoByteDictOperator()
    {
        // Two-byte operator: 12 followed by second byte
        // charset offset (operator 15) with value 2
        // Then a 12 0 (version) with no operands just to test two-byte
        var dictData = new byte[]
        {
            141, // operand 2 (2 + 139 = 141)
            15,  // operator 15 (charset)
            12, 0, // two-byte operator 12 00 (version) with no operands
        };
        var dict = CffParser.ParseDict(dictData);

        Assert.True(dict.ContainsKey(15));
        Assert.Equal(2.0, dict[15][0]);
        Assert.True(dict.ContainsKey(1200)); // 12 00 → key 1200
    }
}
