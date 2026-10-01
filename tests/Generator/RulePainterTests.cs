using Aspose.Pdf;
using Aspose.Pdf.Content;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>The rules a border's sides are painted with (<see cref="RulePainter"/>):
/// a fitted dash pattern, a double rule's thirds, a two-tone rule's halves and
/// the darker tone they take, and the strokes a shared boundary gets. Every
/// number here is one read back from the content the painter writes.</summary>
public sealed class RulePainterTests
{
    [Fact]
    public void AFittedDashHoldsAWholeNumberOfDashesWithHalfAGapAtEachEnd()
    {
        // 200 pt dashed 5/3.5: 24 dashes, the gap stretched to 3.33, the phase a dash and half a gap.
        var (pattern, phase) = RulePainter.FitDashes(new[] { 5.0, 3.5 }, 0, 200);
        Assert.Equal(5, pattern[0], 6);
        Assert.Equal(200.0 / 24 - 5, pattern[1], 6);
        Assert.Equal(5 + pattern[1] / 2, phase, 6);
        // A run the pattern already divides keeps its gap.
        var (whole, _) = RulePainter.FitDashes(new[] { 1.0, 1.5 }, 0, 200);
        Assert.Equal(1.5, whole[1], 6);
        // A dash of no length (round dots) fits its gap alone.
        var (dots, dotPhase) = RulePainter.FitDashes(new[] { 0.0, 2.5 }, 0, 53.95);
        Assert.Equal(53.95 / 22, dots[1], 6);
        Assert.Equal(dots[1] / 2, dotPhase, 6);
        // Anything but a dash and a gap is left alone.
        var (kept, keptPhase) = RulePainter.FitDashes(new[] { 3.0, 3.0, 1.0 }, 4, 200);
        Assert.Equal(new[] { 3.0, 3.0, 1.0 }, kept);
        Assert.Equal(4, keptPhase);
    }

    [Fact]
    public void ALoneRuleStrokesInItsDashesFromItsOffsetWithRoundCaps()
    {
        var builder = new ContentStreamBuilder();
        var dots = new GraphInfo { LineWidth = 2, DashLengths = new[] { 0.0, 5 }, DashOffset = 2.5, RoundDashCaps = true };
        RulePainter.StrokeRule(builder, dots, 10, 20, 110);
        var content = System.Text.Encoding.ASCII.GetString(builder.Build());
        Assert.Contains("[0 5] 2.5 d", content);
        Assert.Contains("1 J", content);
        Assert.Contains("2 w", content);
        Assert.Equal((10.0, 20.0, 110.0, 20.0), Parse(builder.Build()).Strokes.Select(s => (s.X1, s.Y1, s.X2, s.Y2)).Single());
        // Without an offset the whole-point phase holds; without a colour the rule is black.
        builder = new ContentStreamBuilder();
        RulePainter.StrokeRule(builder, new GraphInfo { DashArray = new[] { 2 }, DashPhase = 1 }, 0, 0, 50);
        content = System.Text.Encoding.ASCII.GetString(builder.Build());
        Assert.Contains("[2] 1 d", content);
        Assert.Contains("0 0 0 RG", content);
    }

    [Fact]
    public void TheDarkerToneKeepsTheHueAndDropsTheBrightness()
    {
        var face = RulePainter.Darker(Color.FromRgbBytes(212, 208, 200));
        Assert.Equal((128, 125, 121), (face.R, face.G, face.B));
        var red = RulePainter.Darker(Color.FromRgbBytes(255, 0, 0));
        Assert.Equal((171, 0, 0), (red.R, red.G, red.B));
        var gray = RulePainter.Darker(Color.FromRgbBytes(128, 128, 128));
        Assert.Equal(44, gray.R);
        var black = RulePainter.Darker(Color.FromRgbBytes(0, 0, 0));
        Assert.Equal(0, black.R);
    }

    [Fact]
    public void ADoubleRuleIsTwoThirdsABandApart()
    {
        var border = Box(3, new GraphInfo { LineWidth = 3, Style = RuleStyle.Double });
        var fills = Fills(border, 36, 700, 206, 42);
        // The top: an outer band the full width at the box edge, an inner band a
        // third wide two thirds in, standing two thirds of the side widths in from the corners.
        Assert.Contains(fills, f => Near(f.X, 36) && Near(f.W, 206) && Near(f.Y, 741) && Near(f.H, 1));
        Assert.Contains(fills, f => Near(f.X, 38) && Near(f.W, 202) && Near(f.Y, 739) && Near(f.H, 1));
        // The left: outer band the full height, inner band inset by two thirds of the top and bottom.
        Assert.Contains(fills, f => Near(f.X, 36) && Near(f.W, 1) && Near(f.Y, 700) && Near(f.H, 42));
        Assert.Contains(fills, f => Near(f.X, 38) && Near(f.W, 1) && Near(f.Y, 702) && Near(f.H, 38));
        Assert.Equal(8, fills.Count);
    }

    [Fact]
    public void ATwoToneRuleIsTwoHalvesLitBySide()
    {
        var face = Color.FromRgbBytes(212, 208, 200);
        var dark = RulePainter.Darker(face);
        var groove = Fills(Box(4, new GraphInfo { LineWidth = 4, Style = RuleStyle.Groove, Color = face }), 36, 700, 208, 44);
        // Groove: the top's outer half dark and inner half lit; the bottom the other way round.
        Assert.Contains(groove, f => Near(f.Y, 742) && Near(f.H, 2) && Near(f.W, 208) && Same(f, dark));
        Assert.Contains(groove, f => Near(f.Y, 740) && Near(f.H, 2) && Near(f.W, 204) && Near(f.X, 38) && Same(f, face));
        Assert.Contains(groove, f => Near(f.Y, 700) && Near(f.H, 2) && Near(f.W, 208) && Same(f, face));
        Assert.Contains(groove, f => Near(f.Y, 702) && Near(f.H, 2) && Near(f.W, 204) && Same(f, dark));
        // Inset: both halves of the top dark, both halves of the right lit.
        var inset = Fills(Box(4, new GraphInfo { LineWidth = 4, Style = RuleStyle.Inset, Color = face }), 36, 700, 208, 44);
        Assert.Contains(inset, f => Near(f.Y, 742) && Same(f, dark));
        Assert.Contains(inset, f => Near(f.Y, 740) && Same(f, dark));
        Assert.Contains(inset, f => Near(f.X, 242) && Same(f, face));
        Assert.Contains(inset, f => Near(f.X, 240) && Same(f, face));
    }

    [Fact]
    public void ADashedSideIsStrokedBetweenItsNeighboursClockwise()
    {
        var border = Box(2, new GraphInfo { LineWidth = 2, DashLengths = new[] { 8.0, 2 }, DashPhase = 4 });
        // The box 36..240 x 348.34..406.29 the reference framed with a fixed 8/2 dash.
        var strokes = Strokes(border, 36, 348.34, 204, 57.95);
        Assert.Equal(4, strokes.Count);
        // Top left to right, one width in from each side; bottom right to left; left upward; right downward.
        Assert.Equal((38, 405.29, 238, 405.29), strokes[0]);
        Assert.Equal((238, 349.34, 38, 349.34), strokes[1]);
        Assert.Equal((37, 350.34, 37, 404.29), strokes[2]);
        Assert.Equal((239, 404.29, 239, 350.34), strokes[3]);
    }

    [Fact]
    public void ADoubleSegmentOnASharedBoundaryIsTwoStrokesAThirdApart()
    {
        var builder = new ContentStreamBuilder();
        var border = new BorderInfo(BorderSide.All, 3f);
        RulePainter.StrokeSegment(builder, new GraphInfo { LineWidth = 3, Style = RuleStyle.Double }, border, 3, 36, 100, 139, 100);
        var strokes = Parse(builder.Build()).Strokes;
        Assert.Equal(2, strokes.Count);
        Assert.Contains(strokes, s => Near(s.Y1, 101) && Near(s.X1, 36) && Near(s.X2, 139));
        Assert.Contains(strokes, s => Near(s.Y1, 99));
    }

    private static BorderInfo Box(float width, GraphInfo side)
    {
        var border = new BorderInfo(BorderSide.All, width);
        border.Top = Clone(side);
        border.Bottom = Clone(side);
        border.Left = Clone(side);
        border.Right = Clone(side);
        return border;
    }

    private static GraphInfo Clone(GraphInfo side) => (GraphInfo)side.Clone();

    private static List<Rect> Fills(BorderInfo border, double x, double y, double w, double h)
    {
        var builder = new ContentStreamBuilder();
        RulePainter.PaintBox(builder, border, x, y, w, h);
        return Parse(builder.Build()).Fills;
    }

    private static List<(double X1, double Y1, double X2, double Y2)> Strokes(BorderInfo border, double x, double y, double w, double h)
    {
        var builder = new ContentStreamBuilder();
        RulePainter.PaintBox(builder, border, x, y, w, h);
        return Parse(builder.Build()).Strokes.Select(s => (Math.Round(s.X1, 2), Math.Round(s.Y1, 2), Math.Round(s.X2, 2), Math.Round(s.Y2, 2))).ToList();
    }

    /// <summary>The painter's own output, read token by token: the rectangles it
    /// fills (with the fill colour in force) and the lines it strokes.</summary>
    private static (List<Rect> Fills, List<Line> Strokes) Parse(byte[] content)
    {
        var fills = new List<Rect>();
        var strokes = new List<Line>();
        var operands = new List<double>();
        double r = 0, g = 0, b = 0;
        double[]? rect = null;
        double? fromX = null, fromY = null;
        foreach (var token in System.Text.Encoding.ASCII.GetString(content).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                operands.Add(number);
                continue;
            }
            switch (token)
            {
                case "rg": (r, g, b) = (operands[0], operands[1], operands[2]); break;
                case "re": rect = operands.ToArray(); break;
                case "f" when rect is { } re: fills.Add(new Rect(re[0], re[1], re[2], re[3], r, g, b)); rect = null; break;
                case "m": (fromX, fromY) = (operands[0], operands[1]); break;
                case "l" when fromX is { } x0 && fromY is { } y0: strokes.Add(new Line(x0, y0, operands[0], operands[1])); fromX = null; break;
            }
            operands.Clear();
        }
        return (fills, strokes);
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;

    private static bool Same(Rect fill, Color colour) =>
        Near(fill.R * 255, colour.R) && Near(fill.G * 255, colour.G) && Near(fill.B * 255, colour.B);

    private sealed record Rect(double X, double Y, double W, double H, double R, double G, double B);

    private sealed record Line(double X1, double Y1, double X2, double Y2);
}
