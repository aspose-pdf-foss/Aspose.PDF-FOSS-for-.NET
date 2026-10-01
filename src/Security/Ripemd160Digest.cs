namespace Aspose.Pdf.Security;

/// <summary>
/// Pure C# RIPEMD-160 (ISO/IEC 10118-3): two parallel lines of five 16-step rounds over
/// little-endian words, combined crosswise into the five chaining values.
/// </summary>
internal static class Ripemd160Digest
{
    private static readonly uint[] InitialValues = [0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0];

    // Round constants of the left and the right line, one per round of 16 steps.
    private static readonly uint[] LeftConstants = [0x00000000, 0x5A827999, 0x6ED9EBA1, 0x8F1BBCDC, 0xA953FD4E];
    private static readonly uint[] RightConstants = [0x50A28BE6, 0x5C4DD124, 0x6D703EF3, 0x7A6D76E9, 0x00000000];

    // The message word each step reads.
    private static readonly byte[] LeftWords =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        7, 4, 13, 1, 10, 6, 15, 3, 12, 0, 9, 5, 2, 14, 11, 8,
        3, 10, 14, 4, 9, 15, 8, 1, 2, 7, 0, 6, 13, 11, 5, 12,
        1, 9, 11, 10, 0, 8, 12, 4, 13, 3, 7, 15, 14, 5, 6, 2,
        4, 0, 5, 9, 7, 12, 2, 10, 14, 1, 3, 8, 11, 6, 15, 13,
    ];
    private static readonly byte[] RightWords =
    [
        5, 14, 7, 0, 9, 2, 11, 4, 13, 6, 15, 8, 1, 10, 3, 12,
        6, 11, 3, 7, 0, 13, 5, 10, 14, 15, 8, 12, 4, 9, 1, 2,
        15, 5, 1, 3, 7, 14, 6, 9, 11, 8, 12, 2, 10, 0, 4, 13,
        8, 6, 4, 1, 3, 11, 15, 0, 5, 12, 2, 13, 9, 7, 10, 14,
        12, 15, 10, 4, 1, 5, 8, 7, 6, 2, 13, 14, 0, 3, 9, 11,
    ];

    // The left rotation of each step.
    private static readonly byte[] LeftShifts =
    [
        11, 14, 15, 12, 5, 8, 7, 9, 11, 13, 14, 15, 6, 7, 9, 8,
        7, 6, 8, 13, 11, 9, 7, 15, 7, 12, 15, 9, 11, 7, 13, 12,
        11, 13, 6, 7, 14, 9, 13, 15, 14, 8, 13, 6, 5, 12, 7, 5,
        11, 12, 14, 15, 14, 15, 9, 8, 9, 14, 5, 6, 8, 6, 5, 12,
        9, 15, 5, 11, 6, 8, 13, 12, 5, 12, 13, 14, 11, 8, 5, 6,
    ];
    private static readonly byte[] RightShifts =
    [
        8, 9, 9, 11, 13, 15, 15, 5, 7, 7, 8, 11, 14, 14, 12, 6,
        9, 13, 15, 7, 12, 8, 9, 11, 7, 7, 12, 7, 6, 15, 13, 11,
        9, 7, 15, 11, 8, 6, 6, 14, 12, 13, 5, 14, 13, 13, 7, 5,
        15, 5, 8, 11, 14, 14, 6, 14, 6, 9, 12, 9, 12, 5, 15, 8,
        8, 5, 12, 9, 12, 5, 14, 6, 8, 13, 6, 5, 15, 13, 11, 11,
    ];

    private const int BlockSize = 64;
    private const int StepsPerRound = 16;
    private const int Steps = 80;

    public static byte[] Hash(byte[] data) => Hash(data, 0, data.Length);

    /// <summary>The 20-byte RIPEMD-160 digest of the bytes in the range.</summary>
    public static byte[] Hash(byte[] data, int offset, int count)
    {
        var h = (uint[])InitialValues.Clone();
        var padded = Pad(data, offset, count);
        Span<uint> x = stackalloc uint[16];
        for (var block = 0; block < padded.Length; block += BlockSize)
        {
            for (var i = 0; i < 16; i++)
                x[i] = (uint)(padded[block + i * 4] | padded[block + i * 4 + 1] << 8 | padded[block + i * 4 + 2] << 16 | padded[block + i * 4 + 3] << 24);
            Compress(h, x);
        }

        var result = new byte[20];
        for (var i = 0; i < 5; i++)
        {
            result[i * 4] = (byte)h[i];
            result[i * 4 + 1] = (byte)(h[i] >> 8);
            result[i * 4 + 2] = (byte)(h[i] >> 16);
            result[i * 4 + 3] = (byte)(h[i] >> 24);
        }
        return result;
    }

    // One block through both lines, folded into the chaining values.
    private static void Compress(uint[] h, ReadOnlySpan<uint> x)
    {
        uint al = h[0], bl = h[1], cl = h[2], dl = h[3], el = h[4];
        uint ar = h[0], br = h[1], cr = h[2], dr = h[3], er = h[4];
        for (var j = 0; j < Steps; j++)
        {
            var round = j / StepsPerRound;
            var t = RotL(al + F(round, bl, cl, dl) + x[LeftWords[j]] + LeftConstants[round], LeftShifts[j]) + el;
            al = el; el = dl; dl = RotL(cl, 10); cl = bl; bl = t;
            t = RotL(ar + F(4 - round, br, cr, dr) + x[RightWords[j]] + RightConstants[round], RightShifts[j]) + er;
            ar = er; er = dr; dr = RotL(cr, 10); cr = br; br = t;
        }
        var carry = h[1] + cl + dr;
        h[1] = h[2] + dl + er;
        h[2] = h[3] + el + ar;
        h[3] = h[4] + al + br;
        h[4] = h[0] + bl + cr;
        h[0] = carry;
    }

    // The boolean function of a round.
    private static uint F(int round, uint x, uint y, uint z) => round switch
    {
        0 => x ^ y ^ z,
        1 => (x & y) | (~x & z),
        2 => (x | ~y) ^ z,
        3 => (x & z) | (y & ~z),
        _ => x ^ (y | ~z),
    };

    private static uint RotL(uint v, int n) => (v << n) | (v >> (32 - n));

    // MD-style padding: 0x80, zeros, then the bit length as a little-endian 64-bit number.
    private static byte[] Pad(byte[] data, int offset, int count)
    {
        var total = (count + 1 + 8 + BlockSize - 1) / BlockSize * BlockSize;
        var result = new byte[total];
        Array.Copy(data, offset, result, 0, count);
        result[count] = 0x80;
        var bits = (long)count * 8;
        for (var i = 0; i < 8; i++)
            result[total - 8 + i] = (byte)(bits >> (8 * i));
        return result;
    }
}
