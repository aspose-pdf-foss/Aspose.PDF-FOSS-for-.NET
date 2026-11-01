using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>The integers of an XML template are read as the numbers they are whatever the culture of the machine:
/// a table's repeated rows and columns, a cell's spans, a heading's level and a text's style flags written with a
/// '+' - in a template a person wrote and in one the library wrote itself, which alone states row spans.</summary>
public class XmlBindingCultureTests
{
    private const string Template = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Document xmlns=\"Aspose.Pdf\"><Page>"
        + "<Heading Level=\"1\" IsAutoSequence=\"true\"><TextSegment>Top</TextSegment></Heading>"
        + "<Heading Level=\"+2\" IsAutoSequence=\"true\"><TextSegment>Sub</TextSegment></Heading>"
        + "<TextFragment><TextState FontStyle=\"+1\"/><TextSegment>Styled</TextSegment></TextFragment>"
        + "<TextFragment><TextSegment>Plain</TextSegment></TextFragment>"
        + "<Table ColumnWidths=\"150 150\" RepeatingRowsCount=\"+1\">"
        + "<Row><Cell ColSpan=\"+2\"><TextFragment><TextSegment>Wide</TextSegment></TextFragment></Cell></Row>"
        + "<Row><Cell><TextFragment><TextSegment>Left</TextSegment></TextFragment></Cell>"
        + "<Cell><TextFragment><TextSegment>Right</TextSegment></TextFragment></Cell></Row>"
        + "</Table></Page></Document>";

    /// <summary>The one table bound onto <paramref name="doc"/>'s first page.</summary>
    private static Table BoundTable(Document doc) => doc.Pages[1].Paragraphs.OfType<Table>().Single();

    /// <summary>What <see cref="Template"/> binds to under <paramref name="culture"/>: the table's repeated rows and
    /// first cell's span, the page's text, and the faces of the styled and the plain text.</summary>
    private static (string Counts, string Text, string? Styled, string? Plain) Bound(string culture)
    {
        using var scope = new CultureScope(culture);
        using var saved = new MemoryStream();
        string counts;
        using (var doc = new Document())
        {
            doc.BindXml(new MemoryStream(Encoding.UTF8.GetBytes(Template)));
            var table = BoundTable(doc);
            counts = FormattableString.Invariant($"{table.RepeatingRowsCount} {table.Rows[0].Cells[0].ColSpan}");
            doc.Save(saved);
        }
        using var reopened = new Document(new MemoryStream(saved.ToArray()));
        var text = new TextAbsorber();
        reopened.Pages[1].Accept(text);
        string? Face(string phrase)
        {
            var absorber = new TextFragmentAbsorber(phrase);
            reopened.Pages[1].Accept(absorber);
            return absorber.TextFragments.FirstOrDefault()?.TextState.Font?.FontName;
        }
        return (counts, text.Text, Face("Styled"), Face("Plain"));
    }

    /// <summary>The template the library writes for a table that repeats its first row and column and has a cell
    /// spanning two columns and one spanning two rows, with each of those counts rewritten with a '+'.</summary>
    private static string WrittenTemplate()
    {
        using var doc = new Document();
        var table = new Table { ColumnWidths = "150 150", RepeatingRowsCount = 1, RepeatingColumnsCount = 1 };
        table.Rows.Add().Cells.Add("Wide").ColSpan = 2;
        var middle = table.Rows.Add();
        middle.Cells.Add("Tall").RowSpan = 2;
        middle.Cells.Add("Right");
        table.Rows.Add().Cells.Add("Below");
        doc.Pages.Add().Paragraphs.Add(table);
        using var xml = new MemoryStream();
        doc.SaveXml(xml);
        return Encoding.UTF8.GetString(xml.ToArray())
            .Replace("RepeatingRowsCount=\"1\"", "RepeatingRowsCount=\"+1\"")
            .Replace("RepeatingColumnsCount=\"1\"", "RepeatingColumnsCount=\"+1\"")
            .Replace("ColSpan=\"2\"", "ColSpan=\"+2\"")
            .Replace("RowSpan=\"2\"", "RowSpan=\"+2\"");
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the sign, which a culture parse will not take for '+'
    [InlineData("he-IL")] // a left-to-right mark before the sign
    [InlineData(CultureScope.SyntheticName)] // both at once, on every runtime
    public void AnAuthoredTemplatesSignedCountsAreReadAsUnderTheInvariantCulture(string culture)
    {
        var expected = Bound(string.Empty);
        Assert.Equal("1 2", expected.Counts);
        Assert.Contains("1.1", expected.Text, StringComparison.Ordinal); // the level-2 heading numbers under the first
        Assert.NotEqual(expected.Plain, expected.Styled); // flag 1 sets the text bold

        Assert.Equal(expected, Bound(culture));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AWrittenTemplatesSignedCountsAreReadAsUnderTheInvariantCulture(string culture)
    {
        var xml = WrittenTemplate();
        string BoundCounts(string c)
        {
            using var scope = new CultureScope(c);
            using var doc = new Document();
            doc.BindXml(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
            var table = BoundTable(doc);
            return FormattableString.Invariant(
                $"{table.RepeatingRowsCount} {table.RepeatingColumnsCount} {table.Rows[0].Cells[0].ColSpan} {table.Rows[1].Cells[0].RowSpan}");
        }

        Assert.Equal("1 1 2 2", BoundCounts(string.Empty));
        Assert.Equal("1 1 2 2", BoundCounts(culture));
    }
}
