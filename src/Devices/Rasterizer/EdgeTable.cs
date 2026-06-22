namespace Aspose.Pdf.Devices.Rasterizer;

/// <summary>
/// A single edge in a polygon, represented as a line segment for scanline processing.
/// YMin/YMax are fractional so the filler can do sub-pixel AA — snapping to integer
/// scanlines would collapse a half-row stroke into either zero coverage or one row
/// at full opacity, which is exactly the GDI+-vs-us border mismatch we need to fix.
/// </summary>
internal struct Edge
{
    public double YMin;    // fractional y at top of edge (inclusive)
    public double YMax;    // fractional y at bottom of edge (exclusive)
    public double XAtYMin; // x coordinate at YMin
    public double InvSlope; // dx/dy — constant along a straight segment
    public int Direction;  // +1 downward, -1 upward (used by non-zero winding fill)
}

/// <summary>
/// Collects polygon edges from line segments and Bezier curves.
/// Feeds into <see cref="ScanlineFiller"/> for rasterization.
/// </summary>
internal sealed class EdgeTable
{
    private readonly List<Edge> _edges = new();

    /// <summary>All edges sorted by YMin.</summary>
    public List<Edge> Edges => _edges;

    public void AddLine(double x0, double y0, double x1, double y1)
    {
        // Horizontal edges contribute no winding crossings; skip them.
        if (Math.Abs(y1 - y0) < 0.001) return;

        int dir;
        if (y0 > y1)
        {
            (x0, y0, x1, y1) = (x1, y1, x0, y0);
            dir = -1; // upward in original orientation
        }
        else
        {
            dir = 1; // downward
        }

        var invSlope = (x1 - x0) / (y1 - y0);

        // Keep fractional Y — the filler samples at fractional sub-scanlines and a
        // sub-pixel-thin edge (y0=32.5, y1=33.5) must remain distinguishable from a
        // full-pixel edge (32→34). Rounding here would drop or fuse such edges.
        _edges.Add(new Edge
        {
            YMin = y0,
            YMax = y1,
            XAtYMin = x0,
            InvSlope = invSlope,
            Direction = dir,
        });
    }

    /// <summary>Add a cubic Bezier curve (flattened to line segments).</summary>
    public void AddCubicBezier(double x0, double y0, double cx1, double cy1,
        double cx2, double cy2, double x3, double y3)
    {
        FlattenCubic(x0, y0, cx1, cy1, cx2, cy2, x3, y3, 0);
    }

    /// <summary>Add a quadratic Bezier curve (TrueType splines).</summary>
    public void AddQuadBezier(double x0, double y0, double cx, double cy, double x1, double y1)
    {
        FlattenQuad(x0, y0, cx, cy, x1, y1, 0);
    }

    public void SortByYMin()
    {
        _edges.Sort((a, b) => a.YMin.CompareTo(b.YMin));
    }

    private void FlattenCubic(double x0, double y0, double cx1, double cy1,
        double cx2, double cy2, double x3, double y3, int depth)
    {
        if (depth > 16)
        {
            AddLine(x0, y0, x3, y3);
            return;
        }

        // Check flatness: max deviation of control points from the line (x0,y0)→(x3,y3)
        var dx = x3 - x0;
        var dy = y3 - y0;
        var d2 = Math.Abs((cx1 - x3) * dy - (cy1 - y3) * dx);
        var d3 = Math.Abs((cx2 - x3) * dy - (cy2 - y3) * dx);
        var denom = dx * dx + dy * dy;

        if ((d2 + d3) * (d2 + d3) <= 0.25 * denom || denom < 0.001)
        {
            AddLine(x0, y0, x3, y3);
            return;
        }

        // De Casteljau subdivision at t=0.5
        var mx01 = (x0 + cx1) * 0.5;
        var my01 = (y0 + cy1) * 0.5;
        var mx12 = (cx1 + cx2) * 0.5;
        var my12 = (cy1 + cy2) * 0.5;
        var mx23 = (cx2 + x3) * 0.5;
        var my23 = (cy2 + y3) * 0.5;
        var mx012 = (mx01 + mx12) * 0.5;
        var my012 = (my01 + my12) * 0.5;
        var mx123 = (mx12 + mx23) * 0.5;
        var my123 = (my12 + my23) * 0.5;
        var mx0123 = (mx012 + mx123) * 0.5;
        var my0123 = (my012 + my123) * 0.5;

        FlattenCubic(x0, y0, mx01, my01, mx012, my012, mx0123, my0123, depth + 1);
        FlattenCubic(mx0123, my0123, mx123, my123, mx23, my23, x3, y3, depth + 1);
    }

    private void FlattenQuad(double x0, double y0, double cx, double cy,
        double x1, double y1, int depth)
    {
        if (depth > 16)
        {
            AddLine(x0, y0, x1, y1);
            return;
        }

        // Flatness check
        var dx = x1 - x0;
        var dy = y1 - y0;
        var d = Math.Abs((cx - x1) * dy - (cy - y1) * dx);
        var denom = dx * dx + dy * dy;

        if (d * d <= 0.25 * denom || denom < 0.001)
        {
            AddLine(x0, y0, x1, y1);
            return;
        }

        // Subdivision at t=0.5
        var mx0 = (x0 + cx) * 0.5;
        var my0 = (y0 + cy) * 0.5;
        var mx1 = (cx + x1) * 0.5;
        var my1 = (cy + y1) * 0.5;
        var mmx = (mx0 + mx1) * 0.5;
        var mmy = (my0 + my1) * 0.5;

        FlattenQuad(x0, y0, mx0, my0, mmx, mmy, depth + 1);
        FlattenQuad(mmx, mmy, mx1, my1, x1, y1, depth + 1);
    }
}
