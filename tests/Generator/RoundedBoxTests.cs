using Aspose.Pdf;
using Aspose.Pdf.Content;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A box with rounded corners (<see cref="CornerRadii"/>,
/// <see cref="RulePainter.PaintRoundedBox"/>): how its reaches resolve, how its
/// background and border are clipped, where a solid side is split from its
/// neighbours, and the path and clip of a dashed side. Every number is one the
/// reference layout wrote for the same box.</summary>
public sealed class RoundedBoxTests
{
    [Fact]
    public void AReachIsHeldToHalfTheBoxOnItsOwnAxis()
    {
        // 100 on a 152 x 19.98 box: 76 across, 9.99 down.
        Assert.Equal((76, 9.99), new CornerRadius(100).Resolve(152, 19.98));
        // A fraction is of the box's own width or height.
        var percent = new CornerRadius(0.2, 5) { HorizontalIsFraction = true };
        var (x, y) = percent.Resolve(152, 19.98);
        Assert.Equal(30.4, x, 6);
        Assert.Equal(5, y, 6);
        // Nothing on either axis leaves the corner square.
        Assert.Equal((0, 0), new CornerRadius(10, 0).Resolve(152, 19.98));
    }

    [Fact]
    public void AShortDashedRunKeepsItsWholeShareAsTheGap()
    {
        // 17.98 of a 2 pt dashed side (10 on, 7 off): two cycles of 8.99, no room for
        // a dash and a gap, so the whole share is the gap.
        var (pattern, phase) = RulePainter.FitDashes(new[] { 10.0, 7 }, 0, 17.98);
        Assert.Equal(10, pattern[0], 6);
        Assert.Equal(8.99, pattern[1], 6);
        Assert.Equal(14.495, phase, 6);
        // 17.98 of a 1 pt side (5 on, 3.5 off): three cycles of 5.993, the dash taken off.
        var (thin, _) = RulePainter.FitDashes(new[] { 5.0, 3.5 }, 0, 17.98);
        Assert.Equal(17.98 / 3 - 5, thin[1], 6);
    }

    [Fact]
    public void TheBorderIsClippedCornerByCornerToTheRing()
    {
        // A 2 pt solid rule round 36..190 x 780.02..802, every corner 10.
        var ops = Paint(Solid(2), 36, 780.02, 154, 21.98, new CornerRadii(new CornerRadius(10)));
        Assert.Equal(4, ops.Count(o => o.Name == "W"));
        Assert.Equal(4, ops.Count(o => o.Name == "W*"));
        // A corner whose rule is wider than its reach leaves no inner curve to clip.
        var wide = Paint(Solid(4), 36, 708.07, 158, 25.98, new CornerRadii(new CornerRadius(2)));
        Assert.Equal(4, wide.Count(o => o.Name == "W"));
        Assert.Equal(0, wide.Count(o => o.Name == "W*"));
    }

    [Fact]
    public void ASolidSideIsSplitOnTheMitreCarriedToTheNearerReach()
    {
        // A 6 pt top over 1 pt sides, every corner 10: the top-left mitre (1, 6) reaches
        // the 10 down before the 10 across, at (37.67, 690.07).
        var border = Solid(1);
        border.Top = new GraphInfo { LineWidth = 6 };
        var ops = Paint(border, 36, 675.09, 152, 24.98, new CornerRadii(new CornerRadius(10)));
        var top = FirstMoveAfterFill(ops);
        Assert.Equal(37.67, top.X, 2);
        Assert.Equal(690.07, top.Y, 2);
        // A rule wider than its reach splits on the plain mitre: (40, 730.05).
        var wide = Paint(Solid(4), 36, 708.07, 158, 25.98, new CornerRadii(new CornerRadius(2)));
        var plain = FirstMoveAfterFill(wide);
        Assert.Equal(40, plain.X, 2);
        Assert.Equal(730.05, plain.Y, 2);
    }

    [Fact]
    public void ADashedSideRunsThroughItsCornersCurvesAndStopsWhereItsMitresCross()
    {
        // Dashed 2 pt sides and a 6 pt top, corners 10 x 14 (held to 12.99 on a 25.98 box).
        var border = Dashed(2);
        border.Top = new GraphInfo { LineWidth = 6, DashLengths = new[] { 30.0, 21 }, DashesFitTheLength = true };
        var ops = Paint(border, 36, 776.02, 154, 25.98, new CornerRadii(new CornerRadius(10, 14)));
        var strokes = ops.Where(o => o.Name == "S").ToList();
        Assert.Equal(4, strokes.Count);
        // The top starts on the left band's middle a quarter width short of the reach
        // (12.99 - 1.5), curves to the top band's middle (10 - 0.5), and is dashed on
        // the straight run of 150.
        var topStart = ops.First(o => o.Name == "m" && Near(o.Args[0], 37) && Near(o.Args[1], 790.51));
        var topCurve = ops[ops.IndexOf(topStart) + 1];
        Assert.Equal("c", topCurve.Name);
        Assert.Equal(45.5, topCurve.Args[4], 2);
        Assert.Equal(799, topCurve.Args[5], 2);
        Assert.Contains(ops, o => o.Name == "d" && Near(o.Args[0], 30) && Near(o.Args[1], 20) && Near(o.Args[2], 40));
        // The right side's inner points pass each other: its clip closes where the
        // top-right mitre (-2, -6) and the bottom-right mitre (-2, 2) cross.
        Assert.Contains(ops, o => o.Name == "l" && Near(o.Args[0], 183.505) && Near(o.Args[1], 782.515));
    }

    [Fact]
    public void ABackgroundKeptToTheContentBoxIsRoundedOnThatBox()
    {
        // A 150 x 17.98 content box inside a 2 pt rule and 8 pt padding, corners 10:
        // its own outline curves 10 across and 8.99 down.
        var ops = Paint(Solid(2), 36, 566.11, 170, 37.98, new CornerRadii(new CornerRadius(10)),
            fill: (46, 576.11, 150, 17.98));
        Assert.Contains(ops, o => o.Name == "c" && Near(o.Args[4], 46) && Near(o.Args[5], 585.1));
        Assert.Contains(ops, o => o.Name == "l" && Near(o.Args[0], 56) && Near(o.Args[1], 594.09));
    }

    private static BorderInfo Solid(float width)
    {
        var border = new BorderInfo(BorderSide.All, width);
        border.Top = new GraphInfo { LineWidth = width };
        border.Bottom = new GraphInfo { LineWidth = width };
        border.Left = new GraphInfo { LineWidth = width };
        border.Right = new GraphInfo { LineWidth = width };
        return border;
    }

    private static BorderInfo Dashed(float width)
    {
        var border = Solid(width);
        foreach (var side in new[] { border.Top, border.Bottom, border.Left, border.Right })
        {
            side.DashLengths = new[] { 5.0 * width, 3.5 * width };
            side.DashesFitTheLength = true;
        }
        return border;
    }

    private static List<Op> Paint(BorderInfo border, double x, double y, double w, double h, CornerRadii radii,
        (double X, double Y, double W, double H)? fill = null)
    {
        var builder = new ContentStreamBuilder();
        var corners = radii.Resolve(w, h);
        RulePainter.PaintRoundedBox(builder, border, x, y, w, h, corners,
            fill is null ? null : Color.FromRgbBytes(255, 175, 175), fill ?? (x, y, w, h), null);
        return Parse(builder.Build());
    }

    /// <summary>The first point a fill path starts from once the clips are set: the
    /// top side's first inner point.</summary>
    private static (double X, double Y) FirstMoveAfterFill(List<Op> ops)
    {
        var rg = ops.FindIndex(o => o.Name == "rg");
        var move = ops.Skip(rg).First(o => o.Name == "m");
        return (move.Args[0], move.Args[1]);
    }

    private static List<Op> Parse(byte[] content)
    {
        var ops = new List<Op>();
        var operands = new List<double>();
        // A dash array's numbers count as the operator's operands, ahead of its phase.
        foreach (var token in System.Text.Encoding.ASCII.GetString(content)
                     .Replace("[", " ").Replace("]", " ")
                     .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                operands.Add(number);
                continue;
            }
            ops.Add(new Op(token, operands.ToArray()));
            operands.Clear();
        }
        return ops;
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;

    private sealed record Op(string Name, double[] Args);
}
