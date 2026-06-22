namespace Aspose.Pdf.Text;

/// <summary>
/// Minimal CFF (Compact Font Format) parser that extracts glyph widths from CFF font data.
/// Handles CFF data embedded in PDF as FontFile3 (Type1C or CIDFontType0C).
/// Based on Adobe Technical Note #5176 (CFF specification).
/// </summary>
internal sealed class CffParser
{
    private readonly byte[] _data;
    private int _glyphCount;
    private int _defaultWidthX;
    private int _nominalWidthX;

    public CffParser(byte[] cffData)
    {
        _data = cffData ?? throw new ArgumentNullException(nameof(cffData));
    }

    /// <summary>Get the number of glyphs in the font.</summary>
    public int GlyphCount => _glyphCount;

    /// <summary>Get the default width for glyphs without explicit width.</summary>
    public int DefaultWidth => _defaultWidthX;

    /// <summary>
    /// Parse CFF data and extract glyph widths.
    /// Returns a dictionary mapping glyph index to width in font units.
    /// </summary>
    public Dictionary<int, int> ExtractWidths()
    {
        var widths = new Dictionary<int, int>();
        if (_data.Length < 4) return widths;

        // Parse header
        // byte 0: major version
        // byte 1: minor version
        // byte 2: header size
        // byte 3: offSize (absolute offset size)
        var hdrSize = _data[2];
        if (hdrSize > _data.Length) return widths;

        var pos = (int)hdrSize;

        // Skip Name INDEX
        pos = SkipIndex(pos);
        if (pos < 0) return widths;

        // Parse Top DICT INDEX — extract charStrings offset and Private DICT location
        var topDictIndex = ParseIndex(pos);
        if (topDictIndex.count == 0) return widths;

        pos = topDictIndex.dataEnd;

        // Parse the first Top DICT
        var topDictData = ReadIndexEntry(topDictIndex, 0);
        var topDict = ParseDict(topDictData);

        // Skip String INDEX
        pos = SkipIndex(pos);
        if (pos < 0) return widths;

        // Skip Global Subr INDEX
        pos = SkipIndex(pos);
        if (pos < 0) return widths;

        // Get charStrings offset from Top DICT (operator 17)
        var charStringsOffset = GetDictInt(topDict, 17, 0);
        if (charStringsOffset == 0) return widths;

        // Parse CharStrings INDEX to get glyph count
        if (charStringsOffset >= _data.Length) return widths;
        var charStringsIndex = ParseIndex(charStringsOffset);
        _glyphCount = charStringsIndex.count;

        // Get Private DICT location from Top DICT (operator 18 = size, offset pair).
        var privateDictSize = 0;
        var privateDictOffset = 0;
        if (topDict.TryGetValue(18, out var privateValues) && privateValues.Count >= 2)
        {
            privateDictSize = (int)privateValues[0];
            privateDictOffset = (int)privateValues[1];
        }
        else if (topDict.TryGetValue(1236, out var fdArrayVals) && fdArrayVals.Count >= 1)
        {
            // CID-keyed CFF: the Top DICT has no Private; per-font Private DICTs live in
            // the FDArray (op 12 36). Subset CIDFonts almost always have a single Font
            // DICT, so use FDArray[0]'s Private. Without this defaultWidthX/nominalWidthX
            // stay 0 and every charstring width comes out as the raw delta from nominal
            // (often negative), collapsing text spacing (e.g. 35987 banner).
            var fdArrayIndex = ParseIndex((int)fdArrayVals[0]);
            if (fdArrayIndex.count > 0)
            {
                var fdDict = ParseDict(ReadIndexEntry(fdArrayIndex, 0));
                if (fdDict.TryGetValue(18, out var fdPriv) && fdPriv.Count >= 2)
                {
                    privateDictSize = (int)fdPriv[0];
                    privateDictOffset = (int)fdPriv[1];
                }
            }
        }

        // Parse Private DICT for defaultWidthX (op 20) and nominalWidthX (op 21)
        _defaultWidthX = 0;
        _nominalWidthX = 0;
        if (privateDictSize > 0 && privateDictOffset > 0 &&
            privateDictOffset + privateDictSize <= _data.Length)
        {
            var privateDictData = new byte[privateDictSize];
            Array.Copy(_data, privateDictOffset, privateDictData, 0, privateDictSize);
            var privateDict = ParseDict(privateDictData);

            _defaultWidthX = GetDictInt(privateDict, 20, 0);
            _nominalWidthX = GetDictInt(privateDict, 21, 0);
        }

        // Extract widths from each charstring
        for (var i = 0; i < _glyphCount; i++)
        {
            var csData = ReadIndexEntry(charStringsIndex, i);
            if (csData.Length == 0)
            {
                widths[i] = _defaultWidthX;
                continue;
            }

            var width = ExtractCharstringWidth(csData);
            widths[i] = width;
        }

        return widths;
    }

    /// <summary>
    /// Extract the width from a Type 2 charstring.
    /// In Type 2, the first value pushed onto the stack before the first operator
    /// may be the width delta (added to nominalWidthX). If no width is present,
    /// defaultWidthX is used.
    /// </summary>
    private int ExtractCharstringWidth(byte[] csData)
    {
        // We need to determine if the charstring has an optional width value.
        // According to the Type 2 spec, the width is present if there is an "extra"
        // operand on the stack when the first drawing or hint operator is encountered.
        //
        // Strategy: decode operands until we hit the first operator, then check
        // if the operand count indicates a width is present.

        var operands = new List<double>();
        var pos = 0;

        while (pos < csData.Length)
        {
            var b0 = csData[pos];

            if (b0 == 12)
            {
                // Two-byte operator — the first operator we hit
                // For 2-byte operators: check parity based on specific operator
                pos += 2;
                return DetermineWidth(operands, b0, csData.Length > 1 ? csData[1] : 0, isTwoByte: true);
            }

            if (b0 <= 27 || b0 == 29 || b0 == 31)
            {
                // Single-byte operator (0-27 are operators, 29 is reserved in charstring context as callgsubr, 31 is hvcurveto)
                // Actually in Type 2: 0-11 and 13-27 are operators, 12 is escape, 28 is shortint, 29-31 are operators
                if (b0 <= 11 || (b0 >= 13 && b0 <= 27) || b0 >= 29)
                {
                    return DetermineWidth(operands, b0, 0, isTwoByte: false);
                }
            }

            if (b0 == 28)
            {
                // 2-byte integer
                if (pos + 2 >= csData.Length) break;
                var val = (short)((csData[pos + 1] << 8) | csData[pos + 2]);
                operands.Add(val);
                pos += 3;
                continue;
            }

            if (b0 >= 32 && b0 <= 246)
            {
                operands.Add(b0 - 139);
                pos++;
                continue;
            }

            if (b0 >= 247 && b0 <= 250)
            {
                if (pos + 1 >= csData.Length) break;
                var val = (b0 - 247) * 256 + csData[pos + 1] + 108;
                operands.Add(val);
                pos += 2;
                continue;
            }

            if (b0 >= 251 && b0 <= 254)
            {
                if (pos + 1 >= csData.Length) break;
                var val = -(b0 - 251) * 256 - csData[pos + 1] - 108;
                operands.Add(val);
                pos += 2;
                continue;
            }

            if (b0 == 255)
            {
                // 4-byte fixed-point number (16.16) in Type 2
                if (pos + 4 >= csData.Length) break;
                var intPart = (short)((csData[pos + 1] << 8) | csData[pos + 2]);
                var fracPart = (csData[pos + 3] << 8) | csData[pos + 4];
                operands.Add(intPart + fracPart / 65536.0);
                pos += 5;
                continue;
            }

            // Unknown byte, skip
            pos++;
        }

        return _defaultWidthX;
    }

    /// <summary>
    /// Given the operands collected before the first operator, determine the glyph width.
    /// The width is optional and depends on the operator and the parity of the operand count.
    /// </summary>
    private int DetermineWidth(List<double> operands, int op, int op2, bool isTwoByte)
    {
        if (operands.Count == 0) return _defaultWidthX;

        // Determine expected operand count for the operator to decide if width is present.
        // For most hint/drawing operators, if there's an odd number of operands, the first is width.
        // For endchar (14) with 0 args → no width; with 1 arg → width.
        // For hstem/vstem (1,3) and hstemhm/vstemhm (18,23): pairs of values, odd count means width present.
        // For hmoveto (22): expects 1 arg, 2 means width present.
        // For vmoveto (4): expects 1 arg, 2 means width present.
        // For rmoveto (21): expects 2 args, 3 means width present.
        // For endchar (14): expects 0 args, 1 means width present.

        bool hasWidth;

        if (isTwoByte)
        {
            // Two-byte operators (12 xx) — width if odd count (hint-like operators)
            hasWidth = operands.Count % 2 != 0;
        }
        else
        {
            switch (op)
            {
                case 1:  // hstem
                case 3:  // vstem
                case 18: // hstemhm
                case 23: // vstemhm
                    hasWidth = operands.Count % 2 != 0;
                    break;

                case 14: // endchar
                    hasWidth = operands.Count >= 1;
                    break;

                case 4:  // vmoveto
                case 22: // hmoveto
                    hasWidth = operands.Count >= 2;
                    break;

                case 21: // rmoveto
                    hasWidth = operands.Count >= 3;
                    break;

                case 19: // hintmask
                case 20: // cntrmask
                    hasWidth = operands.Count % 2 != 0;
                    break;

                default:
                    // For other operators (drawing), width if odd
                    hasWidth = operands.Count % 2 != 0;
                    break;
            }
        }

        if (hasWidth)
            return _nominalWidthX + (int)operands[0];

        return _defaultWidthX;
    }

    // ── INDEX parsing ──────────────────────────────────────────────────

    internal record struct IndexInfo(int count, int offSize, int offsetArrayStart, int dataStart, int dataEnd);

    /// <summary>
    /// Parse a CFF INDEX structure at the given position. Static so the shared
    /// glyph-outline interpreter can reuse it without instantiating CffParser.
    /// </summary>
    internal static IndexInfo ParseIndex(byte[] data, int pos)
    {
        if (pos + 2 > data.Length)
            return new IndexInfo(0, 0, 0, pos, pos);

        var count = (data[pos] << 8) | data[pos + 1];
        if (count == 0)
            return new IndexInfo(0, 0, 0, pos + 2, pos + 2);

        var offSize = data[pos + 2];
        var offsetArrayStart = pos + 3;
        var offsetArrayLen = (count + 1) * offSize;

        if (offsetArrayStart + offsetArrayLen > data.Length)
            return new IndexInfo(0, 0, 0, pos, pos);

        var lastOffset = ReadOffsetStatic(data, offsetArrayStart + count * offSize, offSize);
        var dataStart = offsetArrayStart + offsetArrayLen;
        var dataEnd = dataStart + lastOffset - 1; // offsets are 1-based

        return new IndexInfo(count, offSize, offsetArrayStart, dataStart, dataEnd);
    }

    private IndexInfo ParseIndex(int pos) => ParseIndex(_data, pos);

    /// <summary>
    /// Read the data for entry i from an INDEX structure. Static overload so the
    /// Type 2 interpreter can read subroutine bodies directly from the shared
    /// CFF byte array without holding a CffParser instance.
    /// </summary>
    internal static byte[] ReadIndexEntry(byte[] data, IndexInfo index, int i)
    {
        if (i < 0 || i >= index.count) return [];

        var off1 = ReadOffsetStatic(data, index.offsetArrayStart + i * index.offSize, index.offSize);
        var off2 = ReadOffsetStatic(data, index.offsetArrayStart + (i + 1) * index.offSize, index.offSize);
        var start = index.dataStart + off1 - 1; // offsets are 1-based
        var length = off2 - off1;

        if (start < 0 || length <= 0 || start + length > data.Length) return [];

        var result = new byte[length];
        Array.Copy(data, start, result, 0, length);
        return result;
    }

    private byte[] ReadIndexEntry(IndexInfo index, int i) => ReadIndexEntry(_data, index, i);

    /// <summary>
    /// Skip past an INDEX structure, returning the position after it.
    /// Returns -1 if the data is malformed.
    /// </summary>
    private int SkipIndex(int pos)
    {
        var index = ParseIndex(pos);
        if (index.count == 0 && index.dataEnd == pos + 2)
            return pos + 2; // Empty INDEX is just count=0 (2 bytes)
        return index.dataEnd;
    }

    // ── DICT parsing ───────────────────────────────────────────────────

    /// <summary>
    /// Parse a DICT structure. Returns operator → list of operand values.
    /// Operators 0-21 are single-byte; 12 xx are two-byte (encoded as 1200+xx).
    /// </summary>
    internal static Dictionary<int, List<double>> ParseDict(byte[] dictData)
    {
        var result = new Dictionary<int, List<double>>();
        var operands = new List<double>();
        var pos = 0;

        while (pos < dictData.Length)
        {
            var b0 = dictData[pos];

            if (b0 <= 21)
            {
                // Operator
                int op;
                if (b0 == 12)
                {
                    // Two-byte operator
                    pos++;
                    if (pos >= dictData.Length) break;
                    op = 1200 + dictData[pos];
                }
                else
                {
                    op = b0;
                }

                result[op] = new List<double>(operands);
                operands.Clear();
                pos++;
                continue;
            }

            if (b0 == 28)
            {
                // 2-byte integer
                if (pos + 2 >= dictData.Length) break;
                var val = (short)((dictData[pos + 1] << 8) | dictData[pos + 2]);
                operands.Add(val);
                pos += 3;
                continue;
            }

            if (b0 == 29)
            {
                // 4-byte integer
                if (pos + 4 >= dictData.Length) break;
                var val = (dictData[pos + 1] << 24) | (dictData[pos + 2] << 16) |
                          (dictData[pos + 3] << 8) | dictData[pos + 4];
                operands.Add(val);
                pos += 5;
                continue;
            }

            if (b0 == 30)
            {
                // Real number (BCD encoded)
                pos++;
                operands.Add(ParseBcdReal(dictData, ref pos));
                continue;
            }

            if (b0 >= 32 && b0 <= 246)
            {
                operands.Add(b0 - 139);
                pos++;
                continue;
            }

            if (b0 >= 247 && b0 <= 250)
            {
                if (pos + 1 >= dictData.Length) break;
                var val = (b0 - 247) * 256 + dictData[pos + 1] + 108;
                operands.Add(val);
                pos += 2;
                continue;
            }

            if (b0 >= 251 && b0 <= 254)
            {
                if (pos + 1 >= dictData.Length) break;
                var val = -(b0 - 251) * 256 - dictData[pos + 1] - 108;
                operands.Add(val);
                pos += 2;
                continue;
            }

            // Skip unknown bytes
            pos++;
        }

        return result;
    }

    /// <summary>
    /// Parse a BCD-encoded real number from DICT data.
    /// Each nibble: 0-9=digit, a='.', b='E', c='E-', d=reserved, e='-', f=end
    /// </summary>
    internal static double ParseBcdReal(byte[] data, ref int pos)
    {
        var chars = new List<char>();
        var done = false;

        while (pos < data.Length && !done)
        {
            var b = data[pos++];
            for (var nibbleIdx = 0; nibbleIdx < 2 && !done; nibbleIdx++)
            {
                var nibble = nibbleIdx == 0 ? (b >> 4) & 0x0F : b & 0x0F;
                switch (nibble)
                {
                    case >= 0 and <= 9:
                        chars.Add((char)('0' + nibble));
                        break;
                    case 0x0A:
                        chars.Add('.');
                        break;
                    case 0x0B:
                        chars.Add('E');
                        break;
                    case 0x0C:
                        chars.Add('E');
                        chars.Add('-');
                        break;
                    case 0x0E:
                        chars.Add('-');
                        break;
                    case 0x0F:
                        done = true;
                        break;
                }
            }
        }

        if (chars.Count == 0) return 0;
        var str = new string(chars.ToArray());
        return double.TryParse(str, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static int GetDictInt(Dictionary<int, List<double>> dict, int op, int defaultValue)
    {
        if (dict.TryGetValue(op, out var values) && values.Count > 0)
            return (int)values[0];
        return defaultValue;
    }

    private int ReadUInt16(int offset) =>
        (_data[offset] << 8) | _data[offset + 1];

    private int ReadOffset(int pos, int offSize) =>
        ReadOffsetStatic(_data, pos, offSize);

    internal static int ReadOffsetStatic(byte[] data, int pos, int offSize)
    {
        var val = 0;
        for (var i = 0; i < offSize; i++)
        {
            if (pos + i >= data.Length) return 0;
            val = (val << 8) | data[pos + i];
        }
        return val;
    }
}
