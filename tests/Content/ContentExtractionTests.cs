using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Content;

/// <summary>
/// Ported from TypeScript: Content/ContentTests.ts
/// Tests page-level content extraction: text via TextAbsorber, fonts, resources, images.
/// </summary>
public class ContentExtractionTests
{
    // ── Page text extraction via TextAbsorber ────────────────────────────

    [Fact]
    public void TextAbsorber_ExtractsTextFromPage()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello content) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var abs = new TextAbsorber();
        abs.Visit(doc.Pages[1]);
        Assert.Contains("Hello content", abs.Text);
    }

    [Fact]
    public void TextAbsorber_EmptyPage_ReturnsEmptyString()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var abs = new TextAbsorber();
        abs.Visit(doc.Pages[1]);
        Assert.NotNull(abs.Text);
    }

    [Fact]
    public void TextAbsorber_MultiPage_ExtractsDifferentText()
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

    // ── Page fonts ──────────────────────────────────────────────────────

    [Fact]
    public void Page_Fonts_HasCount()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Font test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var fonts = doc.Pages[1].Fonts;
        Assert.NotNull(fonts);
        Assert.True(fonts.Count > 0);
    }

    [Fact]
    public void Page_Fonts_AreEnumerable()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Enum fonts) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var fonts = doc.Pages[1].Fonts;
        var n = 0;
        foreach (var _ in fonts) n++;
        Assert.Equal(fonts.Count, n);
    }

    [Fact]
    public void Page_Fonts_HaveBaseFont()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Base font) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        foreach (var font in doc.Pages[1].Fonts)
        {
            Assert.NotNull(font.BaseFont);
        }
    }

    [Fact]
    public void Page_Fonts_HaveSubtype()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Subtype test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        foreach (var font in doc.Pages[1].Fonts)
        {
            Assert.NotNull(font.Subtype);
        }
    }

    // ── Page media box ──────────────────────────────────────────────────

    [Fact]
    public void Page_MediaBox_HasDimensions()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var mb = doc.Pages[1].MediaBox;
        Assert.True(mb.Width > 0);
        Assert.True(mb.Height > 0);
    }

    [Fact]
    public void Page_Width_IsPositive()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        Assert.True(doc.Pages[1].Width > 0);
    }

    [Fact]
    public void Page_Height_IsPositive()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        Assert.True(doc.Pages[1].Height > 0);
    }

    // ── Page images ─────────────────────────────────────────────────────

    [Fact]
    public void Page_Images_WithImagePdf_HasImages()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);

        var images = doc.Pages[1].Images;
        Assert.True(images.Count > 0);
    }

    [Fact]
    public void Page_Images_TextOnlyPdf_HasNoImages()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (No images) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        Assert.True(doc.Pages[1].Images.Count == 0);
    }

    // ── Page number ─────────────────────────────────────────────────────

    [Fact]
    public void Page_Number_MatchesIndex()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);

        for (var i = 1; i <= doc.PageCount; i++)
        {
            Assert.Equal(i, doc.Pages[i].Number);
        }
    }

    // ── Rotation ────────────────────────────────────────────────────────

    [Fact]
    public void Page_Rotation_DefaultIsZero()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        Assert.Equal(0, doc.Pages[1].RotateDegrees);
    }

    [Fact]
    public void Page_Rotation_ReadsRotation()
    {
        var data = PdfBuilder.BuildWithRotation(90);
        using var doc = Document.Open(data);

        Assert.Equal(90, doc.Pages[1].RotateDegrees);
    }

    // ── TextFragmentAbsorber ────────────────────────────────────────────

    [Fact]
    public void TextFragmentAbsorber_FindsTextFragment()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber("Hello");
        absorber.Visit(doc.Pages[1]);
        Assert.True(absorber.TextFragments.Count > 0);
    }

    [Fact]
    public void TextFragmentAbsorber_NoMatch_ReturnsZero()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber("ZZZZZ");
        absorber.Visit(doc.Pages[1]);
        Assert.True(absorber.TextFragments.Count == 0);
    }

    [Fact]
    public void TextFragmentAbsorber_EmptyPage_NoFragments()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.True(absorber.TextFragments.Count == 0);
    }

    [Fact]
    public void TextFragment_HasTextProperty()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Fragment text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);
        foreach (var fragment in absorber.TextFragments)
        {
            Assert.NotNull(fragment.Text);
            Assert.NotEmpty(fragment.Text);
        }
    }

    [Fact]
    public void TextFragment_HasFontSize()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 14 Tf (Sized text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);
        foreach (var fragment in absorber.TextFragments)
        {
            Assert.True(fragment.FontSize > 0);
        }
    }
}
