using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Security;
using Aspose.Pdf.Signatures;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

public class UnsignedContentAbsorberTests
{
    private static PdfCertificate CreateTestCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Absorber Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        return PdfCertificate.FromPfx(cert.Export(X509ContentType.Pfx, "test"), "test");
    }

    private static string SignedTempFile()
    {
        var signed = PdfSigner.Sign(PdfBuilder.BuildMinimal(), CreateTestCertificate());
        var path = Path.Combine(Path.GetTempPath(), $"absorber-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, signed);
        return path;
    }

    private static UnsignedContentAbsorber.Result Measure(string path)
    {
        using var facade = new PdfFileSignature();
        facade.BindPdf(path);
        return new UnsignedContentAbsorber(facade).TryGetContent();
    }

    private static void EditIncrementally(string path, Action<Document> edit)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var document = new Document(fs);
        edit(document);
        document.Save();
    }

    [Fact]
    public void EntirelySigned_WhenNothingFollowsTheSignature()
    {
        var path = SignedTempFile();
        try
        {
            var result = Measure(path);
            Assert.True(result.Success);
            Assert.Equal(SignaturesCoverage.EntirelySigned, result.Coverage);
            Assert.Empty(result.UnsignedContent.Pages);
            Assert.Empty(result.UnsignedContent.Annotations);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TextAppendedIncrementally_MakesThePageUnsigned()
    {
        var path = SignedTempFile();
        try
        {
            EditIncrementally(path, document =>
                new TextBuilder(document.Pages[1]).AppendText(new TextFragment("added after signing") { Position = new Position(50, 700) }));
            var result = Measure(path);
            Assert.True(result.Success);
            Assert.Equal(SignaturesCoverage.PartiallySigned, result.Coverage);
            Assert.Single(result.UnsignedContent.Pages);
            Assert.Empty(result.UnsignedContent.Forms);
            Assert.Empty(result.UnsignedContent.XForms);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PageAddedIncrementally_IsUnsigned()
    {
        var path = SignedTempFile();
        try
        {
            EditIncrementally(path, document => document.Pages.Add());
            var result = Measure(path);
            Assert.Equal(SignaturesCoverage.PartiallySigned, result.Coverage);
            Assert.Single(result.UnsignedContent.Pages);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Undefined_WhenTheDocumentHasNoSignature()
    {
        var path = Path.Combine(Path.GetTempPath(), $"absorber-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, PdfBuilder.BuildMinimal());
        try
        {
            var result = Measure(path);
            Assert.True(result.Success);
            Assert.Equal(SignaturesCoverage.Undefined, result.Coverage);
            Assert.NotEmpty(result.Message);
        }
        finally { File.Delete(path); }
    }
}
