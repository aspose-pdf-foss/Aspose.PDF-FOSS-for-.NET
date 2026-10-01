using Aspose.Pdf.Drawing;
using Xunit;

namespace Aspose.Pdf.Tests.Drawing;

/// <summary>A linear gradient laid out with CSS colour-stop rules: positions normalized,
/// hints as powers, and the area covered by clipping, padding, repeating or reflecting.</summary>
public sealed class LinearGradientLayoutTests
{
    private const double Tolerance = 1e-9;
    private static readonly double[] Box = [0, 0, 100, 50];

    private static GradientStopSpec Stop(double gray, double offset = double.NaN,
        GradientOffsetKind kind = GradientOffsetKind.Relative, double hint = 0, GradientHintKind hintKind = GradientHintKind.None) =>
        new([gray, gray, gray], double.IsNaN(offset) ? 0 : offset, double.IsNaN(offset) ? GradientOffsetKind.Auto : kind, hint, hintKind);

    private static string Pieces(LinearGradientLayout layout) => string.Join(" ",
        layout.Segments.Select(s => $"{s.Start:0.###}..{s.End:0.###}:{s.From[0]:0.#}>{s.To[0]:0.#}" + (s.Exponent == 1 ? "" : $"^{s.Exponent:0.####}")));

    [Fact]
    public void PositionlessStopsAreSpreadEvenlyBetweenTheirNeighbours()
    {
        var layout = LinearGradientLayout.Compute(0, 0, 100, 0,
            [Stop(0), Stop(0.2), Stop(0.4, 0.8), Stop(0.6), Stop(1)], GradientSpread.None, Box)!;
        Assert.Equal("0..0.4:0>0.2 0.4..0.8:0.2>0.4 0.8..0.9:0.4>0.6 0.9..1:0.6>1", Pieces(layout));
        Assert.Equal([0, 0, 100, 0], layout.Coords);
        Assert.Equal([0, 1], layout.Domain);
    }

    [Fact]
    public void AStopBeforeAnEarlierOneIsMovedUpToIt()
    {
        var layout = LinearGradientLayout.Compute(0, 0, 100, 0,
            [Stop(0, 0.6), Stop(0.5, 0.3), Stop(1, 0.9)], GradientSpread.None, Box)!;
        Assert.Equal("0.6..0.6:0>0.5 0.6..0.9:0.5>1", Pieces(layout));
        Assert.Equal([60, 0, 90, 0], layout.Coords, new ToleranceComparer());
        Assert.Equal([0.6, 0.9], layout.Domain, new ToleranceComparer());
    }

    [Fact]
    public void DistancesAreFractionsOfTheVector()
    {
        var layout = LinearGradientLayout.Compute(0, 0, 30, 40,
            [Stop(0, 10, GradientOffsetKind.Absolute), Stop(1, 40, GradientOffsetKind.Absolute)], GradientSpread.None, Box)!;
        Assert.Equal([6, 8, 24, 32], layout.Coords, new ToleranceComparer());
        Assert.Equal([0.2, 0.8], layout.Domain, new ToleranceComparer());
    }

    [Fact]
    public void AHintIsAPowerAndAHintAtAnEndIsAFlatColour()
    {
        Assert.Equal("0..1:0>1^0.5", Pieces(LinearGradientLayout.Compute(0, 0, 100, 0,
            [Stop(0, hint: 0.25, hintKind: GradientHintKind.RelativeBetweenStops), Stop(1)], GradientSpread.None, Box)!));
        Assert.Equal("0..1:0>1^0.5757", Pieces(LinearGradientLayout.Compute(0, 0, 100, 0,
            [Stop(0, hint: 30, hintKind: GradientHintKind.AbsoluteOnGradient), Stop(1)], GradientSpread.None, Box)!));
        Assert.Equal("0..0.5:0.5>0.5 0.5..1:0.5>0.5", Pieces(LinearGradientLayout.Compute(0, 0, 100, 0,
            [Stop(0, hint: 0, hintKind: GradientHintKind.RelativeBetweenStops),
             Stop(0.5, hint: 1, hintKind: GradientHintKind.RelativeBetweenStops), Stop(1)], GradientSpread.None, Box)!));
        // A hint past the next stop moves the stop up to it: the piece before is flat.
        Assert.Equal("0..0.7:0>0 0.7..1:0.5>1", Pieces(LinearGradientLayout.Compute(0, 0, 100, 0,
            [Stop(0, hint: 0.7, hintKind: GradientHintKind.RelativeOnGradient), Stop(0.5, 0.5), Stop(1)], GradientSpread.None, Box)!));
    }

    [Fact]
    public void PaddingAddsFlatEndPiecesOutToTheArea()
    {
        var layout = LinearGradientLayout.Compute(20, 0, 60, 0, [Stop(0), Stop(1)], GradientSpread.Pad, Box)!;
        Assert.Equal("-0.5..0:0>0 0..1:0>1 1..2:1>1", Pieces(layout));
        Assert.Equal([0, 0, 100, 0], layout.Coords, new ToleranceComparer());
        Assert.Equal([-0.5, 2], layout.Domain, new ToleranceComparer());
    }

    [Fact]
    public void WithoutASpreadTheGradientIsClippedToItsStopsOrIsNothing()
    {
        var layout = LinearGradientLayout.Compute(20, 0, 60, 0, [Stop(0), Stop(1)], GradientSpread.None, Box)!;
        Assert.Equal([20, 0, 60, 0], layout.Coords, new ToleranceComparer());
        Assert.Null(LinearGradientLayout.Compute(0, 0, 100, 0, [Stop(0, 0.5), Stop(1, 0.5)], GradientSpread.None, Box));
        Assert.Null(LinearGradientLayout.Compute(10, 10, 10, 10, [Stop(0), Stop(1)], GradientSpread.None, Box));
    }

    [Fact]
    public void RepeatingJumpsBackAtEachCopyAndReflectingMirrorsEveryOther()
    {
        var repeat = LinearGradientLayout.Compute(0, 0, 40, 0, [Stop(0), Stop(0.5, 0.3), Stop(1)], GradientSpread.Repeat, Box)!;
        Assert.Equal("0..0.3:0>0.5 0.3..1:0.5>1 1..1:1>0 1..1.3:0>0.5 1.3..2:0.5>1 2..2:1>0 2..2.3:0>0.5 2.3..3:0.5>1", Pieces(repeat));
        Assert.Equal([0, 2.5], repeat.Domain, new ToleranceComparer());

        var reflect = LinearGradientLayout.Compute(30, 0, 50, 0, [Stop(0), Stop(1)], GradientSpread.Reflect, Box)!;
        Assert.Equal("-2..-1:0>1 -1..0:1>0 0..1:0>1 1..2:1>0 2..3:0>1 3..4:1>0", Pieces(reflect));
        Assert.Equal([-1.5, 3.5], reflect.Domain, new ToleranceComparer());
        Assert.Equal([0, 0, 100, 0], reflect.Coords, new ToleranceComparer());
    }

    [Fact]
    public void OneColourIsThatColourAcrossTheBottomOfTheArea()
    {
        var layout = LinearGradientLayout.Compute(10, 10, 10, 10, [Stop(0), Stop(1)], GradientSpread.Pad, Box)!;
        Assert.Equal("0..1:1>1", Pieces(layout));
        Assert.Equal([0, 0, 100, 0], layout.Coords);
    }

    private sealed class ToleranceComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < Tolerance;

        public int GetHashCode(double obj) => 0;
    }
}
