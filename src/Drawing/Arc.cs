using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

/// <summary>
/// An arc shape (portion of an ellipse).
/// </summary>
public sealed class Arc : Shape
{
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double RadiusX { get; set; }
    public double RadiusY { get; set; }

    /// <summary>Start angle in degrees (0 = 3 o'clock, counter-clockwise).</summary>
    public double StartAngle { get; set; }

    /// <summary>Sweep angle in degrees (positive = counter-clockwise).</summary>
    public double SweepAngle { get; set; }

    /// <summary>Centre X (alias of <see cref="CenterX"/>).</summary>
    public double PosX { get => CenterX; set => CenterX = value; }

    /// <summary>Centre Y (alias of <see cref="CenterY"/>).</summary>
    public double PosY { get => CenterY; set => CenterY = value; }

    /// <summary>Radius (sets both <see cref="RadiusX"/> and <see cref="RadiusY"/>).</summary>
    public double Radius
    {
        get => RadiusX;
        set { RadiusX = value; RadiusY = value; }
    }

    /// <summary>Start angle in degrees (alias of <see cref="StartAngle"/>).</summary>
    public double Alpha { get => StartAngle; set => StartAngle = value; }

    /// <summary>End angle in degrees; setting recomputes <see cref="SweepAngle"/> as <c>value − Alpha</c>.</summary>
    public double Beta
    {
        get => StartAngle + SweepAngle;
        set => SweepAngle = value - StartAngle;
    }

    public Arc(double centerX, double centerY, double radiusX, double radiusY,
        double startAngle, double sweepAngle)
    {
        CenterX = centerX; CenterY = centerY;
        RadiusX = radiusX; RadiusY = radiusY;
        StartAngle = startAngle; SweepAngle = sweepAngle;
    }

    /// <summary>
    /// Constructor matching the public API: Arc(posX, posY, radius, alpha, beta)
    /// where alpha = start angle, beta = end angle.
    /// </summary>
    public Arc(double centerX, double centerY, double radius, double alpha, double beta)
        : this(centerX, centerY, radius, radius, alpha, beta - alpha)
    {
    }

    /// <summary>Single-precision overload matching the public API.</summary>
    public Arc(float posX, float posY, float radius, float alpha, float beta)
        : this((double)posX, (double)posY, (double)radius, (double)alpha, (double)beta)
    {
    }

    /// <summary>The point the arc starts at, as <c>{x, y}</c>.</summary>
    /// <remarks>Callers chain shapes off an arc — a line that continues from where the
    /// curve ended has to ask the arc where that is, rather than re-deriving it and
    /// drifting from what was drawn. Both helpers therefore use the same parametrisation
    /// as the drawing code below: x = CenterX + RadiusX·cos(θ), y = CenterY +
    /// RadiusY·sin(θ), θ in degrees measured counter-clockwise from 3 o'clock.</remarks>
    internal float[] GetStartPosition() => PointAtAngle(StartAngle);

    /// <summary>The point the arc ends at, as <c>{x, y}</c>. See
    /// <see cref="GetStartPosition"/>.</summary>
    internal float[] GetEndPosition() => PointAtAngle(Beta);

    private float[] PointAtAngle(double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        return new[]
        {
            (float)(CenterX + RadiusX * Math.Cos(rad)),
            (float)(CenterY + RadiusY * Math.Sin(rad)),
        };
    }

    /// <summary>Whether this arc lies entirely within a <paramref name="containerWidth"/>×<paramref name="containerHeight"/>
    /// box anchored at the origin. Only the SWEPT extent counts — an arc whose unswept
    /// side would overhang the container is accepted. Extremes occur at the two endpoints
    /// and at every axis angle (multiple of 90°) inside the sweep; the sweep magnitude is
    /// taken modulo a full turn (a whole number of extra turns adds no extent).</summary>
    public override bool CheckBounds(double containerWidth, double containerHeight)
    {
        var sweep = SweepAngle % 360;
        if (sweep == 0 && SweepAngle != 0) sweep = SweepAngle > 0 ? 360 : -360;
        var a0 = StartAngle;
        var a1 = StartAngle + sweep;
        var lo = System.Math.Min(a0, a1);
        var hi = System.Math.Max(a0, a1);
        const double eps = 1e-9;
        bool Fits(double deg)
        {
            var rad = deg * System.Math.PI / 180.0;
            var x = CenterX + RadiusX * System.Math.Cos(rad);
            var y = CenterY + RadiusY * System.Math.Sin(rad);
            return x >= -eps && y >= -eps
                && x <= containerWidth + eps && y <= containerHeight + eps;
        }
        if (!Fits(a0) || !Fits(a1)) return false;
        for (var k = System.Math.Ceiling(lo / 90.0) * 90.0; k <= hi; k += 90.0)
            if (!Fits(k)) return false;
        return true;
    }

    internal override bool TryAppendGeometry(ContentStreamBuilder builder)
    {
        AppendOutline(builder);
        return true;
    }

    private void AppendOutline(ContentStreamBuilder builder)
    {
        // Approximate arc with Bézier curves (max 90° per segment)
        var startRad = StartAngle * Math.PI / 180;
        var sweepRad = SweepAngle * Math.PI / 180;
        var segments = (int)Math.Ceiling(Math.Abs(sweepRad) / (Math.PI / 2));
        if (segments < 1) segments = 1;

        var segmentAngle = sweepRad / segments;
        var currentAngle = startRad;

        var startX = CenterX + RadiusX * Math.Cos(currentAngle);
        var startY = CenterY + RadiusY * Math.Sin(currentAngle);
        builder.MoveTo(startX, startY);

        for (var i = 0; i < segments; i++)
        {
            var endAngle = currentAngle + segmentAngle;
            AppendArcSegment(builder, currentAngle, endAngle);
            currentAngle = endAngle;
        }
    }

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        ApplyStyle(builder, page);
        AppendOutline(builder);

        // A FillColor closes the arc (the PDF fill operator implicitly closes the
        // subpath, chord-style) and paints the enclosed region; otherwise stroke only.
        Paint(builder);
    }

    private void AppendArcSegment(ContentStreamBuilder builder, double a1, double a2)
    {
        // Bézier approximation for an arc segment
        var alpha = (a2 - a1) / 2;
        var cosAlpha = Math.Cos(alpha);
        var sinAlpha = Math.Sin(alpha);
        var cotAlpha = cosAlpha / sinAlpha;
        var phi = (a1 + a2) / 2;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);
        var lambda = (4.0 - cosAlpha) / 3.0;

        // Avoid division by zero for very small angles
        if (Math.Abs(sinAlpha) < 1e-10) return;

        var mu = sinAlpha + (cosAlpha - lambda) * cotAlpha;

        var p2x = CenterX + RadiusX * (lambda * cosPhi + mu * sinPhi);
        var p2y = CenterY + RadiusY * (lambda * sinPhi - mu * cosPhi);
        var p3x = CenterX + RadiusX * (lambda * cosPhi - mu * sinPhi);
        var p3y = CenterY + RadiusY * (lambda * sinPhi + mu * cosPhi);
        var p4x = CenterX + RadiusX * Math.Cos(a2);
        var p4y = CenterY + RadiusY * Math.Sin(a2);

        builder.CurveTo(p2x, p2y, p3x, p3y, p4x, p4y);
    }
}
