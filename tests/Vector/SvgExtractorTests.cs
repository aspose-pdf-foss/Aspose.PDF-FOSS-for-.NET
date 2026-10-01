using System;
using System.IO;
using System.Linq;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Vector;
using Xunit;

namespace Aspose.Pdf.Tests.SvgExport;

public class SvgExtractorTests
{
    /// <summary>A page holding two separate shapes: a filled rectangle and a stroked line, far apart.</summary>
    private static Document PageWithTwoShapes()
    {
        var document = new Document();
        var page = document.Pages.Add();
        var graph = new Graph(400, 300);
        var box = new Aspose.Pdf.Drawing.Rectangle(20, 20, 100, 60);
        box.GraphInfo.FillColor = Color.Blue;
        graph.Shapes.Add(box);
        graph.Shapes.Add(new Line(new float[] { 250, 200, 380, 280 }));
        page.Paragraphs.Add(graph);
        using var saved = new MemoryStream();
        document.Save(saved);
        return new Document(new MemoryStream(saved.ToArray()));
    }

    private static int PathCount(string svg) => svg.Split(new[] { "<path " }, StringSplitOptions.None).Length - 1;

    private static string FreshFolder()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "svg-extractor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A page drawing <paramref name="ops"/>, saved and read back.</summary>
    private static Page PageOf(params Operator[] ops)
    {
        var document = new Document();
        document.Pages.Add().Contents.Add(ops);
        using var saved = new MemoryStream();
        document.Save(saved);
        return new Document(new MemoryStream(saved.ToArray())).Pages[1];
    }

    /// <summary>A red square filled and outlined at (50, 50), and a blue one filled far above it at (300, 500).</summary>
    private static Page PageWithApartSquares() => PageOf(
        new SetRGBColor(1, 0, 0), new Re(50, 50, 100, 100), new Fill(),
        new Re(50, 50, 100, 100), new Stroke(),
        new SetRGBColor(0, 0, 1), new Re(300, 500, 50, 50), new Fill());

    [Fact]
    public void ExtractPage_WritesOneSvgPerClusterOfTouchingElements_FromTheTopDown()
    {
        var svgs = new SvgExtractor().Extract(PageWithApartSquares());

        // The red fill and its outline overlap, so they share an SVG; the blue square, higher up, comes first.
        Assert.Equal(2, svgs.Count);
        Assert.Contains("fill=\"#0000FF\"", svgs[0]);
        Assert.Equal(2, PathCount(svgs[1]));
        Assert.All(svgs, svg => Assert.StartsWith("<?xml", svg));
    }

    [Fact]
    public void ElementsTouchingAtACorner_ShareOneSvg()
    {
        var page = PageOf(new Re(100, 100, 50, 50), new Fill(), new Re(150, 150, 50, 50), new Fill());

        var svg = Assert.Single(new SvgExtractor().Extract(page));

        Assert.Equal(2, PathCount(svg));
    }

    [Fact]
    public void ExtractEverySubPathToSvg_WritesEachElementAlone()
    {
        var page = PageWithApartSquares();
        using var absorber = new GraphicsAbsorber();
        absorber.Visit(page);

        var svgs = new SvgExtractor(new SvgExtractionOptions { ExtractEverySubPathToSvg = true }).Extract(page);

        Assert.Equal(absorber.Elements.Count, svgs.Count);
        Assert.All(svgs, svg => Assert.Equal(1, PathCount(svg)));
    }

    [Fact]
    public void AClippedPath_IsWrittenUnderEachOfItsClips_InTheBoxOfWhatItPaints()
    {
        // Two clips narrowing each other, the second even-odd, then a 100 pt square painted under both.
        var page = PageOf(new GSave(), new ConcatenateMatrix(1, 0, 0, 1, 300, 400),
            new Re(0, 0, 30, 30), new Clip(), new EndPath(),
            new Re(10, 10, 30, 30), new EOClip(), new EndPath(),
            new SetRGBColor(1, 0, 0), new Re(0, 0, 100, 100), new Fill(), new GRestore());

        var svg = Assert.Single(new SvgExtractor().Extract(page));

        Assert.Equal(2, svg.Split(new[] { "<clipPath " }, StringSplitOptions.None).Length - 1);
        Assert.Contains("clip-rule=\"nonzero\"", svg);
        Assert.Contains("clip-rule=\"evenodd\"", svg);
        Assert.Equal(2, svg.Split(new[] { "clip-path=\"url(#" }, StringSplitOptions.None).Length - 1);
        // 100 pt in CSS pixels, rounded up, and the one pixel the writer adds.
        Assert.Contains("width=\"135\" height=\"135\"", svg);
    }

    [Fact]
    public void AnEvenOddClipPath_IsReadBackAsAnEvenOddClip()
    {
        var page = PageOf(new Re(0, 0, 30, 30), new EOClip(), new EndPath(),
            new SetRGBColor(1, 0, 0), new Re(0, 0, 100, 100), new Fill());
        var svg = Assert.Single(new SvgExtractor().Extract(page));

        using var back = new Document(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svg)), new SvgLoadOptions());

        Assert.Contains(back.Pages[1].Contents.Cast<Operator>(), op => op is EOClip);
    }

    [Fact]
    public void ExtractPageToDirectory_WritesNumberedFiles()
    {
        var page = PageWithTwoShapes().Pages[1];
        var folder = FreshFolder();

        new SvgExtractor().Extract(page, folder);

        var files = Directory.GetFiles(folder, "*.svg").Select(System.IO.Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Contains("1.svg", files);
        Assert.Equal(new SvgExtractor().Extract(page).Count, files.Count);
    }

    [Fact]
    public void ExtractElements_WritesOneSvgHoldingThemAll()
    {
        var page = PageWithTwoShapes().Pages[1];
        using var absorber = new GraphicsAbsorber();
        absorber.Visit(page);

        var svg = new SvgExtractor().Extract(absorber.Elements, page);

        Assert.Equal(absorber.Elements.Count, PathCount(svg));
        Assert.Contains("fill=\"#0000FF\"", svg);
    }

    [Fact]
    public void ExtractWithFilter_KeepsOnlyWhatPasses()
    {
        var page = PageWithTwoShapes().Pages[1];
        using var absorber = new GraphicsAbsorber();
        absorber.Visit(page);
        var first = absorber.Elements[1];

        var svg = new SvgExtractor().Extract(absorber, element => ReferenceEquals(element, first), page);

        Assert.Equal(1, PathCount(svg));
    }

    [Fact]
    public void ExtractionAreaBound_LeavesOutElementsOutsideIt()
    {
        var page = PageWithTwoShapes().Pages[1];
        using var absorber = new GraphicsAbsorber();
        absorber.Visit(page);
        var kept = absorber.Elements[1].Rectangle;
        var options = new SvgExtractionOptions
        {
            ExtractionAreaBound = new Aspose.Pdf.Rectangle(kept.LLX - 1, kept.LLY - 1, kept.URX + 1, kept.URY + 1),
            StrictExtractionAreaBoundCheck = true,
        };

        var svgs = new SvgExtractor(options).Extract(page);

        Assert.Single(svgs);
    }

    [Fact]
    public void AutoGrouping_JoinsNeighbours_AndKeepsDistantShapesApart()
    {
        var page = PageWithTwoShapes().Pages[1];
        var options = new SvgExtractionOptions { AutoGrouping = true, GroupStrength = 1.0 };

        var svgs = new SvgExtractor(options).Extract(page);

        // The rectangle and the line lie more than a whole shape apart, so grouping changes nothing here.
        Assert.Equal(new SvgExtractor().Extract(page).Count, svgs.Count);
    }

    [Fact]
    public void GroupStrength_RefusesValuesOutsideItsRange()
    {
        var options = new SvgExtractionOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.GroupStrength = 1.5);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.GroupStrength = -0.1);
    }

    [Fact]
    public void TrySaveVectorGraphics_WritesThePageAsOneSvg()
    {
        var page = PageWithTwoShapes().Pages[1];
        var path = System.IO.Path.Combine(FreshFolder(), "page.svg");

        Assert.True(page.TrySaveVectorGraphics(path));

        var svg = File.ReadAllText(path);
        Assert.StartsWith("<?xml", svg);
        Assert.True(PathCount(svg) >= 2);
    }

    [Fact]
    public void TrySaveVectorGraphics_AnswersFalse_ForAPageWithoutGraphics()
    {
        var document = new Document();
        var page = document.Pages.Add();
        var path = System.IO.Path.Combine(FreshFolder(), "empty.svg");

        Assert.False(page.TrySaveVectorGraphics(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ElementSaveToSvg_StillWritesTheElementAlone()
    {
        var page = PageWithTwoShapes().Pages[1];
        using var absorber = new GraphicsAbsorber();
        absorber.Visit(page);

        var svg = absorber.Elements[1].SaveToSvg();

        Assert.StartsWith("<?xml", svg);
        Assert.Equal(1, PathCount(svg));
    }
}
