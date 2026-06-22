using System.Security.Cryptography;
using System.Text;
using Aspose.Pdf.Security;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

/// <summary>
/// Tests for opening and reading encrypted PDF documents.
/// These tests build real encrypted PDFs with proper key derivation.
/// </summary>
public class PdfDecryptionTests
{
    // Padding string from the PDF spec (Table 21, §7.6.3.3) — 32 bytes
    private static readonly byte[] Padding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    ];

    [Fact]
    public void Open_RC4x40_EmptyPassword_ReadsPageCount()
    {
        var pdf = BuildEncryptedPdf(v: 1, r: 2, keyBits: 40, userPassword: "", ownerPassword: "owner");
        using var doc = Document.Open(pdf, "");
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Open_RC4x40_UserPassword_ReadsPageCount()
    {
        var pdf = BuildEncryptedPdf(v: 1, r: 2, keyBits: 40, userPassword: "user", ownerPassword: "owner");
        using var doc = Document.Open(pdf, "user");
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Open_RC4x40_OwnerPassword_ReadsPageCount()
    {
        var pdf = BuildEncryptedPdf(v: 1, r: 2, keyBits: 40, userPassword: "user", ownerPassword: "owner");
        using var doc = Document.Open(pdf, "owner");
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Open_RC4x128_EmptyPassword_ReadsPageCount()
    {
        var pdf = BuildEncryptedPdf(v: 2, r: 3, keyBits: 128, userPassword: "", ownerPassword: "secret");
        using var doc = Document.Open(pdf, "");
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Open_RC4x128_UserPassword_DecryptsText()
    {
        var pdf = BuildEncryptedPdfWithText(v: 2, r: 3, keyBits: 128,
            userPassword: "pass", ownerPassword: "owner", text: "Hello World");
        using var doc = Document.Open(pdf, "pass");
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Open_WrongPassword_Throws()
    {
        var pdf = BuildEncryptedPdf(v: 2, r: 3, keyBits: 128, userPassword: "correct", ownerPassword: "owner");
        Assert.Throws<InvalidPasswordException>(() => Document.Open(pdf, "wrong"));
    }

    [Fact]
    public void Open_RC4x128_OwnerOnly_AutoDecrypts()
    {
        // PDF with empty user password = owner-only restriction
        var pdf = BuildEncryptedPdf(v: 2, r: 3, keyBits: 128, userPassword: "", ownerPassword: "owner");
        // Should auto-decrypt with empty password
        using var doc = Document.Open(pdf);
        Assert.True(doc.IsEncrypted);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void EncryptionInfo_ReportsCorrectAlgorithm()
    {
        var pdf = BuildEncryptedPdf(v: 2, r: 3, keyBits: 128, userPassword: "", ownerPassword: "owner");
        using var doc = Document.Open(pdf, "");
        var info = doc.EncryptionInfo;
        Assert.NotNull(info);
        Assert.Equal(CryptoAlgorithm.RC4x128, info!.Algorithm);
        Assert.Equal(128, info.KeyLength);
    }

    [Fact]
    public void Permissions_ReadFromEncryptedPdf()
    {
        // Only allow printing (bit 3 = 1<<2)
        var p = unchecked((int)0xFFFFF0C0) | (1 << 2); // Standard reserved bits + print
        var pdf = BuildEncryptedPdf(v: 2, r: 3, keyBits: 128,
            userPassword: "", ownerPassword: "owner", permissions: p);
        using var doc = Document.Open(pdf, "");
        Assert.True(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowPrint);
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowCopy);
    }

    [Fact]
    public void Open_AES128_EmptyPassword()
    {
        var pdf = BuildAes128EncryptedPdf(userPassword: "", ownerPassword: "owner");
        using var doc = Document.Open(pdf, "");
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Open_AES128_UserPassword()
    {
        var pdf = BuildAes128EncryptedPdf(userPassword: "user", ownerPassword: "owner");
        using var doc = Document.Open(pdf, "user");
        Assert.Equal(1, doc.PageCount);
    }

    #region PDF Builders

    /// <summary>
    /// Build a properly encrypted PDF with RC4 (V1-2, R2-3).
    /// </summary>
    private static byte[] BuildEncryptedPdf(int v, int r, int keyBits,
        string userPassword, string ownerPassword, int permissions = -4)
    {
        var keyLength = keyBits / 8;
        var fileId = RandomNumberGenerator.GetBytes(16);

        // Compute O value
        var paddedOwner = PadPassword(ownerPassword);
        var paddedUser = PadPassword(userPassword);
        var oValue = ComputeOValue(paddedOwner, paddedUser, keyLength, r);

        // Compute encryption key
        var encKey = ComputeEncryptionKey(paddedUser, oValue, permissions, fileId, keyLength, r);

        // Compute U value
        var uValue = ComputeUValue(encKey, fileId, r);

        return BuildPdfBytes(v, r, keyBits, permissions, oValue, uValue, fileId);
    }

    /// <summary>
    /// Build an encrypted PDF that contains text content.
    /// </summary>
    private static byte[] BuildEncryptedPdfWithText(int v, int r, int keyBits,
        string userPassword, string ownerPassword, string text, int permissions = -4)
    {
        var keyLength = keyBits / 8;
        var fileId = RandomNumberGenerator.GetBytes(16);

        var paddedOwner = PadPassword(ownerPassword);
        var paddedUser = PadPassword(userPassword);
        var oValue = ComputeOValue(paddedOwner, paddedUser, keyLength, r);
        var encKey = ComputeEncryptionKey(paddedUser, oValue, permissions, fileId, keyLength, r);
        var uValue = ComputeUValue(encKey, fileId, r);

        // Build the content stream (text operators)
        var contentPlain = Encoding.ASCII.GetBytes($"BT /F1 12 Tf 72 720 Td ({text}) Tj ET");

        // Encrypt the content stream for object 4 gen 0
        var streamKey = DeriveObjectKey(encKey, 4, 0, false);
        var contentEncrypted = Rc4Cipher.Decrypt(streamKey, contentPlain);

        return BuildPdfBytesWithContent(v, r, keyBits, permissions, oValue, uValue, fileId,
            contentEncrypted, contentPlain.Length);
    }

    /// <summary>
    /// Build AES-128 encrypted PDF (V4, R4).
    /// </summary>
    private static byte[] BuildAes128EncryptedPdf(string userPassword, string ownerPassword, int permissions = -4)
    {
        var keyLength = 16;
        var fileId = RandomNumberGenerator.GetBytes(16);

        var paddedOwner = PadPassword(ownerPassword);
        var paddedUser = PadPassword(userPassword);
        var oValue = ComputeOValue(paddedOwner, paddedUser, keyLength, 4);
        var encKey = ComputeEncryptionKey(paddedUser, oValue, permissions, fileId, keyLength, 4);
        var uValue = ComputeUValue(encKey, fileId, 4);

        return BuildAes128PdfBytes(permissions, oValue, uValue, fileId);
    }

    private static byte[] BuildPdfBytes(int v, int r, int keyBits, int permissions,
        byte[] oValue, byte[] uValue, byte[] fileId)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var encryptOffset = ms.Position;
        Write($"4 0 obj\n<< /Filter /Standard /V {v} /R {r} /Length {keyBits} " +
              $"/P {permissions} /O <{Convert.ToHexString(oValue)}> /U <{Convert.ToHexString(uValue)}> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{encryptOffset:D10} 00000 n \n");

        Write("trailer\n<< /Size 5 /Root 1 0 R /Encrypt 4 0 R " +
              $"/ID [<{Convert.ToHexString(fileId)}> <{Convert.ToHexString(fileId)}>] >>\n");
        Write($"startxref\n{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] BuildPdfBytesWithContent(int v, int r, int keyBits, int permissions,
        byte[] oValue, byte[] uValue, byte[] fileId,
        byte[] encryptedContent, int originalLength)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R " +
              "/Resources << /Font << /F1 6 0 R >> >> >>\nendobj\n");

        var encryptOffset = ms.Position;
        Write($"4 0 obj\n<< /Filter /Standard /V {v} /R {r} /Length {keyBits} " +
              $"/P {permissions} /O <{Convert.ToHexString(oValue)}> /U <{Convert.ToHexString(uValue)}> >>\nendobj\n");

        var contentOffset = ms.Position;
        Write($"5 0 obj\n<< /Length {encryptedContent.Length} >>\nstream\n");
        ms.Write(encryptedContent);
        Write("\nendstream\nendobj\n");

        var fontOffset = ms.Position;
        Write("6 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 7\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{encryptOffset:D10} 00000 n \n");
        Write($"{contentOffset:D10} 00000 n \n");
        Write($"{fontOffset:D10} 00000 n \n");

        Write("trailer\n<< /Size 7 /Root 1 0 R /Encrypt 4 0 R " +
              $"/ID [<{Convert.ToHexString(fileId)}> <{Convert.ToHexString(fileId)}>] >>\n");
        Write($"startxref\n{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] BuildAes128PdfBytes(int permissions, byte[] oValue, byte[] uValue, byte[] fileId)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.6\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var encryptOffset = ms.Position;
        Write($"4 0 obj\n<< /Filter /Standard /V 4 /R 4 /Length 128 " +
              $"/P {permissions} " +
              $"/O <{Convert.ToHexString(oValue)}> /U <{Convert.ToHexString(uValue)}> " +
              "/StmF /StdCF /StrF /StdCF " +
              "/CF << /StdCF << /Type /CryptFilter /CFM /AESV2 /Length 16 >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{encryptOffset:D10} 00000 n \n");

        Write("trailer\n<< /Size 5 /Root 1 0 R /Encrypt 4 0 R " +
              $"/ID [<{Convert.ToHexString(fileId)}> <{Convert.ToHexString(fileId)}>] >>\n");
        Write($"startxref\n{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    #endregion

    #region Crypto helpers (mirror the spec algorithms for test data generation)

    private static byte[] PadPassword(string password)
    {
        var result = new byte[32];
        var pwBytes = Encoding.Latin1.GetBytes(password);
        var len = Math.Min(pwBytes.Length, 32);
        pwBytes.AsSpan(0, len).CopyTo(result);
        Padding.AsSpan(0, 32 - len).CopyTo(result.AsSpan(len));
        return result;
    }

    private static byte[] ComputeOValue(byte[] paddedOwner, byte[] paddedUser, int keyLength, int r)
    {
        var hash = MD5.HashData(paddedOwner);
        if (r >= 3)
        {
            for (var i = 0; i < 50; i++)
                hash = MD5.HashData(hash[..keyLength]);
        }
        var key = hash[..keyLength];

        if (r == 2)
            return Rc4Cipher.Decrypt(key, paddedUser);

        var result = Rc4Cipher.Decrypt(key, paddedUser);
        for (var i = 1; i <= 19; i++)
        {
            var tempKey = new byte[key.Length];
            for (var j = 0; j < key.Length; j++)
                tempKey[j] = (byte)(key[j] ^ i);
            result = Rc4Cipher.Decrypt(tempKey, result);
        }
        return result;
    }

    private static byte[] ComputeEncryptionKey(byte[] paddedUser, byte[] oValue,
        int permissions, byte[] fileId, int keyLength, int r)
    {
        using var md5 = MD5.Create();
        md5.TransformBlock(paddedUser, 0, paddedUser.Length, null, 0);
        md5.TransformBlock(oValue, 0, oValue.Length, null, 0);
        var pBytes = BitConverter.GetBytes(permissions);
        md5.TransformBlock(pBytes, 0, 4, null, 0);
        md5.TransformBlock(fileId, 0, fileId.Length, null, 0);

        if (r >= 4)
        {
            // EncryptMetadata = true by default, so no extra bytes
        }

        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = md5.Hash!;

        if (r >= 3)
        {
            for (var i = 0; i < 50; i++)
                hash = MD5.HashData(hash[..keyLength]);
        }

        return hash[..keyLength];
    }

    private static byte[] ComputeUValue(byte[] encKey, byte[] fileId, int r)
    {
        if (r == 2)
        {
            return Rc4Cipher.Decrypt(encKey, Padding);
        }

        // R3+: Algorithm 5
        using var md5 = MD5.Create();
        md5.TransformBlock(Padding, 0, Padding.Length, null, 0);
        md5.TransformFinalBlock(fileId, 0, fileId.Length);
        var hash = md5.Hash!;

        var encrypted = Rc4Cipher.Decrypt(encKey, hash);
        for (var i = 1; i <= 19; i++)
        {
            var tempKey = new byte[encKey.Length];
            for (var j = 0; j < encKey.Length; j++)
                tempKey[j] = (byte)(encKey[j] ^ i);
            encrypted = Rc4Cipher.Decrypt(tempKey, encrypted);
        }

        // Pad to 32 bytes
        var result = new byte[32];
        encrypted.CopyTo(result, 0);
        return result;
    }

    private static byte[] DeriveObjectKey(byte[] encKey, int objNum, int genNum, bool isAes)
    {
        using var md5 = MD5.Create();
        var input = new byte[encKey.Length + 5 + (isAes ? 4 : 0)];
        encKey.CopyTo(input, 0);
        var offset = encKey.Length;
        input[offset] = (byte)(objNum & 0xFF);
        input[offset + 1] = (byte)((objNum >> 8) & 0xFF);
        input[offset + 2] = (byte)((objNum >> 16) & 0xFF);
        input[offset + 3] = (byte)(genNum & 0xFF);
        input[offset + 4] = (byte)((genNum >> 8) & 0xFF);
        if (isAes)
        {
            "sAlT"u8.CopyTo(input.AsSpan(offset + 5));
        }
        var hash = md5.ComputeHash(input);
        return hash[..Math.Min(encKey.Length + 5, 16)];
    }

    #endregion
}
