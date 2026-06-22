using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public class PdfFileSignatureTests
{
    [Fact]
    public void IsContainSignature_UnsignedPdf_ReturnsFalse()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var facade = new PdfFileSignature();
        facade.BindPdf(pdf);
        Assert.False(facade.IsContainSignature());
    }

    [Fact]
    public void IsContainSignature_SignedPdf_ReturnsTrue()
    {
        var pdf = BuildSignedPdf();
        var facade = new PdfFileSignature();
        facade.BindPdf(pdf);
        Assert.True(facade.IsContainSignature());
    }

    [Fact]
    public void GetSignNames_ReturnsList()
    {
        var pdf = BuildSignedPdf();
        var facade = new PdfFileSignature();
        facade.BindPdf(pdf);
        var names = facade.GetSignNames();
        Assert.Single(names);
        Assert.Equal("sig", names[0]);
    }

    [Fact]
    public void GetSignerName_ReturnsName()
    {
        var pdf = BuildSignedPdf();
        var facade = new PdfFileSignature();
        facade.BindPdf(pdf);
        Assert.Equal("Jane Smith", facade.GetSignerName("sig"));
    }

    [Fact]
    public void GetReason_ReturnsReason()
    {
        var pdf = BuildSignedPdf();
        var facade = new PdfFileSignature();
        facade.BindPdf(pdf);
        Assert.Equal("Reviewed", facade.GetReason("sig"));
    }

    [Fact]
    public void GetLocation_ReturnsLocation()
    {
        var pdf = BuildSignedPdf();
        var facade = new PdfFileSignature();
        facade.BindPdf(pdf);
        Assert.Equal("London", facade.GetLocation("sig"));
    }

    private static byte[] BuildSignedPdf()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm 5 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var sigValOffset = ms.Position;
        Write("6 0 obj\n<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached " +
              "/Name (Jane Smith) /Reason (Reviewed) /Location (London) " +
              "/ByteRange [0 50 100 200] /Contents <CAFE> >>\nendobj\n");

        var fieldOffset = ms.Position;
        Write("4 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Sig /T (sig) /V 6 0 R " +
              "/Rect [0 0 0 0] >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var formOffset = ms.Position;
        Write("5 0 obj\n<< /Fields [4 0 R] /SigFlags 3 >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 7\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{fieldOffset:D10} 00000 n \n");
        Write($"{formOffset:D10} 00000 n \n");
        Write($"{sigValOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }
}
