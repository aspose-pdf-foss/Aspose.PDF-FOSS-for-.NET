using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class SaveRoundtripTests
{
    [Fact]
    public void Save_MinimalPdf_Roundtrip()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Save_MultiPage_Roundtrip()
    {
        var data = PdfBuilder.BuildMultiPage(4);
        using var doc = Document.Open(data);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.Equal(4, doc2.PageCount);
    }

    [Fact]
    public void Save_WithFormField_Roundtrip()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.True(doc2.HasForm);
        Assert.Single(doc2.Form!);
        Assert.Equal("Name", doc2.Form!.Fields[0].PartialName);
        Assert.Equal("John", doc2.Form!.Fields[0].Value);
    }

    [Fact]
    public void Save_WithText_PreservesContent()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Round trip text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        var absorber = new TextAbsorber();
        absorber.Visit(doc2.Pages[1]);
        Assert.Contains("Round trip text", absorber.Text);
    }

    [Fact]
    public void Save_ToStream_Works()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        using var ms = new MemoryStream();
        doc.Save(ms);

        Assert.True(ms.Length > 0);
        ms.Position = 0;
        using var doc2 = Document.Open(ms);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Save_ToFile_Works()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var tmpPath = Path.GetTempFileName();
        try
        {
            doc.Save(tmpPath);
            Assert.True(File.Exists(tmpPath));
            Assert.True(new FileInfo(tmpPath).Length > 0);

            using var doc2 = Document.Open(tmpPath);
            Assert.Equal(1, doc2.PageCount);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Fact]
    public void Save_CreatedDocument_WithPages()
    {
        using var doc = Document.Create();
        doc.Pages.Add(595, 842); // A4
        doc.Pages.Add(842, 595); // A4 landscape
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.Equal(2, doc2.PageCount);
    }

    [Fact]
    public void Save_AfterDeletePage_Roundtrip()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);
        doc.Pages.Delete(2);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.Equal(2, doc2.PageCount);
    }

    [Fact]
    public void Open_FromStream_Works()
    {
        var data = PdfBuilder.BuildMinimal();
        using var ms = new MemoryStream(data);
        using var doc = Document.Open(ms);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Open_FromFilePath_Works()
    {
        var data = PdfBuilder.BuildMinimal();
        var tmpPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmpPath, data);
            using var doc = Document.Open(tmpPath);
            Assert.Equal(1, doc.PageCount);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }
}
