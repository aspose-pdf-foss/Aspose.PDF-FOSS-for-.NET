namespace Aspose.Pdf.Security;

/// <summary>
/// Pure C# DES / 3DES implementation (FIPS 46-3).
/// Used by PKCS#12 parser for legacy PFX decryption.
/// </summary>
internal sealed class DesCipher
{
    private readonly ulong[] _subkeys = new ulong[16];

    public DesCipher(byte[] key8)
    {
        if (key8.Length != 8) throw new ArgumentException("DES key must be 8 bytes");
        GenerateSubkeys(key8);
    }

    public void EncryptBlock(byte[] input, int inOff, byte[] output, int outOff)
        => ProcessBlock(input, inOff, output, outOff, false);

    public void DecryptBlock(byte[] input, int inOff, byte[] output, int outOff)
        => ProcessBlock(input, inOff, output, outOff, true);

    /// <summary>3DES-EDE3 CBC decrypt with PKCS#5 unpadding.</summary>
    public static byte[] TripleDesDecryptCbc(byte[] key24, byte[] iv, byte[] data)
    {
        var c1 = new DesCipher(key24[..8]);
        var c2 = new DesCipher(key24[8..16]);
        var c3 = new DesCipher(key24[16..24]);
        var output = new byte[data.Length];
        var prev = (byte[])iv.Clone();
        var tmp1 = new byte[8];
        var tmp2 = new byte[8];

        for (var i = 0; i < data.Length; i += 8)
        {
            // 3DES-EDE3 decrypt: D_K1(E_K2(D_K3(C)))
            c3.DecryptBlock(data, i, tmp1, 0);
            c2.EncryptBlock(tmp1, 0, tmp2, 0);
            c1.DecryptBlock(tmp2, 0, output, i);
            for (var j = 0; j < 8; j++)
                output[i + j] ^= prev[j];
            Array.Copy(data, i, prev, 0, 8);
        }

        // PKCS#5 unpadding
        if (output.Length > 0)
        {
            var pad = output[^1];
            if (pad >= 1 && pad <= 8)
            {
                var valid = true;
                for (var i = output.Length - pad; i < output.Length; i++)
                    if (output[i] != pad) { valid = false; break; }
                if (valid) return output[..^pad];
            }
        }
        return output;
    }

    /// <summary>3DES-EDE3 CBC encrypt with PKCS#5 padding.</summary>
    public static byte[] TripleDesEncryptCbc(byte[] key24, byte[] iv, byte[] data)
    {
        var padLen = 8 - (data.Length % 8);
        var padded = new byte[data.Length + padLen];
        data.CopyTo(padded, 0);
        for (var i = data.Length; i < padded.Length; i++) padded[i] = (byte)padLen;

        var c1 = new DesCipher(key24[..8]);
        var c2 = new DesCipher(key24[8..16]);
        var c3 = new DesCipher(key24[16..24]);
        var output = new byte[padded.Length];
        var prev = (byte[])iv.Clone();
        var block = new byte[8];
        var tmp1 = new byte[8];
        var tmp2 = new byte[8];

        for (var i = 0; i < padded.Length; i += 8)
        {
            for (var j = 0; j < 8; j++) block[j] = (byte)(padded[i + j] ^ prev[j]);
            // 3DES-EDE3 encrypt: E_K3(D_K2(E_K1(P)))
            c1.EncryptBlock(block, 0, tmp1, 0);
            c2.DecryptBlock(tmp1, 0, tmp2, 0);
            c3.EncryptBlock(tmp2, 0, output, i);
            Array.Copy(output, i, prev, 0, 8);
        }
        return output;
    }

    // ── Core DES ───────────────────────────────────────────────────

    private void ProcessBlock(byte[] input, int inOff, byte[] output, int outOff, bool decrypt)
    {
        var block = Pack64(input, inOff);
        block = Permute(block, IP, 64);

        var left = (uint)(block >> 32);
        var right = (uint)(block & 0xFFFFFFFF);

        for (var i = 0; i < 16; i++)
        {
            var ki = decrypt ? _subkeys[15 - i] : _subkeys[i];
            var temp = right;
            right = left ^ Feistel(right, ki);
            left = temp;
        }

        // Swap and final permutation
        var combined = ((ulong)right << 32) | left;
        combined = Permute(combined, FP, 64);
        Unpack64(combined, output, outOff);
    }

    private static uint Feistel(uint halfBlock, ulong subkey)
    {
        // Expansion: 32 bits → 48 bits
        var expanded = Expand(halfBlock);
        // XOR with subkey
        expanded ^= subkey;
        // S-box substitution: 48 bits → 32 bits
        uint result = 0;
        for (var i = 0; i < 8; i++)
        {
            var sixBits = (int)((expanded >> (42 - i * 6)) & 0x3F);
            var row = ((sixBits >> 4) & 0x02) | (sixBits & 0x01);
            var col = (sixBits >> 1) & 0x0F;
            result |= (uint)SBoxes[i, row * 16 + col] << (28 - i * 4);
        }
        // P permutation: 32 bits → 32 bits
        return Permute32(result);
    }

    private static ulong Expand(uint r)
    {
        // Expansion permutation E: 32 → 48 bits
        ulong result = 0;
        for (var i = 0; i < 48; i++)
        {
            var bit = (r >> (32 - E[i])) & 1;
            result |= (ulong)bit << (47 - i);
        }
        return result;
    }

    private static uint Permute32(uint input)
    {
        uint result = 0;
        for (var i = 0; i < 32; i++)
        {
            var bit = (input >> (32 - P[i])) & 1;
            result |= bit << (31 - i);
        }
        return result;
    }

    private static ulong Permute(ulong input, int[] table, int inputBits)
    {
        ulong result = 0;
        for (var i = 0; i < table.Length; i++)
        {
            var bit = (input >> (inputBits - table[i])) & 1;
            result |= bit << (table.Length - 1 - i);
        }
        return result;
    }

    private void GenerateSubkeys(byte[] key)
    {
        var key64 = Pack64(key, 0);

        // PC-1: 64 → 56 bits
        var pc1Result = Permute(key64, PC1, 64);
        var c = (uint)(pc1Result >> 28) & 0x0FFFFFFF;
        var d = (uint)(pc1Result & 0x0FFFFFFF);

        for (var round = 0; round < 16; round++)
        {
            var shift = LeftShifts[round];
            c = ((c << shift) | (c >> (28 - shift))) & 0x0FFFFFFF;
            d = ((d << shift) | (d >> (28 - shift))) & 0x0FFFFFFF;

            var cd = ((ulong)c << 28) | d;
            _subkeys[round] = Permute(cd, PC2, 56);
        }
    }

    private static ulong Pack64(byte[] b, int off)
    {
        return ((ulong)b[off] << 56) | ((ulong)b[off + 1] << 48) | ((ulong)b[off + 2] << 40) |
               ((ulong)b[off + 3] << 32) | ((ulong)b[off + 4] << 24) | ((ulong)b[off + 5] << 16) |
               ((ulong)b[off + 6] << 8) | b[off + 7];
    }

    private static void Unpack64(ulong v, byte[] b, int off)
    {
        b[off] = (byte)(v >> 56); b[off + 1] = (byte)(v >> 48);
        b[off + 2] = (byte)(v >> 40); b[off + 3] = (byte)(v >> 32);
        b[off + 4] = (byte)(v >> 24); b[off + 5] = (byte)(v >> 16);
        b[off + 6] = (byte)(v >> 8); b[off + 7] = (byte)v;
    }

    // ── DES Tables ─────────────────────────────────────────────────

    private static readonly int[] LeftShifts = [1, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 1];

    // Initial Permutation
    private static readonly int[] IP =
    [
        58, 50, 42, 34, 26, 18, 10,  2, 60, 52, 44, 36, 28, 20, 12,  4,
        62, 54, 46, 38, 30, 22, 14,  6, 64, 56, 48, 40, 32, 24, 16,  8,
        57, 49, 41, 33, 25, 17,  9,  1, 59, 51, 43, 35, 27, 19, 11,  3,
        61, 53, 45, 37, 29, 21, 13,  5, 63, 55, 47, 39, 31, 23, 15,  7,
    ];

    // Final Permutation (IP inverse)
    private static readonly int[] FP =
    [
        40,  8, 48, 16, 56, 24, 64, 32, 39,  7, 47, 15, 55, 23, 63, 31,
        38,  6, 46, 14, 54, 22, 62, 30, 37,  5, 45, 13, 53, 21, 61, 29,
        36,  4, 44, 12, 52, 20, 60, 28, 35,  3, 43, 11, 51, 19, 59, 27,
        34,  2, 42, 10, 50, 18, 58, 26, 33,  1, 41,  9, 49, 17, 57, 25,
    ];

    // Expansion permutation
    private static readonly int[] E =
    [
        32,  1,  2,  3,  4,  5,  4,  5,  6,  7,  8,  9,
         8,  9, 10, 11, 12, 13, 12, 13, 14, 15, 16, 17,
        16, 17, 18, 19, 20, 21, 20, 21, 22, 23, 24, 25,
        24, 25, 26, 27, 28, 29, 28, 29, 30, 31, 32,  1,
    ];

    // P permutation
    private static readonly int[] P =
    [
        16,  7, 20, 21, 29, 12, 28, 17,  1, 15, 23, 26,  5, 18, 31, 10,
         2,  8, 24, 14, 32, 27,  3,  9, 19, 13, 30,  6, 22, 11,  4, 25,
    ];

    // PC-1 (Permuted Choice 1)
    private static readonly int[] PC1 =
    [
        57, 49, 41, 33, 25, 17,  9,  1, 58, 50, 42, 34, 26, 18,
        10,  2, 59, 51, 43, 35, 27, 19, 11,  3, 60, 52, 44, 36,
        63, 55, 47, 39, 31, 23, 15,  7, 62, 54, 46, 38, 30, 22,
        14,  6, 61, 53, 45, 37, 29, 21, 13,  5, 28, 20, 12,  4,
    ];

    // PC-2 (Permuted Choice 2)
    private static readonly int[] PC2 =
    [
        14, 17, 11, 24,  1,  5,  3, 28, 15,  6, 21, 10,
        23, 19, 12,  4, 26,  8, 16,  7, 27, 20, 13,  2,
        41, 52, 31, 37, 47, 55, 30, 40, 51, 45, 33, 48,
        44, 49, 39, 56, 34, 53, 46, 42, 50, 36, 29, 32,
    ];

    // S-boxes (8 boxes, each 4 rows × 16 columns)
    private static readonly int[,] SBoxes =
    {
        {
            14,  4, 13,  1,  2, 15, 11,  8,  3, 10,  6, 12,  5,  9,  0,  7,
             0, 15,  7,  4, 14,  2, 13,  1, 10,  6, 12, 11,  9,  5,  3,  8,
             4,  1, 14,  8, 13,  6,  2, 11, 15, 12,  9,  7,  3, 10,  5,  0,
            15, 12,  8,  2,  4,  9,  1,  7,  5, 11,  3, 14, 10,  0,  6, 13,
        },
        {
            15,  1,  8, 14,  6, 11,  3,  4,  9,  7,  2, 13, 12,  0,  5, 10,
             3, 13,  4,  7, 15,  2,  8, 14, 12,  0,  1, 10,  6,  9, 11,  5,
             0, 14,  7, 11, 10,  4, 13,  1,  5,  8, 12,  6,  9,  3,  2, 15,
            13,  8, 10,  1,  3, 15,  4,  2, 11,  6,  7, 12,  0,  5, 14,  9,
        },
        {
            10,  0,  9, 14,  6,  3, 15,  5,  1, 13, 12,  7, 11,  4,  2,  8,
            13,  7,  0,  9,  3,  4,  6, 10,  2,  8,  5, 14, 12, 11, 15,  1,
            13,  6,  4,  9,  8, 15,  3,  0, 11,  1,  2, 12,  5, 10, 14,  7,
             1, 10, 13,  0,  6,  9,  8,  7,  4, 15, 14,  3, 11,  5,  2, 12,
        },
        {
             7, 13, 14,  3,  0,  6,  9, 10,  1,  2,  8,  5, 11, 12,  4, 15,
            13,  8, 11,  5,  6, 15,  0,  3,  4,  7,  2, 12,  1, 10, 14,  9,
            10,  6,  9,  0, 12, 11,  7, 13, 15,  1,  3, 14,  5,  2,  8,  4,
             3, 15,  0,  6, 10,  1, 13,  8,  9,  4,  5, 11, 12,  7,  2, 14,
        },
        {
             2, 12,  4,  1,  7, 10, 11,  6,  8,  5,  3, 15, 13,  0, 14,  9,
            14, 11,  2, 12,  4,  7, 13,  1,  5,  0, 15, 10,  3,  9,  8,  6,
             4,  2,  1, 11, 10, 13,  7,  8, 15,  9, 12,  5,  6,  3,  0, 14,
            11,  8, 12,  7,  1, 14,  2, 13,  6, 15,  0,  9, 10,  4,  5,  3,
        },
        {
            12,  1, 10, 15,  9,  2,  6,  8,  0, 13,  3,  4, 14,  7,  5, 11,
            10, 15,  4,  2,  7, 12,  9,  5,  6,  1, 13, 14,  0, 11,  3,  8,
             9, 14, 15,  5,  2,  8, 12,  3,  7,  0,  4, 10,  1, 13, 11,  6,
             4,  3,  2, 12,  9,  5, 15, 10, 11, 14,  1,  7,  6,  0,  8, 13,
        },
        {
             4, 11,  2, 14, 15,  0,  8, 13,  3, 12,  9,  7,  5, 10,  6,  1,
            13,  0, 11,  7,  4,  9,  1, 10, 14,  3,  5, 12,  2, 15,  8,  6,
             1,  4, 11, 13, 12,  3,  7, 14, 10, 15,  6,  8,  0,  5,  9,  2,
             6, 11, 13,  8,  1,  4, 10,  7,  9,  5,  0, 15, 14,  2,  3, 12,
        },
        {
            13,  2,  8,  4,  6, 15, 11,  1, 10,  9,  3, 14,  5,  0, 12,  7,
             1, 15, 13,  8, 10,  3,  7,  4, 12,  5,  6, 11,  0, 14,  9,  2,
             7, 11,  4,  1,  9, 12, 14,  2,  0,  6, 10, 13, 15,  3,  5,  8,
             2,  1, 14,  7,  4, 10,  8, 13, 15, 12,  9,  0,  3,  5,  6, 11,
        },
    };
}
