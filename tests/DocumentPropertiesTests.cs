using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class DocumentPropertiesTests
{
    [Fact]
    public void IsEncrypted_MinimalPdf_False()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.False(doc.IsEncrypted);
        Assert.Null(doc.EncryptionInfo);
    }

    [Fact]
    public void Permissions_Unencrypted_AllowAll()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var perms = new Aspose.Pdf.Facades.DocumentPrivilege(doc.Permissions);
        Assert.True(perms.AllowPrint);
        Assert.True(perms.AllowCopy);
        Assert.True(perms.AllowModifyContents);
    }

    [Fact]
    public void OpenAction_MinimalPdf_Null()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Null(doc.OpenAction);
    }

    [Fact]
    public void EmbeddedFiles_MinimalPdf_Empty()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.False(doc.HasEmbeddedFiles);
        Assert.Empty(doc.EmbeddedFiles);
    }

    [Fact]
    public void PageLayout_MinimalPdf_Default()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Null(doc.PageLayoutName);
        Assert.Equal(PageLayout.Default, doc.PageLayout);
    }

    [Fact]
    public void PageMode_MinimalPdf_UseNone()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Null(doc.PageModeName);
        Assert.Equal(PageMode.UseNone, doc.PageMode);
    }

    [Fact]
    public void Language_MinimalPdf_Null()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Null(doc.Language);
    }

    [Fact]
    public void IsTagged_MinimalPdf_False()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.False(doc.IsTagged);
    }

    [Fact]
    public void DocumentInfo_ParseDate_ValidDate()
    {
        var dt = DocumentInfo.ParseDate("D:20240115120000");
        Assert.NotNull(dt);
        Assert.Equal(2024, dt!.Value.Year);
        Assert.Equal(1, dt.Value.Month);
        Assert.Equal(15, dt.Value.Day);
        Assert.Equal(12, dt.Value.Hour);
    }

    [Fact]
    public void DocumentInfo_ParseDate_Null()
    {
        Assert.Null(DocumentInfo.ParseDate(null));
        Assert.Null(DocumentInfo.ParseDate(""));
    }

    [Fact]
    public void DocumentInfo_ParseDate_ShortDate()
    {
        var dt = DocumentInfo.ParseDate("D:2024");
        Assert.NotNull(dt);
        Assert.Equal(2024, dt!.Value.Year);
    }
}
