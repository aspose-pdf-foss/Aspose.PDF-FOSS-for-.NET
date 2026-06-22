using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Core;
using Aspose.Pdf.Facades;
using Aspose.Pdf.IO;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public class PdfFileEditorTests
{
    [Fact]
    public void Concatenate_TwoDocuments()
    {
        var pdf1 = PdfBuilder.BuildMinimal();
        var pdf2 = PdfBuilder.BuildMinimal();

        var editor = new PdfFileEditor();
        var result = editor.Concatenate(pdf1, pdf2);

        using var doc = Document.Open(result);
        Assert.Equal(2, doc.PageCount);
    }

    [Fact]
    public void Concatenate_SingleDocument_ReturnsSame()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var editor = new PdfFileEditor();
        var result = editor.Concatenate(pdf);
        Assert.Equal(pdf, result);
    }

    [Fact]
    public void Extract_FirstPage()
    {
        var pdf1 = PdfBuilder.BuildMinimal();
        var pdf2 = PdfBuilder.BuildMinimal();
        var editor = new PdfFileEditor();
        var combined = editor.Concatenate(pdf1, pdf2);

        var extracted = editor.Extract(combined, 1, 1);
        using var doc = Document.Open(extracted);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Split_IntoIndividualPages()
    {
        var pdf1 = PdfBuilder.BuildMinimal();
        var pdf2 = PdfBuilder.BuildMinimal();
        var editor = new PdfFileEditor();
        var combined = editor.Concatenate(pdf1, pdf2);

        var pages = editor.Split(combined);
        Assert.Equal(2, pages.Length);

        foreach (var page in pages)
        {
            using var doc = Document.Open(page);
            Assert.Equal(1, doc.PageCount);
        }
    }

    [Fact]
    public void Delete_Page()
    {
        var pdf1 = PdfBuilder.BuildMinimal();
        var pdf2 = PdfBuilder.BuildMinimal();
        var editor = new PdfFileEditor();
        var combined = editor.Concatenate(pdf1, pdf2);

        var result = editor.Delete(combined, 1);
        using var doc = Document.Open(result);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Concatenate_SameFontName_DifferentFonts_BothPreserved()
    {
        // Doc1 has /F1 -> Helvetica, Doc2 has /F1 -> Courier.
        // Each page keeps its own resource dict so no global renaming is needed.
        var pdf1 = PdfBuilder.BuildWithNamedFont("F1", "Helvetica", "Hello");
        var pdf2 = PdfBuilder.BuildWithNamedFont("F1", "Courier", "World");

        var editor = new PdfFileEditor();
        var result = editor.Concatenate(pdf1, pdf2);

        using var doc = Document.Open(result);
        Assert.Equal(2, doc.PageCount);

        // Page 1: font resolves to Helvetica via its own /Resources
        var page1Fonts = doc.Pages[1].Fonts;
        Assert.Single(page1Fonts);
        Assert.Equal("Helvetica", page1Fonts[1].BaseFont);

        // Page 2: font resolves to Courier via its own /Resources
        var page2Fonts = doc.Pages[2].Fonts;
        Assert.Single(page2Fonts);
        Assert.Equal("Courier", page2Fonts[1].BaseFont);
    }

    [Fact]
    public void Concatenate_SameFontName_DifferentFonts_ContentStreamReferencesFont()
    {
        // Verify page 2 content stream still references a font name (not broken)
        var pdf1 = PdfBuilder.BuildWithNamedFont("F1", "Helvetica", "Hello");
        var pdf2 = PdfBuilder.BuildWithNamedFont("F1", "Courier", "World");

        var editor = new PdfFileEditor();
        var result = editor.Concatenate(pdf1, pdf2);

        var reader = PdfReader.FromBytes(result);
        var catalog = reader.Catalog;
        var pagesDict = reader.ResolveDict(catalog.Get("Pages"));
        var kids = reader.Resolve(pagesDict!.Get("Kids")) as PdfArray;

        // Page 2 content stream should reference the font name that exists in its own resource dict
        var page2Dict = reader.ResolveDict(kids![1]);
        var contents2 = reader.Resolve(page2Dict!.Get("Contents")) as PdfStream;
        var contentText = Encoding.Latin1.GetString(contents2!.RawData);
        // Each page keeps its own resources so the font name is preserved as-is
        Assert.Contains("/F1", contentText);
    }

    [Fact]
    public void Concatenate_SameXObjectName_DifferentImages_BothPreserved()
    {
        // Doc1 has /Im1 -> red image, Doc2 has /Im1 -> blue image.
        // Each page keeps its own resource dict, so both are named /Im1 in their respective pages.
        var pdf1 = PdfBuilder.BuildWithNamedImage("Im1", 255, 0, 0);
        var pdf2 = PdfBuilder.BuildWithNamedImage("Im1", 0, 0, 255);

        var editor = new PdfFileEditor();
        var result = editor.Concatenate(pdf1, pdf2);

        var reader = PdfReader.FromBytes(result);
        var catalog = reader.Catalog;
        var pagesDict = reader.ResolveDict(catalog.Get("Pages"));
        var kids = reader.Resolve(pagesDict!.Get("Kids")) as PdfArray;

        // Page 1: XObject named /Im1 in its own resource dict
        var page1Dict = reader.ResolveDict(kids![0]);
        var res1 = reader.ResolveDict(page1Dict!.Get("Resources"));
        var xobj1 = reader.ResolveDict(res1!.Get("XObject"));
        Assert.True(xobj1!.ContainsKey("Im1"));

        // Page 2: XObject also named /Im1 in its own resource dict (no global conflict)
        var page2Dict = reader.ResolveDict(kids[1]);
        var res2 = reader.ResolveDict(page2Dict!.Get("Resources"));
        var xobj2 = reader.ResolveDict(res2!.Get("XObject"));
        Assert.True(xobj2!.ContainsKey("Im1"));

        // Verify content stream of page 2 uses /Im1
        var contents2 = reader.Resolve(page2Dict.Get("Contents")) as PdfStream;
        var contentText = Encoding.Latin1.GetString(contents2!.RawData);
        Assert.Contains("/Im1", contentText);
    }

    [Fact]
    public void Concatenate_NoConflicts_NoRenames()
    {
        // Doc1 has /F1 -> Helvetica, Doc2 has /F2 -> Courier (no conflict)
        var pdf1 = PdfBuilder.BuildWithNamedFont("F1", "Helvetica", "Hello");
        var pdf2 = PdfBuilder.BuildWithNamedFont("F2", "Courier", "World");

        var editor = new PdfFileEditor();
        var result = editor.Concatenate(pdf1, pdf2);

        using var doc = Document.Open(result);
        Assert.Equal(2, doc.PageCount);

        // Page 1 keeps F1
        var page1Fonts = doc.Pages[1].Fonts;
        Assert.Single(page1Fonts);
        Assert.Equal("F1", page1Fonts[1].ResourceName);
        Assert.Equal("Helvetica", page1Fonts[1].BaseFont);

        // Page 2 keeps F2 (no rename needed)
        var page2Fonts = doc.Pages[2].Fonts;
        Assert.Single(page2Fonts);
        Assert.Equal("F2", page2Fonts[1].ResourceName);
        Assert.Equal("Courier", page2Fonts[1].BaseFont);
    }

    [Fact]
    public void Concatenate_SameFontName_SameFont_NoRename()
    {
        // Both docs have /F1 -> Helvetica (identical resource) — should NOT rename
        var pdf1 = PdfBuilder.BuildWithNamedFont("F1", "Helvetica", "Hello");
        var pdf2 = PdfBuilder.BuildWithNamedFont("F1", "Helvetica", "World");

        var editor = new PdfFileEditor();
        var result = editor.Concatenate(pdf1, pdf2);

        using var doc = Document.Open(result);
        Assert.Equal(2, doc.PageCount);

        // Both pages should have F1 -> Helvetica (no rename)
        var page1Fonts = doc.Pages[1].Fonts;
        Assert.Single(page1Fonts);
        Assert.Equal("F1", page1Fonts[1].ResourceName);

        var page2Fonts = doc.Pages[2].Fonts;
        Assert.Single(page2Fonts);
        Assert.Equal("F1", page2Fonts[1].ResourceName);
    }

    [Fact]
    public void Concatenate_ThreeDocuments_DifferentFontsPerPage()
    {
        // All three docs have /F1 but with different fonts — each page keeps its own resources.
        var pdf1 = PdfBuilder.BuildWithNamedFont("F1", "Helvetica", "One");
        var pdf2 = PdfBuilder.BuildWithNamedFont("F1", "Courier", "Two");
        var pdf3 = PdfBuilder.BuildWithNamedFont("F1", "Times-Roman", "Three");

        var editor = new PdfFileEditor();
        var result = editor.Concatenate(pdf1, pdf2, pdf3);

        using var doc = Document.Open(result);
        Assert.Equal(3, doc.PageCount);

        // Each page resolves its font via its own /Resources
        Assert.Equal("Helvetica", doc.Pages[1].Fonts[1].BaseFont);
        Assert.Equal("Courier", doc.Pages[2].Fonts[1].BaseFont);
        Assert.Equal("Times-Roman", doc.Pages[3].Fonts[1].BaseFont);
    }

    [Fact]
    public void Concatenate_ThreeDocuments_SharedAndDifferentFonts()
    {
        // Doc1: F1 -> Helvetica, Doc2: F1 -> Courier, Doc3: F1 -> Helvetica
        var pdf1 = PdfBuilder.BuildWithNamedFont("F1", "Helvetica", "One");
        var pdf2 = PdfBuilder.BuildWithNamedFont("F1", "Courier", "Two");
        var pdf3 = PdfBuilder.BuildWithNamedFont("F1", "Helvetica", "Three");

        var editor = new PdfFileEditor();
        var result = editor.Concatenate(pdf1, pdf2, pdf3);

        using var doc = Document.Open(result);
        Assert.Equal(3, doc.PageCount);

        Assert.Equal("Helvetica", doc.Pages[1].Fonts[1].BaseFont);
        Assert.Equal("Courier", doc.Pages[2].Fonts[1].BaseFont);
        Assert.Equal("Helvetica", doc.Pages[3].Fonts[1].BaseFont);
    }

    [Fact]
    public void Concatenate_TwoPdfsWithSameXObjectName_BothPagesRenderCorrectly()
    {
        // Both PDFs use /Im1 — with per-page resource dicts, no renaming needed.
        var pdf1 = PdfBuilder.BuildWithNamedImage("Im1", 255, 0, 0);
        var pdf2 = PdfBuilder.BuildWithNamedImage("Im1", 0, 255, 0);

        var editor = new PdfFileEditor();
        var result = editor.Concatenate(pdf1, pdf2);

        // Both pages must be present and each must have /Im1 in their own resource dicts
        using var doc = Document.Open(result);
        Assert.Equal(2, doc.Pages.Count);
    }
}
