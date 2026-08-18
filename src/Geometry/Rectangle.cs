using System.Globalization;

namespace Aspose.Pdf;

/// <summary>
/// Represents a point in 2D space.
/// </summary>
public sealed class Point
{
    public double X { get; set; }
    public double Y { get; set; }

    public Point(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Checks whether this point is approximately equal to another within a given tolerance.
    /// </summary>
    public bool NearEqual(Point other, double delta) =>
        Math.Abs(X - other.X) <= delta && Math.Abs(Y - other.Y) <= delta;

    /// <summary>Origin point (0, 0). Returns a fresh instance per call so callers can mutate it.</summary>
    public static Point Trivial => new(0, 0);

    /// <summary>Euclidean distance between two points.</summary>
    public static double Distance(Point point1, Point point2)
    {
        var dx = point1.X - point2.X;
        var dy = point1.Y - point2.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Convert to a <see cref="System.Drawing.PointF"/>.</summary>
    public System.Drawing.PointF ToPoint() => new((float)X, (float)Y);

    public override string ToString() => $"({X:F2}, {Y:F2})";
}

/// <summary>
/// Represents a point in 3D space (used by 3D annotations).
/// </summary>
public sealed class Point3D
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public Point3D() { }
    public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }

    /// <summary>Origin point (0, 0, 0). Returns a fresh instance per call.</summary>
    public static Point3D Trivial => new(0, 0, 0);

    /// <summary>String form expected by the public API: <c>{X=1.2,Y=-3.4,Z=5.6}</c>.</summary>
    public override string ToString() =>
        $"{{X={X.ToString(CultureInfo.InvariantCulture)},Y={Y.ToString(CultureInfo.InvariantCulture)},Z={Z.ToString(CultureInfo.InvariantCulture)}}}";
}

/// <summary>
/// Represents a rectangle defined by lower-left and upper-right coordinates
/// (PDF user-space convention: Y increases upward).
/// </summary>
public sealed class Rectangle
{
    public double LLX { get; set; }
    public double LLY { get; set; }
    public double URX { get; set; }
    public double URY { get; set; }

    public Rectangle(double llx, double lly, double urx, double ury)
    {
        // Auto-normalize: ensure LLX <= URX and LLY <= URY
        LLX = Math.Min(llx, urx);
        LLY = Math.Min(lly, ury);
        URX = Math.Max(llx, urx);
        URY = Math.Max(lly, ury);
    }

    /// <summary>
    /// Creates a rectangle. When <paramref name="normalizeCoordinates"/> is
    /// true, swapped coordinates are corrected automatically; when false,
    /// throws <see cref="ArgumentException"/> on inverted coordinates.
    /// </summary>
    public Rectangle(double llx, double lly, double urx, double ury, bool normalizeCoordinates)
    {
        if (normalizeCoordinates)
        {
            LLX = Math.Min(llx, urx);
            LLY = Math.Min(lly, ury);
            URX = Math.Max(llx, urx);
            URY = Math.Max(lly, ury);
        }
        else
        {
            if (llx > urx || lly > ury)
                throw new ArgumentException(
                    $"Invalid rectangle coordinates: LLX ({llx}) must be <= URX ({urx}) and LLY ({lly}) must be <= URY ({ury}). " +
                    "Use the normalizing constructor or pass normalizeCoordinates: true.");
            LLX = llx;
            LLY = lly;
            URX = urx;
            URY = ury;
        }
    }

    public double Width => URX - LLX;
    public double Height => URY - LLY;

    /// <summary>An empty rectangle (all coordinates zero). Equivalent to <c>new Rectangle(0, 0, 0, 0)</c>.</summary>
    public static Rectangle Empty => new(0, 0, 0, 0);

    /// <summary>The trivial / sentinel rectangle (-1, -1, -1, -1) used as a "no value" marker.</summary>
    public static Rectangle Trivial => new(-1, -1, -1, -1, normalizeCoordinates: true);

    /// <summary>True when this rectangle has zero width AND zero height (LLX == URX and LLY == URY).</summary>
    public bool IsPoint => Math.Abs(URX - LLX) < 1e-12 && Math.Abs(URY - LLY) < 1e-12;

    /// <summary>True when this rectangle equals the <see cref="Trivial"/> sentinel
    /// (-1, -1, -1, -1) used as a "no value" marker by the PDF library.</summary>
    public bool IsTrivial =>
        Math.Abs(LLX - (-1)) < 1e-12 && Math.Abs(LLY - (-1)) < 1e-12 &&
        Math.Abs(URX - (-1)) < 1e-12 && Math.Abs(URY - (-1)) < 1e-12;

    /// <summary>Returns a shallow copy of this rectangle (matches the canonical <c>object Clone()</c> signature).</summary>
    public object Clone() => new Rectangle(LLX, LLY, URX, URY);

    /// <summary>
    /// Returns true when the rectangle has no positive area — coordinates are inverted (llx >= urx or lly >= ury)
    /// or have zero width/height. Matches the public API behavior.
    /// </summary>
    public bool IsEmpty => URX <= LLX || URY <= LLY;

    /// <summary>Geometric centre of the rectangle.</summary>
    public Point Center() => new((LLX + URX) / 2.0, (LLY + URY) / 2.0);

    /// <summary>Test whether <paramref name="x"/>, <paramref name="y"/> falls
    /// inside the rectangle (inclusive of edges).</summary>
    public bool ContainsPoint(double x, double y) =>
        x >= LLX && x <= URX && y >= LLY && y <= URY;

    /// <summary>Test whether <paramref name="point"/> falls inside the
    /// rectangle. When <paramref name="inclusive"/> is true edges count as
    /// inside; when false they don't.</summary>
    public bool Contains(Point point, bool inclusive)
    {
        if (point is null) return false;
        return inclusive
            ? point.X >= LLX && point.X <= URX && point.Y >= LLY && point.Y <= URY
            : point.X >  LLX && point.X <  URX && point.Y >  LLY && point.Y <  URY;
    }

    /// <summary>Test whether the line segment (<paramref name="x1"/>,<paramref name="y1"/>) →
    /// (<paramref name="x2"/>,<paramref name="y2"/>) lies entirely within
    /// the rectangle (both endpoints inside, inclusive of edges).</summary>
    public bool ContainsLine(double x1, double y1, double x2, double y2)
        => ContainsPoint(x1, y1) && ContainsPoint(x2, y2);

    public bool Contains(double x, double y) => ContainsPoint(x, y);

    public bool Contains(Rectangle other) =>
        other.LLX >= LLX && other.LLY >= LLY &&
        other.URX <= URX && other.URY <= URY;

    /// <summary>Geometric intersection. When the rectangles do not overlap,
    /// returns <see cref="Empty"/> rather than null so callers can chain
    /// without nullability dance — check <see cref="IsEmpty"/> on the result.</summary>
    public Rectangle Intersect(Rectangle otherRect)
    {
        if (otherRect is null) return Empty;
        var llx = Math.Max(LLX, otherRect.LLX);
        var lly = Math.Max(LLY, otherRect.LLY);
        var urx = Math.Min(URX, otherRect.URX);
        var ury = Math.Min(URY, otherRect.URY);
        if (llx >= urx || lly >= ury) return Empty;
        return new Rectangle(llx, lly, urx, ury);
    }

    /// <summary>True when this rectangle has any area in common with
    /// <paramref name="otherRect"/>.</summary>
    public bool IsIntersect(Rectangle otherRect)
    {
        if (otherRect is null) return false;
        return LLX < otherRect.URX && URX > otherRect.LLX
            && LLY < otherRect.URY && URY > otherRect.LLY;
    }

    /// <summary>Translate this rectangle by (<paramref name="dx"/>,<paramref name="dy"/>) in place.</summary>
    public void MoveBy(double dx, double dy)
    {
        LLX += dx; URX += dx;
        LLY += dy; URY += dy;
    }

    /// <summary>Rotate this rectangle in place by the given <see cref="Rotation"/>.
    /// PDF user-space rotation; the bounding box is recomputed so the
    /// resulting LL/UR corners stay normalised.</summary>
    public void Rotate(Rotation angle) => Rotate((int)angle);

    /// <summary>Rotate this rectangle in place by <paramref name="angle"/> degrees
    /// (typically 0/90/180/270; arbitrary angles compute the bounding box of
    /// the rotated corner points).</summary>
    public void Rotate(int angle)
    {
        var a = ((angle % 360) + 360) % 360;
        // A quarter-turn (90/270) swaps the X and Y
        // coordinate pairs (the box keeps positive coords and takes the rotated
        // dimensions); a half-turn (0/180) leaves the axis-aligned box unchanged.
        // Earlier this rotated the corners about the ORIGIN, which pushed the box to
        // negative, off-page coordinates (rotated FreeText vanished).
        if (a == 90 || a == 270)
        {
            (LLX, LLY, URX, URY) = (LLY, LLX, URY, URX);
            return;
        }
        if (a == 0 || a == 180) return;

        // Arbitrary angle: fall back to the bounding box of the rotated corners.
        var rad = a * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var pts = ToPoints();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var p in pts)
        {
            var x = p.X * cos - p.Y * sin;
            var y = p.X * sin + p.Y * cos;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        LLX = minX; LLY = minY; URX = maxX; URY = maxY;
    }

    /// <summary>The four corners of the rectangle in
    /// LL → LR → UR → UL order.</summary>
    public Point[] ToPoints() => new[]
    {
        new Point(LLX, LLY),
        new Point(URX, LLY),
        new Point(URX, URY),
        new Point(LLX, URY),
    };

    /// <summary>Parse a rectangle from the canonical
    /// <c>"LLX LLY URX URY"</c> form (whitespace- or comma-separated).
    /// Brackets <c>[ ]</c> are optional and stripped.</summary>
    public static Rectangle Parse(string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        var trimmed = value.Trim().Trim('[', ']').Trim();
        var parts = trimmed.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            throw new FormatException($"Cannot parse '{value}' as Rectangle: expected 4 numbers, got {parts.Length}.");
        return new Rectangle(
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture),
            double.Parse(parts[3], CultureInfo.InvariantCulture));
    }

    public override bool Equals(object? obj) => Equals(obj as Rectangle);

    /// <summary>Coordinate-wise equality with a small floating-point tolerance.</summary>
    public bool Equals(Rectangle? other) =>
        other is not null &&
        Math.Abs(LLX - other.LLX) < 1e-10 && Math.Abs(LLY - other.LLY) < 1e-10 &&
        Math.Abs(URX - other.URX) < 1e-10 && Math.Abs(URY - other.URY) < 1e-10;

    public override int GetHashCode() => HashCode.Combine(LLX, LLY, URX, URY);

    /// <summary>
    /// Checks whether this rectangle is approximately equal to another within a given tolerance.
    /// </summary>
    public bool NearEquals(Rectangle other, double delta) =>
        Math.Abs(LLX - other.LLX) <= delta &&
        Math.Abs(LLY - other.LLY) <= delta &&
        Math.Abs(URX - other.URX) <= delta &&
        Math.Abs(URY - other.URY) <= delta;

    /// <summary>
    /// Returns the union (bounding box) of this rectangle and another.
    /// </summary>
    public Rectangle Join(Rectangle otherRect) =>
        new(Math.Min(LLX, otherRect.LLX), Math.Min(LLY, otherRect.LLY),
            Math.Max(URX, otherRect.URX), Math.Max(URY, otherRect.URY));

    public override string ToString() => $"[{LLX:F2} {LLY:F2} {URX:F2} {URY:F2}]";

    /// <summary>Convert from System.Drawing.Rectangle (x, y, width, height) to PDF Rectangle (LLX/LLY/URX/URY).</summary>
    public static Rectangle FromRect(System.Drawing.Rectangle src) =>
        new(src.X, src.Y, src.X + src.Width, src.Y + src.Height);

    /// <summary>Convert from System.Drawing.RectangleF.</summary>
    public static Rectangle FromRect(System.Drawing.RectangleF src) =>
        new(src.X, src.Y, src.X + src.Width, src.Y + src.Height);

    /// <summary>Convert this PDF Rectangle to System.Drawing.Rectangle.</summary>
    public System.Drawing.Rectangle ToRect() =>
        new((int)LLX, (int)LLY, (int)(URX - LLX), (int)(URY - LLY));

    internal static Rectangle FromPdfArray(Core.PdfArray array)
    {
        static double Num(Core.PdfObject obj) => obj switch
        {
            Core.PdfInteger i => i.Value,
            Core.PdfReal r => r.Value,
            _ => 0,
        };

        return new Rectangle(Num(array[0]), Num(array[1]), Num(array[2]), Num(array[3]));
    }

    internal static Rectangle FromPdfArray(Core.PdfArray array, IO.PdfReader reader)
    {
        double Num(Core.PdfObject obj)
        {
            var resolved = reader.Resolve(obj);
            return resolved switch
            {
                Core.PdfInteger i => i.Value,
                Core.PdfReal r => r.Value,
                _ => 0,
            };
        }

        return new Rectangle(Num(array[0]), Num(array[1]), Num(array[2]), Num(array[3]));
    }
}

/// <summary>
/// Standard page size constants (dimensions in points, portrait orientation).
/// </summary>
public sealed class PageSize
{
    /// <summary>Width in points.</summary>
    public float Width { get; set; }
    /// <summary>Height in points.</summary>
    public float Height { get; set; }

    public PageSize(double width, double height)
    {
        Width = (float)width;
        Height = (float)height;
    }

    public PageSize(float x, float y)
    {
        Width = x;
        Height = y;
    }

    /// <summary>True when the page is wider than tall. Setting swaps Width / Height
    /// when needed to flip orientation.</summary>
    public bool IsLandscape
    {
        get => Width > Height;
        set
        {
            if (value && Width < Height) (Width, Height) = (Height, Width);
            else if (!value && Width > Height) (Width, Height) = (Height, Width);
        }
    }

    /// <summary>ISO A0 — 841 × 1189 mm (2383.937 × 3370.394 pt).</summary>
    public static PageSize A0 => new(2383.937, 3370.394);

    /// <summary>ISO A1 — 594 × 841 mm (1683.780 × 2383.937 pt).</summary>
    public static PageSize A1 => new(1683.780, 2383.937);

    /// <summary>ISO A2 — 420 × 594 mm (1190.551 × 1683.780 pt).</summary>
    public static PageSize A2 => new(1190.551, 1683.780);

    /// <summary>ISO A3 — 297 × 420 mm (841.890 × 1190.551 pt).</summary>
    public static PageSize A3 => new(841.890, 1190.551);

    /// <summary>ISO A4. Exposed as the rounded 595 × 842 pt
    /// (not the exact 595.276 × 841.890) — the values callers resize/compare
    /// against via PageSize.A4.</summary>
    public static PageSize A4 => new(595, 842);

    /// <summary>ISO A5 — 148 × 210 mm (419.528 × 595.276 pt).</summary>
    public static PageSize A5 => new(419.528, 595.276);

    /// <summary>ISO A6 — 105 × 148 mm (297.638 × 419.528 pt).</summary>
    public static PageSize A6 => new(297.638, 419.528);

    /// <summary>ISO B5 — 176 × 250 mm (498.898 × 708.661 pt).</summary>
    public static PageSize B5 => new(498.898, 708.661);

    /// <summary>US Letter — 8.5 × 11 in (612 × 792 pt).</summary>
    public static PageSize Letter => new(612, 792);

    /// <summary>US Legal — 8.5 × 14 in (612 × 1008 pt).</summary>
    public static PageSize Legal => new(612, 1008);

    /// <summary>ANSI B / Tabloid (Ledger landscape) — 11 × 17 in (792 × 1224 pt).</summary>
    public static PageSize P11x17 => new(792, 1224);

    /// <summary>ANSI B / Ledger landscape — 17 × 11 in (1224 × 792 pt).</summary>
    public static PageSize PageLedger => new(1224, 792);

    /// <summary>Alias for <see cref="Letter"/> (legacy Generator naming).</summary>
    public static PageSize PageLetter => Letter;

    /// <summary>Alias for <see cref="Legal"/> (legacy Generator naming).</summary>
    public static PageSize PageLegal => Legal;
}
