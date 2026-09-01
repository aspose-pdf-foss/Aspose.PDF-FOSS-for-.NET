using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

/// <summary>
/// A shape built out of OTHER shapes: the children's outlines go into one path and
/// that path is painted once, with this shape's own <see cref="Shape.GraphInfo"/>.
/// </summary>
/// <remarks>
/// Distinct from <see cref="DrawingPath"/>, which is a path described directly as
/// move/line/curve segments. This one composes existing shapes, which is what lets a
/// caller fill the region a line and two arcs enclose between them — painting each
/// child separately fills each child, never the region they bound together.
/// A child that cannot contribute a bare outline is rendered on its own rather than
/// dropped, so an unsupported shape degrades to its individual paint.
/// </remarks>
public sealed class Path : Shape
{
    /// <summary>The shapes whose outlines make up this path, in order.</summary>
    public List<Shape> Shapes { get; set; } = [];

    public Path() { }

    public Path(Shape[] shapes)
    {
        if (shapes is not null) Shapes.AddRange(shapes);
    }

    /// <summary>True when every child fits the container.</summary>
    public override bool CheckBounds(double containerWidth, double containerHeight)
    {
        foreach (var s in Shapes)
            if (!s.CheckBounds(containerWidth, containerHeight)) return false;
        return true;
    }

    internal override bool TryAppendGeometry(ContentStreamBuilder builder)
    {
        var any = false;
        foreach (var s in Shapes)
            any |= s.TryAppendGeometry(builder);
        return any;
    }

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        if (Shapes.Count == 0) return;

        var standalone = new List<Shape>();
        var any = false;
        ApplyStyle(builder, page);
        foreach (var s in Shapes)
        {
            if (s.TryAppendGeometry(builder)) any = true;
            else standalone.Add(s);
        }
        // Only reach the paint operator when something actually entered the path —
        // otherwise the operator would apply to whatever path preceded this shape.
        if (any) Paint(builder);

        foreach (var s in standalone) s.Render(builder, page);
    }
}
