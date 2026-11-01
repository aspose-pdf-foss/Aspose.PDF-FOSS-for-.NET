using System.Text;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Tests.Redaction;
using Aspose.Pdf.Vector;
using Xunit;

namespace Aspose.Pdf.Tests.Drawing;

/// <summary>A line cap or join written back into a content stream is PDF syntax, whatever the culture
/// of the machine: an operand out of range in a malformed source stays the number it was.</summary>
public class LineStyleCultureTests
{
    // A malformed source: the line cap and join operands are negative.
    private const string Source = "-1 J -1 j 1 0 0 RG 2 w 100 100 m 200 200 l S";

    [Theory]
    [InlineData("sv-SE")] // U+2212 minus sign
    [InlineData("ar-SA")] // a letter mark before the minus sign
    [InlineData(CultureScope.SyntheticName)] // all of these at once, on every runtime
    public void AnAbsorbedPathReplayedOnAPageRestatesItsLineJoin(string culture)
    {
        using var doc = Document.Open(RawPage.Build(Source, "<< >>"));
        using var absorber = new GraphicsAbsorber();
        absorber.Visit(doc.Pages[1]);
        var target = doc.Pages.Add();

        using (new CultureScope(culture))
            absorber.Elements[1].AddOnPage(target);

        var replayed = Encoding.ASCII.GetString(target.GetContentStreamBytes() ?? []);
        Assert.Contains("\n-1 j\n", replayed);
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("ar-SA")]
    [InlineData(CultureScope.SyntheticName)]
    public void APageWhoseOperatorsAreEditedWritesItsLineCapAndJoinBack(string culture)
    {
        using var doc = Document.Open(RawPage.Build(Source, "<< >>"));
        var page = doc.Pages[1];

        byte[] saved;
        using (new CultureScope(culture))
        {
            page.Contents.Insert(1, new GSave());
            page.Contents.Add(new GRestore());
            using var output = new MemoryStream();
            doc.Save(output);
            saved = output.ToArray();
        }

        using var reopened = Document.Open(saved);
        var content = Encoding.ASCII.GetString(reopened.Pages[1].GetContentStreamBytes() ?? []);
        Assert.Contains("\n-1 J\n", content);
        Assert.Contains("\n-1 j\n", content);
    }
}
