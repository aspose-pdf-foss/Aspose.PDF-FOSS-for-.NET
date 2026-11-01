using System.Collections.Generic;
using System.IO;
using Aspose.Pdf;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

/// <summary>A multiline rich text box whose face is NAMED by its Style string seats its first baseline a
/// face-keyed depth under the border inset (the Standard-14 faces here need no system font); a single-line
/// field keeps its centred seat.</summary>
public class StyleNamedMultilineFirstLineTests
{
    private const double BoxBottom = 355, BoxTop = 427;

    private static Document ThreeLineField(string style, bool multiline)
    {
        var document = new Document();
        document.Pages.Add();
        var field = new RichTextBoxField(document.Pages[1], new Rectangle(250, 1140, 550, 1068));
        field.SetPosition(new Point(225, BoxBottom));
        field.PartialName = "notes";
        field.Scrollable = false;
        field.Multiline = multiline;
        field.Style = style;
        field.MaxLen = 200;
        field.Value = "Test\nTest\nTest";
        document.Form.Add(field, 1);
        return document;
    }

    private static List<double> AppearanceTdYs(Document document)
    {
        using var saved = new MemoryStream();
        document.Save(saved);
        using var reopened = new Document(new MemoryStream(saved.ToArray()));
        var ys = new List<double>();
        foreach (Operator op in ((Field)reopened.Form[1]).NormalAppearance!.Contents)
            if (op is MoveTextPosition move) ys.Add(move.Y);
        return ys;
    }

    [Theory]
    [InlineData("font: 'Helvetica'  10pt", 58.37, -11.49)]
    [InlineData("font: 'Courier'  10pt", 59.37, -12.03)]
    [InlineData("font: 'Courier' bold 10pt", 59.47, -11.99)]
    public void FirstBaselineHangsTheFaceKeyedDepthUnderTheInset(string style, double firstY, double pitch)
    {
        using var document = ThreeLineField(style, multiline: true);
        var ys = AppearanceTdYs(document);
        Assert.Equal(3, ys.Count);
        Assert.Equal(firstY, ys[0], 2);
        Assert.Equal(pitch, ys[1], 2);
    }

    [Theory]
    [InlineData("font: 'Helvetica'  10pt", 422.3)]
    [InlineData("font: 'Courier'  10pt", 423.8)]
    [InlineData("font: 'Courier' bold 10pt", 423.9)]
    public void FlattenedFirstLineTopSitsWhereTheReferencePutsIt(string style, double firstLineTop)
    {
        using var document = ThreeLineField(style, multiline: true);
        document.Flatten();
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(document.Pages[1]);
        var top = absorber.TextFragments[1].Rectangle!.URY;
        Assert.True(top < BoxTop, $"first line top {top} is not below the box top {BoxTop}");
        Assert.Equal(firstLineTop, top, 1);
    }

    [Fact]
    public void SingleLineSeatIsUnchanged()
    {
        using var document = ThreeLineField("font: 'Courier'  15pt", multiline: false);
        Assert.Equal(31.526, AppearanceTdYs(document)[0], 3);
    }
}
