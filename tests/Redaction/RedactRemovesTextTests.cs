using Aspose.Pdf.Annotations;
using Aspose.Pdf.Text;
using Xunit;
using static Aspose.Pdf.Tests.Redaction.Redacting;

namespace Aspose.Pdf.Tests.Redaction;

/// <summary>The glyphs <see cref="RedactionAnnotation.Redact"/> removes: every glyph under the
/// rectangle, however it is shown, and from the saved file too - while every glyph outside it stays
/// where it was.</summary>
public class RedactRemovesTextTests
{
    private const string Fonts = "<< /Font << /F1 5 0 R >> >>";

    private static byte[] TextPage(string content) =>
        RawPage.Build(content, Fonts, new RawPage.Obj(RawPage.Helvetica));

    [Fact]
    public void TextInsideTheRectangleGoesFromThePageAndTheFile()
    {
        var pdf = TextPage("BT /F1 12 Tf 72 700 Td (Card:) Tj ET " +
                           "BT /F1 12 Tf 200 700 Td (4111111111111111) Tj ET " +
                           "BT /F1 12 Tf 400 700 Td (end) Tj ET");

        var saved = Save(pdf, new Rectangle(195, 695, 330, 715));

        using var doc = Document.Open(saved);
        var text = TextOf(doc);
        Assert.DoesNotContain("4111", text);
        Assert.Contains("Card:", text);
        Assert.Contains("end", text);
        Assert.False(SavedFile.Holds(saved, "4111111111111111"));
    }

    [Fact]
    public void OnlyTheOccurrenceUnderTheRectangleGoesWhenAWordRepeatsOnTheLine()
    {
        // Helvetica 12: "AAA SECRET BBB " is 106.704 wide, so the second SECRET runs from x 178.704
        // to 227.376 and CCC starts at 230.712.
        var pdf = TextPage("BT /F1 12 Tf 72 700 Td (AAA SECRET BBB SECRET CCC) Tj ET");

        using var doc = Document.Open(Save(pdf, new Rectangle(175, 695, 229, 715)));

        var text = TextOf(doc);
        Assert.Contains("AAA SECRET BBB", text);
        Assert.Contains("CCC", text);
        Assert.Equal(1, CountOf(text, "SECRET"));
    }

    [Fact]
    public void AKernedPieceOfAShowArrayGoesAndItsNeighboursKeepTheirPlaces()
    {
        var pdf = TextPage("BT /F1 12 Tf 72 600 Td [(Keep) -2000 (SECRETTJ) -2000 (After)] TJ ET");
        double afterX;
        using (var before = Document.Open(pdf)) afterX = XOf(before, "After");

        // "Keep" is 28.02 wide, each gap 24: SECRETTJ runs from x 124.02 to 186.024, After starts at 210.024.
        var saved = Save(pdf, new Rectangle(120, 595, 190, 615));

        using var doc = Document.Open(saved);
        var text = TextOf(doc);
        Assert.DoesNotContain("SECRET", text);
        Assert.Contains("Keep", text);
        Assert.Contains("After", text);
        Assert.Equal(afterX, XOf(doc, "After"), 2);
        Assert.False(SavedFile.Holds(saved, "SECRETTJ"));
    }

    [Fact]
    public void PartOfAWordUnderTheRectangleGoesAndTheRestOfTheLineKeepsItsPlace()
    {
        // Helvetica 12 digits are 6.672 wide: the run starts at x 100, so digits 5 to 12 (1-based)
        // run from x 126.688 to 180.064.
        var pdf = TextPage("BT /F1 12 Tf 100 500 Td (1234567890123456 tail) Tj ET");
        double tailX;
        using (var before = Document.Open(pdf)) tailX = XOf(before, "tail");

        var saved = Save(pdf, new Rectangle(127, 495, 180, 515));

        using var doc = Document.Open(saved);
        var text = TextOf(doc);
        Assert.Contains("1234", text);
        Assert.Contains("3456", text);
        Assert.DoesNotContain("56789012", text);
        Assert.Equal(tailX, XOf(doc, "tail"), 2);
        Assert.False(SavedFile.Holds(saved, "56789012"));
    }

    [Fact]
    public void TheLinesAboveAndBelowARectangleDrawnRoundOneLineStay()
    {
        var pdf = TextPage("BT /F1 12 Tf 14.4 TL 72 700 Td (Line above) Tj T* (Secret line) Tj T* (Line below) Tj ET");

        // The middle line's baseline is 685.6: the rectangle runs from its descent to its ascent.
        using var doc = Document.Open(Save(pdf, new Rectangle(70, 682.6, 200, 694.6)));

        var text = TextOf(doc);
        Assert.Contains("Line above", text);
        Assert.Contains("Line below", text);
        Assert.DoesNotContain("Secret", text);
    }

    [Fact]
    public void ALineWhoseFontBoxOnlyPokesIntoTheRectangleStays()
    {
        // Helvetica here states no metrics: its glyphs are taken to reach from 3 below the
        // baseline (500) to 9 above it. The rectangle starts 2.4 below their top - a fifth of their
        // height, above the ink of lower-case letters.
        var pdf = TextPage("BT /F1 12 Tf 72 500 Td (neighbour line) Tj ET BT /F1 12 Tf 72 520 Td (SECRETLINE) Tj ET");

        using var doc = Document.Open(Save(pdf, new Rectangle(70, 506.6, 300, 532)));

        var text = TextOf(doc);
        Assert.Contains("neighbour line", text);
        Assert.DoesNotContain("SECRETLINE", text);
    }

    [Fact]
    public void ARemovedCharacterBetweenWordsReadsAsAGap()
    {
        // Helvetica 12: "Date" is 26.016 wide and the colon 3.336, from x 98.016 to 101.352.
        var pdf = TextPage("BT /F1 12 Tf 72 700 Td [(Date:) -500 (13 November)] TJ ET");

        using var doc = Document.Open(Save(pdf, new Rectangle(97.5, 695, 101.8, 715)));

        var text = TextOf(doc);
        Assert.DoesNotContain(":", text);
        Assert.Contains("Date ", text);
        Assert.Contains("13 November", text);
    }

    [Fact]
    public void ACharacterEndingAShowReadsAsAGapWhenTheNextTextIsPlacedAnew()
    {
        // Helvetica 11.04: "Date" ends at x 322.48, the colon runs to 325.55; the next show is placed
        // at 326 on its own.
        var pdf = TextPage("BT /F1 11.04 Tf 1 0 0 1 299.21 670.42 Tm [(D) -4 (ate) 8 (:)] TJ ET " +
                           "BT /F1 11.04 Tf 1 0 0 1 326.0 670.42 Tm [( )] TJ ET " +
                           "BT /F1 11.04 Tf 1 0 0 1 329.07 670.42 Tm [(13 November)] TJ ET");

        using var doc = Document.Open(Save(pdf, new Rectangle(322.4, 665, 325.6, 685)));

        Assert.Contains("Date  13 November", TextOf(doc));
    }

    [Fact]
    public void ACharacterEndingAShowKeepsItsAdvanceWhenTheNextShowFollowsOnTheLine()
    {
        // "Date" ends at x 98.016; the colon runs to 101.352; " 13" follows from the pen.
        var pdf = TextPage("BT /F1 12 Tf 72 700 Td (Date:) Tj ( 13 November) Tj ET");
        double x;
        using (var before = Document.Open(pdf)) x = XOf(before, "13 November");

        using var doc = Document.Open(Save(pdf, new Rectangle(97.5, 695, 101.8, 715)));

        Assert.DoesNotContain(":", TextOf(doc));
        Assert.Equal(x, XOf(doc, "13 November"), 2);
    }

    [Fact]
    public void InvisibleTextUnderTheRectangleGoes()
    {
        var pdf = TextPage("BT /F1 12 Tf 72 700 Td (Visible) Tj ET " +
                           "BT 3 Tr /F1 12 Tf 100 300 Td (HIDDEN-SSN) Tj ET");

        var saved = Save(pdf, new Rectangle(95, 295, 200, 315));

        using var doc = Document.Open(saved);
        Assert.DoesNotContain("HIDDEN", TextOf(doc));
        Assert.Contains("Visible", TextOf(doc));
        Assert.False(SavedFile.Holds(saved, "HIDDEN-SSN"));
    }

    [Fact]
    public void RotatedTextUnderTheRectangleGoes()
    {
        // Rotated a quarter turn: the run climbs from (300, 300) up the page.
        var pdf = TextPage("BT /F1 12 Tf 0 1 -1 0 300 300 Tm (ROTSECRET) Tj ET " +
                           "BT /F1 12 Tf 72 700 Td (Visible) Tj ET");

        var saved = Save(pdf, new Rectangle(285, 295, 305, 380));

        using var doc = Document.Open(saved);
        Assert.DoesNotContain("ROTSECRET", TextOf(doc));
        Assert.Contains("Visible", TextOf(doc));
        Assert.False(SavedFile.Holds(saved, "ROTSECRET"));
    }

    [Fact]
    public void AQuoteShowKeepsItsLineMove()
    {
        var pdf = TextPage("BT /F1 12 Tf 14 TL 72 700 Td (First) Tj (SECRETQ) ' (Third) ' ET");
        double thirdY;
        using (var before = Document.Open(pdf)) thirdY = YOf(before, "Third");

        // SECRETQ sits on the second line, baseline 686.
        using var doc = Document.Open(Save(pdf, new Rectangle(70, 683, 200, 696)));

        Assert.DoesNotContain("SECRETQ", TextOf(doc));
        Assert.Equal(thirdY, YOf(doc, "Third"), 2);
    }

    private static int CountOf(string text, string word)
    {
        var n = 0;
        for (var i = text.IndexOf(word, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(word, i + word.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    private static Rectangle RectOf(Document doc, string phrase)
    {
        var absorber = new TextFragmentAbsorber(phrase);
        doc.Pages[1].Accept(absorber);
        Assert.Equal(1, absorber.TextFragments.Count);
        return absorber.TextFragments[1].Rectangle!;
    }

    private static double XOf(Document doc, string phrase) => RectOf(doc, phrase).LLX;

    private static double YOf(Document doc, string phrase) => RectOf(doc, phrase).LLY;
}
