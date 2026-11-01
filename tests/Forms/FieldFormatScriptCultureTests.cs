using System.Linq;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

/// <summary>The arguments of a field's built-in number and percent format scripts are read as the numbers they are
/// whatever the culture of the machine: a negative digit count or style keeps its minus sign.</summary>
public class FieldFormatScriptCultureTests
{
    private static readonly string[] Scripts =
    {
        "AFNumber_Format(-1, 0, -1, 0, \"\", true);",
        "AFPercent_Format(-1, 0);",
    };

    /// <summary>The text each script makes of one value under <paramref name="culture"/>.</summary>
    private static string?[] Formatted(string culture)
    {
        using var scope = new CultureScope(culture);
        return Scripts.Select(script => FieldFormatScript.TryApply(script, "-1234.5")).ToArray();
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the minus sign, which a culture parse will not take for '-'
    [InlineData("he-IL")] // a left-to-right mark before the minus sign
    [InlineData(CultureScope.SyntheticName)] // both at once, on every runtime
    public void AFormatScriptWithANegativeArgumentFormatsAsUnderTheInvariantCulture(string culture)
    {
        var expected = Formatted(string.Empty);
        Assert.All(expected, text => Assert.NotNull(text));

        Assert.Equal(expected, Formatted(culture));
    }
}
