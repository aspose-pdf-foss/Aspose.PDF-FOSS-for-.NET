using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public class PdfPageEditorTests
{
    [Fact]
    public void RotatePages_AllPages()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();
        var result = editor.RotatePages(input, 90);

        using var doc = Document.Open(result);
        Assert.Equal(90, doc.Pages[1].RotateDegrees);
    }

    [Fact]
    public void RotatePages_SpecificPage()
    {
        var input = PdfBuilder.BuildMultiPage(3);
        var editor = new PdfPageEditor();
        var result = editor.RotatePages(input, 180, 2);

        using var doc = Document.Open(result);
        Assert.Equal(0, doc.Pages[1].RotateDegrees);
        Assert.Equal(180, doc.Pages[2].RotateDegrees);
        Assert.Equal(0, doc.Pages[3].RotateDegrees);
    }

    [Fact]
    public void SetRotation_Absolute()
    {
        var input = PdfBuilder.BuildWithRotation(90);
        var editor = new PdfPageEditor();
        var result = editor.SetRotation(input, 270);

        using var doc = Document.Open(result);
        Assert.Equal(270, doc.Pages[1].RotateDegrees);
    }

    [Fact]
    public void ResizePages_AllPages()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();
        var newBox = new Rectangle(0, 0, 500, 700);
        var result = editor.ResizePages(input, newBox);

        using var doc = Document.Open(result);
        var mb = doc.Pages[1].MediaBox;
        Assert.Equal(500, mb.Width, 1);
        Assert.Equal(700, mb.Height, 1);
    }

    [Fact]
    public void SetCropBox_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();
        var cropRect = new Rectangle(10, 20, 400, 600);

        var result = editor.SetCropBox(input, 1, cropRect);

        var readBack = editor.GetCropBox(result, 1);
        Assert.NotNull(readBack);
        Assert.Equal(10, readBack!.LLX, 1);
        Assert.Equal(20, readBack.LLY, 1);
        Assert.Equal(400, readBack.URX, 1);
        Assert.Equal(600, readBack.URY, 1);
    }

    [Fact]
    public void SetTrimBox_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();
        var trimRect = new Rectangle(15, 15, 580, 780);

        var result = editor.SetTrimBox(input, 1, trimRect);

        var readBack = editor.GetTrimBox(result, 1);
        Assert.NotNull(readBack);
        Assert.Equal(15, readBack!.LLX, 1);
        Assert.Equal(15, readBack.LLY, 1);
        Assert.Equal(580, readBack.URX, 1);
        Assert.Equal(780, readBack.URY, 1);
    }

    [Fact]
    public void SetBleedBox_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();
        var bleedRect = new Rectangle(5, 5, 607, 787);

        var result = editor.SetBleedBox(input, 1, bleedRect);

        var readBack = editor.GetBleedBox(result, 1);
        Assert.NotNull(readBack);
        Assert.Equal(5, readBack!.LLX, 1);
        Assert.Equal(5, readBack.LLY, 1);
        Assert.Equal(607, readBack.URX, 1);
        Assert.Equal(787, readBack.URY, 1);
    }

    [Fact]
    public void SetArtBox_RoundTrip()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();
        var artRect = new Rectangle(50, 50, 500, 700);

        var result = editor.SetArtBox(input, 1, artRect);

        var readBack = editor.GetArtBox(result, 1);
        Assert.NotNull(readBack);
        Assert.Equal(50, readBack!.LLX, 1);
        Assert.Equal(50, readBack.LLY, 1);
        Assert.Equal(500, readBack.URX, 1);
        Assert.Equal(700, readBack.URY, 1);
    }

    [Fact]
    public void UnsetBoxes_ReturnNull()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();

        // No boxes explicitly set on a minimal PDF
        Assert.Null(editor.GetCropBox(input, 1));
        Assert.Null(editor.GetTrimBox(input, 1));
        Assert.Null(editor.GetBleedBox(input, 1));
        Assert.Null(editor.GetArtBox(input, 1));
    }

    [Fact]
    public void SetMargins_ReducesVisibleArea()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();

        // Get original MediaBox dimensions
        using var origDoc = Document.Open(input);
        var origMb = origDoc.Pages[1].MediaBox;
        var origWidth = origMb.Width;
        var origHeight = origMb.Height;

        // Set margins: 50 left, 30 bottom, 50 right, 30 top
        var result = editor.SetMargins(input, 1, 50, 30, 50, 30);

        // CropBox should now be inset from MediaBox
        var cropBox = editor.GetCropBox(result, 1);
        Assert.NotNull(cropBox);
        Assert.Equal(origMb.LLX + 50, cropBox!.LLX, 1);
        Assert.Equal(origMb.LLY + 30, cropBox.LLY, 1);
        Assert.Equal(origMb.URX - 50, cropBox.URX, 1);
        Assert.Equal(origMb.URY - 30, cropBox.URY, 1);

        // Visible area should be smaller than original
        Assert.True(cropBox.Width < origWidth);
        Assert.True(cropBox.Height < origHeight);
    }

    [Fact]
    public void ScalePage_ChangesMediaBox()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();

        // Get original dimensions
        using var origDoc = Document.Open(input);
        var origMb = origDoc.Pages[1].MediaBox;
        var origWidth = origMb.Width;
        var origHeight = origMb.Height;

        // Scale 2x horizontally, 0.5x vertically
        var result = editor.ScalePage(input, 1, 2.0, 0.5);

        using var doc = Document.Open(result);
        var mb = doc.Pages[1].MediaBox;
        Assert.Equal(origWidth * 2, mb.Width, 1);
        Assert.Equal(origHeight * 0.5, mb.Height, 1);
    }

    [Fact]
    public void ScalePage_UniformScale()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();

        using var origDoc = Document.Open(input);
        var origMb = origDoc.Pages[1].MediaBox;

        var result = editor.ScalePage(input, 1, 1.5, 1.5);

        using var doc = Document.Open(result);
        var mb = doc.Pages[1].MediaBox;
        Assert.Equal(origMb.Width * 1.5, mb.Width, 1);
        Assert.Equal(origMb.Height * 1.5, mb.Height, 1);
    }

    [Fact]
    public void SetCropBox_VerifiedInDocument()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();
        var cropRect = new Rectangle(25, 25, 575, 775);

        var result = editor.SetCropBox(input, 1, cropRect);

        // Verify via Document API that CropBox is set
        using var doc = Document.Open(result);
        var page = doc.Pages[1];
        var cb = page.CropBox;
        Assert.Equal(25, cb.LLX, 1);
        Assert.Equal(25, cb.LLY, 1);
        Assert.Equal(575, cb.URX, 1);
        Assert.Equal(775, cb.URY, 1);
    }

    [Fact]
    public void SetMultipleBoxes_IndependentlyStored()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();

        var result = editor.SetCropBox(input, 1, new Rectangle(10, 10, 590, 790));
        result = editor.SetTrimBox(result, 1, new Rectangle(20, 20, 580, 780));
        result = editor.SetBleedBox(result, 1, new Rectangle(5, 5, 607, 807));
        result = editor.SetArtBox(result, 1, new Rectangle(50, 50, 550, 750));

        var crop = editor.GetCropBox(result, 1);
        var trim = editor.GetTrimBox(result, 1);
        var bleed = editor.GetBleedBox(result, 1);
        var art = editor.GetArtBox(result, 1);

        Assert.NotNull(crop);
        Assert.NotNull(trim);
        Assert.NotNull(bleed);
        Assert.NotNull(art);

        Assert.Equal(10, crop!.LLX, 1);
        Assert.Equal(20, trim!.LLX, 1);
        Assert.Equal(5, bleed!.LLX, 1);
        Assert.Equal(50, art!.LLX, 1);
    }

    [Fact]
    public void SetCropBox_InvalidPageNumber_Throws()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new PdfPageEditor();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            editor.SetCropBox(input, 0, new Rectangle(0, 0, 100, 100)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            editor.SetCropBox(input, 2, new Rectangle(0, 0, 100, 100)));
    }
}
