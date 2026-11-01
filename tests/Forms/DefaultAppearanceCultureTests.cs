using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

/// <summary>A default appearance (/DA) is a piece of content stream, written the same whatever the
/// culture of the machine: its font size and colour keep their decimal points.</summary>
public class DefaultAppearanceCultureTests
{
    private const string Expected = "/Helvetica 10.5 Tf 1.000 0.000 0.000 rg";

    private static DefaultAppearance RedHelvetica() => new("Helvetica", 10.5, System.Drawing.Color.Red);

    [Theory]
    [InlineData("uk-UA")] // decimal comma
    [InlineData("ar-SA")] // Arabic decimal separator
    [InlineData(CultureScope.SyntheticName)] // all of these at once, on every runtime
    public void AFreeTextAnnotationsDefaultAppearanceIsWrittenWithDecimalPoints(string culture)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();

        FreeTextAnnotation annot;
        using (new CultureScope(culture))
            annot = new FreeTextAnnotation(page, new Rectangle(100, 600, 300, 650), RedHelvetica());

        Assert.Equal(Expected, annot.DefaultAppearance);
    }

    [Theory]
    [InlineData("uk-UA")]
    [InlineData("ar-SA")]
    [InlineData(CultureScope.SyntheticName)]
    public void AFieldsDefaultAppearanceIsWrittenWithDecimalPoints(string culture)
    {
        using var doc = new Document();
        var field = new TextBoxField(doc.Pages.Add(), new Rectangle(100, 600, 300, 620));

        using (new CultureScope(culture))
            field.DefaultAppearance = RedHelvetica();

        Assert.Equal(Expected, (field.Dict.Get("DA") as PdfString)?.ToText());
    }
}
