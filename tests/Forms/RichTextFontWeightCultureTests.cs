using System.IO;
using System.Linq;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

/// <summary>A rich text field's markup is read the same whatever the culture of the machine: a numeric font weight
/// written with a '+' draws in the bold face, as a browser draws it.</summary>
public class RichTextFontWeightCultureTests
{
    /// <summary>The font resources the field's appearance selects, in order, once the markup is set under
    /// <paramref name="culture"/>.</summary>
    private static string AppearanceFonts(string culture)
    {
        using var scope = new CultureScope(culture);
        using var saved = new MemoryStream();
        using (var document = new Document())
        {
            document.Pages.Add();
            var field = new RichTextBoxField(document.Pages[1], new Rectangle(100, 600, 400, 700)) { PartialName = "notes" };
            document.Form.Add(field, 1);
            field.RichTextValue = "<body><p><span style=\"font-weight:+700\">Heavy</span> plain</p></body>";
            document.Save(saved);
        }
        using var reopened = new Document(new MemoryStream(saved.ToArray()));
        return string.Join(" ", ((Field)reopened.Form[1]).NormalAppearance!.Contents.OfType<SelectFont>().Select(f => f.FontName));
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the sign, which a culture parse will not take for '+'
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void ASignedNumericFontWeightDrawsInTheBoldFaceAsUnderTheInvariantCulture(string culture)
    {
        var expected = AppearanceFonts(string.Empty);
        Assert.Contains("FB", expected.Split(' ')); // the bold face's resource

        Assert.Equal(expected, AppearanceFonts(culture));
    }
}
