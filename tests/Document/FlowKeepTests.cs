using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>What a flow paragraph asks of the page break: to stay whole, to be
/// followed on its page by the next one, to get its top margin again where it
/// continues, and to close its background at the descent where it breaks --
/// and the two box limits a block may add, a floor on its width and a cap on
/// its height.</summary>
public sealed class FlowKeepTests
{
    private const double Margin = 36;
    private const string Long = "words that wrap onto three lines on this page so that the paragraph straddles the page break at the bottom and shows what the layout does with it, with more words to be sure of a third line and a distinct start to the part that continues";

    [Fact]
    public void AKeptParagraphThatWouldSplitStartsOnTheNextPage()
    {
        var split = FirstLines(Build(fillers: 28, tail: Fragment(Long)));
        Assert.Equal(2, split.Count);
        Assert.False(split[1].StartsWith("words that wrap"), split[1]);
        var kept = FirstLines(Build(fillers: 28, tail: Fragment(Long, f => f.IsKeptTogether = true)));
        Assert.StartsWith("words that wrap", kept[1]);
    }

    [Fact]
    public void AParagraphTallerThanAPageSplitsThoughKept()
    {
        var tall = string.Join(" ", Enumerable.Repeat(Long, 24));
        var pages = FirstLines(Build(fillers: 1, tail: Fragment(tall, f => f.IsKeptTogether = true)));
        Assert.Equal(2, pages.Count);
        Assert.StartsWith("filler", pages[0]);
    }

    [Fact]
    public void AParagraphKeptWithTheNextMovesWhenTheNextCannotStart()
    {
        var doc = Build(fillers: 28, tail: Fragment("kept with next", f => f.IsKeptWithNext = true), then: Fragment(Long));
        var pages = FirstLines(doc);
        Assert.StartsWith("kept with next", pages[1]);
        var stays = FirstLines(Build(fillers: 27, tail: Fragment("kept with next", f => f.IsKeptWithNext = true), then: Fragment(Long)));
        Assert.False(stays[1].StartsWith("kept with next"), stays[1]);
        Assert.False(stays[1].StartsWith("words that wrap"), stays[1]);
    }

    [Fact]
    public void TheContinuationGetsItsTopMarginAgainAndClosesItsBoxAtTheDescent()
    {
        var plain = Build(fillers: 28, tail: Fragment(Long, f => { f.TextState.BackgroundColor = Color.Yellow; f.TextState.FormattingOptions.BlockBackground = true; }));
        var asked = Build(fillers: 28, tail: Fragment(Long, f =>
        {
            f.TextState.BackgroundColor = Color.Yellow;
            f.TextState.FormattingOptions.BlockBackground = true;
            f.TextState.FormattingOptions.TopMarginAfterBreak = true;
            f.TextState.FormattingOptions.BreakBoxDescentEm = 0.2484;
        }));
        Assert.Equal(FirstBaseline(plain, 2) - 4, FirstBaseline(asked, 2), 3);
        var plainBox = LastFill(plain, 1);
        var askedBox = LastFill(asked, 1);
        Assert.Equal(plainBox.Y + plainBox.Height, askedBox.Y + askedBox.Height, 3);
        Assert.Equal(LastBaseline(asked, 1) - 0.2484 * 12, askedBox.Y, 3);
    }

    [Fact]
    public void AMinWidthFloorsTheBoxAndAMaxHeightCapsIt()
    {
        var floored = Box(f => { f.BlockBoxed = true; f.BlockWidth = 200; f.BlockMinWidth = 300; });
        Assert.Equal(300, floored.Width, 6);
        var capped = Box(f => { f.BlockBoxed = true; f.BoxHeight = 30; f.BoxMaxHeight = 20; });
        Assert.Equal(20, capped.Height, 6);
        var floorWins = Box(f => { f.BlockBoxed = true; f.BoxMinHeight = 40; f.BoxMaxHeight = 30; });
        Assert.Equal(40, floorWins.Height, 6);
    }

    private static TextFragment Fragment(string text, Action<TextFragment>? shape = null)
    {
        var tf = new TextFragment(text);
        tf.TextState.FontSize = 12;
        tf.TextState.LineSpacing = 6;
        tf.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        tf.TextState.LineBoxAscentEm = 0.8;
        tf.TextState.LineBoxDescentEm = 0.2484;
        tf.Margin = new MarginInfo(0, 4, 0, 4);
        shape?.Invoke(tf);
        return tf;
    }

    private static Aspose.Pdf.Document Build(int fillers, TextFragment tail, TextFragment? then = null)
    {
        var doc = new Aspose.Pdf.Document();
        var page = doc.Pages.Add(595, 842);
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        for (var i = 0; i < fillers; i++) page.Paragraphs.Add(Fragment("filler " + i));
        page.Paragraphs.Add(tail);
        if (then is not null) page.Paragraphs.Add(then);
        return Aspose.Pdf.Document.Open(doc.ToArray());
    }

    private static List<string> FirstLines(Aspose.Pdf.Document doc)
    {
        var lines = new List<string>();
        for (var p = 1; p <= doc.Pages.Count; p++)
        {
            var absorber = new TextFragmentAbsorber();
            doc.Pages[p].Accept(absorber);
            lines.Add(absorber.TextFragments.Count > 0 ? absorber.TextFragments[1].Text : "");
        }
        return lines;
    }

    private static double FirstBaseline(Aspose.Pdf.Document doc, int page) =>
        doc.Pages[page].Contents.OfType<MoveTextPosition>().First().Y;

    private static double LastBaseline(Aspose.Pdf.Document doc, int page)
    {
        double y = 0;
        foreach (var op in doc.Pages[page].Contents)
        {
            if (op is MoveTextPosition td) y = td.Y;
            else if (op is MoveToNextLine) y -= 18;
        }
        return y;
    }

    private static Re LastFill(Aspose.Pdf.Document doc, int page)
    {
        Re? last = null; Re? pending = null;
        foreach (var op in doc.Pages[page].Contents)
        {
            if (op is Re re) pending = re;
            else if (op is Aspose.Pdf.Operators.Fill && pending is not null) { last = pending; pending = null; }
        }
        return last ?? throw new Xunit.Sdk.XunitException("no fill");
    }

    private static Re Box(Action<TextFragment> shape)
    {
        using var doc = new Aspose.Pdf.Document();
        var page = doc.Pages.Add(595, 842);
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.Paragraphs.Add(Fragment("boxed", f => { f.TextState.BackgroundColor = Color.Yellow; f.TextState.FormattingOptions.BlockBackground = true; shape(f); }));
        using var reopened = Aspose.Pdf.Document.Open(doc.ToArray());
        return LastFill(reopened, 1);
    }
}
