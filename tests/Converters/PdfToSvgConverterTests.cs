using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Converters;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>
/// Ported from TypeScript: Converters/SvgConverterTests.ts
/// Tests PdfToSvgConverter and SvgDevice with synthetic PDFs.
/// </summary>
public class PdfToSvgConverterTests
{
    [Fact]
    public void Constructor_CreatesInstance()
    {
        var converter = new PdfToSvgConverter();
        Assert.NotNull(converter);
    }

    [Fact]
    public void SavePageAsSvg_ReturnsSvgMarkupForPage1()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (SVG test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToSvgConverter();
        var svg = converter.SavePageAsSvg(doc, 1);
        Assert.Contains("<svg", svg);
        Assert.Contains("xmlns", svg);
    }

    [Fact]
    public void SaveAllPagesAsSvg_ReturnsArrayMatchingPageCount()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);

        var converter = new PdfToSvgConverter();
        var pages = converter.SaveAllPagesAsSvg(doc);
        Assert.Equal(3, pages.Length);
        foreach (var svg in pages)
        {
            Assert.Contains("<svg", svg);
        }
    }

    [Fact]
    public void SavePageAsSvg_ContainsTextContent()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (SVG text content) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToSvgConverter();
        var svg = converter.SavePageAsSvg(doc, 1);
        Assert.Contains("SVG text content", svg);
    }

    [Fact]
    public void SavePageAsSvg_ContainsViewBox()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var converter = new PdfToSvgConverter();
        var svg = converter.SavePageAsSvg(doc, 1);
        // Page geometry is carried by the pt-space viewBox; width/height are CSS px.
        Assert.Contains("viewBox=\"0 0 612 792\"", svg);
        Assert.Contains("width=\"816\"", svg);
    }

    [Fact]
    public void SavePageAsSvg_ContainsPathForRectangle()
    {
        var content = Encoding.ASCII.GetBytes("100 200 50 30 re S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToSvgConverter();
        var svg = converter.SavePageAsSvg(doc, 1);
        Assert.Contains("<path", svg);
    }

    [Fact]
    public void SvgDevice_CanBeConstructed()
    {
        var device = new SvgDevice();
        Assert.NotNull(device);
    }

    [Fact]
    public void SvgDevice_Process_ReturnsSvg()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Device test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.StartsWith("<?xml", svg);
        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);
    }

    [Fact]
    public void SvgDevice_Process_ContainsTextElement()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello SVG) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("<text", svg);
        Assert.Contains("Hello SVG", svg);
    }

    [Fact]
    public void SaveAllPagesAsSvg_SinglePage_ReturnsSingleElement()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (single) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToSvgConverter();
        var pages = converter.SaveAllPagesAsSvg(doc);
        Assert.Single(pages);
    }
}
