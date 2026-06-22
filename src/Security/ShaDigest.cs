namespace Aspose.Pdf.Security;

/// <summary>
/// Pure C# SHA-2 family (SHA-256, SHA-384, SHA-512) implementation (FIPS 180-4).
/// Replaces System.Security.Cryptography.SHA256/SHA384/SHA512 dependency.
/// </summary>
internal static class ShaDigest
{
    // ── SHA-256 ────────────────────────────────────────────────────

    public static byte[] Sha256(byte[] data) => Sha256(data, 0, data.Length);

    /// <summary>
    /// SHA-256 hash per FIPS 180-4 §6.2.2.
    /// Initial hash values h0–h7 are the fractional parts of the square roots of
    /// the first 8 primes (FIPS 180-4 §5.3.3).
    /// </summary>
    public static byte[] Sha256(byte[] data, int offset, int count)
    {
        // Initial hash values — fractional parts of sqrt(2), sqrt(3), . sqrt(19)
        uint h0 = 0x6A09E667, h1 = 0xBB67AE85, h2 = 0x3C6EF372, h3 = 0xA54FF53A;
        uint h4 = 0x510E527F, h5 = 0x9B05688C, h6 = 0x1F83D9AB, h7 = 0x5BE0CD19;

        var padded = Pad32(data, offset, count);
        Span<uint> w = stackalloc uint[64];

        // Process each 512-bit (64-byte) block
        for (var blk = 0; blk < padded.Length; blk += 64)
        {
            // Prepare message schedule (FIPS 180-4 §6.2.2 step 1)
            for (var i = 0; i < 16; i++)
                w[i] = ReadBE32(padded, blk + i * 4);
            for (var i = 16; i < 64; i++)
            {
                var s0 = RotR32(w[i - 15], 7) ^ RotR32(w[i - 15], 18) ^ (w[i - 15] >> 3);
                var s1 = RotR32(w[i - 2], 17) ^ RotR32(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }

            // Initialize working variables (FIPS 180-4 §6.2.2 step 2)
            uint a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;

            // 64 compression rounds (FIPS 180-4 §6.2.2 step 3)
            for (var i = 0; i < 64; i++)
            {
                var S1 = RotR32(e, 6) ^ RotR32(e, 11) ^ RotR32(e, 25);   // Σ1(e)
                var ch = (e & f) ^ (~e & g);                                // Ch(e,f,g)
                var temp1 = h + S1 + ch + K256[i] + w[i];
                var S0 = RotR32(a, 2) ^ RotR32(a, 13) ^ RotR32(a, 22);   // Σ0(a)
                var maj = (a & b) ^ (a & c) ^ (b & c);                     // Maj(a,b,c)
                var temp2 = S0 + maj;

                h = g; g = f; f = e; e = d + temp1;
                d = c; c = b; b = a; a = temp1 + temp2;
            }

            // Add compressed chunk to hash value (FIPS 180-4 §6.2.2 step 4)
            h0 += a; h1 += b; h2 += c; h3 += d;
            h4 += e; h5 += f; h6 += g; h7 += h;
        }

        var result = new byte[32];
        WriteBE32(result, 0, h0); WriteBE32(result, 4, h1);
        WriteBE32(result, 8, h2); WriteBE32(result, 12, h3);
        WriteBE32(result, 16, h4); WriteBE32(result, 20, h5);
        WriteBE32(result, 24, h6); WriteBE32(result, 28, h7);
        return result;
    }

    // ── SHA-384 ────────────────────────────────────────────────────

    public static byte[] Sha384(byte[] data)
    {
        var full = Sha512Core(data, 0, data.Length,
            0xCBBB9D5DC1059ED8, 0x629A292A367CD507,
            0x9159015A3070DD17, 0x152FECD8F70E5939,
            0x67332667FFC00B31, 0x8EB44A8768581511,
            0xDB0C2E0D64F98FA7, 0x47B5481DBEFA4FA4);
        return full[..48];
    }

    // ── SHA-512 ────────────────────────────────────────────────────

    public static byte[] Sha512(byte[] data)
    {
        return Sha512Core(data, 0, data.Length,
            0x6A09E667F3BCC908, 0xBB67AE8584CAA73B,
            0x3C6EF372FE94F82B, 0xA54FF53A5F1D36F1,
            0x510E527FADE682D1, 0x9B05688C2B3E6C1F,
            0x1F83D9ABFB41BD6B, 0x5BE0CD19137E2179);
    }

    // ── SHA-512 core (shared by SHA-384 and SHA-512) ───────────────

    private static byte[] Sha512Core(byte[] data, int offset, int count,
        ulong iv0, ulong iv1, ulong iv2, ulong iv3,
        ulong iv4, ulong iv5, ulong iv6, ulong iv7)
    {
        ulong h0 = iv0, h1 = iv1, h2 = iv2, h3 = iv3;
        ulong h4 = iv4, h5 = iv5, h6 = iv6, h7 = iv7;

        var padded = Pad64(data, offset, count);
        Span<ulong> w = stackalloc ulong[80];

        for (var blk = 0; blk < padded.Length; blk += 128)
        {
            for (var i = 0; i < 16; i++)
                w[i] = ReadBE64(padded, blk + i * 8);
            for (var i = 16; i < 80; i++)
            {
                var s0 = RotR64(w[i - 15], 1) ^ RotR64(w[i - 15], 8) ^ (w[i - 15] >> 7);
                var s1 = RotR64(w[i - 2], 19) ^ RotR64(w[i - 2], 61) ^ (w[i - 2] >> 6);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }

            ulong a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;

            for (var i = 0; i < 80; i++)
            {
                var S1 = RotR64(e, 14) ^ RotR64(e, 18) ^ RotR64(e, 41);
                var ch = (e & f) ^ (~e & g);
                var temp1 = h + S1 + ch + K512[i] + w[i];
                var S0 = RotR64(a, 28) ^ RotR64(a, 34) ^ RotR64(a, 39);
                var maj = (a & b) ^ (a & c) ^ (b & c);
                var temp2 = S0 + maj;

                h = g; g = f; f = e; e = d + temp1;
                d = c; c = b; b = a; a = temp1 + temp2;
            }

            h0 += a; h1 += b; h2 += c; h3 += d;
            h4 += e; h5 += f; h6 += g; h7 += h;
        }

        var result = new byte[64];
        WriteBE64(result, 0, h0); WriteBE64(result, 8, h1);
        WriteBE64(result, 16, h2); WriteBE64(result, 24, h3);
        WriteBE64(result, 32, h4); WriteBE64(result, 40, h5);
        WriteBE64(result, 48, h6); WriteBE64(result, 56, h7);
        return result;
    }

    // ── Padding ────────────────────────────────────────────────────

    private static byte[] Pad32(byte[] data, int offset, int count)
    {
        var bitLen = (long)count * 8;
        // Need: count + 1 + padding + 8, aligned to 64
        var padded = count + 1 + 8;
        var totalLen = ((padded + 63) / 64) * 64;
        var result = new byte[totalLen];
        Array.Copy(data, offset, result, 0, count);
        result[count] = 0x80;
        // Big-endian 64-bit length at end
        result[totalLen - 8] = (byte)(bitLen >> 56);
        result[totalLen - 7] = (byte)(bitLen >> 48);
        result[totalLen - 6] = (byte)(bitLen >> 40);
        result[totalLen - 5] = (byte)(bitLen >> 32);
        result[totalLen - 4] = (byte)(bitLen >> 24);
        result[totalLen - 3] = (byte)(bitLen >> 16);
        result[totalLen - 2] = (byte)(bitLen >> 8);
        result[totalLen - 1] = (byte)bitLen;
        return result;
    }

    private static byte[] Pad64(byte[] data, int offset, int count)
    {
        // SHA-512 uses 128-byte blocks and 128-bit length field
        var padded = count + 1 + 16;
        var totalLen = ((padded + 127) / 128) * 128;
        var result = new byte[totalLen];
        Array.Copy(data, offset, result, 0, count);
        result[count] = 0x80;
        // Big-endian 128-bit length (high 64 bits are 0 for practical sizes)
        var bitLen = (long)count * 8;
        result[totalLen - 8] = (byte)(bitLen >> 56);
        result[totalLen - 7] = (byte)(bitLen >> 48);
        result[totalLen - 6] = (byte)(bitLen >> 40);
        result[totalLen - 5] = (byte)(bitLen >> 32);
        result[totalLen - 4] = (byte)(bitLen >> 24);
        result[totalLen - 3] = (byte)(bitLen >> 16);
        result[totalLen - 2] = (byte)(bitLen >> 8);
        result[totalLen - 1] = (byte)bitLen;
        return result;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static uint RotR32(uint v, int n) => (v >> n) | (v << (32 - n));
    private static ulong RotR64(ulong v, int n) => (v >> n) | (v << (64 - n));

    private static uint ReadBE32(byte[] b, int i)
        => (uint)((b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]);

    private static ulong ReadBE64(byte[] b, int i)
        => ((ulong)b[i] << 56) | ((ulong)b[i + 1] << 48) | ((ulong)b[i + 2] << 40) | ((ulong)b[i + 3] << 32) |
           ((ulong)b[i + 4] << 24) | ((ulong)b[i + 5] << 16) | ((ulong)b[i + 6] << 8) | b[i + 7];

    private static void WriteBE32(byte[] b, int i, uint v)
    {
        b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16);
        b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v;
    }

    private static void WriteBE64(byte[] b, int i, ulong v)
    {
        b[i] = (byte)(v >> 56); b[i + 1] = (byte)(v >> 48);
        b[i + 2] = (byte)(v >> 40); b[i + 3] = (byte)(v >> 32);
        b[i + 4] = (byte)(v >> 24); b[i + 5] = (byte)(v >> 16);
        b[i + 6] = (byte)(v >> 8); b[i + 7] = (byte)v;
    }

    // ── Constants ───────────────────────────────────────────────────

    private static readonly uint[] K256 =
    [
        0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5,
        0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
        0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3,
        0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
        0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC,
        0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
        0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7,
        0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
        0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13,
        0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
        0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3,
        0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
        0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5,
        0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
        0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208,
        0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2,
    ];

    private static readonly ulong[] K512 =
    [
        0x428A2F98D728AE22, 0x7137449123EF65CD, 0xB5C0FBCFEC4D3B2F, 0xE9B5DBA58189DBBC,
        0x3956C25BF348B538, 0x59F111F1B605D019, 0x923F82A4AF194F9B, 0xAB1C5ED5DA6D8118,
        0xD807AA98A3030242, 0x12835B0145706FBE, 0x243185BE4EE4B28C, 0x550C7DC3D5FFB4E2,
        0x72BE5D74F27B896F, 0x80DEB1FE3B1696B1, 0x9BDC06A725C71235, 0xC19BF174CF692694,
        0xE49B69C19EF14AD2, 0xEFBE4786384F25E3, 0x0FC19DC68B8CD5B5, 0x240CA1CC77AC9C65,
        0x2DE92C6F592B0275, 0x4A7484AA6EA6E483, 0x5CB0A9DCBD41FBD4, 0x76F988DA831153B5,
        0x983E5152EE66DFAB, 0xA831C66D2DB43210, 0xB00327C898FB213F, 0xBF597FC7BEEF0EE4,
        0xC6E00BF33DA88FC2, 0xD5A79147930AA725, 0x06CA6351E003826F, 0x142929670A0E6E70,
        0x27B70A8546D22FFC, 0x2E1B21385C26C926, 0x4D2C6DFC5AC42AED, 0x53380D139D95B3DF,
        0x650A73548BAF63DE, 0x766A0ABB3C77B2A8, 0x81C2C92E47EDAEE6, 0x92722C851482353B,
        0xA2BFE8A14CF10364, 0xA81A664BBC423001, 0xC24B8B70D0F89791, 0xC76C51A30654BE30,
        0xD192E819D6EF5218, 0xD69906245565A910, 0xF40E35855771202A, 0x106AA07032BBD1B8,
        0x19A4C116B8D2D0C8, 0x1E376C085141AB53, 0x2748774CDF8EEB99, 0x34B0BCB5E19B48A8,
        0x391C0CB3C5C95A63, 0x4ED8AA4AE3418ACB, 0x5B9CCA4F7763E373, 0x682E6FF3D6B2B8A3,
        0x748F82EE5DEFB2FC, 0x78A5636F43172F60, 0x84C87814A1F0AB72, 0x8CC702081A6439EC,
        0x90BEFFFA23631E28, 0xA4506CEBDE82BDE9, 0xBEF9A3F7B2C67915, 0xC67178F2E372532B,
        0xCA273ECEEA26619C, 0xD186B8C721C0C207, 0xEADA7DD6CDE0EB1E, 0xF57D4F7FEE6ED178,
        0x06F067AA72176FBA, 0x0A637DC5A2C898A6, 0x113F9804BEF90DAE, 0x1B710B35131C471B,
        0x28DB77F523047D84, 0x32CAAB7B40C72493, 0x3C9EBE0A15C9BEBC, 0x431D67C49C100D4C,
        0x4CC5D4BECB3E42B6, 0x597F299CFC657E2A, 0x5FCB6FAB3AD6FAEC, 0x6C44198C4A475817,
    ];
}
