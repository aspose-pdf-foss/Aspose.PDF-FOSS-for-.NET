namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    // A frame around a plot is not a table. A lattice of a single cell that holds
    // a drawing - the slanting strokes and traced curves of a chart - is the
    // figure's frame, and the lattices inside it (the axes with their ticks) are
    // parts of the figure.

    /// <summary>A single-cell lattice holding at least this many drawing strokes is a
    /// figure's frame. A tick, a cross or a strike-through in a box is a mark of a
    /// few strokes; a plotted curve is many.</summary>
    private const int MinFigureStrokes = 8;

    /// <summary>Remove the single-cell tables that frame a drawing, and the tables inside them.</summary>
    private static void DropFigureFrames(List<AbsorbedTable> tables, List<PendingLine> drawn)
    {
        if (drawn.Count < MinFigureStrokes) return;
        var frames = new List<Rectangle>();
        foreach (var table in tables)
        {
            if (table.Rect is not { } rect || table.Rows.Count != 1 || table.Rows[0].Cells.Count != 1) continue;
            // Strokes on the frame itself (rounded corners) are not inside it.
            var strokes = drawn.Count(s => InsideBy(rect, (s.X1 + s.X2) / 2, (s.Y1 + s.Y2) / 2, EdgeTol));
            if (strokes >= MinFigureStrokes) frames.Add(rect);
        }
        if (frames.Count == 0) return;
        tables.RemoveAll(t => t.Rect is { } r && frames.Any(f =>
            InsideBy(f, r.LLX, r.LLY, -EdgeTol) && InsideBy(f, r.URX, r.URY, -EdgeTol)));
    }

    /// <summary>Whether the point lies inside the box, at least <paramref name="margin"/> from its sides.</summary>
    private static bool InsideBy(Rectangle box, double x, double y, double margin) =>
        x >= box.LLX + margin && x <= box.URX - margin && y >= box.LLY + margin && y <= box.URY - margin;
}
