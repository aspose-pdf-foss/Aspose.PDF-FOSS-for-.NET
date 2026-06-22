using Aspose.Pdf.Facades;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public class PdfFileSecurityTests
{
    [Fact]
    public void EncryptFile_AES128_CanOpenWithUserPassword()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user123", "owner456",
            algorithm: CryptoAlgorithm.AESx128);

        using var doc = Document.Open(encrypted, "user123");
        Assert.True(doc.IsEncrypted);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void EncryptFile_AES256_CanOpenWithOwnerPassword()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user123", "owner456",
            algorithm: CryptoAlgorithm.AESx256);

        using var doc = Document.Open(encrypted, "owner456");
        Assert.True(doc.IsEncrypted);
        Assert.Equal(CryptoAlgorithm.AESx256, doc.EncryptionInfo!.Algorithm);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void EncryptFile_RC4x128()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user", "owner",
            algorithm: CryptoAlgorithm.RC4x128);

        using var doc = Document.Open(encrypted, "user");
        Assert.True(doc.IsEncrypted);
        Assert.Equal(CryptoAlgorithm.RC4x128, doc.EncryptionInfo!.Algorithm);
    }

    [Fact]
    public void DecryptFile_RemovesEncryption()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user", "owner");
        var decrypted = security.DecryptFile(encrypted, "owner");

        using var doc = Document.Open(decrypted);
        Assert.False(doc.IsEncrypted);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void ChangePasswords_CanOpenWithNewPassword()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user", "owner");
        var changed = security.ChangePasswords(encrypted, "owner", "newUser", "newOwner");

        using var doc = Document.Open(changed, "newUser");
        Assert.True(doc.IsEncrypted);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void ChangePasswords_OldPasswordNoLongerWorks()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user", "owner");
        var changed = security.ChangePasswords(encrypted, "owner", "newUser", "newOwner");

        Assert.Throws<InvalidPasswordException>(() => Document.Open(changed, "user"));
    }

    [Fact]
    public void EncryptFile_WithDocumentPrivilege_AllowPrintOnly()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var priv = new DocumentPrivilege { AllowPrint = true };
        var encrypted = security.EncryptFile(pdf, "user", "owner", permissions: priv);

        using var doc = Document.Open(encrypted, "owner");
        Assert.True(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowPrint);
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowCopy);
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowModifyContents);
    }

    [Fact]
    public void DecryptFile_WrongPassword_Throws()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user", "owner");

        Assert.Throws<InvalidPasswordException>(() =>
            security.DecryptFile(encrypted, "wrongpassword"));
    }

    [Fact]
    public void RoundTrip_EncryptDecrypt_PreservesContent()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user", "owner",
            algorithm: CryptoAlgorithm.AESx128);
        var decrypted = security.DecryptFile(encrypted, "owner");

        // Verify the decrypted document is valid and has the same structure
        using var doc = Document.Open(decrypted);
        Assert.False(doc.IsEncrypted);
        Assert.Equal(1, doc.PageCount);
        Assert.Null(doc.EncryptionInfo);
    }

    [Fact]
    public void EncryptFile_AlreadyEncrypted_ReEncryptsWithNewAlgorithm()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        // First encryption with RC4-128
        var encrypted1 = security.EncryptFile(pdf, "user1", "owner1",
            algorithm: CryptoAlgorithm.RC4x128);

        // Decrypt, then re-encrypt with AES-256
        var decrypted = security.DecryptFile(encrypted1, "owner1");
        var encrypted2 = security.EncryptFile(decrypted, "user2", "owner2",
            algorithm: CryptoAlgorithm.AESx256);

        using var doc = Document.Open(encrypted2, "user2");
        Assert.True(doc.IsEncrypted);
        Assert.Equal(CryptoAlgorithm.AESx256, doc.EncryptionInfo!.Algorithm);
    }

    [Theory]
    [InlineData(CryptoAlgorithm.RC4x40)]
    [InlineData(CryptoAlgorithm.RC4x128)]
    [InlineData(CryptoAlgorithm.AESx128)]
    [InlineData(CryptoAlgorithm.AESx256)]
    public void EncryptFile_AllAlgorithms_ProduceEncryptedOutput(CryptoAlgorithm algorithm)
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "u", "o", algorithm: algorithm);

        using var doc = Document.Open(encrypted, "u");
        Assert.True(doc.IsEncrypted);
        Assert.Equal(algorithm, doc.EncryptionInfo!.Algorithm);
    }

    [Fact]
    public void EncryptFile_ForbidAll_NoPermissions()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user", "owner",
            permissions: DocumentPrivilege.ForbidAll);

        using var doc = Document.Open(encrypted, "owner");
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowPrint);
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowCopy);
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowModifyContents);
        Assert.False(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowModifyAnnotations);
    }

    [Fact]
    public void EncryptFile_AllowAll_AllPermissions()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user", "owner",
            permissions: DocumentPrivilege.AllowAll);

        using var doc = Document.Open(encrypted, "owner");
        Assert.True(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowPrint);
        Assert.True(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowCopy);
        Assert.True(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowModifyContents);
        Assert.True(new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions).AllowModifyAnnotations);
    }

    [Fact]
    public void ChangePasswords_WithDifferentAlgorithm()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user", "owner",
            algorithm: CryptoAlgorithm.RC4x128);
        var changed = security.ChangePasswords(encrypted, "owner",
            "newUser", "newOwner", CryptoAlgorithm.AESx256);

        using var doc = Document.Open(changed, "newUser");
        Assert.True(doc.IsEncrypted);
        Assert.Equal(CryptoAlgorithm.AESx256, doc.EncryptionInfo!.Algorithm);
    }

    [Fact]
    public void DecryptFile_CanOpenWithoutPassword()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var security = new PdfFileSecurity();

        var encrypted = security.EncryptFile(pdf, "user", "owner",
            algorithm: CryptoAlgorithm.AESx128);
        var decrypted = security.DecryptFile(encrypted, "owner");

        // After decryption, the document should open without any password
        using var doc = Document.Open(decrypted);
        Assert.False(doc.IsEncrypted);
    }
}
