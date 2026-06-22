namespace Aspose.Pdf.Security;

/// <summary>
/// Pure C# MD5 implementation (RFC 1321).
/// Replaces System.Security.Cryptography.MD5 dependency.
/// </summary>
internal sealed class Md5Digest
{
    private uint _a = 0x67452301;
    private uint _b = 0xEFCDAB89;
    private uint _c = 0x98BADCFE;
    private uint _d = 0x10325476;
    private readonly byte[] _buffer = new byte[64];
    private int _bufferLen;
    private long _totalLen;

    public void Update(byte[] data, int offset, int count)
    {
        _totalLen += count;
        var i = 0;

        // Fill buffer if partially full
        if (_bufferLen > 0)
        {
            var fill = Math.Min(64 - _bufferLen, count);
            Array.Copy(data, offset, _buffer, _bufferLen, fill);
            _bufferLen += fill;
            i += fill;
            if (_bufferLen == 64)
            {
                ProcessBlock(_buffer, 0);
                _bufferLen = 0;
            }
        }

        // Process full blocks
        while (i + 64 <= count)
        {
            ProcessBlock(data, offset + i);
            i += 64;
        }

        // Store remaining
        if (i < count)
        {
            Array.Copy(data, offset + i, _buffer, 0, count - i);
            _bufferLen = count - i;
        }
    }

    public byte[] Finish()
    {
        var bitLen = _totalLen * 8;

        // Append 0x80
        _buffer[_bufferLen++] = 0x80;

        // If buffer > 56 bytes, pad to 64, process, then pad again
        if (_bufferLen > 56)
        {
            Array.Clear(_buffer, _bufferLen, 64 - _bufferLen);
            ProcessBlock(_buffer, 0);
            _bufferLen = 0;
        }

        Array.Clear(_buffer, _bufferLen, 56 - _bufferLen);

        // Append length in bits (little-endian)
        _buffer[56] = (byte)bitLen;
        _buffer[57] = (byte)(bitLen >> 8);
        _buffer[58] = (byte)(bitLen >> 16);
        _buffer[59] = (byte)(bitLen >> 24);
        _buffer[60] = (byte)(bitLen >> 32);
        _buffer[61] = (byte)(bitLen >> 40);
        _buffer[62] = (byte)(bitLen >> 48);
        _buffer[63] = (byte)(bitLen >> 56);
        ProcessBlock(_buffer, 0);

        var result = new byte[16];
        WriteLE(result, 0, _a);
        WriteLE(result, 4, _b);
        WriteLE(result, 8, _c);
        WriteLE(result, 12, _d);
        return result;
    }

    /// <summary>One-shot hash.</summary>
    public static byte[] Hash(byte[] data)
    {
        var md5 = new Md5Digest();
        md5.Update(data, 0, data.Length);
        return md5.Finish();
    }

    /// <summary>One-shot hash of a slice.</summary>
    public static byte[] Hash(byte[] data, int offset, int count)
    {
        var md5 = new Md5Digest();
        md5.Update(data, offset, count);
        return md5.Finish();
    }

    private void ProcessBlock(byte[] data, int offset)
    {
        var a = _a;
        var b = _b;
        var c = _c;
        var d = _d;

        // Decode 16 little-endian uint32s
        Span<uint> m = stackalloc uint[16];
        for (var i = 0; i < 16; i++)
            m[i] = ReadLE(data, offset + i * 4);

        // Round 1
        a = FF(a, b, c, d, m[0],  7,  0xD76AA478);
        d = FF(d, a, b, c, m[1],  12, 0xE8C7B756);
        c = FF(c, d, a, b, m[2],  17, 0x242070DB);
        b = FF(b, c, d, a, m[3],  22, 0xC1BDCEEE);
        a = FF(a, b, c, d, m[4],  7,  0xF57C0FAF);
        d = FF(d, a, b, c, m[5],  12, 0x4787C62A);
        c = FF(c, d, a, b, m[6],  17, 0xA8304613);
        b = FF(b, c, d, a, m[7],  22, 0xFD469501);
        a = FF(a, b, c, d, m[8],  7,  0x698098D8);
        d = FF(d, a, b, c, m[9],  12, 0x8B44F7AF);
        c = FF(c, d, a, b, m[10], 17, 0xFFFF5BB1);
        b = FF(b, c, d, a, m[11], 22, 0x895CD7BE);
        a = FF(a, b, c, d, m[12], 7,  0x6B901122);
        d = FF(d, a, b, c, m[13], 12, 0xFD987193);
        c = FF(c, d, a, b, m[14], 17, 0xA679438E);
        b = FF(b, c, d, a, m[15], 22, 0x49B40821);

        // Round 2
        a = GG(a, b, c, d, m[1],  5,  0xF61E2562);
        d = GG(d, a, b, c, m[6],  9,  0xC040B340);
        c = GG(c, d, a, b, m[11], 14, 0x265E5A51);
        b = GG(b, c, d, a, m[0],  20, 0xE9B6C7AA);
        a = GG(a, b, c, d, m[5],  5,  0xD62F105D);
        d = GG(d, a, b, c, m[10], 9,  0x02441453);
        c = GG(c, d, a, b, m[15], 14, 0xD8A1E681);
        b = GG(b, c, d, a, m[4],  20, 0xE7D3FBC8);
        a = GG(a, b, c, d, m[9],  5,  0x21E1CDE6);
        d = GG(d, a, b, c, m[14], 9,  0xC33707D6);
        c = GG(c, d, a, b, m[3],  14, 0xF4D50D87);
        b = GG(b, c, d, a, m[8],  20, 0x455A14ED);
        a = GG(a, b, c, d, m[13], 5,  0xA9E3E905);
        d = GG(d, a, b, c, m[2],  9,  0xFCEFA3F8);
        c = GG(c, d, a, b, m[7],  14, 0x676F02D9);
        b = GG(b, c, d, a, m[12], 20, 0x8D2A4C8A);

        // Round 3
        a = HH(a, b, c, d, m[5],  4,  0xFFFA3942);
        d = HH(d, a, b, c, m[8],  11, 0x8771F681);
        c = HH(c, d, a, b, m[11], 16, 0x6D9D6122);
        b = HH(b, c, d, a, m[14], 23, 0xFDE5380C);
        a = HH(a, b, c, d, m[1],  4,  0xA4BEEA44);
        d = HH(d, a, b, c, m[4],  11, 0x4BDECFA9);
        c = HH(c, d, a, b, m[7],  16, 0xF6BB4B60);
        b = HH(b, c, d, a, m[10], 23, 0xBEBFBC70);
        a = HH(a, b, c, d, m[13], 4,  0x289B7EC6);
        d = HH(d, a, b, c, m[0],  11, 0xEAA127FA);
        c = HH(c, d, a, b, m[3],  16, 0xD4EF3085);
        b = HH(b, c, d, a, m[6],  23, 0x04881D05);
        a = HH(a, b, c, d, m[9],  4,  0xD9D4D039);
        d = HH(d, a, b, c, m[12], 11, 0xE6DB99E5);
        c = HH(c, d, a, b, m[15], 16, 0x1FA27CF8);
        b = HH(b, c, d, a, m[2],  23, 0xC4AC5665);

        // Round 4
        a = II(a, b, c, d, m[0],  6,  0xF4292244);
        d = II(d, a, b, c, m[7],  10, 0x432AFF97);
        c = II(c, d, a, b, m[14], 15, 0xAB9423A7);
        b = II(b, c, d, a, m[5],  21, 0xFC93A039);
        a = II(a, b, c, d, m[12], 6,  0x655B59C3);
        d = II(d, a, b, c, m[3],  10, 0x8F0CCC92);
        c = II(c, d, a, b, m[10], 15, 0xFFEFF47D);
        b = II(b, c, d, a, m[1],  21, 0x85845DD1);
        a = II(a, b, c, d, m[8],  6,  0x6FA87E4F);
        d = II(d, a, b, c, m[15], 10, 0xFE2CE6E0);
        c = II(c, d, a, b, m[6],  15, 0xA3014314);
        b = II(b, c, d, a, m[13], 21, 0x4E0811A1);
        a = II(a, b, c, d, m[4],  6,  0xF7537E82);
        d = II(d, a, b, c, m[11], 10, 0xBD3AF235);
        c = II(c, d, a, b, m[2],  15, 0x2AD7D2BB);
        b = II(b, c, d, a, m[9],  21, 0xEB86D391);

        _a += a;
        _b += b;
        _c += c;
        _d += d;
    }

    private static uint FF(uint a, uint b, uint c, uint d, uint x, int s, uint t)
        => b + RotL(a + ((b & c) | (~b & d)) + x + t, s);

    private static uint GG(uint a, uint b, uint c, uint d, uint x, int s, uint t)
        => b + RotL(a + ((b & d) | (c & ~d)) + x + t, s);

    private static uint HH(uint a, uint b, uint c, uint d, uint x, int s, uint t)
        => b + RotL(a + (b ^ c ^ d) + x + t, s);

    private static uint II(uint a, uint b, uint c, uint d, uint x, int s, uint t)
        => b + RotL(a + (c ^ (b | ~d)) + x + t, s);

    private static uint RotL(uint v, int n) => (v << n) | (v >> (32 - n));

    private static uint ReadLE(byte[] b, int i)
        => (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));

    private static void WriteLE(byte[] b, int i, uint v)
    {
        b[i] = (byte)v;
        b[i + 1] = (byte)(v >> 8);
        b[i + 2] = (byte)(v >> 16);
        b[i + 3] = (byte)(v >> 24);
    }
}
