using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A paragraph's orphan and widow control at a break (<see cref="LineControls"/>).</summary>
public sealed class LineControlsTests
{
    private static TextFormattingOptions Options(int orphans = 0, int widows = 0, int maxMoved = 0, bool moveWhole = false) =>
        new() { MinOrphanLines = orphans, MinWidowLines = widows, MaxWidowLinesMoved = maxMoved, MoveWholeOnWidowViolation = moveWhole };

    [Fact]
    public void TooFewFirstLinesMoveTheParagraphOnWhole()
    {
        Assert.Equal(0, LineControls.Kept(Options(orphans: 2), 1, 5, first: true, mayMoveWhole: true));
        Assert.Equal(2, LineControls.Kept(Options(orphans: 2), 2, 5, first: true, mayMoveWhole: true));
        // at the top of its region already, it keeps what fits
        Assert.Equal(1, LineControls.Kept(Options(orphans: 2), 1, 5, first: true, mayMoveWhole: false));
        // a later part is no orphan
        Assert.Equal(1, LineControls.Kept(Options(orphans: 2), 1, 5, first: false, mayMoveWhole: true));
    }

    [Fact]
    public void TooFewCarriedLinesAreMadeUpWithinTheAllowance()
    {
        Assert.Equal(3, LineControls.Kept(Options(widows: 2, maxMoved: 1), 4, 5, true, true));
        Assert.Equal(2, LineControls.Kept(Options(widows: 3, maxMoved: 2), 4, 5, true, true));
        // more to move than allowed: the break stands, or the paragraph moves whole when asked
        Assert.Equal(4, LineControls.Kept(Options(widows: 3, maxMoved: 1), 4, 5, true, true));
        Assert.Equal(0, LineControls.Kept(Options(widows: 3, maxMoved: 1, moveWhole: true), 4, 5, true, true));
    }

    [Fact]
    public void MovingLinesNeverBreaksTheOrphanFloor()
    {
        Assert.Equal(4, LineControls.Kept(Options(orphans: 3, widows: 3, maxMoved: 5), 4, 5, true, true));
        // and never leaves nothing behind
        Assert.Equal(4, LineControls.Kept(Options(widows: 5, maxMoved: 5), 4, 5, true, true));
    }

    [Fact]
    public void AParagraphThatFitsOrControlsNothingKeepsWhatFits()
    {
        Assert.Equal(5, LineControls.Kept(Options(orphans: 3, widows: 3, maxMoved: 3), 5, 5, true, true));
        Assert.Equal(2, LineControls.Kept(Options(), 2, 5, true, true));
        Assert.False(LineControls.Any(Options(orphans: 1, widows: 1)));
        Assert.True(LineControls.Any(Options(widows: 2)));
    }
}
