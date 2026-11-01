using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A replacement rewrites the show-text array it edits as PDF syntax, whatever the
/// culture of the machine: the kerning adjustments it keeps keep their ASCII minus signs.</summary>
public class TextReplacerCultureTests
{
    [Theory]
    [InlineData("sv-SE")] // U+2212 minus sign
    [InlineData("ar-SA")] // a letter mark before the minus sign
    [InlineData(CultureScope.SyntheticName)] // all of these at once, on every runtime
    public void TheKerningAroundAnEditedWordKeepsItsMinusSigns(string culture)
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td [(Hello) -250 (World) -100 (again)] TJ ET");
        using var doc = Document.Open(PdfBuilder.BuildWithTextContent(content));
        var absorber = new TextFragmentAbsorber("World");
        absorber.Visit(doc.Pages[1]);

        using (new CultureScope(culture))
            absorber.TextFragments[1].Text = "PDF";

        var rewritten = Encoding.ASCII.GetString(doc.Pages[1].GetContentStreamBytes() ?? []);
        Assert.Contains("[(Hello) -250 (PDF) -100 (again)] TJ", rewritten);
    }
}
