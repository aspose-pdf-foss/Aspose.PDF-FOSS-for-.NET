using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>Tabs among runs (<see cref="TextSegment.IsTab"/>): where the wrapper puts them
/// (<see cref="TextFormattingOptions.RunTabStops"/>, <see cref="TextFormattingOptions.RunTabInterval"/>)
/// and what the flow draws for them.</summary>
public sealed class RunTabStopsTests
{
    // Every character is one unit wide, a space half a unit; "\t" is a tab run.
    private static double Measure(int run, string text)
    {
        if (text == "\t") return 0;
        var w = 0.0;
        foreach (var c in text) w += c == ' ' ? 0.5 : 1;
        return w;
    }

    private static List<List<RunPiece>> Wrap(double width, TabStops? stops, double interval, params string[] runs)
    {
        var rules = new RunWrapRules { TabRuns = ri => runs[ri] == "\t", TabStops = stops, TabInterval = interval };
        var words = RunWordWrap.SplitIntoWords(runs, Measure, rules);
        return RunWordWrap.Wrap(words, width, 0, null, rules, Measure);
    }

    private static TabStops Stops(float position, TabAlignmentType alignment, char anchor = '.')
    {
        var stops = new TabStops();
        var stop = stops.Add(position);
        stop.AlignmentType = alignment;
        stop.AnchorCharacter = anchor;
        return stops;
    }

    private static double TabRoom(List<List<RunPiece>> lines) => lines[0].First(p => p.TabAdvance is not null).TabAdvance!.Value;

    [Fact]
    public void ADefaultTabGoesToTheNextMultipleOfTheInterval()
    {
        Assert.Equal(2, TabRoom(Wrap(100, null, 4, "ab", "\t", "c")), 6);
        // exactly on a multiple: the whole interval
        Assert.Equal(4, TabRoom(Wrap(100, null, 4, "abcd", "\t", "c")), 6);
    }

    [Fact]
    public void ADefaultTabStopsAtTheLineEnd()
    {
        var lines = Wrap(10, null, 4, "abcdefghi", "\t", "x");
        Assert.Equal(1, TabRoom(lines), 6);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void ATabGoesToTheFirstStopPastWhereTheLineHasGot()
    {
        var stops = new TabStops();
        stops.Add(2f);
        stops.Add(6f);
        // "abc" reaches 3: the stop at 2 is behind it, the one at 6 is taken
        Assert.Equal(3, TabRoom(Wrap(100, stops, 50, "abc", "\t", "d")), 6);
    }

    [Fact]
    public void RightCentreAndAnchorStopsAlignWhatFollows()
    {
        Assert.Equal(6, TabRoom(Wrap(100, Stops(10, TabAlignmentType.Right), 50, "a", "\t", "bcd")), 6);
        Assert.Equal(7.5, TabRoom(Wrap(100, Stops(10, TabAlignmentType.Center), 50, "a", "\t", "bcd")), 6);
        // "12.5" has two units before its full stop
        Assert.Equal(7, TabRoom(Wrap(100, Stops(10, TabAlignmentType.Decimal), 50, "a", "\t", "12.5")), 6);
        Assert.Equal(7, TabRoom(Wrap(100, Stops(10, TabAlignmentType.Decimal, ','), 50, "a", "\t", "12,5 x")), 6);
        // no anchor character: ends at the stop
        Assert.Equal(5, TabRoom(Wrap(100, Stops(10, TabAlignmentType.Decimal), 50, "a", "\t", "1234")), 6);
    }

    [Fact]
    public void EverythingUpToTheNextTabIsAligned()
    {
        Assert.Equal(5, TabRoom(Wrap(100, Stops(10, TabAlignmentType.Right), 50, "a", "\t", "bc", "de")), 6);
    }

    [Fact]
    public void AnAlignedTabIsPulledBackToEndWithinTheLine()
    {
        // "bcdef" would start at 5 and end at 10 past the 8 wide line: pulled back to end there.
        Assert.Equal(2, TabRoom(Wrap(8, Stops(10, TabAlignmentType.Right), 50, "a", "\t", "bcdef")), 6);
        // what follows fills the line: no room at all
        Assert.Equal(0, TabRoom(Wrap(8, Stops(10, TabAlignmentType.Right), 50, "a", "\t", "bcdefgh")), 6);
    }

    [Fact]
    public void ALeftStopPastTheLineIsGoneToAndWhatFollowsStartsTheNextLine()
    {
        var stops = new TabStops();
        stops.Add(20f);
        var lines = Wrap(10, stops, 50, "abc", "\t", "d");
        Assert.Equal(17, TabRoom(lines), 6);
        Assert.Equal(2, lines.Count);
        Assert.Equal("d", lines[1][0].Text);
    }

    [Fact]
    public void AnAlignedTabLastOnTheLineReachesItsStop()
    {
        Assert.Equal(9, TabRoom(Wrap(100, Stops(10, TabAlignmentType.Right), 50, "a", "\t")), 6);
    }

    [Fact]
    public void TheFlowDrawsALeaderAcrossTheTabAndMovesTheTextOn()
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(36, 36, 36, 36);
        var fragment = new TextFragment();
        fragment.TextState.FontSize = 12;
        fragment.TextState.FormattingOptions.SegmentsFlowAsRuns = true;
        var stops = new TabStops();
        var stop = stops.Add(300f);
        stop.AlignmentType = TabAlignmentType.Right;
        stop.LeaderRule = new GraphInfo { LineWidth = 1, DashLengths = [0, 4], RoundDashCaps = true };
        fragment.TextState.FormattingOptions.RunTabStops = stops;
        fragment.Segments.Add(new TextSegment("Item"));
        fragment.Segments.Add(new TextSegment(string.Empty) { IsTab = true });
        fragment.Segments.Add(new TextSegment("12"));
        page.Paragraphs.Add(fragment);
        using var reopened = Document.Open(doc.ToArray());
        var xs = new List<double>();
        var moves = new List<double>();
        foreach (var op in reopened.Pages[1].Contents)
        {
            if (op is MoveTextPosition td) xs.Add(td.X);
            if (op is MoveTo m) moves.Add(m.X);
        }
        // "12" is 13.344 wide at 12 pt: it ends at the stop, 300 from the margin
        Assert.Equal(36 + 300 - 13.344, xs[^1], 3);
        // the leader starts where "Item" (23.34 wide) ends
        Assert.Contains(moves, x => Math.Abs(x - (36 + 23.34)) < 0.01);
    }
}
