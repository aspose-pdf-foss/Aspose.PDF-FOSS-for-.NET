using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Converters;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

public class PdfToMarkdownConverterTests
{
    [Fact]
    public void SaveAsMarkdown_ExtractsText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello Markdown) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);
        Assert.Contains("Hello Markdown", md);
    }

    [Fact]
    public void SavePageAsMarkdown_SinglePage()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Page content) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SavePageAsMarkdown(doc, 1);
        Assert.Contains("Page content", md);
    }

    [Fact]
    public void SaveAllPagesAsMarkdown_ReturnsArray()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (MD text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var pages = converter.SaveAllPagesAsMarkdown(doc);
        Assert.Single(pages);
    }

    [Fact]
    public void CustomOptions_PageBreak()
    {
        var opts = new MarkdownConverterOptions { PageBreak = "\n\n===\n\n" };
        var converter = new PdfToMarkdownConverter(opts);
        Assert.NotNull(converter);
    }

    [Fact]
    public void EscapeMarkdown_SpecialCharacters()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (a*b_c[d]) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);
        // Special markdown characters should be escaped
        Assert.Contains("\\*", md);
        Assert.Contains("\\_", md);
        Assert.Contains("\\[", md);
        Assert.Contains("\\]", md);
    }

    [Fact]
    public void DefaultOptions_ValuesCorrect()
    {
        var opts = new MarkdownConverterOptions();
        Assert.Equal(24, opts.H1Threshold);
        Assert.Equal(18, opts.H2Threshold);
        Assert.Equal(14, opts.H3Threshold);
        Assert.True(opts.IncludeTables);
        Assert.Equal("\n---\n\n", opts.PageBreak);
        Assert.Null(opts.ImageOutputDirectory);
    }

    // ── Image extraction tests ──

    [Fact]
    public void ImageExtraction_ProducesMarkdownImageSyntax()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);

        var tmpDir = Path.Combine(Path.GetTempPath(), $"md_img_test_{Guid.NewGuid():N}");
        try
        {
            var opts = new MarkdownConverterOptions { ImageOutputDirectory = tmpDir };
            var converter = new PdfToMarkdownConverter(opts);
            var md = converter.SaveAsMarkdown(doc);

            Assert.Contains("![Image](images/image_p1_0.png)", md);
            Assert.True(File.Exists(Path.Combine(tmpDir, "image_p1_0.png")));
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ImageExtraction_SkippedWhenNoOutputDirectory()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);

        Assert.DoesNotContain("![Image]", md);
    }

    // ── Link preservation tests ──

    [Fact]
    public void LinkPreservation_WrapsTextInLinkSyntax()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 710 Td (Click here) Tj ET");
        var data = PdfBuilder.BuildWithTextAndLink(content, "https://example.com",
            linkLlx: 0, linkLly: 0, linkUrx: 612, linkUry: 792);
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);

        Assert.Contains("[Click here](https://example.com)", md);
    }

    [Fact]
    public void LinkPreservation_StandaloneLinkEmitted()
    {
        // Link annotation with no overlapping text
        var data = PdfBuilder.BuildWithLinkAnnotation("https://standalone.example.com");
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);

        Assert.Contains("[Link](https://standalone.example.com)", md);
    }

    // ── Bold/Italic detection tests ──

    [Fact]
    public void BoldText_WrappedInDoubleAsterisks()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Bold text here) Tj ET");
        var data = PdfBuilder.BuildWithMultipleFonts(
            ("F1", "Helvetica-Bold", content));
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);

        Assert.Contains("**Bold text here**", md);
    }

    [Fact]
    public void ItalicText_WrappedInSingleAsterisks()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Italic text here) Tj ET");
        var data = PdfBuilder.BuildWithMultipleFonts(
            ("F1", "Helvetica-Oblique", content));
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);

        Assert.Contains("*Italic text here*", md);
    }

    [Fact]
    public void BoldItalicText_WrappedInTripleAsterisks()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Bold Italic) Tj ET");
        var data = PdfBuilder.BuildWithMultipleFonts(
            ("F1", "Helvetica-BoldOblique", content));
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);

        Assert.Contains("***Bold Italic***", md);
    }

    [Fact]
    public void RegularText_NoFormatting()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Normal text) Tj ET");
        var data = PdfBuilder.BuildWithMultipleFonts(
            ("F1", "Helvetica", content));
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);

        Assert.Contains("Normal text", md);
        Assert.DoesNotContain("**Normal text**", md);
        Assert.DoesNotContain("*Normal text*", md);
    }

    // ── Horizontal rule detection tests ──

    [Fact]
    public void HorizontalRule_DetectedFromLineOperators()
    {
        var data = PdfBuilder.BuildWithHorizontalRule();
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);

        Assert.Contains("---", md);
        Assert.Contains("Above the line", md);
        Assert.Contains("Below the line", md);
    }

    [Fact]
    public void HorizontalRule_NotDetectedForShortLines()
    {
        // Short line: only 100 units wide on a 612-wide page (< 80%)
        var content = Encoding.ASCII.GetBytes("100 400 m 200 400 l S\nBT /F1 12 Tf (Some text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToMarkdownConverter();
        var md = converter.SaveAsMarkdown(doc);

        // The short line should not be detected as a horizontal rule
        // "---" might appear in page break settings but not from line detection
        Assert.Contains("Some text", md);
        // Count occurrences of "---" - should only be 0 since it's a single page
        var ruleCount = md.Split("---").Length - 1;
        Assert.Equal(0, ruleCount);
    }
}
