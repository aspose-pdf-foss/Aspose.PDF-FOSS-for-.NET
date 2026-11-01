using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

/// <summary>An XFDF file's integers are read as the numbers they are whatever the culture of the machine: an
/// annotation for page -1 is passed over, one for page +1 lands on the second page, an attached file of size +5 is
/// five bytes, a sound of +16 bits and +2 channels keeps them and a distance format of precision +100 shows
/// hundredths.</summary>
public class XfdfImportCultureTests
{
    private const string Xfdf = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><annots>"
        + "<text page=\"-1\" rect=\"10,10,50,50\" name=\"a1\"><contents>Minus</contents></text>"
        + "<text page=\"+1\" rect=\"60,10,100,50\" name=\"a2\"><contents>Plus</contents></text>"
        + "<text page=\"0\" rect=\"110,10,150,50\" name=\"a3\"><contents>Zero</contents></text>"
        + "<fileattachment page=\"0\" rect=\"160,10,200,50\" name=\"a4\" file=\"note.txt\" size=\"+5\">"
        + "<contents>Attached</contents><data encoding=\"base64\">aGVsbG8=</data></fileattachment>"
        + "<sound page=\"0\" rect=\"210,10,250,50\" name=\"a5\" rate=\"8000\" bits=\"+16\" channels=\"+2\">"
        + "<contents>Heard</contents><data encoding=\"base64\">AAAA</data></sound>"
        + "<square page=\"0\" rect=\"260,10,300,50\" name=\"a6\"><contents>Measured</contents>"
        + "<measure rateValue=\"1 in = 1 in\"><distance d=\"+100\" f=\"D\" u=\"in\"/></measure></square>"
        + "</annots></xfdf>";

    /// <summary>Each imported annotation's page and contents, an attached file's size, a sound's bits and channels and
    /// the precision of a measure's distance format, once the XFDF is imported into two blank pages under
    /// <paramref name="culture"/> and the document saved.</summary>
    private static string Imported(string culture)
    {
        using var scope = new CultureScope(culture);
        using var saved = new MemoryStream();
        using (var doc = new Document())
        {
            doc.Pages.Add();
            doc.Pages.Add();
            doc.ImportAnnotationsFromXfdf(new MemoryStream(Encoding.UTF8.GetBytes(Xfdf)));
            doc.Save(saved);
        }
        using var reopened = new Document(new MemoryStream(saved.ToArray()));
        var seen = new StringBuilder();
        for (var p = 1; p <= reopened.Pages.Count; p++)
            foreach (var a in reopened.Pages[p].Annotations.Cast<Annotation>())
            {
                seen.Append(FormattableString.Invariant($"{p}: {a.Contents}"));
                if (a is FileAttachmentAnnotation file)
                    seen.Append(FormattableString.Invariant($" of {file.File?.Params?.Size} bytes"));
                if (reopened.Reader.ResolveStream(a.Dict.Get("Sound")) is { } sound)
                    seen.Append(FormattableString.Invariant($" in {sound.Dict.GetInt("B")} bits, {sound.Dict.GetInt("C")} channels"));
                if (reopened.Reader.ResolveDict(a.Dict.Get("Measure")) is { } measure
                    && reopened.Reader.Resolve(measure.Get("D")) is PdfArray { Count: > 0 } distances
                    && reopened.Reader.ResolveDict(distances[0]) is { } format)
                    seen.Append(FormattableString.Invariant($" to 1/{format.GetInt("D")}"));
                seen.Append('\n');
            }
        return seen.ToString();
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the sign, which a culture parse will not take for '-' or '+'
    [InlineData("he-IL")] // a left-to-right mark before the sign
    [InlineData(CultureScope.SyntheticName)] // both at once, on every runtime
    public void ASignedIntegerPlacesAndSizesTheAnnotationsAsUnderTheInvariantCulture(string culture)
    {
        var expected = Imported(string.Empty);
        // XFDF counts pages from 0: there is no page -1
        Assert.Equal("1: Zero\n1: Attached of 5 bytes\n1: Heard in 16 bits, 2 channels\n1: Measured to 1/100\n2: Plus\n", expected);

        Assert.Equal(expected, Imported(culture));
    }
}
