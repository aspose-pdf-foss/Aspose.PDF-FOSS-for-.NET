using System.Text;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Tests.Redaction;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

/// <summary>The objects a signature appends are PDF syntax, whatever the culture of the machine
/// that signs: a real keeps its decimal point and a negative number its ASCII minus.</summary>
public class SignedRevisionCultureTests
{
    // A crop box with fractional sides that starts left of and below the origin; the signature
    // rewrites the page it stands on, so the box is written again in the appended revision.
    private const string CropBox = "/CropBox [-10 -20 600.5 790.25]";

    [Theory]
    [InlineData("uk-UA")] // decimal comma
    [InlineData("sv-SE")] // decimal comma, U+2212 minus sign
    [InlineData("ar-SA")] // Arabic decimal separator, a letter mark before the minus sign
    [InlineData(CultureScope.SyntheticName)] // all of these at once, on every runtime
    public void ThePageAVisibleSignatureRewritesKeepsItsBoxUnderAnyCulture(string culture)
    {
        var pdf = RawPage.Build(string.Empty, "<< >>", string.Empty, CropBox);
        var appearance = new SignatureAppearance { SignerName = "Tester", Rect = new Rectangle(100, 100, 300, 160) };

        byte[] signed;
        using (new CultureScope(culture))
            signed = PdfSigner.SignWithAppearance(pdf, PdfSignerTests.CreateTestCertificate(), null, appearance);

        var revision = Encoding.ASCII.GetString(signed, pdf.Length, signed.Length - pdf.Length);
        Assert.Contains(CropBox, revision);
        using var doc = Document.Open(signed);
        var box = doc.Pages[1].CropBox;
        Assert.Equal(-10, box.LLX);
        Assert.Equal(-20, box.LLY);
        Assert.Equal(600.5, box.URX);
        Assert.Equal(790.25, box.URY);
    }
}
