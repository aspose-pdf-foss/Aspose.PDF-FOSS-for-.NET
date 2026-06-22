using Aspose.Pdf.Core;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class IncrementalWriterTests
{
    [Fact]
    public void SaveIncremental_PreservesOriginalContent()
    {
        var pdf = PdfBuilder.BuildWithDocumentInfo(title: "Original");
        using var doc = Document.Open(pdf);

        // Modify the Info dictionary
        var newInfo = new PdfDictionary();
        newInfo.Set("Title", new PdfString(System.Text.Encoding.Latin1.GetBytes("Modified")));

        var saved = doc.SaveIncremental((4, newInfo));

        // The incremental save should be longer than the original (appended data)
        Assert.True(saved.Length > pdf.Length);

        // Original bytes should be preserved at the start
        Assert.Equal(pdf, saved[..pdf.Length]);
    }

    [Fact]
    public void SaveIncremental_ResultIsValidPdf()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        // Add a new object
        var newDict = new PdfDictionary();
        newDict.Set("Test", new PdfString(System.Text.Encoding.Latin1.GetBytes("Hello")));

        var saved = doc.SaveIncremental((10, newDict));

        // Should be parseable as a valid PDF
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void SaveIncremental_MultipleModifiedObjects()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var obj1 = new PdfDictionary();
        obj1.Set("Key1", new PdfString(System.Text.Encoding.Latin1.GetBytes("Val1")));

        var obj2 = new PdfDictionary();
        obj2.Set("Key2", new PdfString(System.Text.Encoding.Latin1.GetBytes("Val2")));

        var saved = doc.SaveIncremental((10, obj1), (11, obj2));
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void SaveIncremental_HasPrevInTrailer()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var dummy = new PdfDictionary();
        var saved = doc.SaveIncremental((10, dummy));

        // The new trailer should have /Prev pointing to original xref
        var text = System.Text.Encoding.ASCII.GetString(saved);
        Assert.Contains("/Prev ", text);

        // There should be two %%EOF markers
        var eofCount = 0;
        var idx = 0;
        while ((idx = text.IndexOf("%%EOF", idx, StringComparison.Ordinal)) >= 0)
        {
            eofCount++;
            idx += 5;
        }
        Assert.Equal(2, eofCount);
    }
}
