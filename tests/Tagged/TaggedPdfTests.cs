using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

public class TaggedPdfTests
{
    [Fact]
    public void IsTagged_TaggedPdf_True()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        Assert.True(doc.IsTagged);
    }

    [Fact]
    public void HasStructTree_TaggedPdf_True()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        Assert.True(doc.HasStructTree);
        Assert.NotNull(doc.StructTreeRoot);
    }

    [Fact]
    public void StructTreeRoot_HasDocumentElement()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        var root = doc.StructTreeRoot!;
        Assert.Single(root.Children);
        Assert.Equal("Document", root.Children[0].StructureType);
    }

    [Fact]
    public void StructureElement_HasParagraphChild()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        var docElem = doc.StructTreeRoot!.Children[0];
        Assert.Single(docElem.Children);
        Assert.Equal("P", docElem.Children[0].StructureType);
    }

    [Fact]
    public void StructureElement_AltText()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        var para = doc.StructTreeRoot!.Children[0].Children[0];
        Assert.Equal("A paragraph", para.AltText);
    }

    [Fact]
    public void MinimalPdf_NotTagged()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.False(doc.IsTagged);
        Assert.False(doc.HasStructTree);
        Assert.Null(doc.StructTreeRoot);
    }

    [Fact]
    public void TaggedPdf_PdfVersion()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        Assert.Equal("1.7", doc.PdfVersion);
    }

    [Fact]
    public void StructureElement_Attributes_FromBuildTaggedWithAttributes()
    {
        var data = BuildTaggedWithAttributes();
        using var doc = Document.Open(data);
        var para = doc.StructTreeRoot!.Children[0].Children[0];
        Assert.NotNull(para.Attributes);
        Assert.Equal("Block", para.Attributes!["Placement"]);
        Assert.Equal("Table", para.Attributes!["O"]);
    }

    [Fact]
    public void StructureElement_NoAttributes_ReturnsNull()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        var docElem = doc.StructTreeRoot!.Children[0];
        Assert.Null(docElem.Attributes);
    }

    [Fact]
    public void StructureElement_MarkedContentIds_FromBuildTaggedWithMcid()
    {
        var data = BuildTaggedWithMcid();
        using var doc = Document.Open(data);
        var para = doc.StructTreeRoot!.Children[0].Children[0];
        var mcids = para.MarkedContentIds;
        Assert.Single(mcids);
        Assert.Equal(0, mcids[0]);
    }

    [Fact]
    public void StructureElement_NoMcids_ReturnsEmpty()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        var docElem = doc.StructTreeRoot!.Children[0];
        Assert.Empty(docElem.MarkedContentIds);
    }

    /// <summary>Build a tagged PDF where /P element has /A attributes dict.</summary>
    private static byte[] BuildTaggedWithAttributes()
    {
        using var ms = new System.IO.MemoryStream();
        void Write(string s) => ms.Write(System.Text.Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.7\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        // P element with /A attributes
        var paraOffset = ms.Position;
        Write("6 0 obj\n<< /Type /StructElem /S /P /P 5 0 R /A << /O /Table /Placement /Block >> >>\nendobj\n");

        var docElemOffset = ms.Position;
        Write("5 0 obj\n<< /Type /StructElem /S /Document /P 4 0 R /K [6 0 R] >>\nendobj\n");

        var structTreeOffset = ms.Position;
        Write("4 0 obj\n<< /Type /StructTreeRoot /K 5 0 R >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 7\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{structTreeOffset:D10} 00000 n \n");
        Write($"{docElemOffset:D10} 00000 n \n");
        Write($"{paraOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>Build a tagged PDF where /P element has /K with MCID integer.</summary>
    private static byte[] BuildTaggedWithMcid()
    {
        using var ms = new System.IO.MemoryStream();
        void Write(string s) => ms.Write(System.Text.Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.7\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        // P element with /K integer MCID
        var paraOffset = ms.Position;
        Write("6 0 obj\n<< /Type /StructElem /S /P /P 5 0 R /K 0 >>\nendobj\n");

        var docElemOffset = ms.Position;
        Write("5 0 obj\n<< /Type /StructElem /S /Document /P 4 0 R /K [6 0 R] >>\nendobj\n");

        var structTreeOffset = ms.Position;
        Write("4 0 obj\n<< /Type /StructTreeRoot /K 5 0 R >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 7\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{structTreeOffset:D10} 00000 n \n");
        Write($"{docElemOffset:D10} 00000 n \n");
        Write($"{paraOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }
}
