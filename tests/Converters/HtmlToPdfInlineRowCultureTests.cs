using System.IO;
using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>The colour of a row of inline cells is content stream syntax, written the same whatever the
/// culture of the machine: a grey keeps its decimal points.</summary>
public class HtmlToPdfInlineRowCultureTests
{
    // #808080: 128/255 of full intensity in each channel.
    private const string Grey = "0.502 0.502 0.502 rg";

    private static string PageContent(string html, string culture)
    {
        using var scope = new CultureScope(culture);
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        return Encoding.ASCII.GetString(doc.Pages[1].GetContentStreamBytes() ?? []);
    }

    [Theory]
    [InlineData("uk-UA")] // decimal comma
    [InlineData("ar-SA")] // Arabic decimal separator
    [InlineData(CultureScope.SyntheticName)] // all of these at once, on every runtime
    public void AGreyCellInARowOfInlineBlocksIsPaintedGrey(string culture)
    {
        const string html = "<html><body><div>"
            + "<div style=\"display:inline-block;width:50%;color:#808080\">Left</div>"
            + "<div style=\"display:inline-block;width:50%\">Right</div>"
            + "</div></body></html>";

        Assert.Contains(Grey, PageContent(html, culture));
    }

    [Theory]
    [InlineData("uk-UA")]
    [InlineData("ar-SA")]
    [InlineData(CultureScope.SyntheticName)]
    public void AGreyCellInARowOfTableCellsIsPaintedGrey(string culture)
    {
        const string html = "<html><body><div style=\"display:table\">"
            + "<div style=\"display:table-cell;width:50%;color:#808080\">Left</div>"
            + "<div style=\"display:table-cell;width:50%;text-align:right\">Right</div>"
            + "</div></body></html>";

        Assert.Contains(Grey, PageContent(html, culture));
    }
}
