using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Content;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Content;

public class PathExtractorTests
{
    [Fact]
    public void ExtractsStrokedLine()
    {
        var content = Encoding.ASCII.GetBytes("100 200 m 300 400 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var extractor = new PathExtractor();
        extractor.Visit(doc.Pages[1]);

        Assert.Single(extractor.Paths);
        var path = extractor.Paths[0];
        Assert.Equal(PathPaintMode.Stroke, path.PaintMode);
        Assert.Equal(2, path.Segments.Count);
        Assert.Equal(PathOperationType.MoveTo, path.Segments[0].Type);
        Assert.Equal(PathOperationType.LineTo, path.Segments[1].Type);
    }

    [Fact]
    public void ExtractsFilledRectangle()
    {
        var content = Encoding.ASCII.GetBytes("1 0 0 rg 50 50 100 200 re f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var extractor = new PathExtractor();
        extractor.Visit(doc.Pages[1]);

        Assert.Single(extractor.Paths);
        var path = extractor.Paths[0];
        Assert.Equal(PathPaintMode.Fill, path.PaintMode);
        Assert.Equal(1.0, path.FillColor[0]); // red
        Assert.Equal(0.0, path.FillColor[1]);
        Assert.Equal(0.0, path.FillColor[2]);
    }

    [Fact]
    public void ExtractsBezierCurve()
    {
        var content = Encoding.ASCII.GetBytes("100 100 m 150 200 200 200 250 100 c S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var extractor = new PathExtractor();
        extractor.Visit(doc.Pages[1]);

        Assert.Single(extractor.Paths);
        Assert.Equal(2, extractor.Paths[0].Segments.Count);
        Assert.Equal(PathOperationType.CurveTo, extractor.Paths[0].Segments[1].Type);
        Assert.Equal(6, extractor.Paths[0].Segments[1].Points.Length);
    }

    [Fact]
    public void Bounds_CalculatedCorrectly()
    {
        var content = Encoding.ASCII.GetBytes("10 20 m 100 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var extractor = new PathExtractor();
        extractor.Visit(doc.Pages[1]);

        var bounds = extractor.Paths[0].Bounds;
        Assert.Equal(10, bounds.LLX);
        Assert.Equal(20, bounds.LLY);
        Assert.Equal(100, bounds.URX);
        Assert.Equal(200, bounds.URY);
    }

    [Fact]
    public void MultiplePaths_ExtractedSeparately()
    {
        var content = Encoding.ASCII.GetBytes(
            "10 10 m 50 50 l S " +
            "100 100 m 200 200 l f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var extractor = new PathExtractor();
        extractor.Visit(doc.Pages[1]);

        Assert.Equal(2, extractor.Paths.Count);
        Assert.Equal(PathPaintMode.Stroke, extractor.Paths[0].PaintMode);
        Assert.Equal(PathPaintMode.Fill, extractor.Paths[1].PaintMode);
    }

    [Fact]
    public void LineWidth_Tracked()
    {
        var content = Encoding.ASCII.GetBytes("2.5 w 10 10 m 50 50 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var extractor = new PathExtractor();
        extractor.Visit(doc.Pages[1]);

        Assert.Equal(2.5, extractor.Paths[0].LineWidth);
    }

    [Fact]
    public void StrokeColor_Tracked()
    {
        var content = Encoding.ASCII.GetBytes("0 0.5 1 RG 10 10 m 50 50 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var extractor = new PathExtractor();
        extractor.Visit(doc.Pages[1]);

        var color = extractor.Paths[0].StrokeColor;
        Assert.Equal(0, color[0]);
        Assert.Equal(0.5, color[1]);
        Assert.Equal(1, color[2]);
    }

    [Fact]
    public void EndPathNoOp_ClearsSegments()
    {
        // "n" ends path without painting
        var content = Encoding.ASCII.GetBytes("10 10 m 50 50 l n 100 100 m 200 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var extractor = new PathExtractor();
        extractor.Visit(doc.Pages[1]);

        // First path (n) should not produce output; only second path (S)
        Assert.Single(extractor.Paths);
    }

    [Fact]
    public void EmptyPage_NoPaths()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var extractor = new PathExtractor();
        extractor.Visit(doc.Pages[1]);

        Assert.Empty(extractor.Paths);
    }
}
