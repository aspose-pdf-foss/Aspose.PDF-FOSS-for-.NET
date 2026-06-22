namespace Aspose.Pdf.Security;

/// <summary>
/// HMAC-SHA1 and HMAC-SHA256 (RFC 2104). Used by PBKDF2 and PKCS#12 KDF.
/// </summary>
internal static class HmacSha
{
    public static byte[] HmacSha1(byte[] key, byte[] data) => Hmac(key, data, Sha1, 64, 20);
    public static byte[] HmacSha256(byte[] key, byte[] data) => Hmac(key, data, ShaDigest.Sha256, 64, 32);

    /// <summary>PBKDF2-HMAC-SHA1 (RFC 2898).</summary>
    public static byte[] Pbkdf2Sha1(byte[] password, byte[] salt, int iterations, int keyLength)
        => Pbkdf2(password, salt, iterations, keyLength, HmacSha1, 20);

    /// <summary>PBKDF2-HMAC-SHA256 (RFC 2898).</summary>
    public static byte[] Pbkdf2Sha256(byte[] password, byte[] salt, int iterations, int keyLength)
        => Pbkdf2(password, salt, iterations, keyLength, HmacSha256, 32);

    /// <summary>PKCS#12 KDF (RFC 7292 Appendix B). Used by pbeWithSHA1And3-KeyTripleDES-CBC.</summary>
    public static byte[] Pkcs12Kdf(byte[] password, byte[] salt, int iterations, int keyLength, byte id)
    {
        const int u = 20; // SHA-1 hash length
        const int v = 64; // SHA-1 block size

        // 1. Construct D (diversifier): v bytes of 'id'
        var d = new byte[v];
        for (var i = 0; i < v; i++) d[i] = id;

        // 2. Construct I = S || P (concatenation of salt and password blocks)
        var sLen = salt.Length == 0 ? 0 : v * ((salt.Length + v - 1) / v);
        var pLen = password.Length == 0 ? 0 : v * ((password.Length + v - 1) / v);
        var iBlock = new byte[sLen + pLen];
        for (var i = 0; i < sLen; i++) iBlock[i] = salt[i % salt.Length];
        for (var i = 0; i < pLen; i++) iBlock[sLen + i] = password[i % password.Length];

        var result = new byte[keyLength];
        var offset = 0;

        while (offset < keyLength)
        {
            // Hash D || I
            var ai = Sha1(Concat(d, iBlock));
            for (var j = 1; j < iterations; j++)
                ai = Sha1(ai);

            var toCopy = Math.Min(u, keyLength - offset);
            Array.Copy(ai, 0, result, offset, toCopy);
            offset += toCopy;

            if (offset >= keyLength) break;

            // Adjust I: B = repeat ai to v bytes, I_j = (I_j + B + 1) mod 2^v
            var b = new byte[v];
            for (var i = 0; i < v; i++) b[i] = ai[i % u];

            for (var j = 0; j < iBlock.Length; j += v)
            {
                var carry = 1;
                for (var k = v - 1; k >= 0; k--)
                {
                    var sum = iBlock[j + k] + b[k] + carry;
                    iBlock[j + k] = (byte)sum;
                    carry = sum >> 8;
                }
            }
        }

        return result;
    }

    /// <summary>Encode password for PKCS#12: UTF-16BE with null terminator.</summary>
    public static byte[] Pkcs12EncodePassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return [];
        var chars = password + "\0";
        var result = new byte[chars.Length * 2];
        for (var i = 0; i < chars.Length; i++)
        {
            result[i * 2] = (byte)(chars[i] >> 8);
            result[i * 2 + 1] = (byte)(chars[i] & 0xFF);
        }
        return result;
    }

    // ── SHA-1 (FIPS 180-4) — needed by PKCS#12 KDF ────────────────

    /// <summary>SHA-1 hash (for legacy CMS signature verification).</summary>
    internal static byte[] Sha1Hash(byte[] data) => Sha1(data);

    private static byte[] Sha1(byte[] data)
    {
        uint h0 = 0x67452301, h1 = 0xEFCDAB89, h2 = 0x98BADCFE, h3 = 0x10325476, h4 = 0xC3D2E1F0;
        var padded = PadSha(data, 64);
        Span<uint> w = stackalloc uint[80];

        for (var blk = 0; blk < padded.Length; blk += 64)
        {
            for (var i = 0; i < 16; i++)
                w[i] = (uint)((padded[blk + i * 4] << 24) | (padded[blk + i * 4 + 1] << 16) |
                              (padded[blk + i * 4 + 2] << 8) | padded[blk + i * 4 + 3]);
            for (var i = 16; i < 80; i++)
                w[i] = RotL(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);

            uint a = h0, b = h1, c = h2, d = h3, e = h4;
            for (var i = 0; i < 80; i++)
            {
                var (f, k) = i switch
                {
                    < 20 => ((b & c) | (~b & d), 0x5A827999u),
                    < 40 => (b ^ c ^ d, 0x6ED9EBA1u),
                    < 60 => ((b & c) | (b & d) | (c & d), 0x8F1BBCDCu),
                    _ => (b ^ c ^ d, 0xCA62C1D6u),
                };
                var temp = RotL(a, 5) + f + e + k + w[i];
                e = d; d = c; c = RotL(b, 30); b = a; a = temp;
            }
            h0 += a; h1 += b; h2 += c; h3 += d; h4 += e;
        }

        var result = new byte[20];
        WriteBE(result, 0, h0); WriteBE(result, 4, h1); WriteBE(result, 8, h2);
        WriteBE(result, 12, h3); WriteBE(result, 16, h4);
        return result;
    }

    private static uint RotL(uint v, int n) => (v << n) | (v >> (32 - n));
    private static void WriteBE(byte[] b, int i, uint v)
    { b[i] = (byte)(v >> 24); b[i+1] = (byte)(v >> 16); b[i+2] = (byte)(v >> 8); b[i+3] = (byte)v; }

    private static byte[] PadSha(byte[] data, int blockSize)
    {
        var bitLen = (long)data.Length * 8;
        var padded = data.Length + 1 + 8;
        var totalLen = ((padded + blockSize - 1) / blockSize) * blockSize;
        var result = new byte[totalLen];
        Array.Copy(data, result, data.Length);
        result[data.Length] = 0x80;
        for (var i = 0; i < 8; i++) result[totalLen - 1 - i] = (byte)(bitLen >> (i * 8));
        return result;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static byte[] Hmac(byte[] key, byte[] data, Func<byte[], byte[]> hash, int blockSize, int hashLen)
    {
        if (key.Length > blockSize) key = hash(key);
        var paddedKey = new byte[blockSize];
        key.CopyTo(paddedKey, 0);

        var ipad = new byte[blockSize + data.Length];
        for (var i = 0; i < blockSize; i++) ipad[i] = (byte)(paddedKey[i] ^ 0x36);
        Array.Copy(data, 0, ipad, blockSize, data.Length);

        var innerHash = hash(ipad);

        var opad = new byte[blockSize + hashLen];
        for (var i = 0; i < blockSize; i++) opad[i] = (byte)(paddedKey[i] ^ 0x5C);
        Array.Copy(innerHash, 0, opad, blockSize, hashLen);

        return hash(opad);
    }

    private static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations, int keyLength,
        Func<byte[], byte[], byte[]> hmac, int hashLen)
    {
        var result = new byte[keyLength];
        var offset = 0;
        var blockNum = 1;

        while (offset < keyLength)
        {
            var saltI = new byte[salt.Length + 4];
            Array.Copy(salt, saltI, salt.Length);
            saltI[^4] = (byte)(blockNum >> 24); saltI[^3] = (byte)(blockNum >> 16);
            saltI[^2] = (byte)(blockNum >> 8); saltI[^1] = (byte)blockNum;

            var u = hmac(password, saltI);
            var t = (byte[])u.Clone();

            for (var i = 1; i < iterations; i++)
            {
                u = hmac(password, u);
                for (var j = 0; j < t.Length; j++) t[j] ^= u[j];
            }

            var toCopy = Math.Min(hashLen, keyLength - offset);
            Array.Copy(t, 0, result, offset, toCopy);
            offset += toCopy;
            blockNum++;
        }

        return result;
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        a.CopyTo(r, 0); b.CopyTo(r, a.Length);
        return r;
    }
}
