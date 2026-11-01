using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Stamps;

/// <summary>A text stamp fitting its font size to a box: the stamp's own box, or the page's where
/// the stamp declares none - a boxless stamp fits the page exactly as one sized to the page.</summary>
public sealed class TextStampAutoFitTests
{
    private const string Text = "111 222 333 Hello World! Test Watermark 1 Test Watermark 2 Test Watermark 3 " +
                                "Test Watermark 4 Test Watermark 5 Test Watermark 6 Test Watermark 7 XXX";
    private const float Initial = 10;
    private const double Tolerance = 0.05;

    [Fact]
    public void AStampWithNoBoxFitsThePageLikeOneSizedToIt()
    {
        var boxed = FittedSize(sized: true);
        var boxless = FittedSize(sized: false);
        Assert.True(boxed > Initial, $"boxed {boxed}");
        Assert.Equal(boxed, boxless, Tolerance);
    }

    [Fact]
    public void AStampNotAskedToFitKeepsItsSize()
    {
        Assert.Equal(Initial, FittedSize(sized: false, fit: false), Tolerance);
    }

    private static double FittedSize(bool sized, bool fit = true)
    {
        using var document = new Document();
        var page = document.Pages.Add();
        var text = new FormattedText(Text, System.Drawing.Color.White, System.Drawing.Color.Black,
            FontStyle.Helvetica, EncodingType.Winansi, true, Initial);
        var stamp = new TextStamp(text)
        {
            AutoAdjustFontSizeToFitStampRectangle = fit,
            AutoAdjustFontSizePrecision = 0.01f,
            Scale = false,
            WordWrapMode = TextFormattingOptions.WordWrapMode.ByWords,
        };
        if (sized)
        {
            var rect = page.GetPageRect(true);
            stamp.Width = rect.Width;
            stamp.Height = rect.Height;
        }
        page.AddStamp(stamp);
        return stamp.FontSize;
    }
}
