using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Decodes CCITT Group 3 and Group 4 fax-encoded image data (PDF 32000 §7.4.6).
/// Supports 1D (K=0), pure 2D (K&lt;0), and mixed 1D/2D (K&gt;0) modes.
/// </summary>
internal static class CcittFaxDecodeFilter
{
    /// <summary>Decodes CCITT fax-encoded data into packed 1-bit rows.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="parms">The filter's /DecodeParms dictionary (K, Columns, Rows and so on); null uses the defaults.</param>
    /// <param name="group4ColumnShift">
    /// When true (the default, for CCITT image XObjects), Group 4 lines are shifted one
    /// column left on output to match the producer convention noted in <see cref="DecodeGroup4"/>.
    /// JBIG2's embedded MMR data is strict T.6 without that quirk, so it passes false.
    /// </param>
    public static byte[] Decode(byte[] data, PdfDictionary? parms, bool group4ColumnShift = true) =>
        Decode(data, parms, group4ColumnShift, null);

    /// <summary><see cref="Decode(byte[], PdfDictionary?, bool)"/>, also telling, row by row,
    /// whether each decoded line was coded one-dimensionally (always for K = 0, by its tag bit
    /// for K &gt; 0, never for Group 4).</summary>
    internal static byte[] Decode(byte[] data, PdfDictionary? parms, bool group4ColumnShift, List<bool>? oneDimensionalRows)
    {
        var k = parms is not null ? (int)parms.GetInt("K", 0) : 0;
        var columns = parms is not null ? (int)parms.GetInt("Columns", 1728) : 1728;
        var rows = parms is not null ? (int)parms.GetInt("Rows", 0) : 0;
        var encodedByteAlign = parms is not null && parms.GetBool("EncodedByteAlign");
        var endOfLine = parms is not null && parms.GetBool("EndOfLine");
        var blackIs1 = parms is not null && parms.GetBool("BlackIs1");

        var reader = new CcittBitReader(data);
        var rowBytes = (columns + 7) / 8;
        // Sized for the declared rows up front: a fax page of 35,000 rows grew a list by
        // doubling, copying and zeroing a hundred megabytes more than it decoded.
        var output = new MemoryStream(rows > 0 && (long)rows * rowBytes <= int.MaxValue ? rows * rowBytes : 0);

        if (k == 0)
        {
            // Group 3 1D
            DecodeGroup31D(reader, columns, rows, encodedByteAlign, endOfLine, output, oneDimensionalRows);
        }
        else if (k > 0)
        {
            // Group 3 2D
            DecodeGroup32D(reader, columns, rows, encodedByteAlign, endOfLine, output, oneDimensionalRows);
        }
        else
        {
            // Group 4 (k < 0)
            DecodeGroup4(reader, columns, rows, encodedByteAlign, output, rowBytes, group4ColumnShift);
            for (var row = 0; oneDimensionalRows is not null && row < output.Length / rowBytes; row++) oneDimensionalRows.Add(false);
        }

        var bytes = output.ToArray();
        // Invert if BlackIs1 is false (default: white=0, black=1 in CCITT,
        // but PDF default expects black=0)
        if (!blackIs1)
        {
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)~bytes[i];
        }

        return bytes;
    }

    private static void DecodeGroup31D(CcittBitReader reader, int columns, int rows,
        bool byteAlign, bool endOfLine, MemoryStream output, List<bool>? oneDimensionalRows)
    {
        var maxRows = rows > 0 ? rows : int.MaxValue;
        var line = new Row(columns);
        for (var row = 0; row < maxRows; row++)
        {
            SkipToLineStart(reader, byteAlign, endOfLine);
            line.Clear();
            if (!Decode1DLine(reader, line, columns))
                break;
            output.Write(line.Bits, 0, line.Bits.Length);
            oneDimensionalRows?.Add(true);
        }
    }

    /// <summary>Move to the first code of a Group 3 line: past the optional EOL and, when
    /// the lines are byte-aligned, past the fill bits. A stream that declares its EOLs puts
    /// each one right before its line (T.4 fill goes before the EOL, never after it), so the
    /// EOL is the line start wherever it begins and aligning first would skip into its
    /// zeros and leave its tail to be read as a code; only a line with no EOL is aligned.</summary>
    private static void SkipToLineStart(CcittBitReader reader, bool byteAlign, bool endOfLine)
    {
        if (endOfLine && reader.TrySkipEol()) return;
        if (byteAlign) reader.AlignToByte();
        reader.TrySkipEol();
    }

    /// <summary>Mixed Group 3 lines: each line's tag bit (T.4 §4.2.1) says how it is coded,
    /// 1 one-dimensionally, 0 two-dimensionally against the line above. K only bounds how
    /// many two-dimensional lines an encoder may run; the decoder follows the tags.</summary>
    private static void DecodeGroup32D(CcittBitReader reader, int columns, int rows,
        bool byteAlign, bool endOfLine, MemoryStream output, List<bool>? oneDimensionalRows)
    {
        var maxRows = rows > 0 ? rows : int.MaxValue;
        var refLine = new Row(columns);
        var line = new Row(columns);
        for (var row = 0; row < maxRows; row++)
        {
            SkipToLineStart(reader, byteAlign, endOfLine);
            line.Clear();
            // Read tag bit: 1=1D, 0=2D
            var tag = reader.ReadBit();
            if (tag < 0) break;
            if (tag == 1)
            {
                if (!Decode1DLine(reader, line, columns)) break;
            }
            else
            {
                if (!Decode2DLine(reader, refLine, line, columns)) break;
            }
            output.Write(line.Bits, 0, line.Bits.Length);
            oneDimensionalRows?.Add(tag == 1);
            line.SettleChanges();
            (refLine, line) = (line, refLine);
        }
    }

    private static void DecodeGroup4(CcittBitReader reader, int columns, int rows,
        bool byteAlign, MemoryStream output, int rowBytes, bool columnShift)
    {
        var maxRows = rows > 0 ? rows : int.MaxValue;
        var refLine = new Row(columns); // all white
        var line = new Row(columns);
        var shifted = new byte[rowBytes];
        for (var row = 0; row < maxRows; row++)
        {
            // With /EncodedByteAlign each scan line's coded data is padded to a byte
            // boundary (PDF 32000 §7.4.6 Table 11); skip the fill bits before decoding
            // the next row, exactly as the Group 3 decoders do. Without this the bit
            // stream desyncs after row 0 and decoding bails after a single (blank) row.
            if (byteAlign) reader.AlignToByte();

            line.Clear();
            if (!Decode2DLine(reader, refLine, line, columns))
                break;
            if (columnShift)
            {
                // Some producers place each painted run
                // one column to the LEFT of where the T.6 §2.4 algorithm with
                // a0 = "first pixel of current run" semantics lands them. Mirror
                // that convention by shifting the decoded line one column left on
                // output. The pre-shift line is retained as the reference for
                // the next row so the 2D run-coding state remains consistent.
                ShiftLeftOneColumn(line.Bits, shifted);
                output.Write(shifted, 0, shifted.Length);
            }
            else
            {
                // JBIG2 embedded MMR is strict T.6 with no producer column-shift quirk.
                output.Write(line.Bits, 0, line.Bits.Length);
            }
            line.SettleChanges();
            (refLine, line) = (line, refLine);
        }
    }

    /// <summary>
    /// Decode <paramref name="rows"/> lines of a strict-T.6 (Group 4) bitmap from a shared
    /// reader, returned row-major packed with black = 1 (no BlackIs1 inversion, no column
    /// shift). Leaves the reader positioned right after the last decoded line so successive
    /// bitplanes (JBIG2 gray-scale image, T.88 §C.5) can be chained on a single reader.
    /// </summary>
    internal static byte[] DecodeGroup4Region(CcittBitReader reader, int columns, int rows)
    {
        var rowBytes = (columns + 7) / 8;
        var output = new MemoryStream(rowBytes * (rows > 0 ? rows : 0));
        var refLine = new Row(columns);
        var line = new Row(columns);
        for (var row = 0; row < rows; row++)
        {
            line.Clear();
            if (!Decode2DLine(reader, refLine, line, columns)) break;
            output.Write(line.Bits, 0, line.Bits.Length);
            line.SettleChanges();
            (refLine, line) = (line, refLine);
        }
        return output.ToArray();
    }

    /// <summary>The row's pixels one column to the left: bit 0 of each byte takes the next
    /// byte's top bit, and the last column the padding past the width, which is white.</summary>
    private static void ShiftLeftOneColumn(byte[] bits, byte[] shifted)
    {
        for (var i = 0; i < bits.Length; i++)
            shifted[i] = (byte)((bits[i] << 1) | (i + 1 < bits.Length ? bits[i + 1] >> 7 : 0));
    }

    private static bool Decode1DLine(CcittBitReader reader, Row line, int columns)
    {
        var pos = 0;
        var isWhite = true; // start with white

        while (pos < columns)
        {
            var runLength = DecodeRunLength(reader, isWhite);
            if (runLength < 0) return false;

            var end = Math.Min(pos + runLength, columns);
            if (!isWhite) line.PaintBlack(pos, end);
            pos = end;
            isWhite = !isWhite;
        }
        return true;
    }

    private static bool Decode2DLine(CcittBitReader reader, Row refLine, Row line, int columns)
    {
        var a0 = -1; // position before start (conceptually at -1)
        var isWhite = true;
        var refIndex = 0;

        while (a0 < columns)
        {
            var mode = Read2DMode(reader);
            if (mode < 0) return false;

            switch (mode)
            {
                case Mode2D.Pass:
                {
                    var b1 = FindB1(refLine, a0, isWhite, ref refIndex);
                    var b2 = refLine.ChangeAfter(b1, columns);
                    // Fill a0 to b2 with current color
                    if (!isWhite) line.PaintBlack(a0, b2);
                    a0 = b2;
                    // color does NOT change on pass
                    break;
                }
                case Mode2D.Horizontal:
                {
                    // Read two run lengths
                    var run1 = DecodeRunLength(reader, isWhite);
                    if (run1 < 0) return false;
                    var run2 = DecodeRunLength(reader, !isWhite);
                    if (run2 < 0) return false;

                    var start = Math.Max(0, a0);
                    // First run, then the second (opposite colour)
                    if (!isWhite) line.PaintBlack(start, start + run1);
                    else line.PaintBlack(start + run1, start + run1 + run2);
                    a0 = start + run1 + run2;
                    break;
                }
                default: // Vertical modes (VL3, VL2, VL1, V0, VR1, VR2, VR3)
                {
                    var offset = mode switch
                    {
                        Mode2D.VL3 => -3,
                        Mode2D.VL2 => -2,
                        Mode2D.VL1 => -1,
                        Mode2D.V0 => 0,
                        Mode2D.VR1 => 1,
                        Mode2D.VR2 => 2,
                        Mode2D.VR3 => 3,
                        _ => 0,
                    };

                    var b1 = FindB1(refLine, a0, isWhite, ref refIndex);
                    var a1 = Compat.Clamp(b1 + offset, 0, columns);

                    if (!isWhite) line.PaintBlack(a0, a1);
                    a0 = a1;
                    isWhite = !isWhite;
                    break;
                }
            }
        }
        return true;
    }

    /// <summary>b1: the first changing element of the reference line to the right of a0 whose
    /// colour is opposite to a0's (ITU-T T.6 §2) - a changing element being a pixel whose colour
    /// differs from the pixel before it, white before position 0. The reference line's changing
    /// elements alternate in colour, the first to black, so the element wanted is the first
    /// past a0 of the right parity; <paramref name="refIndex"/> carries the search forward from
    /// one code to the next, since a0 only moves right in a well-formed line.</summary>
    private static int FindB1(Row refLine, int a0, bool isWhiteA0Color, ref int refIndex)
    {
        var changes = refLine.Changes;
        while (refIndex > 0 && changes[refIndex - 1] > a0) refIndex--;
        while (refIndex < refLine.ChangeCount && changes[refIndex] <= a0) refIndex++;
        var index = refIndex;
        if (((index & 1) == 0) != isWhiteA0Color) index++;   // an even index changes to black
        return index < refLine.ChangeCount ? changes[index] : refLine.Columns;
    }

    private static int DecodeRunLength(CcittBitReader reader, bool isWhite)
    {
        var total = 0;
        while (true)
        {
            var code = isWhite ? ReadWhiteCode(reader) : ReadBlackCode(reader);
            if (code < 0) return -1;
            total += code;
            if (code < 64) break; // terminating code
        }
        return total;
    }

    /// <summary>A decoded row: its pixels packed a bit each, 1 for black, in the bytes the
    /// filter outputs, and - once settled - its changing elements, the positions where the
    /// colour changes, in order, the first of them to black (a row is white before its first
    /// pixel), which the row coded against it reads instead of its pixels.</summary>
    private sealed class Row
    {
        public readonly byte[] Bits;
        public readonly int[] Changes;
        public int ChangeCount;
        public readonly int Columns;

        public Row(int columns)
        {
            Columns = columns;
            Bits = new byte[(columns + 7) / 8];
            Changes = new int[columns + 1];
        }

        /// <summary>All white, no changing elements.</summary>
        public void Clear()
        {
            Array.Clear(Bits, 0, Bits.Length);
            ChangeCount = 0;
        }

        /// <summary>Paint columns <paramref name="from"/> up to <paramref name="to"/> black,
        /// clipped to the row; black over black stays black.</summary>
        public void PaintBlack(int from, int to)
        {
            from = Math.Max(0, from);
            to = Math.Min(to, Columns);
            if (from >= to) return;
            int firstByte = from >> 3, lastByte = (to - 1) >> 3;
            if (firstByte == lastByte)
            {
                Bits[firstByte] |= (byte)((0xFF >> (from & 7)) & (0xFF << (7 - ((to - 1) & 7))));
                return;
            }
            Bits[firstByte] |= (byte)(0xFF >> (from & 7));
            for (var i = firstByte + 1; i < lastByte; i++) Bits[i] = 0xFF;
            Bits[lastByte] |= (byte)(0xFF << (7 - ((to - 1) & 7)));
        }

        /// <summary>Read the changing elements off the pixels: a byte wholly of the current
        /// colour is skipped, any other examined bit by bit.</summary>
        public void SettleChanges()
        {
            ChangeCount = 0;
            var black = false;
            for (var i = 0; i < Bits.Length; i++)
            {
                var b = Bits[i];
                if (b == (black ? 0xFF : 0x00)) continue;
                var end = Math.Min(8, Columns - i * 8);
                for (var k = 0; k < end; k++)
                {
                    var bit = ((b >> (7 - k)) & 1) != 0;
                    if (bit == black) continue;
                    Changes[ChangeCount++] = i * 8 + k;
                    black = bit;
                }
            }
        }

        /// <summary>The changing element after the one at <paramref name="position"/>, or the
        /// row's width when none follows.</summary>
        public int ChangeAfter(int position, int columns)
        {
            if (position >= columns) return columns;
            var index = Array.BinarySearch(Changes, 0, ChangeCount, position);
            return index >= 0 && index + 1 < ChangeCount ? Changes[index + 1] : columns;
        }
    }

    // 2D mode codes
    private static int Read2DMode(CcittBitReader reader)
    {
        var bit = reader.ReadBit();
        if (bit < 0) return -1;

        if (bit == 1) return Mode2D.V0; // 1 = V(0)

        bit = reader.ReadBit();
        if (bit < 0) return -1;

        if (bit == 1)
        {
            // 01x
            bit = reader.ReadBit();
            if (bit < 0) return -1;
            return bit == 1 ? Mode2D.VR1 : Mode2D.VL1;
        }

        bit = reader.ReadBit();
        if (bit < 0) return -1;

        if (bit == 1)
        {
            // 001 = horizontal
            return Mode2D.Horizontal;
        }

        // 000x.
        bit = reader.ReadBit();
        if (bit < 0) return -1;

        if (bit == 1)
        {
            // 0001 = pass
            return Mode2D.Pass;
        }

        bit = reader.ReadBit();
        if (bit < 0) return -1;

        if (bit == 1)
        {
            // 00001x
            bit = reader.ReadBit();
            if (bit < 0) return -1;
            return bit == 1 ? Mode2D.VR2 : Mode2D.VL2;
        }

        bit = reader.ReadBit();
        if (bit < 0) return -1;

        if (bit == 1)
        {
            // 000001x
            bit = reader.ReadBit();
            if (bit < 0) return -1;
            return bit == 1 ? Mode2D.VR3 : Mode2D.VL3;
        }

        return -1; // unrecognized or EOL
    }

    private static class Mode2D
    {
        public const int Pass = 0;
        public const int Horizontal = 1;
        public const int V0 = 2;
        public const int VR1 = 3;
        public const int VR2 = 4;
        public const int VR3 = 5;
        public const int VL1 = 6;
        public const int VL2 = 7;
        public const int VL3 = 8;
    }

    // White and black Huffman code tables for Group 3
    private static int ReadWhiteCode(CcittBitReader reader) => ReadHuffmanCode(reader, WhiteRuns);
    private static int ReadBlackCode(CcittBitReader reader) => ReadHuffmanCode(reader, BlackRuns);

    private static int ReadHuffmanCode(CcittBitReader reader, Dictionary<int, int> runs)
    {
        var accumulated = 0;
        var bitsRead = 0;

        while (bitsRead < 14) // max code length is 13
        {
            var bit = reader.ReadBit();
            if (bit < 0) return -1;
            accumulated = (accumulated << 1) | bit;
            bitsRead++;

            if (runs.TryGetValue(CodeKey(bitsRead, accumulated), out var runLength))
                return runLength;
        }
        return -1; // not found
    }

    // A code by its length and value; the code values fit 13 bits.
    private static int CodeKey(int bits, int code) => (bits << 16) | code;

    // The code tables keyed for one probe per bit read; the first entry of a code wins, as
    // the in-order scan of the table it replaces chose.
    private static Dictionary<int, int> RunsByCode((int bits, int code, int runLength)[] table)
    {
        var runs = new Dictionary<int, int>(table.Length);
        foreach (var (bits, code, runLength) in table)
        {
            var key = CodeKey(bits, code);
            if (!runs.ContainsKey(key)) runs[key] = runLength;
        }
        return runs;
    }

    // Terminating codes (0-63) + Make-up codes (64, 128, . 2560)
    // White codes from ITU-T T.4 Table 2
    private static readonly (int bits, int code, int runLength)[] WhiteCodes =
    [
        // Terminating
        (8,  0b00110101, 0),
        (6,  0b000111, 1),
        (4,  0b0111, 2),
        (4,  0b1000, 3),
        (4,  0b1011, 4),
        (4,  0b1100, 5),
        (4,  0b1110, 6),
        (4,  0b1111, 7),
        (5,  0b10011, 8),
        (5,  0b10100, 9),
        (5,  0b00111, 10),
        (5,  0b01000, 11),
        (6,  0b001000, 12),
        (6,  0b000011, 13),
        (6,  0b110100, 14),
        (6,  0b110101, 15),
        (6,  0b101010, 16),
        (6,  0b101011, 17),
        (7,  0b0100111, 18),
        (7,  0b0001100, 19),
        (7,  0b0001000, 20),
        (7,  0b0010111, 21),
        (7,  0b0000011, 22),
        (7,  0b0000100, 23),
        (7,  0b0101000, 24),
        (7,  0b0101011, 25),
        (7,  0b0010011, 26),
        (7,  0b0100100, 27),
        (7,  0b0011000, 28),
        (8,  0b00000010, 29),
        (8,  0b00000011, 30),
        (8,  0b00011010, 31),
        (8,  0b00011011, 32),
        (8,  0b00010010, 33),
        (8,  0b00010011, 34),
        (8,  0b00010100, 35),
        (8,  0b00010101, 36),
        (8,  0b00010110, 37),
        (8,  0b00010111, 38),
        (8,  0b00101000, 39),
        (8,  0b00101001, 40),
        (8,  0b00101010, 41),
        (8,  0b00101011, 42),
        (8,  0b00101100, 43),
        (8,  0b00101101, 44),
        (8,  0b00000100, 45),
        (8,  0b00000101, 46),
        (8,  0b00001010, 47),
        (8,  0b00001011, 48),
        (8,  0b01010010, 49),
        (8,  0b01010011, 50),
        (8,  0b01010100, 51),
        (8,  0b01010101, 52),
        (8,  0b00100100, 53),
        (8,  0b00100101, 54),
        (8,  0b01011000, 55),
        (8,  0b01011001, 56),
        (8,  0b01011010, 57),
        (8,  0b01011011, 58),
        (8,  0b01001010, 59),
        (8,  0b01001011, 60),
        (8,  0b00110010, 61),
        (8,  0b00110011, 62),
        (8,  0b00110100, 63),
        // Make-up
        (5,  0b11011, 64),
        (5,  0b10010, 128),
        (6,  0b010111, 192),
        (7,  0b0110111, 256),
        (8,  0b00110110, 320),
        (8,  0b00110111, 384),
        (8,  0b01100100, 448),
        (8,  0b01100101, 512),
        (8,  0b01101000, 576),
        (8,  0b01100111, 640),
        (9,  0b011001100, 704),
        (9,  0b011001101, 768),
        (9,  0b011010010, 832),
        (9,  0b011010011, 896),
        (9,  0b011010100, 960),
        (9,  0b011010101, 1024),
        (9,  0b011010110, 1088),
        (9,  0b011010111, 1152),
        (9,  0b011011000, 1216),
        (9,  0b011011001, 1280),
        (9,  0b011011010, 1344),
        (9,  0b011011011, 1408),
        (9,  0b010011000, 1472),
        (9,  0b010011001, 1536),
        (9,  0b010011010, 1600),
        (6,  0b011000, 1664),
        (9,  0b010011011, 1728),
        // Extended make-up (shared with black)
        (11, 0b00000001000, 1792),
        (11, 0b00000001100, 1856),
        (11, 0b00000001101, 1920),
        (12, 0b000000010010, 1984),
        (12, 0b000000010011, 2048),
        (12, 0b000000010100, 2112),
        (12, 0b000000010101, 2176),
        (12, 0b000000010110, 2240),
        (12, 0b000000010111, 2304),
        (12, 0b000000011100, 2368),
        (12, 0b000000011101, 2432),
        (12, 0b000000011110, 2496),
        (12, 0b000000011111, 2560),
    ];

    // Black codes from ITU-T T.4 Table 3
    private static readonly (int bits, int code, int runLength)[] BlackCodes =
    [
        // Terminating
        (10, 0b0000110111, 0),
        (3,  0b010, 1),
        (2,  0b11, 2),
        (2,  0b10, 3),
        (3,  0b011, 4),
        (4,  0b0011, 5),
        (4,  0b0010, 6),
        (5,  0b00011, 7),
        (6,  0b000101, 8),
        (6,  0b000100, 9),
        (7,  0b0000100, 10),
        (7,  0b0000101, 11),
        (7,  0b0000111, 12),
        (8,  0b00000100, 13),
        (8,  0b00000111, 14),
        (9,  0b000011000, 15),
        (10, 0b0000010111, 16),
        (10, 0b0000011000, 17),
        (10, 0b0000001000, 18),
        (11, 0b00001100111, 19),
        (11, 0b00001101000, 20),
        (11, 0b00001101100, 21),
        (11, 0b00000110111, 22),
        (11, 0b00000101000, 23),
        (11, 0b00000010111, 24),
        (11, 0b00000011000, 25),
        (12, 0b000011001010, 26),
        (12, 0b000011001011, 27),
        (12, 0b000011001100, 28),
        (12, 0b000011001101, 29),
        (12, 0b000001101000, 30),
        (12, 0b000001101001, 31),
        (12, 0b000001101010, 32),
        (12, 0b000001101011, 33),
        (12, 0b000011010010, 34),
        (12, 0b000011010011, 35),
        (12, 0b000011010100, 36),
        (12, 0b000011010101, 37),
        (12, 0b000011010110, 38),
        (12, 0b000011010111, 39),
        (12, 0b000001101100, 40),
        (12, 0b000001101101, 41),
        (12, 0b000011011010, 42),
        (12, 0b000011011011, 43),
        (12, 0b000001010100, 44),
        (12, 0b000001010101, 45),
        (12, 0b000001010110, 46),
        (12, 0b000001010111, 47),
        (12, 0b000001100100, 48),
        (12, 0b000001100101, 49),
        (12, 0b000001010010, 50),
        (12, 0b000001010011, 51),
        (12, 0b000000100100, 52),
        (12, 0b000000110111, 53),
        (12, 0b000000111000, 54),
        (12, 0b000000100111, 55),
        (12, 0b000000101000, 56),
        (12, 0b000001011000, 57),
        (12, 0b000001011001, 58),
        (12, 0b000000101011, 59),
        (12, 0b000000101100, 60),
        (12, 0b000001011010, 61),
        (12, 0b000001100110, 62),
        (12, 0b000001100111, 63),
        // Make-up
        (10, 0b0000001111, 64),
        (12, 0b000011001000, 128),
        (12, 0b000011001001, 192),
        (12, 0b000001011011, 256),
        (12, 0b000000110011, 320),
        (12, 0b000000110100, 384),
        (12, 0b000000110101, 448),
        (13, 0b0000001101100, 512),
        (13, 0b0000001101101, 576),
        (13, 0b0000001001010, 640),
        (13, 0b0000001001011, 704),
        (13, 0b0000001001100, 768),
        (13, 0b0000001001101, 832),
        (13, 0b0000001110010, 896),
        (13, 0b0000001110011, 960),
        (13, 0b0000001110100, 1024),
        (13, 0b0000001110101, 1088),
        (13, 0b0000001110110, 1152),
        (13, 0b0000001110111, 1216),
        (13, 0b0000001010010, 1280),
        (13, 0b0000001010011, 1344),
        (13, 0b0000001010100, 1408),
        (13, 0b0000001010101, 1472),
        (13, 0b0000001011010, 1536),
        (13, 0b0000001011011, 1600),
        (13, 0b0000001100100, 1664),
        (13, 0b0000001100101, 1728),
        // Extended make-up
        (11, 0b00000001000, 1792),
        (11, 0b00000001100, 1856),
        (11, 0b00000001101, 1920),
        (12, 0b000000010010, 1984),
        (12, 0b000000010011, 2048),
        (12, 0b000000010100, 2112),
        (12, 0b000000010101, 2176),
        (12, 0b000000010110, 2240),
        (12, 0b000000010111, 2304),
        (12, 0b000000011100, 2368),
        (12, 0b000000011101, 2432),
        (12, 0b000000011110, 2496),
        (12, 0b000000011111, 2560),
    ];

    // Built from the tables above, so declared after them.
    private static readonly Dictionary<int, int> WhiteRuns = RunsByCode(WhiteCodes);
    private static readonly Dictionary<int, int> BlackRuns = RunsByCode(BlackCodes);

    internal sealed class CcittBitReader
    {
        private readonly byte[] _data;
        private int _bytePos;
        private int _bitPos;

        public CcittBitReader(byte[] data)
        {
            _data = data;
        }

        public int ReadBit()
        {
            if (_bytePos >= _data.Length) return -1;
            var bit = (_data[_bytePos] >> (7 - _bitPos)) & 1;
            _bitPos++;
            if (_bitPos == 8)
            {
                _bitPos = 0;
                _bytePos++;
            }
            return bit;
        }

        public void AlignToByte()
        {
            if (_bitPos > 0)
            {
                _bitPos = 0;
                _bytePos++;
            }
        }

        /// <summary>
        /// Consume a trailing T.6 EOFB (end-of-facsimile-block: two EOL codes,
        /// 000000000001 000000000001 = 0x001001, 24 bits) if one begins at the current bit
        /// position, then advance to the next byte boundary. Used between the successive
        /// bitplanes of a JBIG2 MMR gray-scale image (T.88 §C.5), each of which is an
        /// independent T.6 image terminated by an EOFB and byte-aligned to the next plane.
        /// </summary>
        public void ConsumeEofbAndByteAlign()
        {
            var savedByte = _bytePos;
            var savedBit = _bitPos;
            var word = 0;
            var ok = true;
            for (var i = 0; i < 24; i++)
            {
                var bit = ReadBit();
                if (bit < 0) { ok = false; break; }
                word = (word << 1) | bit;
            }
            if (!ok || word != 0x001001)
            {
                _bytePos = savedByte; // not an EOFB — leave the bits for the next plane
                _bitPos = savedBit;
            }
            AlignToByte();
        }

        /// <summary>
        /// Consume an end-of-line code if one begins at the current position. A G3 EOL is
        /// the pattern 000000000001 (eleven or more zero fill bits followed by a 1); it
        /// precedes each scan line when /EndOfLine encoding is used (ITU-T T.4 §4.1.2).
        /// Returns true when an EOL was skipped. When the upcoming bits are not an EOL the
        /// reader position is left unchanged so the caller can decode normal run codes.
        /// </summary>
        public bool TrySkipEol()
        {
            var savedByte = _bytePos;
            var savedBit = _bitPos;
            var zeros = 0;
            while (true)
            {
                var bit = ReadBit();
                if (bit < 0) { _bytePos = savedByte; _bitPos = savedBit; return false; }
                if (bit == 0) { zeros++; continue; }
                // bit == 1: an EOL needs at least eleven leading zeros; anything shorter is a
                // normal code word, so rewind and let the caller decode it.
                if (zeros >= 11) return true;
                _bytePos = savedByte; _bitPos = savedBit;
                return false;
            }
        }
    }
}
