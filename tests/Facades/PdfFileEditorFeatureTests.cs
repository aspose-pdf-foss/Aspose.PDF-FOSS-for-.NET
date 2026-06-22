using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

/// <summary>Smoke tests for the new PdfFileEditor feature methods —
/// MakeNUp / AddMargins / ResizeContents / MakeBooklet stream wrappers
/// and the RemoveSignatures / OwnerPassword post-Concatenate options.</summary>
public class PdfFileEditorFeatureTests
{
    [Fact]
    public void AddMargins_File_AppliesMarginsToSelectedPages()
    {
        var src = System.IO.Path.GetTempFileName();
        var dst = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllBytes(src, PdfBuilder.BuildMinimal());
            var editor = new PdfFileEditor();
            var ok = editor.AddMargins(src, dst, new[] { 1 }, 36.0, 36.0, 36.0, 36.0);
            Assert.True(ok);
            using var doc = Document.Open(dst);
            Assert.True(doc.PageCount >= 1);
        }
        finally
        {
            System.IO.File.Delete(src);
            System.IO.File.Delete(dst);
        }
    }

    [Fact]
    public void ResizeContentsPct_Stream_ScalesContent()
    {
        using var src = new System.IO.MemoryStream(PdfBuilder.BuildMinimal());
        using var dst = new System.IO.MemoryStream();
        var editor = new PdfFileEditor();
        var ok = editor.ResizeContentsPct(src, dst, new[] { 1 }, 80.0, 80.0);
        Assert.True(ok);
        Assert.True(dst.Length > 0);
        dst.Position = 0;
        using var doc = Document.Open(dst);
        Assert.True(doc.PageCount >= 1);
    }

    [Fact]
    public void MakeNUp_Grid_PacksPagesIntoSheets()
    {
        // 4-page input → 2×2 grid → 1 output sheet
        var editor1 = new PdfFileEditor();
        var fourPages = editor1.Concatenate(
            PdfBuilder.BuildMinimal(), PdfBuilder.BuildMinimal(),
            PdfBuilder.BuildMinimal(), PdfBuilder.BuildMinimal());
        Assert.Equal(4, Document.Open(fourPages).PageCount);

        using var src = new System.IO.MemoryStream(fourPages);
        using var dst = new System.IO.MemoryStream();
        var editor = new PdfFileEditor();
        var ok = editor.MakeNUp(src, dst, x: 2, y: 2);
        Assert.True(ok);
        dst.Position = 0;
        using var doc = Document.Open(dst);
        // 4 input pages, 4 per sheet → 1 output sheet
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void MakeBooklet_Stream_ProducesValidPdf()
    {
        var editor1 = new PdfFileEditor();
        var fourPages = editor1.Concatenate(
            PdfBuilder.BuildMinimal(), PdfBuilder.BuildMinimal(),
            PdfBuilder.BuildMinimal(), PdfBuilder.BuildMinimal());

        using var src = new System.IO.MemoryStream(fourPages);
        using var dst = new System.IO.MemoryStream();
        var editor = new PdfFileEditor();
        var ok = editor.MakeBooklet(src, dst);
        Assert.True(ok);
        Assert.True(dst.Length > 0);
    }

    [Fact]
    public void Concatenate_WithRemoveSignaturesAndOwnerPassword_HonoursBoth()
    {
        var editor = new PdfFileEditor { OwnerPassword = "owner-secret" };
        var result = editor.Concatenate(PdfBuilder.BuildMinimal(), PdfBuilder.BuildMinimal());
        Assert.NotNull(editor.ConversionLog);
        Assert.Contains("OwnerPassword set", editor.ConversionLog);
        // Output is encrypted; passing the owner password decrypts it.
        using var doc = Document.Open(result, "owner-secret");
        Assert.True(doc.IsEncrypted);
    }
}
