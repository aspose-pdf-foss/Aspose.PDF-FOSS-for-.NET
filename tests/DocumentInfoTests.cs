using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class DocumentInfoTests
{
    [Fact]
    public void Info_WithTitle_ReturnsTitle()
    {
        var data = PdfBuilder.BuildWithDocumentInfo(title: "My Document");
        using var doc = Document.Open(data);
        Assert.Equal("My Document", doc.Info.Title);
    }

    [Fact]
    public void Info_WithAuthor_ReturnsAuthor()
    {
        var data = PdfBuilder.BuildWithDocumentInfo(author: "John Doe");
        using var doc = Document.Open(data);
        Assert.Equal("John Doe", doc.Info.Author);
    }

    [Fact]
    public void Info_WithSubject_ReturnsSubject()
    {
        var data = PdfBuilder.BuildWithDocumentInfo(subject: "Testing");
        using var doc = Document.Open(data);
        Assert.Equal("Testing", doc.Info.Subject);
    }

    [Fact]
    public void Info_WithCreator_ReturnsCreator()
    {
        var data = PdfBuilder.BuildWithDocumentInfo(creator: "TestApp 1.0");
        using var doc = Document.Open(data);
        Assert.Equal("TestApp 1.0", doc.Info.Creator);
    }

    [Fact]
    public void Info_WithCreationDate_ParsesCorrectly()
    {
        var data = PdfBuilder.BuildWithDocumentInfo(creationDate: "D:20240315093000");
        using var doc = Document.Open(data);
        Assert.NotEqual(DateTime.MinValue, doc.Info.CreationDate);
        Assert.Equal(2024, doc.Info.CreationDate.Year);
        Assert.Equal(3, doc.Info.CreationDate.Month);
        Assert.Equal(15, doc.Info.CreationDate.Day);
        Assert.Equal(9, doc.Info.CreationDate.Hour);
        Assert.Equal(30, doc.Info.CreationDate.Minute);
    }

    [Fact]
    public void Info_AllFields_ReturnCorrectValues()
    {
        var data = PdfBuilder.BuildWithDocumentInfo(
            title: "Report",
            author: "Jane",
            subject: "Q4 Results",
            creator: "ReportGen");
        using var doc = Document.Open(data);
        Assert.Equal("Report", doc.Info.Title);
        Assert.Equal("Jane", doc.Info.Author);
        Assert.Equal("Q4 Results", doc.Info.Subject);
        Assert.Equal("ReportGen", doc.Info.Creator);
    }

    [Fact]
    public void Info_NullFields_ReturnNull()
    {
        var data = PdfBuilder.BuildWithDocumentInfo(title: "Only Title");
        using var doc = Document.Open(data);
        Assert.Equal("Only Title", doc.Info.Title);
        Assert.Null(doc.Info.Author);
        Assert.Null(doc.Info.Subject);
        Assert.Null(doc.Info.Keywords);
        Assert.Null(doc.Info.Producer);
    }

    [Fact]
    public void ParseDate_WithTimezone_ParsesDate()
    {
        var dt = DocumentInfo.ParseDate("D:20231225120000+05'30'");
        Assert.NotNull(dt);
        Assert.Equal(2023, dt!.Value.Year);
        Assert.Equal(12, dt!.Value.Month);
        Assert.Equal(25, dt!.Value.Day);
    }

    [Fact]
    public void ParseDate_InvalidString_ReturnsNull()
    {
        Assert.Null(DocumentInfo.ParseDate("not a date"));
        Assert.Null(DocumentInfo.ParseDate("D:"));
        Assert.Null(DocumentInfo.ParseDate("D:ab"));
    }
}
