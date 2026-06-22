namespace Aspose.Pdf.Security;

/// <summary>
/// Shared cryptographic utilities used by both PdfEncryptor and PdfDecryptor.
/// </summary>
internal static class CryptoHelper
{
    /// <summary>
    /// Algorithm 2.B (PDF 2.0 §7.6.3.3.4): Hash algorithm for R6.
    /// Iterative SHA-256/384/512 + AES-128-CBC.
    /// </summary>
    public static byte[] ComputeHashR6(byte[] password, byte[] salt, byte[] userKey)
    {
        var input = ConcatBytes(password, salt, userKey);
        var hash = ShaDigest.Sha256(input);

        var round = 0;

        while (true)
        {
            // Build K1 = password + hash + userKey, repeated 64 times
            var k1Unit = ConcatBytes(password, hash, userKey);
            var k1 = new byte[k1Unit.Length * 64];
            for (var i = 0; i < 64; i++)
                k1Unit.CopyTo(k1, i * k1Unit.Length);

            // Encrypt K1 with AES-128 CBC using hash[0.16] as key, hash[16.32] as IV
            var aes = new AesCipher(hash[..16]);
            var encrypted = aes.EncryptCbc(k1, hash[16..32], pkcs7Padding: false);

            // Determine which SHA to use based on sum of first 16 bytes mod 3
            var remainder = 0;
            for (var i = 0; i < 16; i++)
                remainder += encrypted[i];
            remainder %= 3;

            hash = remainder switch
            {
                0 => ShaDigest.Sha256(encrypted),
                1 => ShaDigest.Sha384(encrypted),
                2 => ShaDigest.Sha512(encrypted),
                _ => hash
            };

            round++;
            // After round 64, check if lastByte <= round - 32
            if (round >= 64 && encrypted[^1] <= round - 32)
                break;
        }

        return hash[..32];
    }

    /// <summary>
    /// Decrypt 32 bytes with AES-256-CBC using a zero IV and no padding.
    /// Used for decrypting /UE and /OE values.
    /// </summary>
    public static byte[] DecryptAes256NoIv(byte[] key, byte[] data)
    {
        var aes = new AesCipher(key);
        return aes.DecryptCbc(data, new byte[16], pkcs7Padding: false);
    }

    /// <summary>
    /// Encrypt data with AES-256-CBC using a zero IV and no padding.
    /// Used for encrypting /UE and /OE values.
    /// </summary>
    public static byte[] EncryptAes256NoIv(byte[] key, byte[] data)
    {
        var aes = new AesCipher(key);
        return aes.EncryptCbc(data, new byte[16], pkcs7Padding: false);
    }

    /// <summary>
    /// Encrypt 16 bytes with AES-256-ECB and no padding.
    /// Used for building the /Perms value.
    /// </summary>
    public static byte[] EncryptAes256Ecb(byte[] key, byte[] data)
    {
        var aes = new AesCipher(key);
        return aes.EncryptEcb(data);
    }

    /// <summary>
    /// Concatenate multiple byte arrays into one.
    /// </summary>
    public static byte[] ConcatBytes(params byte[][] arrays)
    {
        var totalLength = 0;
        foreach (var a in arrays) totalLength += a.Length;
        var result = new byte[totalLength];
        var offset = 0;
        foreach (var a in arrays)
        {
            a.CopyTo(result, offset);
            offset += a.Length;
        }
        return result;
    }
}
