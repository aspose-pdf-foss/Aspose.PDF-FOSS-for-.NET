using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

public class PdfSignerTests
{
    private static PdfCertificate CreateTestCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Test Signer, O=FOSS Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1));

        var pfxBytes = cert.Export(X509ContentType.Pfx, "test");
        return PdfCertificate.FromPfx(pfxBytes, "test");
    }

    [Fact]
    public void Sign_ProducesSignedPdf()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert);

        using var doc = Document.Open(signed);
        Assert.True(Signature.HasAny(doc));
    }

    [Fact]
    public void Sign_SignatureHasCorrectMetadata()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert, new SignatureOptions
        {
            FieldName = "MySig",
            Reason = "Testing",
            Location = "Unit Test",
        });

        using var doc = Document.Open(signed);
        var sigs = Signature.EnumerateSignatures(doc).ToList();
        Assert.Single(sigs);

        var sig = sigs[0];
        Assert.Equal("MySig", sig.FieldName);
        Assert.Equal("Testing", sig.Reason);
        Assert.Equal("Unit Test", sig.Location);
        Assert.Equal("Adobe.PPKLite", sig.Filter);
        Assert.Equal("adbe.pkcs7.detached", sig.SubFilter);
    }

    [Fact]
    public void Sign_ByteRangeCoversEntireFile()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert);

        using var doc = Document.Open(signed);
        var sigs = Signature.EnumerateSignatures(doc).ToList();
        var sig = sigs[0];

        Assert.NotNull(sig.ByteRange);
        var br = sig.ByteRange!;
        Assert.Equal(4, br.Length);
        Assert.Equal(0, br[0]);
        Assert.Equal(signed.Length, br[2] + br[3]);
    }

    [Fact]
    public void Sign_ContentsHasPkcs7Data()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert);

        using var doc = Document.Open(signed);
        var sig = Signature.EnumerateSignatures(doc).Single();

        Assert.NotNull(sig.ContentsRaw);
        Assert.True(sig.ContentsRaw!.Length > 100);
    }

    [Fact]
    public void Sign_Verify_Roundtrip()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert, new SignatureOptions { FieldName = "Sig1" });

        Assert.True(PdfSigner.Verify(signed, "Sig1"));
    }

    [Fact]
    public void Verify_TamperedPdf_ReturnsFalse()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert);

        var tampered = (byte[])signed.Clone();
        tampered[10] ^= 0xFF;

        Assert.False(PdfSigner.Verify(tampered));
    }

    [Fact]
    public void Sign_PreservesOriginalContent()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert);

        using var doc = Document.Open(signed);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Sign_MultiPageDocument()
    {
        var pdf = PdfBuilder.BuildMultiPage(3);
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert);

        using var doc = Document.Open(signed);
        Assert.Equal(3, doc.PageCount);
        Assert.True(Signature.HasAny(doc));
    }

    [Fact]
    public void Sign_WithCustomFieldName()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert, new SignatureOptions
        {
            FieldName = "ApprovalSignature"
        });

        using var doc = Document.Open(signed);
        var sigs = Signature.EnumerateSignatures(doc).ToList();
        Assert.Equal("ApprovalSignature", sigs[0].FieldName);
    }

    [Fact]
    public void Sign_HasSigningDate()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert);

        using var doc = Document.Open(signed);
        var sig = Signature.EnumerateSignatures(doc).Single();
        Assert.NotEqual(DateTime.MinValue, sig.Date);
    }

    [Fact]
    public void Sign_HasSignerName()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert);

        using var doc = Document.Open(signed);
        var sig = Signature.EnumerateSignatures(doc).Single();
        Assert.Contains("Test Signer", sig.Authority);
    }

    [Fact]
    public void Facade_Sign_And_Verify()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert, new SignatureOptions
        {
            FieldName = "FacadeSig",
            Reason = "Facade test",
        });

        var facade = new Aspose.Pdf.Facades.PdfFileSignature();
        facade.BindPdf(signed);
        Assert.True(facade.IsContainSignature());
        Assert.True(facade.VerifySignature("FacadeSig"));
        Assert.Equal("Facade test", facade.GetReason("FacadeSig"));
    }

    [Fact]
    public void Facade_Verify_UnsignedPdf_ReturnsFalse()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var facade = new Aspose.Pdf.Facades.PdfFileSignature();
        facade.BindPdf(pdf);

        Assert.False(facade.IsContainSignature());
    }

    [Fact]
    public void Sign_DocumentWithExistingForm()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        var cert = CreateTestCertificate();

        var signed = PdfSigner.Sign(pdf, cert);

        using var doc = Document.Open(signed);
        Assert.True(Signature.HasAny(doc));
        Assert.NotNull(doc.Form);
    }

    [Fact]
    public void SignWithAppearance_ContainsSignerNameText()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var appearance = new SignatureAppearance
        {
            SignerName = "John Doe",
            Reason = "Approval",
            Location = "New York",
            SignDate = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            Rect = new Rectangle(100, 100, 350, 200),
            PageNumber = 1,
            FontSize = 10,
        };

        var signed = PdfSigner.SignWithAppearance(pdf, cert, new SignatureOptions
        {
            FieldName = "VisibleSig",
        }, appearance);

        using var doc = Document.Open(signed);
        Assert.True(Signature.HasAny(doc));

        var raw = System.Text.Encoding.Latin1.GetString(signed);
        Assert.Contains("Digitally signed by: John Doe", raw);
        Assert.Contains("Reason: Approval", raw);
        Assert.Contains("Location: New York", raw);
    }

    [Fact]
    public void SignWithAppearance_PositionedOnCorrectPage()
    {
        var pdf = PdfBuilder.BuildMultiPage(3);
        var cert = CreateTestCertificate();

        var appearance = new SignatureAppearance
        {
            SignerName = "Jane Smith",
            Rect = new Rectangle(50, 50, 250, 150),
            PageNumber = 2,
        };

        var signed = PdfSigner.SignWithAppearance(pdf, cert, null, appearance);

        using var doc = Document.Open(signed);
        Assert.True(Signature.HasAny(doc));
        Assert.Equal(3, doc.PageCount);
    }

    [Fact]
    public void SignWithAppearance_Verify_Roundtrip()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();

        var appearance = new SignatureAppearance
        {
            SignerName = "Tester",
            Rect = new Rectangle(10, 10, 200, 60),
        };

        var signed = PdfSigner.SignWithAppearance(pdf, cert,
            new SignatureOptions { FieldName = "VisSig" }, appearance);

        Assert.True(PdfSigner.Verify(signed, "VisSig"));
    }

    [Fact]
    public void BuildSignatureAppearanceStream_HasFormXObjectStructure()
    {
        var appearance = new SignatureAppearance
        {
            SignerName = "Test User",
            Reason = "Testing",
            Location = "Office",
            SignDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Rect = new Rectangle(0, 0, 200, 100),
            FontSize = 10,
        };

        var dict = PdfSigner.BuildSignatureAppearanceStream(appearance);

        Assert.Equal("XObject", (dict.Get("Type") as Aspose.Pdf.Core.PdfName)?.Value);
        Assert.Equal("Form", (dict.Get("Subtype") as Aspose.Pdf.Core.PdfName)?.Value);
        Assert.NotNull(dict.Get("BBox"));
        Assert.NotNull(dict.Get("Resources"));

        var streamData = dict.Get("__StreamData") as Aspose.Pdf.Core.PdfString;
        Assert.NotNull(streamData);
        var text = System.Text.Encoding.Latin1.GetString(streamData!.Value);
        Assert.Contains("Digitally signed by: Test User", text);
        Assert.Contains("Reason: Testing", text);
        Assert.Contains("Location: Office", text);
        Assert.Contains("Date: 2025-01-01 12:00:00", text);
    }

    [Fact]
    public void Sign_CustomSignHash_EmbedsExternalEnvelope()
    {
        var pdf = PdfBuilder.BuildMinimal();
        var cert = CreateTestCertificate();
        var sentinel = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE };
        byte[]? observedHash = null;

        var signed = PdfSigner.Sign(pdf, cert, new SignatureOptions
        {
            FieldName = "ExtSig",
            CustomSignHash = (hash, _) =>
            {
                observedHash = (byte[])hash.Clone();
                return sentinel;
            },
        });

        Assert.NotNull(observedHash);
        Assert.Equal(32, observedHash!.Length); // SHA-256

        using var doc = Document.Open(signed);
        var sigs = Signature.EnumerateSignatures(doc).ToList();
        Assert.Single(sigs);
        var contents = (byte[]?)sigs[0].GetType().GetProperty("ContentsRaw",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(sigs[0]);
        Assert.NotNull(contents);
        // The first 6 bytes of /Contents must be exactly our sentinel envelope
        // (the rest is zero-padded up to ContentsSize).
        for (var i = 0; i < sentinel.Length; i++)
            Assert.Equal(sentinel[i], contents![i]);
    }
}
