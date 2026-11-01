using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Tests.Redaction;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>A structure element's attributes read back as the numbers the file holds, whatever the
/// culture of the machine: a negative integer keeps its ASCII minus sign, as a negative real does.</summary>
public class StructAttributeCultureTests
{
    [Theory]
    [InlineData("sv-SE")] // U+2212 minus sign
    [InlineData("ar-SA")] // a letter mark before the minus sign
    [InlineData(CultureScope.SyntheticName)] // all of these at once, on every runtime
    public void ANegativeIndentReadsBackWithItsMinusSign(string culture)
    {
        var pdf = RawPage.Build(string.Empty, "<< >>",
            "/MarkInfo << /Marked true >> /StructTreeRoot 5 0 R", string.Empty,
            new RawPage.Obj("<< /Type /StructTreeRoot /K 6 0 R >>"),
            new RawPage.Obj("<< /Type /StructElem /S /P /P 5 0 R /A << /O /Layout /TextIndent -10 /StartIndent -2.5 >> >>"));
        using var doc = Document.Open(pdf);
        var paragraph = doc.StructTreeRoot!.Children[0];

        IReadOnlyDictionary<string, string>? attributes;
        using (new CultureScope(culture))
            attributes = paragraph.Attributes;

        Assert.NotNull(attributes);
        Assert.Equal("-10", attributes!["TextIndent"]);
        Assert.Equal("-2.5", attributes["StartIndent"]);
    }
}
