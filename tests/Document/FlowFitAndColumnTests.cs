using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>How a flow line is fitted into what is left of a page when its paragraph
/// asks for lines to fit down to their descent, where its lines are written when their
/// origins are rounded, a box kept whole, and a page whose paragraphs flow in columns.</summary>
public sealed class FlowFitAndColumnTests
{
    private const double Margin = 36;
    private const double Pitch = 18;
    private const double FillerNeed = 4 + Pitch + 4;
    // Line box 18 over a 12 pt face 0.8 + 0.2484 em tall: 2.7096 of leading under the descent.
    private const double LeadingBelow = (Pitch - 12 * (0.8 + 0.2484)) / 2;
    private const string TwoLines = "a paragraph long enough to wrap onto a second line within the width of this narrow page for sure";

    [Fact]
    public void ALineFitsWhenItsDescentAndItsParagraphsMarginFit()
    {
        // The line needs its top margin, its pitch less the leading under its descent,
        // and its bottom margin: 4 + 15.29 + 4 = 23.29; its whole box would need 26.
        var room = 24.5;
        Assert.Single(PageTexts(Build(2, room, Fragment("tail", descent: true))));
        Assert.Equal(2, PageTexts(Build(2, room, Fragment("tail", descent: false))).Count);
        Assert.Equal(2, PageTexts(Build(2, 4 + Pitch - LeadingBelow + 4 - 0.1, Fragment("tail", descent: true))).Count);
    }

    [Fact]
    public void EveryLineKeepsTheRoomForItsParagraphsMargin()
    {
        // Two lines need 4 + 18 + 15.29 + 4 = 41.29: at 40 only the first stays.
        var pages = PageTexts(Build(2, 40, Fragment(TwoLines, descent: true)));
        Assert.Equal(2, pages.Count);
        Assert.Equal(3, pages[0].Count);
        Assert.NotEmpty(pages[1]);
    }

    [Fact]
    public void RoundedOriginsPutEveryLineOnItsOwnRoundedBaseline()
    {
        using var doc = new Aspose.Pdf.Document();
        var page = doc.Pages.Add(200, 400);
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        var tf = Fragment("one two three four five six seven eight nine ten eleven twelve thirteen", descent: false);
        tf.TextState.LineSpacing = 1.4865f;
        tf.TextState.FormattingOptions.LinePositionDecimals = 2;
        page.Paragraphs.Add(tf);
        using var reopened = Aspose.Pdf.Document.Open(doc.ToArray());
        double y = 0;
        var baselines = new List<double>();
        foreach (var op in reopened.Pages[1].Contents)
            if (op is MoveTextPosition td) { y += td.Y; baselines.Add(y); }
        Assert.True(baselines.Count > 2);
        foreach (var baseline in baselines)
            Assert.Equal(Math.Round(baseline, 2), baseline, 6);
    }

    [Fact]
    public void AKeptBoxThatWouldSplitStartsOnTheNextPage()
    {
        BoxBlock Box(bool kept)
        {
            var box = new BoxBlock { Padding = new MarginInfo(4, 4, 4, 4), IsKeptTogether = kept };
            for (var i = 0; i < 3; i++) box.Paragraphs.Add(Fragment("boxed " + i, descent: false));
            return box;
        }
        var split = PageTexts(Build(2, 60, Box(kept: false)));
        Assert.Contains("boxed 0", split[0]);
        var kept = PageTexts(Build(2, 60, Box(kept: true)));
        Assert.DoesNotContain("boxed 0", kept[0]);
        Assert.Equal(new[] { "boxed 0", "boxed 1", "boxed 2" }, kept[1]);
    }

    [Fact]
    public void AKeptBoxTallerThanAPageSplitsWhereItIs()
    {
        var box = new BoxBlock { IsKeptTogether = true };
        for (var i = 0; i < 40; i++) box.Paragraphs.Add(Fragment("boxed " + i, descent: false));
        var pages = PageTexts(Build(2, 60, box));
        Assert.Contains("boxed 0", pages[0]);
    }

    [Fact]
    public void APagesParagraphsFillItsColumnsInTurn()
    {
        using var doc = new Aspose.Pdf.Document();
        var page = doc.Pages.Add(400, 200);
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.ColumnInfo = new ColumnInfo { ColumnCount = 2, ColumnWidths = "150 150", ColumnSpacing = "28" };
        // 128 pt of column height holds four 26 pt paragraphs.
        for (var i = 0; i < 10; i++) page.Paragraphs.Add(Fragment("item " + i, descent: false));
        using var reopened = Aspose.Pdf.Document.Open(doc.ToArray());
        Assert.Equal(2, reopened.Pages.Count);
        var placed = Placed(reopened.Pages[1]);
        Assert.Equal(Margin, placed["item 0"].X, 3);
        Assert.Equal(Margin + 150 + 28, placed["item 4"].X, 3);
        Assert.Equal(placed["item 0"].Y, placed["item 4"].Y, 3);
        Assert.Equal(Margin, Placed(reopened.Pages[2])["item 8"].X, 3);
    }

    private static TextFragment Fragment(string text, bool descent)
    {
        var tf = new TextFragment(text);
        tf.TextState.FontSize = 12;
        tf.TextState.LineSpacing = (float)(Pitch - 12);
        tf.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        tf.TextState.LineBoxAscentEm = 0.8;
        tf.TextState.LineBoxDescentEm = 0.2484;
        tf.Margin = new MarginInfo(0, 4, 0, 4);
        tf.TextState.FormattingOptions.BottomMarginInsideRegion = true;
        tf.TextState.FormattingOptions.TopMarginAfterBreak = true;
        tf.TextState.FormattingOptions.LineFitsToDescent = descent;
        return tf;
    }

    /// <summary>A page holding <paramref name="fillers"/> paragraphs and then exactly
    /// <paramref name="room"/> points above its bottom margin, then the tail.</summary>
    private static Aspose.Pdf.Document Build(int fillers, double room, BaseParagraph tail)
    {
        var doc = new Aspose.Pdf.Document();
        var page = doc.Pages.Add(200, 2 * Margin + fillers * FillerNeed + room);
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        for (var i = 0; i < fillers; i++) page.Paragraphs.Add(Fragment("filler " + i, descent: false));
        page.Paragraphs.Add(tail);
        return Aspose.Pdf.Document.Open(doc.ToArray());
    }

    private static List<List<string>> PageTexts(Aspose.Pdf.Document doc)
    {
        var pages = new List<List<string>>();
        for (var p = 1; p <= doc.Pages.Count; p++)
        {
            var absorber = new TextFragmentAbsorber();
            doc.Pages[p].Accept(absorber);
            pages.Add(absorber.TextFragments.Select(f => f.Text.Trim()).Where(t => t.Length > 0).ToList());
        }
        return pages;
    }

    private static Dictionary<string, (double X, double Y)> Placed(Page page)
    {
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(page);
        return absorber.TextFragments.ToDictionary(f => f.Text.Trim(), f => (f.Position!.XIndent, f.Position.YIndent));
    }
}
