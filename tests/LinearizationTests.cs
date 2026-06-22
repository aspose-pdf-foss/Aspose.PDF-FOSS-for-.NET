using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class LinearizationTests
{
    [Fact]
    public void NonLinearizedPdf_IsLinearized_ReturnsFalse()
    {
        var data = PdfBuilder.BuildMinimal();
        var doc = Document.Open(data);
        Assert.False(doc.IsLinearized);
    }

    [Fact]
    public void LinearizedPdf_IsLinearized_ReturnsTrue()
    {
        var data = BuildLinearizedPdf();
        var doc = Document.Open(data);
        Assert.True(doc.IsLinearized);
    }

    [Fact]
    public void LinearizedPdf_PreservesPageCount()
    {
        var data = BuildLinearizedPdf();
        var doc = Document.Open(data);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void LinearizeDocument_DoesNotThrow()
    {
        var data = PdfBuilder.BuildMinimal();
        var doc = Document.Open(data);
        var ex = Record.Exception(() => doc.LinearizeDocument());
        Assert.Null(ex);
    }

    /// <summary>
    /// Build a minimal linearized PDF where object 1 is the linearization dictionary.
    /// Object numbering:
    ///   1 = Linearization dictionary
    ///   2 = Catalog
    ///   3 = Pages
    ///   4 = Page
    /// </summary>
    private static byte[] BuildLinearizedPdf()
    {
        // We need to build this in two passes because the linearization dict
        // contains the file length (/L) which we only know after building.
        // First pass: build with placeholder length, measure, then rebuild.

        return BuildLinearizedPdfWithLength(0, out _) is { } first
            ? BuildLinearizedPdfCore(first.Length)
            : throw new InvalidOperationException("Failed to build linearized PDF");
    }

    private static byte[] BuildLinearizedPdfWithLength(int fileLength, out int actualLength)
    {
        var result = BuildLinearizedPdfCore(fileLength);
        actualLength = result.Length;
        return result;
    }

    private static byte[] BuildLinearizedPdfCore(int fileLength)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        // Object 1: Linearization dictionary (must be first object in file body)
        var linDictOffset = ms.Position;
        // Use fixed-width 10-digit format so length doesn't change between passes
        var lengthStr = fileLength.ToString("D10");
        Write($"1 0 obj\n<< /Linearized 1 /L {lengthStr} /N 1 /T 0 /H [0 0] /O 4 /E 0 >>\nendobj\n");

        // Object 2: Catalog
        var catalogOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Catalog /Pages 3 0 R >>\nendobj\n");

        // Object 3: Pages
        var pagesOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Pages /Kids [4 0 R] /Count 1 >>\nendobj\n");

        // Object 4: Page
        var pageOffset = ms.Position;
        Write("4 0 obj\n<< /Type /Page /Parent 3 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        // Xref
        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{linDictOffset:D10} 00000 n \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");

        // Trailer
        Write("trailer\n<< /Size 5 /Root 2 0 R >>\n");
        Write("startxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }
}
