using System.Text;
using Aspose.Pdf.Security;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

/// <summary>
/// The digests, AES-GCM, AES key wrap and HKDF against their published test vectors, and the
/// standard and public-key key derivations the encryption handlers build on.
/// </summary>
public class CryptoPrimitivesTests
{
    private static byte[] Hex(string hex) => Compat.FromHexString(hex.Replace(" ", ""));
    private static string Hex(byte[] bytes) => Compat.ToHexString(bytes).ToLowerInvariant();

    [Theory]
    [InlineData("", "d14a028c2a3a2bc9476102bb288234c415a2b01f828ea62ac5b3e42f")]
    [InlineData("abc", "23097d223405d8228642a477bda255b32aadbce4bda0b3f7e36c9da7")]
    public void Sha224_KnownVectors(string input, string expected)
    {
        var data = Encoding.ASCII.GetBytes(input);
        Assert.Equal(expected, Hex(ShaDigest.Sha224(data, 0, data.Length)));
    }

    [Theory]
    [InlineData("", "9c1185a5c5e9fc54612808977ee8f548b2258d31")]
    [InlineData("abc", "8eb208f7e05d987a9b044a8e98c6b087f15a0bfc")]
    [InlineData("The quick brown fox jumps over the lazy dog", "37f332f68db77bd9d7edd4969571ad671cf9dd3b")]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345678901234567890", "9b752e45573d4b39f4dbd3323cab82bf63326bfb")]
    public void Ripemd160_KnownVectors(string input, string expected) =>
        Assert.Equal(expected, Hex(Ripemd160Digest.Hash(Encoding.ASCII.GetBytes(input))));

    [Theory]
    // NIST GCM specification test cases 1, 2, 3 (AES-128) and 13, 14 (AES-256)
    [InlineData("00000000000000000000000000000000", "000000000000000000000000", "", "", "58e2fccefa7e3061367f1d57a4e7455a")]
    [InlineData("00000000000000000000000000000000", "000000000000000000000000", "00000000000000000000000000000000",
        "0388dace60b6a392f328c2b971b2fe78", "ab6e47d42cec13bdf53a67b21257bddf")]
    [InlineData("feffe9928665731c6d6a8f9467308308", "cafebabefacedbaddecaf888",
        "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b391aafd255",
        "42831ec2217774244b7221b784d0d49ce3aa212f2c02a4e035c17e2329aca12e21d514b25466931c7d8f6a5aac84aa051ba30b396a0aac973d58e091473f5985",
        "4d5c2af327cd64a62cf35abd2ba6fab4")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", "000000000000000000000000", "", "", "530f8afbc74536b9a963b4f1c4cb738b")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", "000000000000000000000000", "00000000000000000000000000000000",
        "cea7403d4d606b6e074ec5d3baf39d18", "d0d1c8a799996bf0265b98b5d48ab919")]
    public void AesGcm_NistVectors(string key, string nonce, string plain, string cipher, string tag)
    {
        var sealedData = AesGcmCipher.Seal(Hex(key), Hex(nonce), Hex(plain));
        Assert.Equal(cipher + tag, Hex(sealedData));
        Assert.Equal(plain, Hex(AesGcmCipher.Open(Hex(key), Hex(nonce), sealedData, 0, sealedData.Length)));
    }

    [Fact]
    public void AesGcm_InPieces_MatchesWhole()
    {
        var key = Hex("feffe9928665731c6d6a8f9467308308");
        var nonce = Hex("cafebabefacedbaddecaf888");
        var plain = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog, twice over the lazy dog");
        var cipher = new AesGcmCipher(key, nonce, true);
        var first = cipher.Process(plain, 0, 32);
        var rest = cipher.Process(plain, 32, plain.Length - 32);
        Assert.Equal(Hex(AesGcmCipher.Seal(key, nonce, plain)), Hex(first) + Hex(rest) + Hex(cipher.Tag()));
    }

    [Fact]
    public void AesGcm_TamperedTag_Refused()
    {
        var key = new byte[16];
        var nonce = new byte[12];
        var sealedData = AesGcmCipher.Seal(key, nonce, Encoding.ASCII.GetBytes("payload"));
        sealedData[^1] ^= 1;
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => AesGcmCipher.Open(key, nonce, sealedData, 0, sealedData.Length));
        Assert.Equal(Hex(sealedData), Hex(AesGcmCipher.DecryptObject(key, CryptoHelper.ConcatBytes(nonce, sealedData))[12..]));
    }

    [Fact]
    public void AesGcm_ObjectRoundTrip()
    {
        var key = Hex("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
        var data = Encoding.ASCII.GetBytes("an object's bytes");
        var encrypted = AesGcmCipher.EncryptObject(key, data);
        Assert.Equal(AesGcmCipher.NonceLength + data.Length + AesGcmCipher.TagLength, encrypted.Length);
        Assert.Equal(data, AesGcmCipher.DecryptObject(key, encrypted));
    }

    [Theory]
    // RFC 3394 sections 4.1 and 4.6
    [InlineData("000102030405060708090A0B0C0D0E0F", "00112233445566778899AABBCCDDEEFF", "1FA68B0A8112B447AEF34BD8FB5A7B829D3E862371D2CFE5")]
    [InlineData("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F",
        "00112233445566778899AABBCCDDEEFF000102030405060708090A0B0C0D0E0F",
        "28C9F404C4B810F4CBCCB35CFB87F8263F5786E2D80ED326CBC7F0E71A99F43BFB988B9B7A02DD21")]
    public void AesKeyWrap_Rfc3394Vectors(string kek, string key, string wrapped)
    {
        Assert.Equal(wrapped.ToLowerInvariant(), Hex(AesKeyWrap.Wrap(Hex(kek), Hex(key))));
        Assert.Equal(key.ToLowerInvariant(), Hex(AesKeyWrap.Unwrap(Hex(kek), Hex(wrapped))));
    }

    [Fact]
    public void AesKeyWrap_WrongKek_Refused()
    {
        var wrapped = AesKeyWrap.Wrap(new byte[32], new byte[32]);
        var otherKek = new byte[32];
        otherKek[0] = 1;
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => AesKeyWrap.Unwrap(otherKek, wrapped));
    }

    [Theory]
    // RFC 5869 test cases 1 and 3
    [InlineData("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", "000102030405060708090a0b0c", "f0f1f2f3f4f5f6f7f8f9",
        "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865")]
    [InlineData("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", "", "",
        "8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8")]
    public void Hkdf_Rfc5869Vectors(string ikm, string salt, string info, string okm) =>
        Assert.Equal(okm, Hex(HmacSha.HkdfSha256(Hex(ikm), Hex(salt), Hex(info), okm.Length / 2)));

    [Theory]
    [InlineData(CryptoAlgorithm.RC4x40, 1, 2, 5)]
    [InlineData(CryptoAlgorithm.RC4x128, 2, 3, 16)]
    [InlineData(CryptoAlgorithm.AESx128, 4, 4, 16)]
    public void CreateStandard_OpensWithBothPasswords(CryptoAlgorithm algorithm, int version, int revision, int keyLength)
    {
        var fileId = Hex("0102030405060708090a0b0c0d0e0f10");
        var encryptor = PdfEncryptor.CreateStandard(algorithm, version, revision, keyLength,
            Encoding.ASCII.GetBytes("user"), Encoding.ASCII.GetBytes("owner"), -44, fileId, true);
        var dict = encryptor.BuildEncryptDict();
        var id = new Core.PdfArray { new Core.PdfString(fileId, isHex: true), new Core.PdfString(fileId, isHex: true) };
        var asUser = PdfDecryptor.TryCreate(dict, id, "user");
        var asOwner = PdfDecryptor.TryCreate(dict, id, "owner");
        Assert.NotNull(asUser);
        Assert.NotNull(asOwner);
        Assert.False(asUser!.IsOwnerAuthentication);
        Assert.True(asOwner!.IsOwnerAuthentication);
        Assert.Equal(encryptor.FileKey, asUser.FileKey);
        Assert.Null(PdfDecryptor.TryCreate(dict, id, "wrong"));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(7, 6)]
    public void CreateStandard_Aes256_OpensWithBothPasswords(int revision, int version)
    {
        var encryptor = PdfEncryptor.CreateStandard(CryptoAlgorithm.AESx256, version, revision, 32,
            Encoding.ASCII.GetBytes("user"), Encoding.ASCII.GetBytes("owner"), -44, System.Array.Empty<byte>(), true);
        Assert.Equal(32, encryptor.FileKey.Length);
        Assert.Equal(48, encryptor.OValue.Length);
        Assert.Equal(48, encryptor.UValue.Length);
        Assert.Equal(32, encryptor.OEValue!.Length);
        Assert.Equal(32, encryptor.UEValue!.Length);
        Assert.Equal(16, encryptor.PermsValue!.Length);
        var dict = encryptor.BuildEncryptDict();
        dict.Set("R", new Core.PdfInteger(revision));
        var asUser = PdfDecryptor.TryCreate(dict, null, "user");
        var asOwner = PdfDecryptor.TryCreate(dict, null, "owner");
        Assert.Equal(encryptor.FileKey, asUser!.FileKey);
        Assert.Equal(encryptor.FileKey, asOwner!.FileKey);
        Assert.Null(PdfDecryptor.TryCreate(dict, null, "wrong"));
    }

    [Fact]
    public void Aes256Gcm_Encryptor_EncryptsObjectsWithGcm()
    {
        var encryptor = PdfEncryptor.CreateStandard(CryptoAlgorithm.AESx256, 6, 7, 32,
            Encoding.ASCII.GetBytes("user"), Encoding.ASCII.GetBytes("owner"), -44, System.Array.Empty<byte>(), true);
        var dict = encryptor.BuildEncryptDict();
        Assert.Equal("AESV4", ((Core.PdfDictionary)((Core.PdfDictionary)dict.Get("CF")!).Get("StdCF")!).GetName("CFM"));
        var data = Encoding.ASCII.GetBytes("gcm string");
        var encrypted = encryptor.EncryptString(data, 3, 0);
        Assert.Equal(AesGcmCipher.NonceLength + data.Length + AesGcmCipher.TagLength, encrypted.Length);
        var decryptor = PdfDecryptor.TryCreate(dict, null, "user");
        Assert.Equal(data, decryptor!.DecryptString(encrypted, 3, 0));
    }

    [Fact]
    public void PublicKeyFileKey_MetadataInTheClear_ChangesTheKey()
    {
        var seed = new byte[20];
        var recipients = new List<byte[]> { new byte[] { 1, 2, 3 }, new byte[] { 4, 5 } };
        var withMetadata = PubSecHandler.ComputeFileKey(seed, recipients, false, 16);
        var clearMetadata = PubSecHandler.ComputeFileKey(seed, recipients, false, 16, encryptMetadata: false);
        Assert.NotEqual(withMetadata, clearMetadata);
        var expected = HmacSha.Sha1Hash(CryptoHelper.ConcatBytes(seed, new byte[] { 1, 2, 3, 4, 5 }, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }))[..16];
        Assert.Equal(expected, clearMetadata);
    }
}
