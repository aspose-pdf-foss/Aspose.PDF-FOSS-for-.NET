using System.IO;
using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>Markup is read the same whatever the culture of the machine: under a Turkish culture, whose
/// case rules pair 'i' with a dotted capital and 'I' with a dotless small letter, an upper-case tag is
/// still the tag its lower-case spelling names.</summary>
public class HtmlToPdfTurkishCaseTests
{
    private static string ConvertedText(string html, string culture)
    {
        using var scope = new CultureScope(culture);
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        return absorber.Text;
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("az-Latn-AZ")]
    public void AnUpperCaseScriptBlockIsNoText(string culture)
    {
        const string html = "<HTML><BODY><P>Before</P><SCRIPT>var t = \"<p>Injected</p>\"; document.write(t);</SCRIPT>"
            + "<P>After</P></BODY></HTML>";

        var text = ConvertedText(html, culture);

        Assert.Contains("Before", text, StringComparison.Ordinal);
        Assert.Contains("After", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Injected", text, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("az-Latn-AZ")]
    public void AnUpperCaseListMarkerRuleMarksItsItem(string culture)
    {
        const string html = "<html><head><style>OL { LIST-STYLE: NONE; } LI:NTH-CHILD(2)::BEFORE { CONTENT: \"** \"; }</style></head>"
            + "<body><ol><li>One</li><li>Two</li><li>Three</li></ol></body></html>";

        // Converted under the culture first: in a run of this test alone the converter's patterns are built under it.
        var text = ConvertedText(html, culture);

        var expected = ConvertedText(html, string.Empty);
        Assert.Contains("** ", expected, StringComparison.Ordinal);
        Assert.Equal(expected, text);
    }
}
