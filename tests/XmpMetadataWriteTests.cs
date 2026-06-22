using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests;

public class XmpMetadataWriteTests
{
    [Fact]
    public void GetOrCreateMetadata_OnNewDocument_ReturnsEmptyMetadata()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        Assert.NotNull(xmp);
        Assert.Equal(0, xmp.Count);
    }

    [Fact]
    public void Set_AddsProperty()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        xmp.Set("dc:title", "Test Document");
        Assert.Equal("Test Document", xmp.Get("dc:title"));
        Assert.Equal(1, xmp.Count);
    }

    [Fact]
    public void Set_OverwritesExistingProperty()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        xmp.Set("dc:title", "Original");
        xmp.Set("dc:title", "Updated");
        Assert.Equal("Updated", xmp.Get("dc:title"));
        Assert.Equal(1, xmp.Count);
    }

    [Fact]
    public void Remove_RemovesProperty()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        xmp.Set("dc:title", "Test");
        Assert.True(xmp.Remove("dc:title"));
        Assert.Null(xmp.Get("dc:title"));
        Assert.Equal(0, xmp.Count);
    }

    [Fact]
    public void Remove_NonExistent_ReturnsFalse()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        Assert.False(xmp.Remove("dc:title"));
    }

    [Fact]
    public void Indexer_SetAndGet()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        xmp["xmp:CreatorTool"] = "Test Tool";
        Assert.Equal("Test Tool", xmp["xmp:CreatorTool"]);
    }

    [Fact]
    public void Indexer_SetNull_RemovesProperty()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        xmp["dc:title"] = "Test";
        xmp["dc:title"] = null;
        Assert.Null(xmp["dc:title"]);
    }

    [Fact]
    public void SaveAndReopen_PreservesMetadata()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var xmp = doc.GetOrCreateMetadata();
        xmp.Set("dc:title", "My PDF Title");
        xmp.Set("xmp:CreatorTool", "Aspose.PDF FOSS");
        xmp.Set("pdf:Producer", "Test Producer");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasMetadata);
        var xmp2 = doc2.Metadata!;
        Assert.Equal("My PDF Title", xmp2.Get("dc:title"));
        Assert.Equal("Aspose.PDF FOSS", xmp2.Get("xmp:CreatorTool"));
        Assert.Equal("Test Producer", xmp2.Get("pdf:Producer"));
    }

    [Fact]
    public void SaveAndReopen_MultipleProperties()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var xmp = doc.GetOrCreateMetadata();
        xmp.Set("dc:creator", "Author One; Author Two");
        xmp.Set("dc:subject", "PDF; Testing; Metadata");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var xmp2 = doc2.Metadata!;
        Assert.Contains("Author One", xmp2.Get("dc:creator")!);
        Assert.Contains("Author Two", xmp2.Get("dc:creator")!);
    }

    [Fact]
    public void ToXmpBytes_ContainsXpacketMarkers()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        xmp.Set("dc:title", "Test");

        var bytes = xmp.ToXmpBytes();
        var xml = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("<?xpacket begin=", xml);
        Assert.Contains("<?xpacket end=", xml);
        Assert.Contains("rdf:RDF", xml);
        Assert.Contains("dc:title", xml);
    }

    [Fact]
    public void GetArray_SplitsSemicolonValues()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        xmp.Set("dc:creator", "Author One; Author Two; Author Three");

        var result = xmp.GetArray("dc:creator");
        Assert.Equal(3, result.Count);
        Assert.Equal("Author One", result[0]);
        Assert.Equal("Author Two", result[1]);
        Assert.Equal("Author Three", result[2]);
    }

    [Fact]
    public void GetArray_NonExistentKey_ReturnsEmpty()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        Assert.Empty(xmp.GetArray("dc:creator"));
    }

    [Fact]
    public void SetArray_JoinsValues()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        xmp.SetArray("dc:subject", ["PDF", "Testing", "Metadata"]);
        Assert.Equal("PDF; Testing; Metadata", xmp.Get("dc:subject"));
    }

    [Fact]
    public void PdfAidPart_SetAndGet()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        xmp.PdfAidPart = "2";
        xmp.PdfAidConformance = "B";
        Assert.Equal("2", xmp.PdfAidPart);
        Assert.Equal("B", xmp.PdfAidConformance);
    }

    [Fact]
    public void PdfAidPart_SetNull_Removes()
    {
        using var doc = Document.Create();
        var xmp = doc.GetOrCreateMetadata();
        xmp.PdfAidPart = "1";
        xmp.PdfAidPart = null;
        Assert.Null(xmp.PdfAidPart);
    }

    [Fact]
    public void PdfAid_SurvivesRoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var xmp = doc.GetOrCreateMetadata();
        xmp.PdfAidPart = "3";
        xmp.PdfAidConformance = "A";

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var xmp2 = doc2.Metadata!;
        Assert.Equal("3", xmp2.PdfAidPart);
        Assert.Equal("A", xmp2.PdfAidConformance);
    }
}
