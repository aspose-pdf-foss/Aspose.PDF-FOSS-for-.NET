using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

public class DocumentCreateTests
{
    [Fact]
    public void Create_EmptyDocument()
    {
        using var doc = Document.Create();
        Assert.Equal(0, doc.PageCount);
        // New documents default to PDF 1.7.
        Assert.Equal("1.7", doc.PdfVersion);
    }

    [Fact]
    public void Create_SaveAndReopen()
    {
        using var doc = Document.Create();
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal(0, doc2.PageCount);
    }

    [Fact]
    public void Create_NoForm()
    {
        using var doc = Document.Create();
        Assert.False(doc.HasForm);
        Assert.Empty(doc.Form);
    }

    [Fact]
    public void Create_NoOutlines()
    {
        using var doc = Document.Create();
        Assert.False(doc.HasOutlines);
        Assert.NotNull(doc.Outlines);
        Assert.Empty(doc.Outlines);
    }

    [Fact]
    public void Create_NoMetadata()
    {
        using var doc = Document.Create();
        Assert.False(doc.HasMetadata);
        // Metadata property always returns non-null,
        // but the document has no /Metadata key in the catalog
        Assert.NotNull(doc.Metadata);
    }
}
