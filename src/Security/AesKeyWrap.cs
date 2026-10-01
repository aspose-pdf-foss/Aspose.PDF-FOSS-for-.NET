namespace Aspose.Pdf.Security;

/// <summary>
/// The AES key wrap of RFC 3394: a key of whole 64-bit blocks wrapped under a key-encryption key,
/// with the default initial value A6A6A6A6A6A6A6A6 checked on unwrapping.
/// </summary>
internal static class AesKeyWrap
{
    private const int Steps = 6;
    private static readonly byte[] DefaultIv = [0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6];

    /// <summary>The key wrapped: eight bytes longer than the key.</summary>
    public static byte[] Wrap(byte[] kek, byte[] key)
    {
        if (key.Length < 16 || key.Length % 8 != 0) throw new ArgumentException($"A wrapped key is whole 64-bit blocks, at least two: {key.Length} bytes");
        var aes = new AesCipher(kek);
        var n = key.Length / 8;
        var a = (byte[])DefaultIv.Clone();
        var r = (byte[])key.Clone();
        var block = new byte[16];
        for (var j = 0; j < Steps; j++)
            for (var i = 1; i <= n; i++)
            {
                a.CopyTo(block, 0);
                Array.Copy(r, (i - 1) * 8, block, 8, 8);
                aes.EncryptBlock(block, 0, block, 0);
                XorCounter(block, (long)n * j + i);
                Array.Copy(block, 0, a, 0, 8);
                Array.Copy(block, 8, r, (i - 1) * 8, 8);
            }
        return CryptoHelper.ConcatBytes(a, r);
    }

    /// <summary>The key unwrapped; a wrapping whose check value does not come out is refused.</summary>
    public static byte[] Unwrap(byte[] kek, byte[] wrapped)
    {
        if (wrapped.Length < 24 || wrapped.Length % 8 != 0) throw new System.Security.Cryptography.CryptographicException("Wrapped key length is not whole 64-bit blocks");
        var aes = new AesCipher(kek);
        var n = wrapped.Length / 8 - 1;
        var a = wrapped[..8];
        var r = wrapped[8..];
        var block = new byte[16];
        for (var j = Steps - 1; j >= 0; j--)
            for (var i = n; i >= 1; i--)
            {
                a.CopyTo(block, 0);
                XorCounter(block, (long)n * j + i);
                Array.Copy(r, (i - 1) * 8, block, 8, 8);
                aes.DecryptBlock(block, 0, block, 0);
                Array.Copy(block, 0, a, 0, 8);
                Array.Copy(block, 8, r, (i - 1) * 8, 8);
            }
        var difference = 0;
        for (var i = 0; i < 8; i++) difference |= a[i] ^ DefaultIv[i];
        if (difference != 0) throw new System.Security.Cryptography.CryptographicException("Key unwrap integrity check failed");
        return r;
    }

    // The step counter, big-endian, XORed into the first eight bytes.
    private static void XorCounter(byte[] block, long t)
    {
        for (var k = 7; k >= 0; k--)
        {
            block[k] ^= (byte)t;
            t >>= 8;
        }
    }
}
