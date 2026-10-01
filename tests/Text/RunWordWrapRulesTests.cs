using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class RunWordWrapRulesTests
{
    // Every character is one unit wide, a space half a unit.
    private static double Measure(int run, string text)
    {
        var w = 0.0;
        foreach (var c in text) w += c == ' ' ? 0.5 : 1;
        return w;
    }

    private static readonly RunWrapRules Rules = new()
    {
        BreaksAfter = (run, text, i) => text[i] == '-',
        LineFeedsBreak = true,
        BreakOverlongWords = true,
        DropLineStartSpaces = true,
    };

    private static string[] Lines(double width, double indent, params string[] runs)
    {
        var words = RunWordWrap.SplitIntoWords(runs, Measure, Rules);
        var lines = RunWordWrap.Wrap(words, width, indent, null, Rules, Measure);
        return lines.Select(l => string.Concat(l.Select(p => p.Text))).ToArray();
    }

    [Fact]
    public void PlainRules_WrapAsBefore()
    {
        var runs = new[] { "aaaa bb-cc", " dd" };
        var plain = RunWordWrap.Wrap(RunWordWrap.SplitIntoWords(runs, Measure), 6, 0);
        Assert.Equal(new[] { "aaaa ", "bb-cc ", "dd" }, plain.Select(l => string.Concat(l.Select(p => p.Text))));
    }

    [Fact]
    public void Hyphen_EndsAWord_AndStaysOnTheLine()
    {
        Assert.Equal(new[] { "aaaa-", "bbbb" }, Lines(6, 0, "aaaa-bbbb"));
        Assert.Equal(new[] { "ab--", "cd--", "ef" }, Lines(4.5, 0, "ab--cd--ef"));
    }

    [Fact]
    public void Hyphen_AtTheEndOfARun_StillEndsTheWord()
    {
        Assert.Equal(new[] { "aa-", "bbbb" }, Lines(5, 0, "aa-", "bbbb"));
    }

    [Fact]
    public void LineFeed_ForcesABreak_AndStaysOnTheLineItEnds()
    {
        var words = RunWordWrap.SplitIntoWords(new[] { "first\nsecond\n\nfourth" }, Measure, Rules);
        var lines = RunWordWrap.Wrap(words, 100, 0, null, Rules, Measure);
        Assert.Equal(new[] { "first\n", "second\n", "\n", "fourth" }, lines.Select(l => string.Concat(l.Select(p => p.Text))));
    }

    [Fact]
    public void CarriageReturnLineFeed_IsOneBreak()
    {
        Assert.Equal(new[] { "aa\r", "bb\r\n", "cc" }, Lines(100, 0, "aa\rbb\r\ncc"));
    }

    [Fact]
    public void SpacesBeforeABreak_HangOnTheLine_SpacesAfterItAreDropped()
    {
        Assert.Equal(new[] { "aa  \n", "bb" }, Lines(100, 0, "aa  \n  bb"));
    }

    [Fact]
    public void LeadingSpaces_OfTheFirstLine_AreDropped()
    {
        Assert.Equal(new[] { "lead   ", "many" }, Lines(6, 0, "  lead   many"));
    }

    [Fact]
    public void OverlongWord_MovesToALineOfItsOwn_ThenBreaksByCharacters()
    {
        Assert.Equal(new[] { "ab ", "Superc", "alifra", "gilist", "ic" }, Lines(6, 0, "ab Supercalifragilistic"));
    }

    [Fact]
    public void OverlongWord_OnTheFirstLine_HonoursTheIndent()
    {
        Assert.Equal(new[] { "Su", "percal", "ifragi", "listic" }, Lines(6, 4, "Supercalifragilistic"));
    }

    [Fact]
    public void OverlongWord_Tail_TakesTheFollowingWordsWhenTheyFit()
    {
        Assert.Equal(new[] { "abcdef", "gh ij" }, Lines(6, 0, "abcdefgh ij"));
    }

    [Fact]
    public void OverlongWord_AcrossRuns_KeepsEachRunsPieces()
    {
        var words = RunWordWrap.SplitIntoWords(new[] { "abcd", "efgh" }, Measure, Rules);
        var lines = RunWordWrap.Wrap(words, 3, 0, null, Rules, Measure);
        Assert.Equal(3, lines.Count);
        Assert.Equal(new[] { (0, "abc", 0) }, lines[0].Select(p => (p.Run, p.Text, p.Start)));
        Assert.Equal(new[] { (0, "d", 3), (1, "ef", 0) }, lines[1].Select(p => (p.Run, p.Text, p.Start)));
        Assert.Equal(new[] { (1, "gh", 2) }, lines[2].Select(p => (p.Run, p.Text, p.Start)));
    }

    [Fact]
    public void Pieces_KnowWhereTheyStartInTheirRun()
    {
        var words = RunWordWrap.SplitIntoWords(new[] { "aa bb", "cc dd" }, Measure, Rules);
        var lines = RunWordWrap.Wrap(words, 5, 0, null, Rules, Measure);
        Assert.Equal(new[] { (0, "aa", 0), (0, " ", 2) }, lines[0].Select(p => (p.Run, p.Text, p.Start)));
        Assert.Equal(new[] { (0, "bb", 3), (1, "cc", 0), (1, " ", 2) }, lines[1].Select(p => (p.Run, p.Text, p.Start)));
        Assert.Equal(new[] { (1, "dd", 3) }, lines[2].Select(p => (p.Run, p.Text, p.Start)));
    }

    [Fact]
    public void RulesThatMeasure_RefuseAMissingMeasure()
    {
        var words = RunWordWrap.SplitIntoWords(new[] { "aa" }, Measure, Rules);
        Assert.Throws<ArgumentNullException>(() => RunWordWrap.Wrap(words, 5, 0, null, Rules, (Func<int, string, double>?)null));
    }
}
