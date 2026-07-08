namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// CCITT Group 4 (T.6 MMR, /K -1) encoder for bitonal images. Input is packed
/// 1-bpp rows, MSB first, bit 1 = black. Output decodes with
/// <c>/CCITTFaxDecode &lt;&lt;/K -1 /Columns w /Rows h&gt;&gt;</c> (BlackIs1 false,
/// so decoded 0-bits are black — the PDF default).
/// </summary>
internal static class CcittG4Encoder
{
    public static byte[] Encode(byte[] packed, int width, int height, int stride)
    {
        var bw = new BitWriter();
        var refChanges = new int[width + 2];
        var curChanges = new int[width + 2];
        var refCount = 0; // reference line starts all white — no changing elements

        for (var y = 0; y < height; y++)
        {
            var curCount = 0;
            var color = false; // white
            var rowOff = y * stride;
            for (var x = 0; x < width; x++)
            {
                var black = (packed[rowOff + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                if (black != color)
                {
                    curChanges[curCount++] = x;
                    color = black;
                }
            }
            EncodeRow(bw, refChanges, refCount, curChanges, curCount, width);
            (refChanges, curChanges) = (curChanges, refChanges);
            refCount = curCount;
        }

        // EOFB — two EOL codes
        bw.WriteBits(0b0000_0000_0001, 12);
        bw.WriteBits(0b0000_0000_0001, 12);
        return bw.ToArray();
    }

    private static void EncodeRow(BitWriter bw, int[] refChanges, int refCount,
        int[] curChanges, int curCount, int width)
    {
        var a0 = -1;
        var a0White = true;

        while (a0 < width)
        {
            // a1: first changing element on the coding line right of a0 whose new
            // colour is opposite a0's colour. Changes alternate starting white→black,
            // so even indices begin black runs, odd indices begin white runs.
            var a1Idx = FirstChangeAfter(curChanges, curCount, a0, wantBlackStart: a0White);
            var a1 = a1Idx < curCount ? curChanges[a1Idx] : width;

            // b1: first changing element on the reference line right of a0 with the
            // same "starts colour opposite a0" property; b2 is the one after it.
            var b1Idx = FirstChangeAfter(refChanges, refCount, a0, wantBlackStart: a0White);
            var b1 = b1Idx < refCount ? refChanges[b1Idx] : width;
            var b2 = b1Idx + 1 < refCount ? refChanges[b1Idx + 1] : width;

            if (b2 < a1)
            {
                bw.WriteBits(0b0001, 4); // pass mode
                a0 = b2;
            }
            else if (a1 - b1 >= -3 && a1 - b1 <= 3)
            {
                switch (a1 - b1)
                {
                    case 0: bw.WriteBits(0b1, 1); break;
                    case 1: bw.WriteBits(0b011, 3); break;
                    case 2: bw.WriteBits(0b000011, 6); break;
                    case 3: bw.WriteBits(0b0000011, 7); break;
                    case -1: bw.WriteBits(0b010, 3); break;
                    case -2: bw.WriteBits(0b000010, 6); break;
                    case -3: bw.WriteBits(0b0000010, 7); break;
                }
                a0 = a1;
                a0White = !a0White;
            }
            else
            {
                // horizontal mode: two runs (a0→a1 in a0's colour, a1→a2 opposite)
                var a2Idx = a1Idx + 1;
                var a2 = a2Idx < curCount ? curChanges[a2Idx] : width;
                bw.WriteBits(0b001, 3);
                WriteRun(bw, a1 - (a0 < 0 ? 0 : a0), a0White);
                WriteRun(bw, a2 - a1, !a0White);
                a0 = a2;
            }
        }
    }

    /// <summary>Index of the first change &gt; <paramref name="pos"/> that starts a
    /// black run (even index) or a white run (odd index), per
    /// <paramref name="wantBlackStart"/>. Returns <c>count</c> when none.</summary>
    private static int FirstChangeAfter(int[] changes, int count, int pos, bool wantBlackStart)
    {
        var i = 0;
        while (i < count && changes[i] <= pos) i++;
        if (i < count && (i % 2 == 0) != wantBlackStart) i++;
        return i;
    }

    private static void WriteRun(BitWriter bw, int run, bool white)
    {
        while (run >= 64)
        {
            var m = System.Math.Min(2560, run / 64 * 64);
            EmitMakeup(bw, m, white);
            run -= m;
        }
        var (code, len) = white ? WhiteTerm[run] : BlackTerm[run];
        bw.WriteBits(code, len);
    }

    private static void EmitMakeup(BitWriter bw, int value, bool white)
    {
        if (value <= 1728)
        {
            var (code, len) = white ? WhiteMakeup[value / 64 - 1] : BlackMakeup[value / 64 - 1];
            bw.WriteBits(code, len);
        }
        else
        {
            var (code, len) = ExtMakeup[(value - 1792) / 64];
            bw.WriteBits(code, len);
        }
    }

    private sealed class BitWriter
    {
        private readonly System.Collections.Generic.List<byte> _bytes = new();
        private int _acc;
        private int _nbits;

        public void WriteBits(int code, int length)
        {
            for (var i = length - 1; i >= 0; i--)
            {
                _acc = (_acc << 1) | ((code >> i) & 1);
                if (++_nbits == 8)
                {
                    _bytes.Add((byte)_acc);
                    _acc = 0;
                    _nbits = 0;
                }
            }
        }

        public byte[] ToArray()
        {
            var result = new System.Collections.Generic.List<byte>(_bytes);
            if (_nbits > 0)
                result.Add((byte)(_acc << (8 - _nbits)));
            return result.ToArray();
        }
    }

    // ── ITU-T T.4 run-length code tables (code, bitLength) ─────────────────

    private static readonly (int code, int len)[] WhiteTerm =
    {
        (0b00110101, 8), (0b000111, 6), (0b0111, 4), (0b1000, 4),
        (0b1011, 4), (0b1100, 4), (0b1110, 4), (0b1111, 4),
        (0b10011, 5), (0b10100, 5), (0b00111, 5), (0b01000, 5),
        (0b001000, 6), (0b000011, 6), (0b110100, 6), (0b110101, 6),
        (0b101010, 6), (0b101011, 6), (0b0100111, 7), (0b0001100, 7),
        (0b0001000, 7), (0b0010111, 7), (0b0000011, 7), (0b0000100, 7),
        (0b0101000, 7), (0b0101011, 7), (0b0010011, 7), (0b0100100, 7),
        (0b0011000, 7), (0b00000010, 8), (0b00000011, 8), (0b00011010, 8),
        (0b00011011, 8), (0b00010010, 8), (0b00010011, 8), (0b00010100, 8),
        (0b00010101, 8), (0b00010110, 8), (0b00010111, 8), (0b00101000, 8),
        (0b00101001, 8), (0b00101010, 8), (0b00101011, 8), (0b00101100, 8),
        (0b00101101, 8), (0b00000100, 8), (0b00000101, 8), (0b00001010, 8),
        (0b00001011, 8), (0b01010010, 8), (0b01010011, 8), (0b01010100, 8),
        (0b01010101, 8), (0b00100100, 8), (0b00100101, 8), (0b01011000, 8),
        (0b01011001, 8), (0b01011010, 8), (0b01011011, 8), (0b01001010, 8),
        (0b01001011, 8), (0b00110010, 8), (0b00110011, 8), (0b00110100, 8),
    };

    private static readonly (int code, int len)[] WhiteMakeup =
    {
        (0b11011, 5),      // 64
        (0b10010, 5),      // 128
        (0b010111, 6),     // 192
        (0b0110111, 7),    // 256
        (0b00110110, 8),   // 320
        (0b00110111, 8),   // 384
        (0b01100100, 8),   // 448
        (0b01100101, 8),   // 512
        (0b01101000, 8),   // 576
        (0b01100111, 8),   // 640
        (0b011001100, 9),  // 704
        (0b011001101, 9),  // 768
        (0b011010010, 9),  // 832
        (0b011010011, 9),  // 896
        (0b011010100, 9),  // 960
        (0b011010101, 9),  // 1024
        (0b011010110, 9),  // 1088
        (0b011010111, 9),  // 1152
        (0b011011000, 9),  // 1216
        (0b011011001, 9),  // 1280
        (0b011011010, 9),  // 1344
        (0b011011011, 9),  // 1408
        (0b010011000, 9),  // 1472
        (0b010011001, 9),  // 1536
        (0b010011010, 9),  // 1600
        (0b011000, 6),     // 1664
        (0b010011011, 9),  // 1728
    };

    private static readonly (int code, int len)[] BlackTerm =
    {
        (0b0000110111, 10), (0b010, 3), (0b11, 2), (0b10, 2),
        (0b011, 3), (0b0011, 4), (0b0010, 4), (0b00011, 5),
        (0b000101, 6), (0b000100, 6), (0b0000100, 7), (0b0000101, 7),
        (0b0000111, 7), (0b00000100, 8), (0b00000111, 8), (0b000011000, 9),
        (0b0000010111, 10), (0b0000011000, 10), (0b0000001000, 10), (0b00001100111, 11),
        (0b00001101000, 11), (0b00001101100, 11), (0b00000110111, 11), (0b00000101000, 11),
        (0b00000010111, 11), (0b00000011000, 11), (0b000011001010, 12), (0b000011001011, 12),
        (0b000011001100, 12), (0b000011001101, 12), (0b000001101000, 12), (0b000001101001, 12),
        (0b000001101010, 12), (0b000001101011, 12), (0b000011010010, 12), (0b000011010011, 12),
        (0b000011010100, 12), (0b000011010101, 12), (0b000011010110, 12), (0b000011010111, 12),
        (0b000001101100, 12), (0b000001101101, 12), (0b000011011010, 12), (0b000011011011, 12),
        (0b000001010100, 12), (0b000001010101, 12), (0b000001010110, 12), (0b000001010111, 12),
        (0b000001100100, 12), (0b000001100101, 12), (0b000001010010, 12), (0b000001010011, 12),
        (0b000000100100, 12), (0b000000110111, 12), (0b000000111000, 12), (0b000000100111, 12),
        (0b000000101000, 12), (0b000001011000, 12), (0b000001011001, 12), (0b000000101011, 12),
        (0b000000101100, 12), (0b000001011010, 12), (0b000001100110, 12), (0b000001100111, 12),
    };

    private static readonly (int code, int len)[] BlackMakeup =
    {
        (0b0000001111, 10),    // 64
        (0b000011001000, 12),  // 128
        (0b000011001001, 12),  // 192
        (0b000001011011, 12),  // 256
        (0b000000110011, 12),  // 320
        (0b000000110100, 12),  // 384
        (0b000000110101, 12),  // 448
        (0b0000001101100, 13), // 512
        (0b0000001101101, 13), // 576
        (0b0000001001010, 13), // 640
        (0b0000001001011, 13), // 704
        (0b0000001001100, 13), // 768
        (0b0000001001101, 13), // 832
        (0b0000001110010, 13), // 896
        (0b0000001110011, 13), // 960
        (0b0000001110100, 13), // 1024
        (0b0000001110101, 13), // 1088
        (0b0000001110110, 13), // 1152
        (0b0000001110111, 13), // 1216
        (0b0000001010010, 13), // 1280
        (0b0000001010011, 13), // 1344
        (0b0000001010100, 13), // 1408
        (0b0000001010101, 13), // 1472
        (0b0000001011010, 13), // 1536
        (0b0000001011011, 13), // 1600
        (0b0000001100100, 13), // 1664
        (0b0000001100101, 13), // 1728
    };

    private static readonly (int code, int len)[] ExtMakeup =
    {
        (0b00000001000, 11),   // 1792
        (0b00000001100, 11),   // 1856
        (0b00000001101, 11),   // 1920
        (0b000000010010, 12),  // 1984
        (0b000000010011, 12),  // 2048
        (0b000000010100, 12),  // 2112
        (0b000000010101, 12),  // 2176
        (0b000000010110, 12),  // 2240
        (0b000000010111, 12),  // 2304
        (0b000000011100, 12),  // 2368
        (0b000000011101, 12),  // 2432
        (0b000000011110, 12),  // 2496
        (0b000000011111, 12),  // 2560
    };
}
