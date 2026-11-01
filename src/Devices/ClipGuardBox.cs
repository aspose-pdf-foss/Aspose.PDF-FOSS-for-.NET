namespace Aspose.Pdf.Devices;

/// <summary>
/// Confines the outline of a clip path that reaches astronomically far past the page to a guard
/// box one page span around the device, shared by both rasterisers. "Everything but this box" is
/// commonly written as the box plus a rectangle hundreds of thousands of units wide under W*, and
/// such a clip was dropped as damage, letting what it hides show through. Each straight edge is
/// cut where it crosses the box and every piece is pressed onto it: inside the box nothing moves; a
/// piece left or right of it keeps its rows, so every pixel keeps the winding the whole edge gave
/// it; a piece above or below it lands on a row no pixel of the device reads.
/// </summary>
internal readonly struct ClipGuardBox
{
    private readonly double _left;
    private readonly double _top;
    private readonly double _right;
    private readonly double _bottom;

    /// <summary>The guard box around a device of the given pixel size.</summary>
    internal ClipGuardBox(int width, int height)
    {
        double pad = Math.Max(width, height);
        _left = -pad;
        _top = -pad;
        _right = width + pad;
        _bottom = height + pad;
    }

    /// <summary>Add the line from (x0, y0) to (x1, y1), in device pixels, confined to the box.</summary>
    internal void AddLine(double x0, double y0, double x1, double y1, Action<double, double, double, double> add)
    {
        var cuts = new List<double>(SideCount + 2) { 0 };
        Cut(cuts, x0, x1, _left);
        Cut(cuts, x0, x1, _right);
        Cut(cuts, y0, y1, _top);
        Cut(cuts, y0, y1, _bottom);
        cuts.Add(1);
        cuts.Sort();
        var dx = x1 - x0;
        var dy = y1 - y0;
        for (var i = 1; i < cuts.Count; i++)
        {
            var (ax, ay) = Confine(x0 + dx * cuts[i - 1], y0 + dy * cuts[i - 1]);
            var (bx, by) = Confine(x0 + dx * cuts[i], y0 + dy * cuts[i]);
            add(ax, ay, bx, by);
        }
    }

    private const int SideCount = 4;

    /// <summary>The place along the line where a coordinate going from <paramref name="from"/> to
    /// <paramref name="to"/> crosses <paramref name="side"/>, when it does strictly inside the line.</summary>
    private static void Cut(List<double> cuts, double from, double to, double side)
    {
        if ((from < side && to > side) || (from > side && to < side))
            cuts.Add((side - from) / (to - from));
    }

    private (double X, double Y) Confine(double x, double y) =>
        (Math.Min(Math.Max(x, _left), _right), Math.Min(Math.Max(y, _top), _bottom));
}
