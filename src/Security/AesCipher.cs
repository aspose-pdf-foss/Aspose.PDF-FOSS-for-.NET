namespace Aspose.Pdf.Security;

/// <summary>
/// Pure C# AES (Rijndael-128) implementation (FIPS 197).
/// Supports AES-128 and AES-256 with ECB, CBC modes and PKCS#7/None padding.
/// Replaces System.Security.Cryptography.Aes dependency.
/// </summary>
internal sealed class AesCipher
{
    private readonly uint[,] _ek; // expanded encryption round keys
    private readonly uint[,] _dk; // expanded decryption round keys
    private readonly int _rounds;

    public AesCipher(byte[] key)
    {
        _rounds = key.Length switch
        {
            16 => 10,   // AES-128
            24 => 12,   // AES-192
            32 => 14,   // AES-256
            _ => throw new ArgumentException($"Invalid AES key length: {key.Length}")
        };
        _ek = ExpandKey(key, _rounds);
        _dk = InvertKey(_ek, _rounds);
    }

    // ── Public API ─────────────────────────────────────────────────

    /// <summary>Encrypt with CBC mode. Returns IV (16 bytes) + ciphertext.</summary>
    public byte[] EncryptCbc(byte[] data, byte[] iv, bool pkcs7Padding)
    {
        var input = pkcs7Padding ? PadPkcs7(data) : data;
        if (input.Length % 16 != 0) throw new ArgumentException("Data must be block-aligned for NoPadding");

        var output = new byte[input.Length];
        var prev = (byte[])iv.Clone();

        for (var i = 0; i < input.Length; i += 16)
        {
            // XOR plaintext with previous ciphertext block
            var block = new byte[16];
            for (var j = 0; j < 16; j++)
                block[j] = (byte)(input[i + j] ^ prev[j]);

            EncryptBlock(block, 0, output, i);
            Array.Copy(output, i, prev, 0, 16);
        }

        return output;
    }

    /// <summary>Decrypt CBC ciphertext. Data does NOT include IV prefix.</summary>
    public byte[] DecryptCbc(byte[] data, byte[] iv, bool pkcs7Padding)
    {
        if (data.Length == 0 || data.Length % 16 != 0) return data;

        var output = new byte[data.Length];
        var prev = iv;

        for (var i = 0; i < data.Length; i += 16)
        {
            DecryptBlock(data, i, output, i);
            for (var j = 0; j < 16; j++)
                output[i + j] ^= prev[j];
            prev = data[i..(i + 16)];
        }

        return pkcs7Padding ? UnpadPkcs7(output) : output;
    }

    /// <summary>Encrypt a single 16-byte block (ECB).</summary>
    public byte[] EncryptEcb(byte[] data)
    {
        if (data.Length % 16 != 0) throw new ArgumentException("Data must be 16-byte aligned for ECB");
        var output = new byte[data.Length];
        for (var i = 0; i < data.Length; i += 16)
            EncryptBlock(data, i, output, i);
        return output;
    }

    // ── Block operations ───────────────────────────────────────────

    private void EncryptBlock(byte[] input, int inOff, byte[] output, int outOff)
    {
        // Initial round key addition
        var s0 = Pack(input, inOff) ^ _ek[0, 0];
        var s1 = Pack(input, inOff + 4) ^ _ek[0, 1];
        var s2 = Pack(input, inOff + 8) ^ _ek[0, 2];
        var s3 = Pack(input, inOff + 12) ^ _ek[0, 3];

        // Rounds
        for (var r = 1; r < _rounds; r++)
        {
            var t0 = Te0[s0 >> 24] ^ Te1[(s1 >> 16) & 0xFF] ^ Te2[(s2 >> 8) & 0xFF] ^ Te3[s3 & 0xFF] ^ _ek[r, 0];
            var t1 = Te0[s1 >> 24] ^ Te1[(s2 >> 16) & 0xFF] ^ Te2[(s3 >> 8) & 0xFF] ^ Te3[s0 & 0xFF] ^ _ek[r, 1];
            var t2 = Te0[s2 >> 24] ^ Te1[(s3 >> 16) & 0xFF] ^ Te2[(s0 >> 8) & 0xFF] ^ Te3[s1 & 0xFF] ^ _ek[r, 2];
            var t3 = Te0[s3 >> 24] ^ Te1[(s0 >> 16) & 0xFF] ^ Te2[(s1 >> 8) & 0xFF] ^ Te3[s2 & 0xFF] ^ _ek[r, 3];
            s0 = t0; s1 = t1; s2 = t2; s3 = t3;
        }

        // Final round (no MixColumns)
        var o0 = (SBox[s0 >> 24] << 24) | (SBox[(s1 >> 16) & 0xFF] << 16) | (SBox[(s2 >> 8) & 0xFF] << 8) | SBox[s3 & 0xFF];
        var o1 = (SBox[s1 >> 24] << 24) | (SBox[(s2 >> 16) & 0xFF] << 16) | (SBox[(s3 >> 8) & 0xFF] << 8) | SBox[s0 & 0xFF];
        var o2 = (SBox[s2 >> 24] << 24) | (SBox[(s3 >> 16) & 0xFF] << 16) | (SBox[(s0 >> 8) & 0xFF] << 8) | SBox[s1 & 0xFF];
        var o3 = (SBox[s3 >> 24] << 24) | (SBox[(s0 >> 16) & 0xFF] << 16) | (SBox[(s1 >> 8) & 0xFF] << 8) | SBox[s2 & 0xFF];

        Unpack((uint)(o0 ^ (int)_ek[_rounds, 0]), output, outOff);
        Unpack((uint)(o1 ^ (int)_ek[_rounds, 1]), output, outOff + 4);
        Unpack((uint)(o2 ^ (int)_ek[_rounds, 2]), output, outOff + 8);
        Unpack((uint)(o3 ^ (int)_ek[_rounds, 3]), output, outOff + 12);
    }

    private void DecryptBlock(byte[] input, int inOff, byte[] output, int outOff)
    {
        var s0 = Pack(input, inOff) ^ _dk[0, 0];
        var s1 = Pack(input, inOff + 4) ^ _dk[0, 1];
        var s2 = Pack(input, inOff + 8) ^ _dk[0, 2];
        var s3 = Pack(input, inOff + 12) ^ _dk[0, 3];

        for (var r = 1; r < _rounds; r++)
        {
            var t0 = Td0[s0 >> 24] ^ Td1[(s3 >> 16) & 0xFF] ^ Td2[(s2 >> 8) & 0xFF] ^ Td3[s1 & 0xFF] ^ _dk[r, 0];
            var t1 = Td0[s1 >> 24] ^ Td1[(s0 >> 16) & 0xFF] ^ Td2[(s3 >> 8) & 0xFF] ^ Td3[s2 & 0xFF] ^ _dk[r, 1];
            var t2 = Td0[s2 >> 24] ^ Td1[(s1 >> 16) & 0xFF] ^ Td2[(s0 >> 8) & 0xFF] ^ Td3[s3 & 0xFF] ^ _dk[r, 2];
            var t3 = Td0[s3 >> 24] ^ Td1[(s2 >> 16) & 0xFF] ^ Td2[(s1 >> 8) & 0xFF] ^ Td3[s0 & 0xFF] ^ _dk[r, 3];
            s0 = t0; s1 = t1; s2 = t2; s3 = t3;
        }

        // Final round
        var o0 = (ISBox[s0 >> 24] << 24) | (ISBox[(s3 >> 16) & 0xFF] << 16) | (ISBox[(s2 >> 8) & 0xFF] << 8) | ISBox[s1 & 0xFF];
        var o1 = (ISBox[s1 >> 24] << 24) | (ISBox[(s0 >> 16) & 0xFF] << 16) | (ISBox[(s3 >> 8) & 0xFF] << 8) | ISBox[s2 & 0xFF];
        var o2 = (ISBox[s2 >> 24] << 24) | (ISBox[(s1 >> 16) & 0xFF] << 16) | (ISBox[(s0 >> 8) & 0xFF] << 8) | ISBox[s3 & 0xFF];
        var o3 = (ISBox[s3 >> 24] << 24) | (ISBox[(s2 >> 16) & 0xFF] << 16) | (ISBox[(s1 >> 8) & 0xFF] << 8) | ISBox[s0 & 0xFF];

        Unpack((uint)(o0 ^ (int)_dk[_rounds, 0]), output, outOff);
        Unpack((uint)(o1 ^ (int)_dk[_rounds, 1]), output, outOff + 4);
        Unpack((uint)(o2 ^ (int)_dk[_rounds, 2]), output, outOff + 8);
        Unpack((uint)(o3 ^ (int)_dk[_rounds, 3]), output, outOff + 12);
    }

    // ── Key expansion ──────────────────────────────────────────────

    private static uint[,] ExpandKey(byte[] key, int rounds)
    {
        var nk = key.Length / 4;
        var ek = new uint[rounds + 1, 4];
        var w = new uint[(rounds + 1) * 4];

        for (var i = 0; i < nk; i++)
            w[i] = Pack(key, i * 4);

        for (var i = nk; i < w.Length; i++)
        {
            var t = w[i - 1];
            if (i % nk == 0)
                t = SubWord(RotWord(t)) ^ Rcon[i / nk - 1];
            else if (nk > 6 && i % nk == 4)
                t = SubWord(t);
            w[i] = w[i - nk] ^ t;
        }

        for (var r = 0; r <= rounds; r++)
            for (var c = 0; c < 4; c++)
                ek[r, c] = w[r * 4 + c];

        return ek;
    }

    private static uint[,] InvertKey(uint[,] ek, int rounds)
    {
        var dk = new uint[rounds + 1, 4];
        // First and last rounds stay the same
        for (var c = 0; c < 4; c++)
        {
            dk[0, c] = ek[rounds, c];
            dk[rounds, c] = ek[0, c];
        }
        // Middle rounds: apply InvMixColumns
        for (var r = 1; r < rounds; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                var v = ek[rounds - r, c];
                dk[r, c] = Td0[SBox[v >> 24]] ^ Td1[SBox[(v >> 16) & 0xFF]] ^
                            Td2[SBox[(v >> 8) & 0xFF]] ^ Td3[SBox[v & 0xFF]];
            }
        }
        return dk;
    }

    // ── Padding ────────────────────────────────────────────────────

    private static byte[] PadPkcs7(byte[] data)
    {
        var padLen = 16 - (data.Length % 16);
        var result = new byte[data.Length + padLen];
        data.CopyTo(result, 0);
        for (var i = data.Length; i < result.Length; i++)
            result[i] = (byte)padLen;
        return result;
    }

    private static byte[] UnpadPkcs7(byte[] data)
    {
        if (data.Length == 0) return data;
        var padLen = data[^1];
        if (padLen < 1 || padLen > 16) return data;
        // Validate all padding bytes
        for (var i = data.Length - padLen; i < data.Length; i++)
            if (data[i] != padLen) return data;
        return data[..^padLen];
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static uint Pack(byte[] b, int i)
        => ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3];

    private static void Unpack(uint v, byte[] b, int i)
    {
        b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16);
        b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v;
    }

    private static uint SubWord(uint w)
        => ((uint)SBox[w >> 24] << 24) | ((uint)SBox[(w >> 16) & 0xFF] << 16) |
           ((uint)SBox[(w >> 8) & 0xFF] << 8) | SBox[w & 0xFF];

    private static uint RotWord(uint w) => (w << 8) | (w >> 24);

    // ── AES Tables (pre-computed) ──────────────────────────────────

    private static readonly byte[] SBox = GenerateSBox();
    private static readonly byte[] ISBox = GenerateISBox();
    private static readonly uint[] Te0, Te1, Te2, Te3;
    private static readonly uint[] Td0, Td1, Td2, Td3;

    private static readonly uint[] Rcon =
    [
        0x01000000, 0x02000000, 0x04000000, 0x08000000,
        0x10000000, 0x20000000, 0x40000000, 0x80000000,
        0x1B000000, 0x36000000,
    ];

    static AesCipher()
    {
        Te0 = new uint[256];
        Te1 = new uint[256];
        Te2 = new uint[256];
        Te3 = new uint[256];
        Td0 = new uint[256];
        Td1 = new uint[256];
        Td2 = new uint[256];
        Td3 = new uint[256];

        for (var i = 0; i < 256; i++)
        {
            var s = SBox[i];
            var x2 = Xtime(s);
            var x3 = (byte)(x2 ^ s);

            Te0[i] = ((uint)x2 << 24) | ((uint)s << 16) | ((uint)s << 8) | x3;
            Te1[i] = ((uint)x3 << 24) | ((uint)x2 << 16) | ((uint)s << 8) | s;
            Te2[i] = ((uint)s << 24) | ((uint)x3 << 16) | ((uint)x2 << 8) | s;
            Te3[i] = ((uint)s << 24) | ((uint)s << 16) | ((uint)x3 << 8) | x2;

            var si = ISBox[i];
            var d9 = Mul(si, 9);
            var db = Mul(si, 0x0B);
            var dd = Mul(si, 0x0D);
            var de = Mul(si, 0x0E);

            Td0[i] = ((uint)de << 24) | ((uint)d9 << 16) | ((uint)dd << 8) | db;
            Td1[i] = ((uint)db << 24) | ((uint)de << 16) | ((uint)d9 << 8) | dd;
            Td2[i] = ((uint)dd << 24) | ((uint)db << 16) | ((uint)de << 8) | d9;
            Td3[i] = ((uint)d9 << 24) | ((uint)dd << 16) | ((uint)db << 8) | de;
        }
    }

    private static byte[] GenerateSBox()
    {
        var sbox = new byte[256];
        // Compute multiplicative inverse in GF(2^8), then affine transform
        var inv = new byte[256];
        inv[0] = 0;
        inv[1] = 1;

        // Build log/alog tables for GF(2^8) with generator 3
        var log = new int[256];
        var alog = new int[256];
        var g = 1;
        for (var i = 0; i < 255; i++)
        {
            alog[i] = g;
            log[g] = i;
            g ^= Xtime((byte)g);
        }

        for (var i = 1; i < 256; i++)
            inv[i] = (byte)alog[(255 - log[i]) % 255];

        for (var i = 0; i < 256; i++)
        {
            var b = inv[i];
            // Affine transformation
            sbox[i] = (byte)(b ^ RotL8(b, 1) ^ RotL8(b, 2) ^ RotL8(b, 3) ^ RotL8(b, 4) ^ 0x63);
        }

        return sbox;
    }

    private static byte[] GenerateISBox()
    {
        var isbox = new byte[256];
        for (var i = 0; i < 256; i++)
            isbox[SBox[i]] = (byte)i;
        return isbox;
    }

    private static byte Xtime(byte b) => (byte)((b << 1) ^ ((b >> 7) * 0x1B));

    private static byte Mul(byte a, int b)
    {
        byte result = 0;
        var x = a;
        for (var i = 0; i < 8 && b > 0; i++)
        {
            if ((b & 1) != 0) result ^= x;
            x = Xtime(x);
            b >>= 1;
        }
        return result;
    }

    private static byte RotL8(byte b, int n) => (byte)((b << n) | (b >> (8 - n)));
}
