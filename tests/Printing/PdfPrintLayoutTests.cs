using Aspose.Pdf.Printing;
using Xunit;

namespace Aspose.Pdf.Tests.Printing;

/// <summary>
/// Where a page lands on a sheet: the fit, the scale factor, the quarter turn, and the
/// alignment of a page that does not fill the paper. The rules are the reference's, read off
/// its printed templates; see <c>PdfPrintLayout</c>'s remarks for which template shows which.
/// </summary>
public class PdfPrintLayoutTests
{
    // A4 and US Letter in hundredths of an inch - the unit a printer surface works in.
    private const float A4Width = 827;
    private const float A4Height = 1169;
    private const float LetterWidth = 850;
    private const float LetterHeight = 1100;

    // The viewer's own defaults: no alignment either way.
    private const HorizontalAlignment DefaultHorizontal = HorizontalAlignment.None;
    private const VerticalAlignment DefaultVertical = VerticalAlignment.None;

    [Fact]
    public void AutoResize_FillsTheSheetAndKeepsTheProportions()
    {
        var placement = PdfPrintLayout.Fit(
            A4Width, A4Height, LetterWidth, LetterHeight,
            autoRotate: false, autoResize: true, scaleFactor: 1f,
            DefaultHorizontal, DefaultVertical);

        Assert.False(placement.Rotated);
        // Letter is the wider shape, so width is what runs out first.
        Assert.Equal(A4Width, placement.Width, 3);
        Assert.Equal(LetterHeight / LetterWidth * A4Width, placement.Height, 3);
    }

    [Fact]
    public void TheDefaultAlignment_PinsThePageToTheTopLeft()
    {
        // Letter fitted onto A4 leaves spare height. The reference puts none of it above the page.
        var placement = PdfPrintLayout.Fit(
            A4Width, A4Height, LetterWidth, LetterHeight,
            autoRotate: false, autoResize: true, scaleFactor: 1f,
            DefaultHorizontal, DefaultVertical);

        Assert.Equal(0, placement.X, 3);
        Assert.Equal(0, placement.Y, 3);
    }

    [Fact]
    public void BottomAlignment_PutsTheSpareHeightAboveThePage()
    {
        var placement = PdfPrintLayout.Fit(
            A4Width, A4Height, LetterWidth, LetterHeight,
            autoRotate: false, autoResize: true, scaleFactor: 1f,
            DefaultHorizontal, VerticalAlignment.Bottom);

        Assert.Equal(A4Height - (float)Math.Floor(placement.Height), placement.Y, 3);
    }

    [Fact]
    public void AScaleFactor_MultipliesTheFit_RatherThanReplacingIt()
    {
        var fitted = PdfPrintLayout.Fit(
            A4Width, A4Height, LetterWidth, LetterHeight,
            autoRotate: false, autoResize: true, scaleFactor: 1f,
            DefaultHorizontal, DefaultVertical);
        var scaled = PdfPrintLayout.Fit(
            A4Width, A4Height, LetterWidth, LetterHeight,
            autoRotate: false, autoResize: true, scaleFactor: 0.2f,
            DefaultHorizontal, DefaultVertical);

        Assert.Equal(fitted.Width * 0.2f, scaled.Width, 3);
        Assert.Equal(fitted.Height * 0.2f, scaled.Height, 3);
    }

    [Fact]
    public void WithoutAutoResize_TheScaleFactorAppliesToTheNaturalSize()
    {
        var placement = PdfPrintLayout.Fit(
            A4Width, A4Height, LetterWidth, LetterHeight,
            autoRotate: false, autoResize: false, scaleFactor: 0.5f,
            DefaultHorizontal, DefaultVertical);

        Assert.Equal(LetterWidth / 2, placement.Width, 3);
        Assert.Equal(LetterHeight / 2, placement.Height, 3);
    }

    [Fact]
    public void AutoResizeOnOrOff_DiffersOnlyByTheFit_AtTheSameFactor()
    {
        var off = PdfPrintLayout.Fit(
            A4Width, A4Height, LetterWidth, LetterHeight,
            autoRotate: false, autoResize: false, scaleFactor: 0.5f,
            DefaultHorizontal, DefaultVertical);
        var on = PdfPrintLayout.Fit(
            A4Width, A4Height, LetterWidth, LetterHeight,
            autoRotate: false, autoResize: true, scaleFactor: 0.5f,
            DefaultHorizontal, DefaultVertical);

        var fit = A4Width / LetterWidth;
        Assert.Equal(off.Width * fit, on.Width, 3);
        Assert.Equal(off.Height * fit, on.Height, 3);
    }

    [Fact]
    public void AScaleFactorOfZero_StatesNoFactor_RatherThanCollapsingThePage()
    {
        var fitted = PdfPrintLayout.Fit(
            A4Width, A4Height, LetterWidth, LetterHeight,
            autoRotate: false, autoResize: true, scaleFactor: 1f,
            DefaultHorizontal, DefaultVertical);
        var zero = PdfPrintLayout.Fit(
            A4Width, A4Height, LetterWidth, LetterHeight,
            autoRotate: false, autoResize: true, scaleFactor: 0f,
            DefaultHorizontal, DefaultVertical);

        Assert.Equal(fitted.Width, zero.Width, 3);
        Assert.Equal(fitted.Height, zero.Height, 3);
    }

    [Fact]
    public void AutoRotate_TurnsALandscapePageOntoAPortraitSheet()
    {
        var placement = PdfPrintLayout.Fit(
            A4Width, A4Height, A4Height, A4Width,
            autoRotate: true, autoResize: true, scaleFactor: 1f,
            DefaultHorizontal, DefaultVertical);

        Assert.True(placement.Rotated);
        // Turned, the page is exactly the sheet, so it fills it.
        Assert.Equal(A4Width, placement.Width, 3);
        Assert.Equal(A4Height, placement.Height, 3);
    }

    [Fact]
    public void AutoRotate_LeavesAPageThatAlreadyMatchesTheSheetAlone()
    {
        var placement = PdfPrintLayout.Fit(
            A4Width, A4Height, A4Width, A4Height,
            autoRotate: true, autoResize: true, scaleFactor: 1f,
            DefaultHorizontal, DefaultVertical);

        Assert.False(placement.Rotated);
    }

    [Fact]
    public void CenterAlignment_CentresThePageOnBothAxes()
    {
        var centred = PdfPrintLayout.Fit(
            A4Width, A4Height, A4Width, A4Height,
            autoRotate: false, autoResize: false, scaleFactor: 0.5f,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        Assert.Equal(A4Width / 4, centred.X, 3);
        Assert.Equal(A4Height / 4, centred.Y, 3);
    }

    [Fact]
    public void RightAlignment_PinsThePageToTheRightEdge()
    {
        var right = PdfPrintLayout.Fit(
            A4Width, A4Height, A4Width, A4Height,
            autoRotate: false, autoResize: false, scaleFactor: 0.5f,
            HorizontalAlignment.Right, DefaultVertical);

        Assert.Equal(A4Width / 2, right.X, 3);
        Assert.Equal(0, right.Y, 3);
    }

    [Fact]
    public void APageOfNoExtentTakesTheWholeSheet()
    {
        var placement = PdfPrintLayout.Fit(
            A4Width, A4Height, 0, 0,
            autoRotate: true, autoResize: true, scaleFactor: 1f,
            DefaultHorizontal, DefaultVertical);

        Assert.Equal(A4Width, placement.Width, 3);
        Assert.Equal(A4Height, placement.Height, 3);
        Assert.False(placement.Rotated);
    }
}
