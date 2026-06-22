using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class PdfReaderTests
{
    [Fact]
    public void FromBytes_MinimalPdf_Opens()
    {
        var data = PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(data);
        Assert.NotNull(reader.Trailer);
    }

    [Fact]
    public void Catalog_MinimalPdf_IsCatalog()
    {
        var data = PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(data);
        var catalog = reader.Catalog;
        Assert.Equal("Catalog", ((PdfName)catalog.Get("Type")!).Value);
    }

    [Fact]
    public void Resolve_CatalogPages_ReturnsPages()
    {
        var data = PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(data);
        var catalog = reader.Catalog;
        var pages = reader.ResolveDict(catalog.Get("Pages"));
        Assert.NotNull(pages);
        Assert.Equal("Pages", pages!.GetName("Type"));
        Assert.Equal(1, pages.GetInt("Count"));
    }

    [Fact]
    public void Resolve_PagesKids_ReturnsPageDict()
    {
        var data = PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(data);
        var catalog = reader.Catalog;
        var pages = reader.ResolveDict(catalog.Get("Pages"))!;
        var kids = pages.Get("Kids") as PdfArray;
        Assert.NotNull(kids);
        Assert.Single(kids!);
        var page = reader.ResolveDict(kids[0]);
        Assert.NotNull(page);
        Assert.Equal("Page", page!.GetName("Type"));
    }

    [Fact]
    public void Resolve_PageMediaBox()
    {
        var data = PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(data);
        var catalog = reader.Catalog;
        var pages = reader.ResolveDict(catalog.Get("Pages"))!;
        var kids = (PdfArray)pages.Get("Kids")!;
        var page = reader.ResolveDict(kids[0])!;
        var mediaBox = page.Get("MediaBox") as PdfArray;
        Assert.NotNull(mediaBox);
        Assert.Equal(4, mediaBox!.Count);
        Assert.Equal(0, ((PdfInteger)mediaBox[0]).Value);
        Assert.Equal(0, ((PdfInteger)mediaBox[1]).Value);
        Assert.Equal(612, ((PdfInteger)mediaBox[2]).Value);
        Assert.Equal(792, ((PdfInteger)mediaBox[3]).Value);
    }

    [Fact]
    public void Resolve_NullObject_ReturnsNull()
    {
        var data = PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(data);
        Assert.Null(reader.Resolve(null));
        Assert.Null(reader.Resolve(PdfNull.Instance));
    }

    [Fact]
    public void Resolve_DirectObject_ReturnsSame()
    {
        var data = PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(data);
        var integer = new PdfInteger(42);
        Assert.Same(integer, reader.Resolve(integer));
    }

    [Fact]
    public void Resolve_NonExistentRef_ReturnsNull()
    {
        var data = PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(data);
        var result = reader.Resolve(new PdfIndirectRef(999, 0));
        Assert.Null(result);
    }

    [Fact]
    public void DecodeStream_FlateEncodedContent()
    {
        var content = "BT /F1 12 Tf (Hello) Tj ET"u8.ToArray();
        var data = PdfBuilder.BuildWithFlateStream(content);
        var reader = PdfReader.FromBytes(data);

        // Navigate to the stream
        var catalog = reader.Catalog;
        var pages = reader.ResolveDict(catalog.Get("Pages"))!;
        var kids = (PdfArray)pages.Get("Kids")!;
        var page = reader.ResolveDict(kids[0])!;
        var stream = reader.ResolveStream(page.Get("Contents"));
        Assert.NotNull(stream);

        var decoded = reader.DecodeStream(stream!);
        Assert.Equal(content, decoded);
    }

    [Fact]
    public void Resolve_CachesResults()
    {
        var data = PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(data);
        var catalog1 = reader.Catalog;
        var pages1 = reader.ResolveDict(catalog1.Get("Pages"));
        var pages2 = reader.ResolveDict(catalog1.Get("Pages"));
        Assert.Same(pages1, pages2);
    }

    [Fact]
    public void Resolve_WrongXrefOffset_ScansNearby()
    {
        // Build a minimal PDF, then modify the xref offset for object 3 (page)
        // to be off by a few bytes. The reader should still find the object.
        var data = PdfBuilder.BuildMinimal();

        // Find the xref entry for object 3 and corrupt its offset slightly
        var text = Encoding.ASCII.GetString(data);
        var xrefIdx = text.IndexOf("xref\n");
        Assert.True(xrefIdx > 0);

        // Parse the xref: find entry for object 3 and modify its offset
        // The format is "0000000XXX 00000 n \n" (20 bytes per entry, starting after "0 4\n")
        var entriesStart = xrefIdx + 5; // after "xref\n"
        // Skip "0 4\n"
        var lineEnd = text.IndexOf('\n', entriesStart);
        var entryStart = lineEnd + 1; // after "0 4\n"
        // Entry 0 (free), Entry 1 (catalog), Entry 2 (pages), Entry 3 (page)
        var entry3Start = entryStart + 20 * 3; // each entry is 20 bytes

        // Modify the offset of entry 3 to be wrong by +5 bytes
        var origOffsetStr = text.Substring(entry3Start, 10);
        var origOffset = long.Parse(origOffsetStr);
        var wrongOffset = origOffset + 5;
        var wrongOffsetStr = wrongOffset.ToString("D10");
        var modified = new byte[data.Length];
        Array.Copy(data, modified, data.Length);
        var wrongBytes = Encoding.ASCII.GetBytes(wrongOffsetStr);
        Array.Copy(wrongBytes, 0, modified, entry3Start, 10);

        // The reader should still be able to open and find the page
        using var doc = Document.Open(modified);
        Assert.Equal(1, doc.PageCount);
    }
}
