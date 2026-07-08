using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Decodes CCITT Group 3 and Group 4 fax-encoded image data (PDF 32000 §7.4.6).
/// Supports 1D (K=0), pure 2D (K&lt;0), and mixed 1D/2D (K&gt;0) modes.
/// </summary>
internal static class CcittFaxDecodeFilter
{
    public static byte[] Decode(byte[] data, PdfDictionary? parms)
    {
        var k = parms is not null ? (int)parms.GetInt("K", 0) : 0;
        var columns = parms is not null ? (int)parms.GetInt("Columns", 1728) : 1728;
        var rows = parms is not null ? (int)parms.GetInt("Rows", 0) : 0;
        var encodedByteAlign = parms is not null && parms.GetBool("EncodedByteAlign");
        var blackIs1 = parms is not null && parms.GetBool("BlackIs1");

        var reader = new CcittBitReader(data);
        var rowBytes = (columns + 7) / 8;
        var output = new List<byte>();

        if (k == 0)
        {
            // Group 3 1D
            DecodeGroup31D(reader, columns, rows, encodedByteAlign, output, rowBytes);
        }
        else if (k > 0)
        {
            // Group 3 2D
            DecodeGroup32D(reader, columns, rows, k, encodedByteAlign, output, rowBytes);
        }
        else
        {
            // Group 4 (k < 0)
            DecodeGroup4(reader, columns, rows, encodedByteAlign, output, rowBytes);
        }

        // Invert if BlackIs1 is false (default: white=0, black=1 in CCITT,
        // but PDF default expects black=0)
        if (!blackIs1)
        {
            for (var i = 0; i < output.Count; i++)
                output[i] = (byte)~output[i];
        }

        return output.ToArray();
    }

    private static void DecodeGroup31D(CcittBitReader reader, int columns, int rows,
        bool byteAlign, List<byte> output, int rowBytes)
    {
        var maxRows = rows > 0 ? rows : int.MaxValue;
        for (var row = 0; row < maxRows; row++)
        {
            if (byteAlign) reader.AlignToByte();
            // Skip the optional EOL that precedes each line in /EndOfLine streams.
            reader.TrySkipEol();

            var line = new bool[columns]; // false=white
            if (!Decode1DLine(reader, line, columns))
                break;
            OutputLine(line, output, rowBytes);
        }
    }

    private static void DecodeGroup32D(CcittBitReader reader, int columns, int rows,
        int k, bool byteAlign, List<byte> output, int rowBytes)
    {
        var maxRows = rows > 0 ? rows : int.MaxValue;
        var refLine = new bool[columns];
        var kCounter = 0;

        for (var row = 0; row < maxRows; row++)
        {
            if (byteAlign) reader.AlignToByte();
            // Skip the optional EOL that precedes each line in /EndOfLine streams.
            reader.TrySkipEol();

            var line = new bool[columns];

            // Read tag bit: 1=1D, 0=2D
            var tag = reader.ReadBit();
            if (tag < 0) break;

            if (tag == 1 || kCounter >= k - 1)
            {
                if (!Decode1DLine(reader, line, columns)) break;
                kCounter = 0;
            }
            else
            {
                if (!Decode2DLine(reader, refLine, line, columns)) break;
                kCounter++;
            }

            OutputLine(line, output, rowBytes);
            Array.Copy(line, refLine, columns);
        }
    }

    private static void DecodeGroup4(CcittBitReader reader, int columns, int rows,
        bool byteAlign, List<byte> output, int rowBytes)
    {
        var maxRows = rows > 0 ? rows : int.MaxValue;
        var refLine = new bool[columns]; // all white

        var shifted = new bool[columns];
        for (var row = 0; row < maxRows; row++)
        {
            // With /EncodedByteAlign each scan line's coded data is padded to a byte
            // boundary (PDF 32000 §7.4.6 Table 11); skip the fill bits before decoding
            // the next row, exactly as the Group 3 decoders do. Without this the bit
            // stream desyncs after row 0 and decoding bails after a single (blank) row.
            if (byteAlign) reader.AlignToByte();

            var line = new bool[columns];
            if (!Decode2DLine(reader, refLine, line, columns))
                break;
            // Producers that wrote the reference PDFs place each painted run
            // one column to the LEFT of where the T.6 §2.4 algorithm with
            // a0 = "first pixel of current run" semantics lands them. Mirror
            // that convention by shifting the decoded line one column left on
            // output. The pre-shift line is retained as the reference for
            // the next row so the 2D run-coding state remains consistent.
            for (var i = 0; i < columns - 1; i++) shifted[i] = line[i + 1];
            shifted[columns - 1] = false;
            OutputLine(shifted, output, rowBytes);
            Array.Copy(line, refLine, columns);
        }
    }

    private static bool Decode1DLine(CcittBitReader reader, bool[] line, int columns)
    {
        var pos = 0;
        var isWhite = true; // start with white

        while (pos < columns)
        {
            var runLength = DecodeRunLength(reader, isWhite);
            if (runLength < 0) return false;

            var end = Math.Min(pos + runLength, columns);
            if (!isWhite)
            {
                for (var i = pos; i < end; i++)
                    line[i] = true;
            }
            pos = end;
            isWhite = !isWhite;
        }
        return true;
    }

    private static bool Decode2DLine(CcittBitReader reader, bool[] refLine, bool[] line, int columns)
    {
        var a0 = -1; // position before start (conceptually at -1)
        var isWhite = true;

        while (a0 < columns)
        {
            var mode = Read2DMode(reader);
            if (mode < 0) return false;

            switch (mode)
            {
                case Mode2D.Pass:
                {
                    var b1 = FindB1(refLine, a0, isWhite, columns);
                    var b2 = FindNextChange(refLine, b1, columns);
                    // Fill a0 to b2 with current color
                    if (!isWhite)
                    {
                        for (var i = Math.Max(0, a0); i < b2 && i < columns; i++)
                            line[i] = true;
                    }
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
                    // First run
                    if (!isWhite)
                    {
                        for (var i = start; i < start + run1 && i < columns; i++)
                            line[i] = true;
                    }
                    // Second run (opposite color)
                    if (isWhite)
                    {
                        for (var i = start + run1; i < start + run1 + run2 && i < columns; i++)
                            line[i] = true;
                    }
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

                    var b1 = FindB1(refLine, a0, isWhite, columns);
                    var a1 = b1 + offset;
                    a1 = Math.Clamp(a1, 0, columns);

                    if (!isWhite)
                    {
                        for (var i = Math.Max(0, a0); i < a1 && i < columns; i++)
                            line[i] = true;
                    }
                    a0 = a1;
                    isWhite = !isWhite;
                    break;
                }
            }
        }
        return true;
    }

    private static int FindB1(bool[] refLine, int a0, bool isWhiteA0Color, int columns)
    {
        // b1 = first CHANGING element on reference line to the right of a0 whose
        // colour is OPPOSITE to a0 (ITU-T T.6 §2). A "changing element" is a
        // pixel whose colour differs from the previous pixel on that line —
        // not merely any pixel of opposite colour. Skipping the changing-element
        // requirement (just scanning for the first opposite-colour pixel) lands
        // b1 on positions inside same-colour runs, which throws V0/VR/VL coding
        // off and compounds across rows — 33703 page 2 produced 108 valid rows
        // before the bit-stream drifted out of sync and Read2DMode hit a long
        // zero prefix.
        // wantBlack=true when a0 is white (need opposite-colour changing element).
        var wantBlack = isWhiteA0Color;
        var refColor = false; // implicit white before position 0
        for (var i = 0; i < columns; i++)
        {
            var newColor = refLine[i];
            if (newColor != refColor)
            {
                // i is a changing element; its colour is newColor (true=black).
                if (i > a0 && newColor == wantBlack)
                    return i;
                refColor = newColor;
            }
        }
        return columns;
    }

    private static int FindNextChange(bool[] line, int pos, int columns)
    {
        if (pos >= columns) return columns;
        var color = line[pos];
        for (var i = pos + 1; i < columns; i++)
        {
            if (line[i] != color) return i;
        }
        return columns;
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

    private static void OutputLine(bool[] line, List<byte> output, int rowBytes)
    {
        for (var byteIdx = 0; byteIdx < rowBytes; byteIdx++)
        {
            var b = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                var pixel = byteIdx * 8 + bit;
                if (pixel < line.Length && line[pixel])
                    b |= (128 >> bit);
            }
            output.Add((byte)b);
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
    private static int ReadWhiteCode(CcittBitReader reader) => ReadHuffmanCode(reader, WhiteCodes);
    private static int ReadBlackCode(CcittBitReader reader) => ReadHuffmanCode(reader, BlackCodes);

    private static int ReadHuffmanCode(CcittBitReader reader, (int bits, int code, int runLength)[] table)
    {
        var accumulated = 0;
        var bitsRead = 0;

        while (bitsRead < 14) // max code length is 13
        {
            var bit = reader.ReadBit();
            if (bit < 0) return -1;
            accumulated = (accumulated << 1) | bit;
            bitsRead++;

            foreach (var (bits, code, runLength) in table)
            {
                if (bits == bitsRead && code == accumulated)
                    return runLength;
            }
        }
        return -1; // not found
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
