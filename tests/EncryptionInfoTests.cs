using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class EncryptionInfoTests
{
    [Fact]
    public void IsEncrypted_WithEncryptDict_True()
    {
        var data = PdfBuilder.BuildWithEncryptionDict();
        using var doc = Document.Open(data);
        Assert.True(doc.IsEncrypted);
    }

    [Fact]
    public void EncryptionInfo_RC4x128()
    {
        var data = PdfBuilder.BuildWithEncryptionDict(v: 2, r: 3, length: 128);
        using var doc = Document.Open(data);
        var info = doc.EncryptionInfo;
        Assert.NotNull(info);
        Assert.Equal(CryptoAlgorithm.RC4x128, info!.Algorithm);
        Assert.Equal(128, info.KeyLength);
        Assert.Equal(2, info.Version);
        Assert.Equal(3, info.Revision);
    }

    [Fact]
    public void EncryptionInfo_RC4x40()
    {
        var data = PdfBuilder.BuildWithEncryptionDict(v: 1, r: 2, length: 40);
        using var doc = Document.Open(data);
        var info = doc.EncryptionInfo;
        Assert.NotNull(info);
        Assert.Equal(CryptoAlgorithm.RC4x40, info!.Algorithm);
        Assert.Equal(40, info.KeyLength);
    }

    [Fact]
    public void EncryptionInfo_AESx128()
    {
        var data = PdfBuilder.BuildWithEncryptionDict(v: 4, r: 4, length: 128);
        using var doc = Document.Open(data);
        var info = doc.EncryptionInfo;
        Assert.NotNull(info);
        Assert.Equal(CryptoAlgorithm.AESx128, info!.Algorithm);
    }

    [Fact]
    public void EncryptionInfo_AESx256()
    {
        // V5/R6 requires a password to open; create a real AES-256 encrypted PDF.
        using var src = Document.Create();
        src.Pages.Add();
        src.Encrypt("pass", "owner", DocumentPrivilege.AllowAll, CryptoAlgorithm.AESx256);
        var enc = src.ToArray();

        using var doc = Document.Open(enc, "pass");
        var info = doc.EncryptionInfo;
        Assert.NotNull(info);
        Assert.Equal(CryptoAlgorithm.AESx256, info!.Algorithm);
        Assert.Equal(256, info.KeyLength);
    }

    [Fact]
    public void Permissions_RestrictedPdf()
    {
        // P = 0 means all permissions denied
        var data = PdfBuilder.BuildWithEncryptionDict(permissions: 0);
        using var doc = Document.Open(data);
        var perms = new DocumentPrivilege(doc.Permissions);
        Assert.False(perms.AllowPrint);
        Assert.False(perms.AllowCopy);
        Assert.False(perms.AllowModifyContents);
        Assert.False(perms.AllowModifyAnnotations);
        Assert.False(perms.AllowFillIn);
        Assert.False(perms.AllowScreenReaders);
        Assert.False(perms.AllowAssembly);
        Assert.False(perms.AllowDegradedPrinting);
    }

    [Fact]
    public void Permissions_AllAllowed()
    {
        var perms = DocumentPrivilege.AllowAll;
        Assert.True(perms.AllowPrint);
        Assert.True(perms.AllowCopy);
        Assert.True(perms.AllowModifyContents);
        Assert.True(perms.AllowModifyAnnotations);
        Assert.True(perms.AllowFillIn);
        Assert.True(perms.AllowScreenReaders);
        Assert.True(perms.AllowAssembly);
        // AllowDegradedPrinting is the "printing restricted to degraded quality"
        // flag; AllowAll permits full-quality printing, so it is false.
        Assert.False(perms.AllowDegradedPrinting);
    }

    [Fact]
    public void Permissions_ForbidAll()
    {
        var perms = DocumentPrivilege.ForbidAll;
        Assert.False(perms.AllowPrint);
        Assert.False(perms.AllowCopy);
        Assert.False(perms.AllowModifyContents);
    }

    [Fact]
    public void Permissions_SpecificBits()
    {
        // P = (1<<2) | (1<<4) = print + copy
        var data = PdfBuilder.BuildWithEncryptionDict(permissions: (1 << 2) | (1 << 4));
        using var doc = Document.Open(data);
        var perms = new DocumentPrivilege(doc.Permissions);
        Assert.True(perms.AllowPrint);
        Assert.True(perms.AllowCopy);
        Assert.False(perms.AllowModifyContents);
        Assert.False(perms.AllowModifyAnnotations);
    }
}
