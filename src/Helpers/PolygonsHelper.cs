namespace Aspose.Pdf;

/// <summary>
/// Polygon and rectangle geometry utilities — point/polygon containment,
/// segment hit-testing and rectangle/polygon classification.
/// </summary>
public static class PolygonsHelper
{
    /// <summary>
    /// Position of a rectangle relative to a polygon, as returned by
    /// <see cref="GetRectanglePositionRelativePolygon"/>.
    /// </summary>
    public enum RectanglePosition
    {
        /// <summary>Rectangle lies entirely above the polygon's bounding box.</summary>
        Higher,
        /// <summary>Rectangle lies entirely below the polygon's bounding box.</summary>
        Lower,
        /// <summary>Rectangle is to the left of the polygon and stays inside its vertical extent.</summary>
        Left,
        /// <summary>Rectangle is to the right of the polygon and stays inside its vertical extent.</summary>
        Right,
        /// <summary>Rectangle lies entirely inside the polygon.</summary>
        Contained,
        /// <summary>Rectangle and polygon overlap but neither contains the other.</summary>
        Intersected,
        /// <summary>Rectangle is to the right of the polygon and extends above (only).</summary>
        RightTop,
        /// <summary>Rectangle is to the right of the polygon and extends below (only).</summary>
        RightBottom,
        /// <summary>Rectangle is to the left of the polygon and extends above (only).</summary>
        LeftTop,
        /// <summary>Rectangle is to the left of the polygon and extends below (only).</summary>
        LeftBottom,
        /// <summary>Rectangle contains the entire polygon.</summary>
        Containing
    }

    private const double Epsilon = 1e-9;

    /// <summary>
    /// Classifies the position of <paramref name="rect"/> relative to <paramref name="polygon"/>.
    /// </summary>
    public static RectanglePosition GetRectanglePositionRelativePolygon(Rectangle rect, Point[] polygon)
    {
        double polyMinX = double.PositiveInfinity, polyMaxX = double.NegativeInfinity;
        double polyMinY = double.PositiveInfinity, polyMaxY = double.NegativeInfinity;
        foreach (var p in polygon)
        {
            if (p.X < polyMinX) polyMinX = p.X;
            if (p.X > polyMaxX) polyMaxX = p.X;
            if (p.Y < polyMinY) polyMinY = p.Y;
            if (p.Y > polyMaxY) polyMaxY = p.Y;
        }

        if (rect.LLY > polyMaxY) return RectanglePosition.Higher;
        if (rect.URY < polyMinY) return RectanglePosition.Lower;

        if (rect.URX < polyMinX)
        {
            bool extendsAbove = rect.URY > polyMaxY;
            bool extendsBelow = rect.LLY < polyMinY;
            if (extendsAbove && !extendsBelow) return RectanglePosition.LeftTop;
            if (extendsBelow && !extendsAbove) return RectanglePosition.LeftBottom;
            return RectanglePosition.Left;
        }

        if (rect.LLX > polyMaxX)
        {
            bool extendsAbove = rect.URY > polyMaxY;
            bool extendsBelow = rect.LLY < polyMinY;
            if (extendsAbove && !extendsBelow) return RectanglePosition.RightTop;
            if (extendsBelow && !extendsAbove) return RectanglePosition.RightBottom;
            return RectanglePosition.Right;
        }

        bool allPolyInRect = true;
        foreach (var p in polygon)
        {
            if (p.X < rect.LLX - Epsilon || p.X > rect.URX + Epsilon ||
                p.Y < rect.LLY - Epsilon || p.Y > rect.URY + Epsilon)
            {
                allPolyInRect = false;
                break;
            }
        }
        if (allPolyInRect) return RectanglePosition.Containing;

        var corners = new[]
        {
            new Point(rect.LLX, rect.LLY),
            new Point(rect.LLX, rect.URY),
            new Point(rect.URX, rect.URY),
            new Point(rect.URX, rect.LLY),
        };
        bool allCornersInPoly = true;
        foreach (var c in corners)
        {
            if (!IsPointInsidePolygon(polygon, c, includeBoundary: true))
            {
                allCornersInPoly = false;
                break;
            }
        }
        if (allCornersInPoly) return RectanglePosition.Contained;

        return RectanglePosition.Intersected;
    }

    /// <summary>
    /// Returns true when every vertex of <paramref name="inner"/> lies strictly inside
    /// the polygon <paramref name="outer"/>.
    /// </summary>
    public static bool IsPolygonInsidePolygon(Point[] outer, Point[] inner)
        => IsPolygonInsidePolygon(outer, inner, includeBoundary: false);

    /// <summary>
    /// Returns true when every vertex of <paramref name="inner"/> lies inside
    /// the polygon <paramref name="outer"/>. When <paramref name="includeBoundary"/>
    /// is true, vertices on the boundary count as inside.
    /// </summary>
    public static bool IsPolygonInsidePolygon(Point[] outer, Point[] inner, bool includeBoundary)
    {
        foreach (var v in inner)
        {
            if (!IsPointInsidePolygon(outer, v, includeBoundary)) return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="point"/> is strictly inside the polygon
    /// (excluding boundary).
    /// </summary>
    public static bool IsPointInsidePolygon(Point[] polygon, Point point)
        => IsPointInsidePolygon(polygon, point, includeBoundary: false);

    /// <summary>
    /// Returns true when <paramref name="point"/> is inside the polygon. When
    /// <paramref name="includeBoundary"/> is true, points on any edge count as inside.
    /// </summary>
    public static bool IsPointInsidePolygon(Point[] polygon, Point point, bool includeBoundary)
    {
        int n = polygon.Length;
        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            if (IsPointOnLineSegment(a, b, point))
                return includeBoundary;
        }

        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            if ((pi.Y > point.Y) != (pj.Y > point.Y))
            {
                double xIntersect = (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X;
                if (point.X < xIntersect)
                    inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// Returns true when point <paramref name="p"/> lies on the segment from
    /// <paramref name="a"/> to <paramref name="b"/>.
    /// </summary>
    public static bool IsPointOnLineSegment(Point a, Point b, Point p)
    {
        double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        if (Math.Abs(cross) > Epsilon) return false;
        return Math.Min(a.X, b.X) - Epsilon <= p.X && p.X <= Math.Max(a.X, b.X) + Epsilon
            && Math.Min(a.Y, b.Y) - Epsilon <= p.Y && p.Y <= Math.Max(a.Y, b.Y) + Epsilon;
    }

    /// <summary>
    /// Returns true when the horizontal line y = <paramref name="y"/> intersects
    /// any edge of <paramref name="polygon"/>.
    /// </summary>
    public static bool IsIntersectLine(double y, Point[] polygon)
    {
        int n = polygon.Length;
        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            double minY = Math.Min(a.Y, b.Y);
            double maxY = Math.Max(a.Y, b.Y);
            if (minY <= y && y <= maxY) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true when every vertex of <paramref name="polygon"/> is inside
    /// <paramref name="rect"/>. <paramref name="includeBoundary"/> controls whether
    /// vertices on the rectangle's edge count as inside.
    /// </summary>
    public static bool IsPolygonInsideRectangle(Point[] polygon, Rectangle rect, bool includeBoundary)
    {
        foreach (var p in polygon)
        {
            if (includeBoundary)
            {
                if (p.X < rect.LLX - Epsilon || p.X > rect.URX + Epsilon ||
                    p.Y < rect.LLY - Epsilon || p.Y > rect.URY + Epsilon)
                    return false;
            }
            else
            {
                if (p.X <= rect.LLX + Epsilon || p.X >= rect.URX - Epsilon ||
                    p.Y <= rect.LLY + Epsilon || p.Y >= rect.URY - Epsilon)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns true when point (<paramref name="px"/>, <paramref name="py"/>) is
    /// inside the axis-aligned rectangle [0..<paramref name="width"/>] × [0..<paramref name="height"/>].
    /// <paramref name="includeBoundary"/> controls whether points on the rectangle's edge count.
    /// </summary>
    public static bool IsPointInsideRectangle(double px, double py, double width, double height, bool includeBoundary)
    {
        if (includeBoundary)
        {
            return px >= -Epsilon && px <= width + Epsilon
                && py >= -Epsilon && py <= height + Epsilon;
        }
        return px > Epsilon && px < width - Epsilon
            && py > Epsilon && py < height - Epsilon;
    }
}
