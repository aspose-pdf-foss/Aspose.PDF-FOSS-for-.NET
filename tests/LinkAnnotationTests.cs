using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class LinkAnnotationTests
{
    [Fact]
    public void LinkAnnotation_Type_IsLink()
    {
        var data = PdfBuilder.BuildWithLinkAnnotation();
        using var doc = Document.Open(data);
        var annot = doc.Pages[1].Annotations[1];
        Assert.Equal(AnnotationType.Link, annot.AnnotationType);
    }

    [Fact]
    public void LinkAnnotation_Uri_ParsesCorrectly()
    {
        var data = PdfBuilder.BuildWithLinkAnnotation("https://aspose.com");
        using var doc = Document.Open(data);
        var annot = doc.Pages[1].Annotations[1];
        Assert.IsType<LinkAnnotation>(annot);
        var link = (LinkAnnotation)annot;
        Assert.Equal("https://aspose.com", link.Uri);
    }

    [Fact]
    public void LinkAnnotation_Rect_HasValue()
    {
        var data = PdfBuilder.BuildWithLinkAnnotation();
        using var doc = Document.Open(data);
        var annot = doc.Pages[1].Annotations[1];
        Assert.NotNull(annot.Rect);
        Assert.Equal(72, annot.Rect!.LLX);
        Assert.Equal(700, annot.Rect!.LLY);
        Assert.Equal(200, annot.Rect!.URX);
        Assert.Equal(720, annot.Rect!.URY);
    }

    [Fact]
    public void TextAnnotation_IsType()
    {
        var data = PdfBuilder.BuildWithAnnotation();
        using var doc = Document.Open(data);
        var annot = doc.Pages[1].Annotations[1];
        Assert.IsType<TextAnnotation>(annot);
    }

    [Fact]
    public void TextAnnotation_Contents()
    {
        var data = PdfBuilder.BuildWithAnnotation();
        using var doc = Document.Open(data);
        var annot = (TextAnnotation)doc.Pages[1].Annotations[1];
        Assert.Equal("A note", annot.Contents);
    }
}
