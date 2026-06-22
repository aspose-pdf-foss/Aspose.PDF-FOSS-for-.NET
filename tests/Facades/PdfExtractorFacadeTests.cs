using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

/// <summary>
/// Ported from TypeScript: Facades/PdfExtractorTests.ts
/// Tests text extraction (via TextAbsorber) and PdfContentEditor.ExtractText
/// as FOSS equivalent of PdfExtractor.
/// </summary>
public class PdfExtractorFacadeTests
{
    // ── Text extraction via TextAbsorber ─────────────────────────────────

    [Fact]
    public void TextAbsorber_ExtractsTextFromPage()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (hello world) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var abs = new TextAbsorber();
        abs.Visit(doc.Pages[1]);
        Assert.Contains("hello world", abs.Text);
    }

    [Fact]
    public void TextAbsorber_ProducesNonEmptyText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Extracted text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var abs = new TextAbsorber();
        abs.Visit(doc);
        Assert.True(abs.Text.Length > 0);
    }

    // ── PdfContentEditor.ExtractText ────────────────────────────────────

    [Fact]
    public void PdfContentEditor_ExtractText_ReturnsText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Extracted via editor) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);

        var editor = new PdfContentEditor();
        var text = editor.ExtractText(data, 1);
        Assert.Contains("Extracted via editor", text);
    }

    [Fact]
    public void PdfContentEditor_ExtractText_AllPages()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (All pages text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);

        var editor = new PdfContentEditor();
        var text = editor.ExtractText(data);
        Assert.Contains("All pages text", text);
    }

    [Fact]
    public void PdfContentEditor_ExtractText_EmptyPage()
    {
        var data = PdfBuilder.BuildMinimal();

        var editor = new PdfContentEditor();
        var text = editor.ExtractText(data, 1);
        Assert.NotNull(text);
    }

    // ── TextAbsorber on multi-page ──────────────────────────────────────

    [Fact]
    public void TextAbsorber_VisitDocument_DoesNotThrowOnBlank()
    {
        var data = PdfBuilder.BuildMultiPage(2);
        using var doc = Document.Open(data);

        var abs = new TextAbsorber();
        abs.Visit(doc);
        Assert.NotNull(abs.Text);
    }

    [Fact]
    public void TextAbsorber_PerPage_DoesNotThrow()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);

        foreach (var page in doc.Pages)
        {
            var abs = new TextAbsorber();
            abs.Visit(page);
            Assert.NotNull(abs.Text);
        }
    }

    // ── Single page verification ────────────────────────────────────────

    [Fact]
    public void SinglePagePdf_HasExactly1Page()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (single) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        Assert.Equal(1, doc.PageCount);
    }
}
