using Aspose.Pdf.Content;

namespace Aspose.Pdf;

/// <summary>Paints the rules of a border -- the four sides of a box, or one
/// segment of a shared boundary -- in every <see cref="RuleStyle"/> and dash a
/// <see cref="GraphInfo"/> can ask for.
///
/// A side of a box is painted INSIDE the box, its own width in from the edge, and
/// as a filled band wherever it is continuous: a solid rule is one band the whole
/// width, a double rule two bands a third wide with a third between them, a
/// two-tone rule (groove, ridge, inset, outset) two halves in the colour and its
/// darker tone. Every band runs the box's full extent along its side, so the
/// corners are covered, except an INNER band -- the inner third of a double rule,
/// the inner half of a two-tone one -- which stops short of the corners by the
/// same fraction of the neighbouring sides' widths, as the mitre it stands in
/// would have it. A dashed rule is a stroke instead, centred in its band and
/// running between the inner edges of the two sides it meets; its gap may be
/// stretched so the run holds a whole number of dashes, half a gap at each end
/// (<see cref="GraphInfo.DashesFitTheLength"/>).
///
/// On a shared boundary (a collapsed grid's) a rule is one stroke centred on the
/// line: a double rule two strokes a third wide, a third either side of the line;
/// a two-tone rule its colour alone -- there is no outside for the darker tone to
/// face.</summary>
internal static partial class RulePainter
{
    /// <summary>What a two-tone rule's darker tone loses in HSB brightness against
    /// the colour itself: the classic Windows 3D face (212, 208, 200) darkens to
    /// (128, 125, 121). Hue and saturation keep, so every channel scales alike.</summary>
    private const double DarkerToneDrop = 0.33;

    /// <summary>How far in from the box edge the inner band of a double rule
    /// starts, as a fraction of the rule's width -- and how far the band's ends
    /// stand in from the corners, as a fraction of the neighbouring sides' widths.</summary>
    private const double DoubleInnerBandStart = 2.0 / 3.0;

    /// <summary>The width a border side draws with; 0 when the side does not draw.</summary>
    internal static double SideWidth(BorderInfo border, BorderSide flag)
    {
        var (assigned, side) = SideOf(border, flag);
        return border.Side.HasFlag(flag) || assigned ? Math.Max(0, side?.LineWidth > 0 ? side.LineWidth : border.Width) : 0;
    }

    /// <summary>True when the side asks for a dash pattern of any kind.</summary>
    internal static bool HasDash(GraphInfo? side) =>
        side?.DashLengths is { Length: > 0 } || side?.DashArray is { Length: > 0 };

    /// <summary>Paints every side of the box that draws, inside the box.</summary>
    internal static void PaintBox(ContentStreamBuilder builder, BorderInfo border, double x, double y, double w, double h)
    {
        var widths = BoxWidths(border);
        foreach (var flag in BoxSides)
            if (StripOf(flag, x, y, w, h, widths) is { } strip) PaintSide(builder, border, flag, strip);
    }

    /// <summary>The order a box's sides are painted in.</summary>
    private static readonly BorderSide[] BoxSides = { BorderSide.Top, BorderSide.Bottom, BorderSide.Left, BorderSide.Right };

    /// <summary>The widths the four sides of a box draw with.</summary>
    private static (double Top, double Bottom, double Left, double Right) BoxWidths(BorderInfo border) =>
        (SideWidth(border, BorderSide.Top), SideWidth(border, BorderSide.Bottom),
         SideWidth(border, BorderSide.Left), SideWidth(border, BorderSide.Right));

    /// <summary>The band one side of the box occupies; null when the side draws nothing.</summary>
    private static Strip? StripOf(BorderSide flag, double x, double y, double w, double h,
        (double Top, double Bottom, double Left, double Right) widths)
    {
        var (top, bottom, left, right) = widths;
        return flag switch
        {
            BorderSide.Top when top > 0 => new Strip(x, y + h - top, w, top, Horizontal: true, OuterAtHigh: true, Before: left, After: right),
            BorderSide.Bottom when bottom > 0 => new Strip(x, y, w, bottom, Horizontal: true, OuterAtHigh: false, Before: left, After: right),
            BorderSide.Left when left > 0 => new Strip(x, y, left, h, Horizontal: false, OuterAtHigh: false, Before: bottom, After: top),
            BorderSide.Right when right > 0 => new Strip(x + w - right, y, right, h, Horizontal: false, OuterAtHigh: true, Before: bottom, After: top),
            _ => null,
        };
    }

    /// <summary>One segment of a shared boundary, centred on the line from
    /// (x1, y1) to (x2, y2).</summary>
    internal static void StrokeSegment(ContentStreamBuilder builder, GraphInfo? side, BorderInfo border, double width,
        double x1, double y1, double x2, double y2)
    {
        if (width <= 0) return;
        var colour = side?.Color ?? border.Color;
        var style = side?.Style ?? RuleStyle.Solid;
        if (style == RuleStyle.Double)
        {
            var third = width / 3;
            var horizontal = Math.Abs(y1 - y2) < 1e-9;
            var (dx, dy) = horizontal ? (0.0, third) : (third, 0.0);
            Stroke(builder, side, colour, third, x1 + dx, y1 + dy, x2 + dx, y2 + dy);
            Stroke(builder, side, colour, third, x1 - dx, y1 - dy, x2 - dx, y2 - dy);
            return;
        }
        Stroke(builder, side, colour, width, x1, y1, x2, y2);
    }

    /// <summary>The colour's darker tone: the same hue and saturation, the
    /// brightness lowered by <see cref="DarkerToneDrop"/>.</summary>
    internal static Color Darker(Color colour)
    {
        var brightness = Math.Max(colour.R, Math.Max(colour.G, colour.B)) / 255.0;
        if (brightness <= 0) return colour;
        var factor = Math.Max(0, brightness - DarkerToneDrop) / brightness;
        return Color.FromRgbBytes(Scale(colour.R), Scale(colour.G), Scale(colour.B));

        int Scale(byte channel) => (int)Math.Round(channel * factor);
    }

    /// <summary>A dash pattern fitted to a run: the gap stretched (or squeezed) so
    /// the run holds a whole number of dashes, and the phase that starts and ends
    /// the run on half a gap. When a run's share per dash is no longer than the
    /// dash itself, the whole share is the gap. A pattern that is not a dash and a
    /// gap is returned as it is, with the phase it came with.</summary>
    internal static (double[] Pattern, double Phase) FitDashes(double[] pattern, double phase, double length)
    {
        if (pattern.Length != 2 || length <= 0) return (pattern, phase);
        var dash = pattern[0];
        var cycle = dash + pattern[1];
        if (cycle <= 0) return (pattern, phase);
        var count = Math.Max(1, (int)Math.Ceiling(length / cycle - 1e-9));
        // Each of the whole number of cycles is a dash and the gap after it; a share
        // too short to hold the dash is kept whole as the gap rather than squeezing
        // the gap to nothing.
        var share = length / count;
        var gap = share > dash ? share - dash : share;
        return (new[] { dash, gap }, dash + gap / 2);
    }

    /// <summary>The band a side occupies along the box edge: its rectangle, its
    /// direction, which of its long edges is the box's outside, and the widths of
    /// the two sides it meets at its ends (the one at its low end first).</summary>
    private readonly record struct Strip(double X, double Y, double W, double H, bool Horizontal, bool OuterAtHigh,
        double Before, double After)
    {
        /// <summary>The band's thickness: the rule's width.</summary>
        public double Width => Horizontal ? H : W;

        /// <summary>The part of the band from <paramref name="from"/> to
        /// <paramref name="to"/> across (fractions of the width, 0 at the box edge),
        /// its ends standing in by the given fractions of the neighbouring widths.</summary>
        public (double X, double Y, double W, double H) Part(double from, double to, double endInset)
        {
            var width = Width;
            var depth = OuterAtHigh ? width - to * width : from * width;
            var thickness = (to - from) * width;
            var startIn = Before * endInset;
            var endIn = After * endInset;
            return Horizontal
                ? (X + startIn, Y + depth, W - startIn - endIn, thickness)
                : (X + depth, Y + startIn, thickness, H - startIn - endIn);
        }
    }

    private static void PaintSide(ContentStreamBuilder builder, BorderInfo border, BorderSide flag, Strip strip)
    {
        var (_, side) = SideOf(border, flag);
        var colour = side?.Color ?? border.Color;
        if (HasDash(side))
        {
            // A stroke centred in the band, between the inner edges of the sides it
            // meets, run CLOCKWISE round the box -- the top left to right, the right
            // downward, the bottom right to left, the left upward -- which is where
            // an unfitted pattern's first dash falls.
            var half = strip.Width / 2;
            var (x1, y1, x2, y2) = strip.Horizontal
                ? (strip.X + strip.Before, strip.Y + half, strip.X + strip.W - strip.After, strip.Y + half)
                : (strip.X + half, strip.Y + strip.Before, strip.X + half, strip.Y + strip.H - strip.After);
            if (strip.Horizontal == strip.OuterAtHigh)
                Stroke(builder, side, colour, strip.Width, x1, y1, x2, y2);
            else
                Stroke(builder, side, colour, strip.Width, x2, y2, x1, y1);
            return;
        }
        switch (side?.Style ?? RuleStyle.Solid)
        {
            case RuleStyle.Double:
                Fill(builder, colour, strip.Part(0, 1 - DoubleInnerBandStart, 0));
                Fill(builder, colour, strip.Part(DoubleInnerBandStart, 1, DoubleInnerBandStart));
                break;
            case RuleStyle.Groove or RuleStyle.Ridge or RuleStyle.Inset or RuleStyle.Outset:
                var (outer, inner) = Tones(side!.Style, flag, colour);
                Fill(builder, outer, strip.Part(0, 0.5, 0));
                Fill(builder, inner, strip.Part(0.5, 1, 0.5));
                break;
            default:
                Fill(builder, colour, strip.Part(0, 1, 0));
                break;
        }
    }

    /// <summary>The colours of a two-tone side's outer and inner halves.</summary>
    private static (Color Outer, Color Inner) Tones(RuleStyle style, BorderSide flag, Color colour)
    {
        var dark = Darker(colour);
        var lit = flag is BorderSide.Top or BorderSide.Left;
        return style switch
        {
            RuleStyle.Inset => lit ? (dark, dark) : (colour, colour),
            RuleStyle.Outset => lit ? (colour, colour) : (dark, dark),
            RuleStyle.Groove => lit ? (dark, colour) : (colour, dark),
            _ => lit ? (colour, dark) : (dark, colour),
        };
    }

    private static void Fill(ContentStreamBuilder builder, Color colour, (double X, double Y, double W, double H) rect)
    {
        if (rect.W <= 0 || rect.H <= 0) return;
        builder.SetFillColor(colour);
        builder.Rectangle(rect.X, rect.Y, rect.W, rect.H).Fill();
    }

    /// <summary>A lone rule across a box, from <paramref name="x1"/> to <paramref name="x2"/>
    /// at height <paramref name="y"/>: one stroke of the rule's width and colour (black when
    /// it names none), in its dashes and caps.</summary>
    internal static void StrokeRule(ContentStreamBuilder builder, GraphInfo rule, double x1, double y, double x2) =>
        Stroke(builder, rule, rule.Color ?? Color.Black, rule.LineWidth, x1, y, x2, y);

    private static void Stroke(ContentStreamBuilder builder, GraphInfo? side, Color colour, double width,
        double x1, double y1, double x2, double y2)
    {
        var dashed = SetStrokeStyle(builder, side, colour, width, Math.Abs(x2 - x1) + Math.Abs(y2 - y1));
        builder.MoveTo(x1, y1).LineTo(x2, y2).Stroke();
        ResetStrokeStyle(builder, side, dashed);
    }

    /// <summary>Sets the pen a rule strokes with: its width, its colour and, for a
    /// dashed side, its pattern -- fitted to <paramref name="length"/> when the side
    /// asks for that -- and its caps. True when a dash was set.</summary>
    private static bool SetStrokeStyle(ContentStreamBuilder builder, GraphInfo? side, Color colour, double width,
        double length)
    {
        builder.SetLineWidth(width);
        builder.SetStrokeColor(colour);
        if (!HasDash(side)) return false;
        var pattern = side!.DashLengths ?? Array.ConvertAll(side.DashArray!, d => (double)d);
        var phase = side.DashStart;
        if (side.DashesFitTheLength) (pattern, phase) = FitDashes(pattern, phase, length);
        builder.SetDashPattern(pattern, phase);
        if (side.RoundDashCaps) builder.SetLineCap(1);
        return true;
    }

    /// <summary>Puts back the solid pen a dashed rule replaced.</summary>
    private static void ResetStrokeStyle(ContentStreamBuilder builder, GraphInfo? side, bool dashed)
    {
        if (!dashed) return;
        builder.SetDashPattern(Array.Empty<double>(), 0);
        if (side!.RoundDashCaps) builder.SetLineCap(0);
    }

    private static (bool Assigned, GraphInfo? Side) SideOf(BorderInfo border, BorderSide flag) => flag switch
    {
        BorderSide.Top => (border.TopAssigned, border.RawTop),
        BorderSide.Bottom => (border.BottomAssigned, border.RawBottom),
        BorderSide.Left => (border.LeftAssigned, border.RawLeft),
        _ => (border.RightAssigned, border.RawRight),
    };
}
