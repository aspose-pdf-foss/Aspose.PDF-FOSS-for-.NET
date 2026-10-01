using Aspose.Pdf.Content;

namespace Aspose.Pdf;

/// <summary>A box with rounded corners (<see cref="CornerRadii"/>).
///
/// Its background fills the given rectangle inside the rounded outline. Its border
/// is painted inside the RING between that outline and an inner one, each of whose
/// corners reaches as far as the outer corner less the widths of the two sides
/// that meet there (none when that leaves nothing). Within the ring:
/// <list type="bullet">
/// <item>a solid side is a filled quad from its two outer corners to two inner
/// points; each inner point stands on the corner's mitre -- the line from the
/// outer corner in by the two sides' widths -- carried on until it reaches the
/// nearer of the corner's two reaches, and never short of the plain mitre;</item>
/// <item>a dashed side is a stroke along the middle of its band that takes in the
/// curve of both of its corners, its dash fitted to the straight run between its
/// neighbours' inner edges, clipped to the quad from its outer corners to inner
/// points on the mitres at the depth of ITS corners' reach across it (the
/// vertical reach for the top and bottom, the horizontal for the sides) -- where
/// the two points pass each other, the quad closes at the mitres' crossing;</item>
/// <item>every other style is painted as on a square box and the ring cuts it.</item>
/// </list>
/// The middle of a band round a corner is a quarter ellipse whose centre stands a
/// quarter of the side widths nearer the corner than the outline's own centre,
/// its reaches three quarters of the widths shorter than the outline's.</summary>
internal static partial class RulePainter
{
    /// <summary>The control-point fraction of a quarter circle drawn as one cubic curve.</summary>
    private const double QuarterArcKappa = 0.5522847498307936;

    /// <summary>What a band's middle curve gives up against the outline's centre, as a
    /// fraction of the side widths: its centre moves a quarter width toward the corner.</summary>
    private const double MiddleCurveCentreShift = 0.25;

    /// <summary>One corner of the box: the outer point, the directions that lead into
    /// the box from it, its two reaches, and the widths of the vertical and the
    /// horizontal side that meet there.</summary>
    private readonly record struct Corner(double X, double Y, int InX, int InY, double Rx, double Ry,
        double VerticalWidth, double HorizontalWidth)
    {
        public bool Rounded => Rx > 0 && Ry > 0;

        /// <summary>A point in from the corner along its mitre, by <paramref name="k"/> times the widths.</summary>
        public (double X, double Y) OnMitre(double k) => (X + InX * VerticalWidth * k, Y + InY * HorizontalWidth * k);

        /// <summary>The inner end of a solid side at this corner: the mitre carried to the
        /// nearer reach, never short of the widths themselves.</summary>
        public (double X, double Y) SolidInnerPoint() =>
            OnMitre(Math.Max(1, Math.Min(Ratio(Rx, VerticalWidth), Ratio(Ry, HorizontalWidth))));

        /// <summary>The inner end of a stroked side's clip at this corner: the mitre
        /// carried to the depth of the corner's reach ACROSS the side.</summary>
        public (double X, double Y) StrokeInnerPoint(bool horizontalSide) =>
            OnMitre(Math.Max(1, horizontalSide ? Ratio(Ry, HorizontalWidth) : Ratio(Rx, VerticalWidth)));

        /// <summary>The middle of the bands round the corner: the centre and reaches of
        /// its quarter ellipse; null when the corner is square or the bands leave no curve.</summary>
        public (double Cx, double Cy, double Ax, double Ay)? MiddleCurve()
        {
            if (!Rounded) return null;
            var cx = Rx - VerticalWidth * MiddleCurveCentreShift;
            var cy = Ry - HorizontalWidth * MiddleCurveCentreShift;
            var ax = cx - VerticalWidth / 2;
            var ay = cy - HorizontalWidth / 2;
            if (ax <= 0 || ay <= 0) return null;
            return (X + InX * cx, Y + InY * cy, ax, ay);
        }

        /// <summary>Where the middle of the vertical band meets the curve (or the bands' corner).</summary>
        public (double X, double Y) OnVerticalMiddle() => MiddleCurve() is { } m
            ? (m.Cx - InX * m.Ax, m.Cy)
            : (X + InX * VerticalWidth / 2, Y + InY * HorizontalWidth / 2);

        /// <summary>Where the middle of the horizontal band meets the curve (or the bands' corner).</summary>
        public (double X, double Y) OnHorizontalMiddle() => MiddleCurve() is { } m
            ? (m.Cx, m.Cy - InY * m.Ay)
            : (X + InX * VerticalWidth / 2, Y + InY * HorizontalWidth / 2);

        private static double Ratio(double reach, double width) => width > 0 ? reach / width : double.PositiveInfinity;
    }

    /// <summary>Paints a box with rounded corners: its background over
    /// <paramref name="fillRect"/> (under the named alpha state when there is one),
    /// then the border in the ring. The background is clipped to the part of the
    /// fill that lies inside the box, rounded at the same corners, each held to half
    /// of that part -- a background kept to the padding or content box has its own
    /// rounded outline, one grown past the box is cut at the box's.</summary>
    internal static void PaintRoundedBox(ContentStreamBuilder builder, BorderInfo? border,
        double x, double y, double w, double h, ResolvedCorners corners,
        Color? fill, (double X, double Y, double W, double H) fillRect, string? fillAlpha,
        IReadOnlyList<BackgroundPictureLayer>? pictures = null)
    {
        if (fill is not null || pictures is { Count: > 0 })
        {
            // The colour, then the pictures over it, inside the rounded outline.
            builder.SaveState();
            var (cx, cy) = (Math.Max(x, fillRect.X), Math.Max(y, fillRect.Y));
            var (cw, ch) = (Math.Min(x + w, fillRect.X + fillRect.W) - cx, Math.Min(y + h, fillRect.Y + fillRect.H) - cy);
            ClipCornerByCorner(builder, cx, cy, cw, ch, corners.HeldTo(cw, ch));
            if (fill is { } background)
            {
                builder.SaveState();
                if (fillAlpha is not null) builder.SetExtGState(fillAlpha);
                builder.SetFillColor(background);
                builder.Rectangle(fillRect.X, fillRect.Y, fillRect.W, fillRect.H).Fill();
                builder.RestoreState();
            }
            if (pictures is { Count: > 0 }) BackgroundPicturePainter.Paint(builder, pictures);
            builder.RestoreState();
        }
        if (border is null) return;
        var widths = BoxWidths(border);
        var (top, bottom, left, right) = widths;
        if (top <= 0 && bottom <= 0 && left <= 0 && right <= 0) return;

        builder.SaveState();
        ClipCornerByCorner(builder, x, y, w, h, corners);
        var (innerW, innerH) = (w - left - right, h - top - bottom);
        if (innerW > 0 && innerH > 0)
        {
            // The ring, one inner corner at a time: the box less an inner outline
            // curved at that corner and cut straight across the others, by the
            // even-odd rule. Where every clip holds, the inner outline is curved
            // at all four.
            var inner = InnerCorners(corners, widths);
            for (var corner = 0; corner < CornerCount; corner++)
            {
                if (inner[corner].X <= 0) continue;
                builder.Rectangle(x, y, w, h);
                AppendOutline(builder, x + left, y + bottom, innerW, innerH, inner, curved: corner);
                builder.ClipEvenOdd();
            }
        }
        var tl = new Corner(x, y + h, 1, -1, corners.TopLeft.X, corners.TopLeft.Y, left, top);
        var tr = new Corner(x + w, y + h, -1, -1, corners.TopRight.X, corners.TopRight.Y, right, top);
        var br = new Corner(x + w, y, -1, 1, corners.BottomRight.X, corners.BottomRight.Y, right, bottom);
        var bl = new Corner(x, y, 1, 1, corners.BottomLeft.X, corners.BottomLeft.Y, left, bottom);
        // Clockwise: each side from the corner it starts at to the one it ends at.
        PaintRoundedSide(builder, border, BorderSide.Top, tl, tr, w - left - right, x, y, w, h, widths);
        PaintRoundedSide(builder, border, BorderSide.Right, tr, br, h - top - bottom, x, y, w, h, widths);
        PaintRoundedSide(builder, border, BorderSide.Bottom, br, bl, w - left - right, x, y, w, h, widths);
        PaintRoundedSide(builder, border, BorderSide.Left, bl, tl, h - top - bottom, x, y, w, h, widths);
        builder.RestoreState();
    }

    /// <summary>The inner outline's corners: each reach less the width of the side it
    /// runs across; a corner either of whose reaches is used up is square.</summary>
    private static ResolvedCorners InnerCorners(ResolvedCorners outer,
        (double Top, double Bottom, double Left, double Right) widths)
    {
        static (double, double) Less((double X, double Y) r, double across, double down) =>
            r.X - across > 0 && r.Y - down > 0 ? (r.X - across, r.Y - down) : (0, 0);
        return new ResolvedCorners(
            Less(outer.TopLeft, widths.Left, widths.Top),
            Less(outer.TopRight, widths.Right, widths.Top),
            Less(outer.BottomRight, widths.Right, widths.Bottom),
            Less(outer.BottomLeft, widths.Left, widths.Bottom));
    }

    /// <summary>The corners in the order they are clipped: top-left, top-right,
    /// bottom-right, bottom-left.</summary>
    private const int CornerCount = 4;

    /// <summary>Clips to the rounded outline one corner at a time: the box rounded
    /// at that corner alone, for every corner that is rounded. Where every clip
    /// holds is the outline rounded at all of them.</summary>
    private static void ClipCornerByCorner(ContentStreamBuilder builder, double x, double y, double w, double h,
        ResolvedCorners corners)
    {
        for (var corner = 0; corner < CornerCount; corner++)
        {
            if (corners[corner].X <= 0) continue;
            AppendOutline(builder, x, y, w, h, corners.Only(corner), curved: corner);
            builder.Clip();
        }
    }

    /// <summary>A closed outline of the box, anticlockwise from the bottom-left: the
    /// <paramref name="curved"/> corner a quarter ellipse of its reaches, every other
    /// corner cut straight across between the points its reaches give (a square
    /// corner where they are nothing).</summary>
    private static void AppendOutline(ContentStreamBuilder builder, double x, double y, double w, double h,
        ResolvedCorners c, int curved)
    {
        var (right, top) = (x + w, y + h);
        var (tl, tr, br, bl) = (c.TopLeft, c.TopRight, c.BottomRight, c.BottomLeft);
        builder.MoveTo(x + bl.X, y);
        builder.LineTo(right - br.X, y);
        Turn(builder, curved == 2, right - br.X, y, right, y + br.Y, right, y);
        builder.LineTo(right, top - tr.Y);
        Turn(builder, curved == 1, right, top - tr.Y, right - tr.X, top, right, top);
        builder.LineTo(x + tl.X, top);
        Turn(builder, curved == 0, x + tl.X, top, x, top - tl.Y, x, top);
        builder.LineTo(x, y + bl.Y);
        Turn(builder, curved == 3, x, y + bl.Y, x + bl.X, y, x, y);
        builder.ClosePath();
    }

    /// <summary>Round a corner from (x0, y0) to (x1, y1), the corner itself at
    /// (cx, cy): a quarter ellipse when <paramref name="curve"/>, else a straight cut.</summary>
    private static void Turn(ContentStreamBuilder builder, bool curve, double x0, double y0, double x1, double y1,
        double cx, double cy)
    {
        if (!curve)
        {
            builder.LineTo(x1, y1);
            return;
        }
        var k = QuarterArcKappa;
        builder.CurveTo(x0 + (cx - x0) * k, y0 + (cy - y0) * k, x1 + (cx - x1) * k, y1 + (cy - y1) * k, x1, y1);
    }

    private static void PaintRoundedSide(ContentStreamBuilder builder, BorderInfo border, BorderSide flag,
        Corner from, Corner to, double straightRun, double x, double y, double w, double h,
        (double Top, double Bottom, double Left, double Right) widths)
    {
        var width = SideWidth(border, flag);
        if (width <= 0) return;
        var (_, side) = SideOf(border, flag);
        var colour = side?.Color ?? border.Color;
        var horizontal = flag is BorderSide.Top or BorderSide.Bottom;
        if (HasDash(side))
        {
            StrokeRoundedSide(builder, side, colour, width, from, to, horizontal, straightRun);
            return;
        }
        if ((side?.Style ?? RuleStyle.Solid) != RuleStyle.Solid)
        {
            if (StripOf(flag, x, y, w, h, widths) is { } strip) PaintSide(builder, border, flag, strip);
            return;
        }
        var (p1, p2) = (from.SolidInnerPoint(), to.SolidInnerPoint());
        builder.SetFillColor(colour);
        builder.MoveTo(p1.X, p1.Y).LineTo(p2.X, p2.Y).LineTo(to.X, to.Y).LineTo(from.X, from.Y).ClosePath().Fill();
    }

    /// <summary>A dashed side of a rounded box: clipped to its quad, a stroke along the
    /// middle of its band from the curve of the corner it starts at to the curve of
    /// the one it ends at.</summary>
    private static void StrokeRoundedSide(ContentStreamBuilder builder, GraphInfo? side, Color colour, double width,
        Corner from, Corner to, bool horizontal, double straightRun)
    {
        builder.SaveState();
        var dashed = SetStrokeStyle(builder, side, colour, width, straightRun);
        var (p1, p2) = (from.StrokeInnerPoint(horizontal), to.StrokeInnerPoint(horizontal));
        builder.MoveTo(from.X, from.Y);
        if (Passed(from, to, p1, p2, horizontal))
        {
            var cross = MitresCross(from, to);
            builder.LineTo(cross.X, cross.Y);
        }
        else
            builder.LineTo(p1.X, p1.Y).LineTo(p2.X, p2.Y);
        builder.LineTo(to.X, to.Y).ClosePath().Clip();

        // A horizontal side meets its corners' curves on its own middle line last;
        // a vertical one meets them on its middle line first.
        var start = horizontal ? from.OnVerticalMiddle() : from.OnHorizontalMiddle();
        builder.MoveTo(start.X, start.Y);
        AppendMiddleCurve(builder, from, fromVertical: horizontal);
        var end = horizontal ? to.OnHorizontalMiddle() : to.OnVerticalMiddle();
        builder.LineTo(end.X, end.Y);
        AppendMiddleCurve(builder, to, fromVertical: !horizontal);
        builder.Stroke();
        ResetStrokeStyle(builder, side, dashed);
        builder.RestoreState();
    }

    /// <summary>The corner's middle quarter ellipse, from the vertical band's middle to
    /// the horizontal band's, or the other way round; nothing for a square corner.</summary>
    private static void AppendMiddleCurve(ContentStreamBuilder builder, Corner corner, bool fromVertical)
    {
        if (corner.MiddleCurve() is not { } m) return;
        var k = QuarterArcKappa;
        var v = corner.OnVerticalMiddle();
        var hz = corner.OnHorizontalMiddle();
        // Each control point leaves its end toward the corner, along the band.
        var (vcx, vcy) = (v.X, v.Y - corner.InY * m.Ay * k);
        var (hcx, hcy) = (hz.X - corner.InX * m.Ax * k, hz.Y);
        if (fromVertical)
            builder.CurveTo(vcx, vcy, hcx, hcy, hz.X, hz.Y);
        else
            builder.CurveTo(hcx, hcy, vcx, vcy, v.X, v.Y);
    }

    /// <summary>True when a side's two inner points have passed each other.</summary>
    private static bool Passed(Corner from, Corner to, (double X, double Y) p1, (double X, double Y) p2, bool horizontal)
    {
        // Measured along the side from its starting corner toward its end.
        var (along1, along2) = horizontal
            ? ((p1.X - from.X) * from.InX, (p2.X - from.X) * from.InX)
            : ((p1.Y - from.Y) * from.InY, (p2.Y - from.Y) * from.InY);
        return along1 > along2;
    }

    /// <summary>Where the two corners' mitres cross.</summary>
    private static (double X, double Y) MitresCross(Corner a, Corner b)
    {
        var (ax, ay) = (a.InX * a.VerticalWidth, a.InY * a.HorizontalWidth);
        var (bx, by) = (b.InX * b.VerticalWidth, b.InY * b.HorizontalWidth);
        var det = ax * -by - ay * -bx;
        if (Math.Abs(det) < 1e-12) return ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var t = ((b.X - a.X) * -by - (b.Y - a.Y) * -bx) / det;
        return (a.X + ax * t, a.Y + ay * t);
    }
}
