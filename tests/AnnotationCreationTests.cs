using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class AnnotationCreationTests
{
    [Theory]
    [InlineData(ColorsOfCMYK.Cyan, "cyan")]
    [InlineData(ColorsOfCMYK.Magenta, "magenta")]
    [InlineData(ColorsOfCMYK.Yellow, "yellow")]
    [InlineData(ColorsOfCMYK.Black, "black")]
    public void ColorBar_XfdfColorName_RoundTrips(ColorsOfCMYK channel, string name)
    {
        Assert.Equal(name, ColorBarAnnotation.XfdfColorName(channel));
        Assert.Equal(channel, ColorBarAnnotation.FromXfdfColorName(name));
    }

    [Fact]
    public void ColorBar_FromXfdfColorName_RejectsAnUnknownName()
    {
        Assert.Throws<ArgumentException>(() => ColorBarAnnotation.FromXfdfColorName("orange"));
    }

    [Fact]
    public void AddInkAnnotation_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var paths = new[] { new double[] { 100, 200, 150, 250, 200, 200 } };
        page.Annotations.AddInkAnnotation(
            new Rectangle(100, 200, 200, 250), paths);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Single(doc2.Pages[1].Annotations);
        Assert.Equal(AnnotationType.Ink, doc2.Pages[1].Annotations[1].AnnotationType);
    }

    [Fact]
    public void AddStampAnnotation_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        doc.Pages[1].Annotations.AddStampAnnotation(
            new Rectangle(100, 700, 200, 750), "Approved!", "Approved");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Single(doc2.Pages[1].Annotations);
        Assert.Equal(AnnotationType.Stamp, doc2.Pages[1].Annotations[1].AnnotationType);
        Assert.Equal("Approved!", doc2.Pages[1].Annotations[1].Contents);
    }

    [Fact]
    public void AddCaretAnnotation_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        doc.Pages[1].Annotations.AddCaretAnnotation(
            new Rectangle(100, 700, 110, 710), "insert here");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Single(doc2.Pages[1].Annotations);
        Assert.Equal(AnnotationType.Caret, doc2.Pages[1].Annotations[1].AnnotationType);
    }

    [Fact]
    public void AddRedactAnnotation_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        doc.Pages[1].Annotations.AddRedactAnnotation(
            new Rectangle(100, 700, 300, 720));

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Single(doc2.Pages[1].Annotations);
        Assert.Equal(AnnotationType.Redact, doc2.Pages[1].Annotations[1].AnnotationType);
    }

    [Fact]
    public void AddFileAttachmentAnnotation_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var fileData = "Hello World"u8.ToArray();
        doc.Pages[1].Annotations.AddFileAttachmentAnnotation(
            new Rectangle(100, 700, 120, 720), "See attached", "readme.txt", fileData);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Single(doc2.Pages[1].Annotations);
        Assert.Equal(AnnotationType.FileAttachment, doc2.Pages[1].Annotations[1].AnnotationType);
    }

    [Fact]
    public void AddSquigglyAnnotation_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        doc.Pages[1].Annotations.AddSquigglyAnnotation(
            new Rectangle(72, 700, 200, 720));

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Single(doc2.Pages[1].Annotations);
        Assert.Equal(AnnotationType.Squiggly, doc2.Pages[1].Annotations[1].AnnotationType);
    }

    [Fact]
    public void AddPolygonAnnotation_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var vertices = new double[] { 100, 200, 200, 300, 150, 350 };
        doc.Pages[1].Annotations.AddPolygonAnnotation(
            new Rectangle(100, 200, 200, 350), vertices);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Single(doc2.Pages[1].Annotations);
        Assert.Equal(AnnotationType.Polygon, doc2.Pages[1].Annotations[1].AnnotationType);
    }

    [Fact]
    public void AddPolyLineAnnotation_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var vertices = new double[] { 100, 200, 200, 300, 300, 250 };
        doc.Pages[1].Annotations.AddPolyLineAnnotation(
            new Rectangle(100, 200, 300, 300), vertices);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Single(doc2.Pages[1].Annotations);
        Assert.Equal(AnnotationType.PolyLine, doc2.Pages[1].Annotations[1].AnnotationType);
    }

    [Fact]
    public void AddPopupAnnotation_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        doc.Pages[1].Annotations.AddPopupAnnotation(
            new Rectangle(200, 600, 400, 700), open: true);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Single(doc2.Pages[1].Annotations);
        Assert.Equal(AnnotationType.Popup, doc2.Pages[1].Annotations[1].AnnotationType);
    }

    [Fact]
    public void AddWatermarkAnnotation_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        doc.Pages[1].Annotations.AddWatermarkAnnotation(
            new Rectangle(0, 0, 612, 792), "CONFIDENTIAL");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Single(doc2.Pages[1].Annotations);
        Assert.Equal(AnnotationType.Watermark, doc2.Pages[1].Annotations[1].AnnotationType);
        Assert.Equal("CONFIDENTIAL", doc2.Pages[1].Annotations[1].Contents);
    }

    [Fact]
    public void WatermarkAnnotation_IsAnAnnotation_AndComesBackAsOne()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var watermark = new WatermarkAnnotation(page, new Rectangle(100, 500, 400, 600));
        watermark.Contents = "CONFIDENTIAL";
        // Taken by the collection as the annotation it is, not through an overload of its own.
        Annotation asAnnotation = watermark;
        page.Annotations.Add(asAnnotation, considerRotation: false);
        watermark.Opacity = 0.5;
        watermark.SetTextAndState(new[] { "HELLO", "Line 1" }, new Aspose.Pdf.Text.TextState { FontSize = 32 });

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var read = doc2.Pages[1].Annotations[1];
        Assert.IsType<WatermarkAnnotation>(read);
        Assert.Equal(AnnotationType.Watermark, read.AnnotationType);
        Assert.Equal("CONFIDENTIAL", read.Contents);
        Assert.Equal(100, read.Rect!.LLX, 3);
        // The text it was given after it joined the page is painted in its appearance.
        Assert.NotNull(read.Appearance["N"]);
    }

    [Fact]
    public void ANewWatermark_HandsOutANormalAppearanceToDrawInto_AndWhatIsDrawnSurvivesSaving()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var watermark = new WatermarkAnnotation(page, page.Rect);
        var form = watermark.Appearance["N"];
        Assert.NotNull(form);
        form.BBox = page.Rect;
        form.Contents.Add(new Operators.GSave());
        form.Contents.Add(new Operators.ConcatenateMatrix(new Matrix(30, 0, 0, 15, 0, 0)));
        form.Contents.Add(new Operators.GRestore());
        page.Annotations.Add(watermark, considerRotation: false);

        using var saved = Document.Open(doc.ToArray());
        var read = saved.Pages[1].Annotations[1];
        Assert.IsType<WatermarkAnnotation>(read);
        Assert.NotNull(read.NormalAppearance);
        Assert.Equal(3, read.NormalAppearance!.Contents.Count);
    }

    [Fact]
    public void OnlyAWatermarkIsGivenAnAppearanceToDrawInto()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        // Every other kind of annotation draws from its own geometry, and a caller asking for an
        // appearance it has not got is told so.
        Assert.Null(new SquareAnnotation(page, new Rectangle(10, 10, 20, 20)).Appearance["N"]);
        Assert.Null(new TextAnnotation(page, new Rectangle(10, 10, 20, 20)).Appearance["N"]);

        // Nor is one grafted onto a watermark that came out of a document without one.
        var watermark = new WatermarkAnnotation(page, new Rectangle(10, 10, 20, 20));
        watermark.Contents = "no appearance of its own";
        page.Annotations.Add(watermark, considerRotation: false);
        using var saved = Document.Open(doc.ToArray());
        Assert.Null(saved.Pages[1].Annotations[1].Appearance["N"]);
    }

    [Fact]
    public void AddMultipleAnnotationTypes()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        page.Annotations.AddTextAnnotation(new Rectangle(10, 10, 30, 30), "Note");
        page.Annotations.AddHighlightAnnotation(new Rectangle(72, 700, 200, 720));
        page.Annotations.AddInkAnnotation(new Rectangle(100, 500, 200, 550),
            new[] { new double[] { 100, 500, 150, 550 } });

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal(3, doc2.Pages[1].Annotations.Count);
    }
}
