namespace Aspose.Pdf;

/// <summary>How far one corner of a box is rounded: the corner is a quarter of an
/// ellipse <see cref="Horizontal"/> wide and <see cref="Vertical"/> tall. Each
/// reach is in points, or -- when its <c>...IsFraction</c> flag is set -- a
/// fraction of the box's own width (horizontal) or height (vertical), so 0.1 is a
/// tenth of the box. Zero on either axis leaves the corner square.</summary>
public sealed class CornerRadius
{
    /// <summary>A corner rounded to a circle of <paramref name="radius"/> points.</summary>
    public CornerRadius(double radius) : this(radius, radius) { }

    /// <summary>A corner rounded to an ellipse of the two reaches, in points.</summary>
    public CornerRadius(double horizontal, double vertical)
    {
        Horizontal = horizontal;
        Vertical = vertical;
    }

    /// <summary>How far along the top or bottom edge the curve starts.</summary>
    public double Horizontal { get; set; }

    /// <summary>How far along the left or right edge the curve starts.</summary>
    public double Vertical { get; set; }

    /// <summary><see cref="Horizontal"/> is a fraction of the box's width, not points.</summary>
    public bool HorizontalIsFraction { get; set; }

    /// <summary><see cref="Vertical"/> is a fraction of the box's height, not points.</summary>
    public bool VerticalIsFraction { get; set; }

    /// <summary>The corner's two reaches in points on a box of the given size, each
    /// held to half the box on its own axis, so opposite curves can meet but never
    /// cross.</summary>
    internal (double X, double Y) Resolve(double width, double height)
    {
        var x = HorizontalIsFraction ? Horizontal * width : Horizontal;
        var y = VerticalIsFraction ? Vertical * height : Vertical;
        x = Math.Max(0, Math.Min(x, width / 2));
        y = Math.Max(0, Math.Min(y, height / 2));
        return x > 0 && y > 0 ? (x, y) : (0, 0);
    }
}

/// <summary>The rounding of a box's four corners -- CSS <c>border-radius</c>. A
/// rounded box paints its background inside the rounded outline, and its border as
/// the ring between that outline and an inner one whose corners are the outer
/// reaches less the widths of the sides that meet there. A corner left null is
/// square.</summary>
public sealed class CornerRadii
{
    /// <summary>No corner rounded yet.</summary>
    public CornerRadii() { }

    /// <summary>Every corner rounded alike.</summary>
    public CornerRadii(CornerRadius all)
    {
        TopLeft = all;
        TopRight = all;
        BottomRight = all;
        BottomLeft = all;
    }

    /// <summary>The top-left corner.</summary>
    public CornerRadius? TopLeft { get; set; }

    /// <summary>The top-right corner.</summary>
    public CornerRadius? TopRight { get; set; }

    /// <summary>The bottom-right corner.</summary>
    public CornerRadius? BottomRight { get; set; }

    /// <summary>The bottom-left corner.</summary>
    public CornerRadius? BottomLeft { get; set; }

    /// <summary>The four corners' reaches in points on a box of the given size.</summary>
    internal ResolvedCorners Resolve(double width, double height) => new(
        TopLeft?.Resolve(width, height) ?? (0, 0),
        TopRight?.Resolve(width, height) ?? (0, 0),
        BottomRight?.Resolve(width, height) ?? (0, 0),
        BottomLeft?.Resolve(width, height) ?? (0, 0));
}

/// <summary>A box's four corner reaches in points, each (horizontal, vertical).</summary>
internal readonly record struct ResolvedCorners(
    (double X, double Y) TopLeft, (double X, double Y) TopRight,
    (double X, double Y) BottomRight, (double X, double Y) BottomLeft)
{
    /// <summary>True when at least one corner is rounded.</summary>
    public bool Any => TopLeft.X > 0 || TopRight.X > 0 || BottomRight.X > 0 || BottomLeft.X > 0;

    /// <summary>A corner by its index: 0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left.</summary>
    public (double X, double Y) this[int corner] => corner switch
    {
        0 => TopLeft,
        1 => TopRight,
        2 => BottomRight,
        _ => BottomLeft,
    };

    /// <summary>The same corners on a box of the given size: each reach held to half
    /// the box on its own axis, a corner left with nothing on either axis square.</summary>
    public ResolvedCorners HeldTo(double width, double height)
    {
        (double, double) Held((double X, double Y) r)
        {
            var (x, y) = (Math.Min(r.X, Math.Max(0, width / 2)), Math.Min(r.Y, Math.Max(0, height / 2)));
            return x > 0 && y > 0 ? (x, y) : (0, 0);
        }
        return new ResolvedCorners(Held(TopLeft), Held(TopRight), Held(BottomRight), Held(BottomLeft));
    }

    /// <summary>The same box with only the one corner rounded.</summary>
    public ResolvedCorners Only(int corner) => new(
        corner == 0 ? TopLeft : (0, 0), corner == 1 ? TopRight : (0, 0),
        corner == 2 ? BottomRight : (0, 0), corner == 3 ? BottomLeft : (0, 0));
}
