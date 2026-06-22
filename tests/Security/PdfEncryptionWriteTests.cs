using System.Text;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

public class PdfEncryptionWriteTests
{
    [Fact]
    public void Encrypt_RC4x40_ProducesEncryptedPdf()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("user", "owner", algorithm: CryptoAlgorithm.RC4x40);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved, "user");
        Assert.True(doc2.IsEncrypted);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Encrypt_RC4x128_Roundtrip()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("pass", "owner", algorithm: CryptoAlgorithm.RC4x128);

        var saved = doc.ToArray();

        // Should open with correct user password
        using var doc2 = Document.Open(saved, "pass");
        Assert.True(doc2.IsEncrypted);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Encrypt_RC4x128_OwnerPassword()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("user", "owner", algorithm: CryptoAlgorithm.RC4x128);

        var saved = doc.ToArray();

        // Should open with owner password too
        using var doc2 = Document.Open(saved, "owner");
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Encrypt_WrongPassword_Throws()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("user", "owner", algorithm: CryptoAlgorithm.RC4x128);

        var saved = doc.ToArray();
        Assert.Throws<InvalidPasswordException>(() => Document.Open(saved, "wrong"));
    }

    [Fact]
    public void Encrypt_EmptyUserPassword_AutoDecrypts()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("", "owner", algorithm: CryptoAlgorithm.RC4x128);

        var saved = doc.ToArray();

        // Should auto-decrypt with empty password
        using var doc2 = Document.Open(saved);
        Assert.True(doc2.IsEncrypted);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Encrypt_AES128_Roundtrip()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("secret", "admin", algorithm: CryptoAlgorithm.AESx128);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved, "secret");
        Assert.True(doc2.IsEncrypted);
        Assert.Equal(1, doc2.PageCount);
        Assert.Equal(CryptoAlgorithm.AESx128, doc2.EncryptionInfo!.Algorithm);
    }

    [Fact]
    public void Encrypt_WithPermissions_RestrictsCopy()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("", "owner", permissions: DocumentPrivilege.ForbidAll,
            algorithm: CryptoAlgorithm.RC4x128);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc2.Permissions).AllowCopy);
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc2.Permissions).AllowPrint);
    }

    [Fact]
    public void Encrypt_WithPermissions_AllowPrint()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("", "owner", permissions: DocumentPrivilege.AllowAll,
            algorithm: CryptoAlgorithm.RC4x128);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.True(new Aspose.Pdf.Facades.DocumentPrivilege(doc2.Permissions).AllowPrint);
        Assert.True(new Aspose.Pdf.Facades.DocumentPrivilege(doc2.Permissions).AllowCopy);
    }

    [Fact]
    public void Encrypt_EncryptionInfo_ReportsAlgorithm()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("user", "owner", algorithm: CryptoAlgorithm.RC4x40);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved, "user");

        var info = doc2.EncryptionInfo;
        Assert.NotNull(info);
        Assert.Equal(CryptoAlgorithm.RC4x40, info!.Algorithm);
        Assert.Equal(40, info.KeyLength);
    }

    // ── Content verification tests ──────────────────────────────────────────

    [Fact]
    public void Encrypt_RC4x128_ContentIsEncryptedInRawBytes()
    {
        var marker = "PLAINTEXT_MARKER_RC4_TEST";
        var contentBytes = Encoding.ASCII.GetBytes($"BT /F1 12 Tf 100 700 Td ({marker}) Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(contentBytes);

        // Verify plaintext is present before encryption
        Assert.Contains(marker, Encoding.Latin1.GetString(pdf));

        using var doc = Document.Open(pdf);
        doc.Encrypt("user", "owner", algorithm: CryptoAlgorithm.RC4x128);
        var saved = doc.ToArray();

        // Plaintext must NOT appear in the encrypted output
        Assert.DoesNotContain(marker, Encoding.Latin1.GetString(saved));

        // But we can still open and read it
        using var doc2 = Document.Open(saved, "user");
        Assert.True(doc2.IsEncrypted);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Encrypt_AES128_ContentIsEncryptedInRawBytes()
    {
        var marker = "PLAINTEXT_MARKER_AES128_TEST";
        var contentBytes = Encoding.ASCII.GetBytes($"BT /F1 12 Tf 100 700 Td ({marker}) Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(contentBytes);

        Assert.Contains(marker, Encoding.Latin1.GetString(pdf));

        using var doc = Document.Open(pdf);
        doc.Encrypt("user", "owner", algorithm: CryptoAlgorithm.AESx128);
        var saved = doc.ToArray();

        Assert.DoesNotContain(marker, Encoding.Latin1.GetString(saved));

        using var doc2 = Document.Open(saved, "user");
        Assert.True(doc2.IsEncrypted);
        Assert.Equal(1, doc2.PageCount);
    }

    // ── AES-256 tests ───────────────────────────────────────────────────────

    [Fact]
    public void Encrypt_AES256_Roundtrip()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("secret256", "admin256", algorithm: CryptoAlgorithm.AESx256);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved, "secret256");
        Assert.True(doc2.IsEncrypted);
        Assert.Equal(1, doc2.PageCount);
        Assert.Equal(CryptoAlgorithm.AESx256, doc2.EncryptionInfo!.Algorithm);
    }

    [Fact]
    public void Encrypt_AES256_OwnerPassword()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("user256", "owner256", algorithm: CryptoAlgorithm.AESx256);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved, "owner256");
        Assert.True(doc2.IsEncrypted);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Encrypt_AES256_WrongPassword_Throws()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("user256", "owner256", algorithm: CryptoAlgorithm.AESx256);

        var saved = doc.ToArray();
        Assert.Throws<InvalidPasswordException>(() => Document.Open(saved, "wrong"));
    }

    [Fact]
    public void Encrypt_AES256_EmptyUserPassword_AutoDecrypts()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("", "owner256", algorithm: CryptoAlgorithm.AESx256);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.True(doc2.IsEncrypted);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Encrypt_AES256_EncryptionInfo_ReportsAlgorithm()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("user", "owner", algorithm: CryptoAlgorithm.AESx256);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved, "user");

        var info = doc2.EncryptionInfo;
        Assert.NotNull(info);
        Assert.Equal(CryptoAlgorithm.AESx256, info!.Algorithm);
        Assert.Equal(256, info.KeyLength);
        Assert.Equal(5, info.Version);
        Assert.Equal(6, info.Revision);
    }

    [Fact]
    public void Encrypt_AES256_WithPermissions()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("", "owner", permissions: DocumentPrivilege.ForbidAll,
            algorithm: CryptoAlgorithm.AESx256);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc2.Permissions).AllowCopy);
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc2.Permissions).AllowPrint);
    }

    [Fact]
    public void Encrypt_AES256_ContentIsEncryptedInRawBytes()
    {
        var marker = "PLAINTEXT_MARKER_AES256_TEST";
        var contentBytes = Encoding.ASCII.GetBytes($"BT /F1 12 Tf 100 700 Td ({marker}) Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(contentBytes);

        Assert.Contains(marker, Encoding.Latin1.GetString(pdf));

        using var doc = Document.Open(pdf);
        doc.Encrypt("user", "owner", algorithm: CryptoAlgorithm.AESx256);
        var saved = doc.ToArray();

        Assert.DoesNotContain(marker, Encoding.Latin1.GetString(saved));

        using var doc2 = Document.Open(saved, "user");
        Assert.True(doc2.IsEncrypted);
        Assert.Equal(1, doc2.PageCount);
    }
}
