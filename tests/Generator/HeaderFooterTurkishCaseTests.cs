using System.IO;
using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A header's markup is read the same whatever the culture of the machine: under a Turkish culture,
/// whose case rules pair 'I' with a dotless small letter, an upper-case DIV in the data region of a report
/// sheet's header is still a row of its own.</summary>
public class HeaderFooterTurkishCaseTests
{
    // A report sheet's header: two boxed titles over a data region of two rows in an outer block.
    private const string Html = "<html><body style=\"width:19cm\">"
        + "<h5><span style=\"width:50%\">Inspection</span><span style=\"width:50%\">Sheet</span></h5>"
        + "<DIV><DIV>First row</DIV><DIV>Second row</DIV></DIV></body></html>";

    private static readonly string[] Rows = { "First row", "Second row" };

    /// <summary>Where each row's text stands on the page the header was stamped on.</summary>
    private static string Seats(string culture)
    {
        using var scope = new CultureScope(culture);
        using var saved = new MemoryStream();
        using (var doc = new Document())
        {
            var header = new HeaderFooter();
            header.Paragraphs.Add(new HtmlFragment(Html));
            doc.Pages.Add().Header = header;
            doc.Save(saved);
        }
        using var reopened = Document.Open(saved.ToArray());
        var seats = new StringBuilder();
        foreach (var row in Rows)
        {
            var absorber = new TextFragmentAbsorber(row);
            reopened.Pages[1].Accept(absorber);
            foreach (TextFragment f in absorber.TextFragments)
                seats.Append(FormattableString.Invariant($"{row} at {f.Rectangle?.LLX:0.#},{f.Rectangle?.LLY:0.#}\n"));
        }
        return seats.ToString();
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("az-Latn-AZ")]
    public void AnUpperCaseDivInAReportSheetsHeaderIsARowAsUnderTheInvariantCulture(string culture)
    {
        var expected = Seats(string.Empty);
        Assert.All(Rows, r => Assert.Contains(r + " at ", expected, StringComparison.Ordinal));

        Assert.Equal(expected, Seats(culture));
    }
}
