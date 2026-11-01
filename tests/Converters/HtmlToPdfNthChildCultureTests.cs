using System.IO;
using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>A list marker rule's nth-child argument is CSS, read the same whatever the culture of the
/// machine: a negative coefficient or index keeps its ASCII minus sign.</summary>
public class HtmlToPdfNthChildCultureTests
{
    private const string Html = "<html><head><style>"
        + "ol { list-style: none; } li:nth-child(-2n+5)::before { content: \"* \"; } li:nth-child(-1)::before { content: \"! \"; }"
        + "</style></head><body><ol><li>One</li><li>Two</li><li>Three</li><li>Four</li><li>Five</li></ol></body></html>";

    private static string ConvertedText(string culture)
    {
        using var scope = new CultureScope(culture);
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(Html)), new HtmlLoadOptions());
        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        return absorber.Text;
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the minus sign, which a culture parse will not take for '-'
    [InlineData("he-IL")] // a left-to-right mark before the minus sign
    [InlineData(CultureScope.SyntheticName)] // all of these at once, on every runtime
    public void AListWhoseMarkerRulesCountFromTheEndConvertsAsUnderTheInvariantCulture(string culture)
    {
        var expected = ConvertedText(string.Empty);

        Assert.Contains("Three", expected);
        Assert.Equal(expected, ConvertedText(culture));
    }
}
