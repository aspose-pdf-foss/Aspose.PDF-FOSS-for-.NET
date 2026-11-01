using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>A block given a width of its own sets its right-aligned, centred and right-to-left lines in a box of that
/// width from where its lines start, not across the page's text column.</summary>
public class HtmlToPdfDeclaredWidthTests
{
    private const string Words = "Short words set right";
    // Where the default page's text column starts (its 90 pt margin and the 6 pt body inset).
    private const double ColumnLeft = 96;
    private const double Tolerance = 0.5;

    private static Rectangle Place(string html)
    {
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        var absorber = new TextFragmentAbsorber(Words);
        doc.Pages[1].Accept(absorber);
        var place = Assert.Single(absorber.TextFragments.Cast<TextFragment>()).Rectangle;
        Assert.NotNull(place);
        return place;
    }

    [Fact]
    public void ARightAlignedParagraphOfADeclaredWidthEndsAtThatWidthsRightEdge()
    {
        var at = Place($"<html><body><p style=\"text-align:right; width:300pt\">{Words}</p></body></html>");
        Assert.Equal(ColumnLeft + 300, at.URX, Tolerance);
    }

    [Fact]
    public void ACentredParagraphOfADeclaredWidthCentresInItsBox()
    {
        var at = Place($"<html><body><p style=\"text-align:center; width:300pt\">{Words}</p></body></html>");
        Assert.Equal(ColumnLeft + 150, (at.LLX + at.URX) / 2, Tolerance);
    }

    [Fact]
    public void APixelWidthMakesABoxOfThreeQuartersAsManyPoints()
    {
        var at = Place($"<html><body><p style=\"text-align:right; width:300px\">{Words}</p></body></html>");
        Assert.Equal(ColumnLeft + 225, at.URX, Tolerance);
    }

    [Fact]
    public void AParagraphInADivOfADeclaredWidthAlignsInTheDivsBox()
    {
        var at = Place($"<html><body><div style=\"width:300pt\"><p style=\"text-align:right\">{Words}</p></div></body></html>");
        Assert.Equal(ColumnLeft + 300, at.URX, Tolerance);
    }

    [Fact]
    public void ABoxWithALeftMarginStartsThatFarIn()
    {
        var at = Place($"<html><body><p style=\"text-align:right; width:300pt; margin-left:50pt\">{Words}</p></body></html>");
        Assert.Equal(ColumnLeft + 50 + 300, at.URX, Tolerance);
    }

    [Fact]
    public void ARightToLeftParagraphOfADeclaredWidthStartsAtItsBoxsRightEdge()
    {
        var at = Place($"<html><body><p dir=\"rtl\" style=\"width:300pt\">{Words}</p></body></html>");
        Assert.Equal(ColumnLeft + 300, at.URX, Tolerance);
    }

    [Fact]
    public void ABoxAnAutoLeftMarginMovesIsNoFrameForItsLines()
    {
        // An auto left margin pushes the box to the column's right side: its right-aligned line ends past where a box
        // starting at the column's left would end.
        var at = Place($"<html><body><p style=\"text-align:right; width:300pt; margin-left:auto\">{Words}</p></body></html>");
        Assert.True(at.URX > ColumnLeft + 300 + 100, $"ends at {at.URX}");
    }
}
