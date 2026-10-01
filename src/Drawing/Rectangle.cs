using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

/// <summary>
/// A rectangle shape in the Drawing namespace (distinct from Aspose.Pdf.Rectangle which is a page rectangle).
/// </summary>
public sealed class Rectangle : Shape
{
    /// <summary>Gets or sets the X coordinate of the left edge, in points relative to the graph.</summary>
    public double Left { get; set; }
    /// <summary>Gets or sets the Y coordinate of the bottom edge, in points relative to the graph.</summary>
    public double Bottom { get; set; }
    /// <summary>Gets or sets the width in points.</summary>
    public double Width { get; set; }
    /// <summary>Gets or sets the height in points.</summary>
    public double Height { get; set; }

    /// <summary>Corner radius for rounded-rectangle rendering (0 = sharp corners). Stored only.</summary>
    public double RoundedCornerRadius { get; set; }

    /// <summary>Creates a rectangle with its lower-left corner at (<c>left</c>, <c>bottom</c>) and the given width and height, in points relative to the graph.</summary>
    public Rectangle(double left, double bottom, double width, double height)
    {
        Left = left; Bottom = bottom; Width = width; Height = height;
    }

    /// <summary>Single-precision overload matching the public API.</summary>
    public Rectangle(float left, float bottom, float width, float height)
        : this((double)left, (double)bottom, (double)width, (double)height) { }

    /// <summary>Whether the rectangle lies entirely within an origin-anchored container.</summary>
    public override bool CheckBounds(double containerWidth, double containerHeight)
        => Left >= 0 && Bottom >= 0
           && Left + Width <= containerWidth
           && Bottom + Height <= containerHeight;

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        if (TryPaintTransparentFillStroke(builder, page, b => b.Rectangle(Left, Bottom, Width, Height)))
            return;
        ApplyStyle(builder, page);
        if (TryPaintGradient(builder, page, b => b.Rectangle(Left, Bottom, Width, Height)))
            return;
        builder.Rectangle(Left, Bottom, Width, Height);
        Paint(builder);
    }
}
