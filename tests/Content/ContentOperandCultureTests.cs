using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Content;

/// <summary>An integer a caller hands a content builder is written as a PDF integer, whatever the culture of the
/// machine: one out of range stays the number it was, with its ASCII minus sign.</summary>
public class ContentOperandCultureTests
{
    [Theory]
    [InlineData("sv-SE")] // U+2212 minus sign
    [InlineData("ar-SA")] // a letter mark before the minus sign
    [InlineData(CultureScope.SyntheticName)] // a mark before the minus sign, on every runtime
    public void AContentStreamBuildersIntegerOperandsArePdfIntegers(string culture)
    {
        byte[] built;
        using (new CultureScope(culture))
            built = new ContentStreamBuilder().SetLineCap(-1).SetLineJoin(-1).SetTextRenderingMode(-1)
                .BeginMarkedContent("P", -1).Build();

        Assert.Equal("-1 J\n-1 j\n-1 Tr\n/P <</MCID -1>> BDC\n", Encoding.ASCII.GetString(built));
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("ar-SA")]
    [InlineData(CultureScope.SyntheticName)]
    public void AMarkedContentSequencesIdIsAPdfInteger(string culture)
    {
        var bdc = new BDC("Span", new BDCProperties(-1, "en"));

        string written;
        using (new CultureScope(culture))
            written = bdc.ToPdf();

        Assert.Equal("/Span << /MCID -1 /Lang (en) >> BDC", written);
    }
}
