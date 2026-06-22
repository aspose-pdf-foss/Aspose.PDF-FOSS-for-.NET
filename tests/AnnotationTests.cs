using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class AnnotationTests
{
    [Fact]
    public void Page_WithNoAnnotations_ReturnsEmptyCollection()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Empty(doc.Pages[1].Annotations);
    }

    [Fact]
    public void Page_WithAnnotations_ParsesCorrectly()
    {
        var data = PdfBuilder.BuildWithAnnotation();
        using var doc = Document.Open(data);
        var annots = doc.Pages[1].Annotations;
        Assert.Single(annots);
        Assert.Equal(AnnotationType.Text, annots[1].AnnotationType);
        Assert.Equal("A note", annots[1].Contents);
    }

    [Fact]
    public void Annotation_Rect_Parsed()
    {
        var data = PdfBuilder.BuildWithAnnotation();
        using var doc = Document.Open(data);
        var annot = doc.Pages[1].Annotations[1];
        Assert.NotNull(annot.Rect);
        Assert.Equal(100, annot.Rect!.LLX);
    }
}
