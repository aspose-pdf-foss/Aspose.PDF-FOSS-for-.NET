using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class PdfWriterTests
{
    [Fact]
    public void Save_MinimalPdf_ProducesValidPdf()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var saved = doc.ToArray();

        // Verify it starts with %PDF-
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(saved, 0, 10));

        // Verify we can re-open it
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Save_PreservesPageMediaBox()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        var mb = doc2.Pages[1].MediaBox;
        Assert.Equal(612, mb.URX);
        Assert.Equal(792, mb.URY);
    }

    [Fact]
    public void Save_WithTextContent_PreservesText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Round trip test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        var absorber = new Aspose.Pdf.Text.TextAbsorber();
        absorber.Visit(doc2.Pages[1]);
        Assert.Contains("Round trip test", absorber.Text);
    }

    [Fact]
    public void Save_ToStream()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        using var ms = new MemoryStream();
        doc.Save(ms);
        Assert.True(ms.Length > 0);

        using var doc2 = Document.Open(ms.ToArray());
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Save_PreservesPdfVersion()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal("1.4", doc2.PdfVersion);
    }

    [Fact]
    public void Save_PreservesExistingFilter_NoDoubleCompression()
    {
        // Create a PDF with a FlateDecode image, save, and verify data survives
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var pixels = new byte[8 * 8 * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 256);

        var stamp = Aspose.Pdf.ImageStamp.FromRgb(pixels, 8, 8);
        stamp.ApplyTo(doc.Pages[1]);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var img = doc2.Pages[1].Images[1];
        Assert.Equal(8, img.Width);
        Assert.Equal(8, img.Height);
        var decoded = img.GetDecodedData();
        Assert.Equal(pixels, decoded);
    }
}
