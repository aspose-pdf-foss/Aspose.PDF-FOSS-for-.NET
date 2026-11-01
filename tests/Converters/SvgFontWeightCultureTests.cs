using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>An SVG text's numeric font weight is read as the number it is whatever the culture of the machine:
/// '+700' keeps its sign and '650.5' its decimal point, and both draw in the bold face.</summary>
public class SvgFontWeightCultureTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"300\" height=\"100\">"
        + "<text x=\"10\" y=\"20\" font-weight=\"+700\">Signed</text>"
        + "<text x=\"10\" y=\"50\" font-weight=\"650.5\">Fractional</text>"
        + "<text x=\"10\" y=\"80\">Plain</text></svg>";

    private static readonly string[] Phrases = { "Signed", "Fractional", "Plain" };

    /// <summary>The face the first fragment of each phrase is set in.</summary>
    private static string?[] Faces(string culture)
    {
        using var scope = new CultureScope(culture);
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(Svg)), new SvgLoadOptions());
        return Phrases.Select(phrase =>
        {
            var absorber = new TextFragmentAbsorber(phrase);
            doc.Pages.Accept(absorber);
            return absorber.TextFragments.FirstOrDefault()?.TextState.Font?.FontName;
        }).ToArray();
    }

    [Theory]
    [InlineData("uk-UA")] // decimal comma
    [InlineData("ar-SA")] // a letter mark before the sign, which a culture parse will not take for '+'
    [InlineData("he-IL")] // a left-to-right mark before the sign
    [InlineData(CultureScope.SyntheticName)] // both at once, on every runtime
    public void ANumericFontWeightIsReadAsUnderTheInvariantCulture(string culture)
    {
        var expected = Faces(string.Empty);
        Assert.Equal(expected[0], expected[1]);
        Assert.NotEqual(expected[2], expected[0]); // weights from 600 up draw in the bold face

        Assert.Equal(expected, Faces(culture));
    }
}
