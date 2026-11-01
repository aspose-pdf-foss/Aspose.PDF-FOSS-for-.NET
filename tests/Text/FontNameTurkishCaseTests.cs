using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>The family a font's name stands for is read by the invariant culture's case rules: under a Turkish or
/// Azerbaijani culture, whose rules pair 'I' with a dotless small letter, a style word in capitals or in small letters
/// at the end of the name is still a style and not part of the family.</summary>
public class FontNameTurkishCaseTests
{
    private static readonly string[] Names = { "TahomaITALIC", "Tahoma italic", "Verdana OBLIQUE", "GeorgiaBOLDITALIC" };

    private static string Families(string culture)
    {
        using var scope = new CultureScope(culture);
        return string.Join("\n", Names.Select(name => SystemFontResolver.SubstituteFaceName(name).ToString()));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("az-Latn-AZ")]
    public void AStyleWordAtTheEndOfAFontNameIsNoPartOfItsFamilyAsUnderTheInvariantCulture(string culture)
    {
        var expected = Families(string.Empty);
        Assert.DoesNotContain("ITALIC", expected, StringComparison.OrdinalIgnoreCase); // the style words are stripped
        Assert.DoesNotContain("OBLIQUE", expected, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(expected, Families(culture));
    }
}
