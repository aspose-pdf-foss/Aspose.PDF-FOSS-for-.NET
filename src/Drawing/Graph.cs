using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

/// <summary>
/// A graph container that holds drawable shapes and renders them to a content stream.
/// </summary>
public sealed class Graph : BaseParagraph
{
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>
    /// When false the graph is placed at an absolute position (<see cref="Left"/>, <see cref="Top"/>)
    /// and does not participate in the document flow. Default: true.
    /// </summary>
    public bool IsChangePosition { get; set; } = true;

    private double _left;
    private double _top;

    /// <summary>True once <see cref="Left"/> or <see cref="Top"/> has been assigned.
    /// Each assigned offset anchors that axis to the content area (Left from the
    /// left margin, Top from the content top) irrespective of
    /// <see cref="IsChangePosition"/>; the other axis keeps flowing. The flow
    /// cursor then continues below the graph and, after an assigned Left, every
    /// following paragraph starts at that left edge too (measured
    /// 2026-08-23: a Top-only graph inherits the previous Left anchor).</summary>
    internal bool PositionAssigned => LeftAssigned || TopAssigned;

    /// <summary>True once <see cref="Left"/> has been assigned (to any value, including 0).</summary>
    internal bool LeftAssigned { get; private set; }

    /// <summary>True once <see cref="Top"/> has been assigned (to any value, including 0).</summary>
    internal bool TopAssigned { get; private set; }

    /// <summary>Horizontal offset of the graph box from the left content margin.
    /// Assigning it (to any value, including 0) anchors the graph's left edge.</summary>
    public double Left { get => _left; set { _left = value; LeftAssigned = true; } }

    /// <summary>Vertical offset of the graph box from the content top.
    /// Assigning it (to any value, including 0) anchors the graph's top edge.</summary>
    public double Top { get => _top; set { _top = value; TopAssigned = true; } }

    /// <summary>Optional border around the graph area.</summary>
    public BorderInfo? Border { get; set; }

    /// <summary>Default stroke/fill settings inherited by shapes added to this graph.</summary>
    public Aspose.Pdf.GraphInfo GraphInfo { get; set; } = new();

    /// <summary>Z-index for layering; higher draws on top.</summary>
    public new int ZIndex { get; set; }

    /// <summary>Optional title rendered above the graph. Stored only.</summary>
    public Aspose.Pdf.Text.TextFragment? Title { get; set; }

    /// <summary>The list of shapes inside this graph. Alias for <see cref="Add"/>.</summary>
    public BoundsCheckableList<Shape> Shapes { get; set; } = new();

    public Graph(double width, double height)
    {
        Width = width;
        Height = height;
        // The shape list must know the canvas so ThrowExceptionIfDoesNotFit can
        // validate at add time even when callers switch mode with the 1-arg overload.
        Shapes = new BoundsCheckableList<Shape>(BoundsCheckMode.Default, width, height);
    }

    /// <summary>Single-precision overload matching the public API.</summary>
    public Graph(float width, float height) : this((double)width, (double)height) { }

    public void Add(Shape shape) => Shapes.Add(shape);

    /// <summary>Shallow clone — shapes are shared by reference.</summary>
    public override object Clone()
    {
        var copy = new Graph(Width, Height)
        {
            IsChangePosition = IsChangePosition,
            Border = Border,
            GraphInfo = GraphInfo,
            ZIndex = ZIndex,
            Title = Title,
        };
        // Preserve the explicit-position flag: copy the backing fields directly so a
        // graph with untouched Left/Top clones as still-untouched (flowing).
        copy._left = _left;
        copy._top = _top;
        copy.LeftAssigned = LeftAssigned;
        copy.TopAssigned = TopAssigned;
        foreach (var s in Shapes) copy.Shapes.Add(s);
        return copy;
    }

    /// <summary>Render all shapes to a content stream.</summary>
    public byte[] Build()
    {
        return Build(null);
    }

    /// <summary>Render all shapes to a content stream, registering ExtGState resources on the page if needed.</summary>
    public byte[] Build(Page? page)
    {
        return Build(page, 0, 0);
    }

    /// <summary>
    /// Render all shapes to a content stream translated so the graph's local origin
    /// (bottom-left corner) lands at (<paramref name="offsetX"/>, <paramref name="offsetY"/>)
    /// in page coordinates. Shape coordinates are relative to the graph box.
    /// </summary>
    public byte[] Build(Page? page, double offsetX, double offsetY)
    {
        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (offsetX != 0 || offsetY != 0)
            builder.SetMatrix(1, 0, 0, 1, offsetX, offsetY);
        // The GraphInfo transform turns the whole box (shapes AND border) about the
        // graph's local origin: a rotation first, then a skew whose x/y shears are
        // the tangents of the skew angles -- two separate cm operators, the
        // rotation outermost (measured on the expected output, 2026-08-23).
        if (GraphInfo.RotationAngle != 0)
        {
            var rad = GraphInfo.RotationAngle * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            builder.SetMatrix(cos, sin, -sin, cos, 0, 0);
        }
        if (GraphInfo.SkewAngleX != 0 || GraphInfo.SkewAngleY != 0)
        {
            var shearX = Math.Tan(GraphInfo.SkewAngleX * Math.PI / 180.0);
            var shearY = Math.Tan(GraphInfo.SkewAngleY * Math.PI / 180.0);
            builder.SetMatrix(1, shearX, shearY, 1, 0, 0);
        }
        // …and the scaling rates last: the order is translate, then rotation,
        // then ONE `sx 0 0 sy 0 0 cm` (measured 2026-08-26 with each rate alone and both
        // together, with and without a rotation).
        if (GraphInfo.ScalingRateX != 1 || GraphInfo.ScalingRateY != 1)
            builder.SetMatrix(GraphInfo.ScalingRateX, 0, 0, GraphInfo.ScalingRateY, 0, 0);
        foreach (var shape in Shapes)
        {
            // Isolate each shape's graphics state (colour, line width, dash) so a
            // shape that sets no explicit colour falls back to the default black
            // rather than inheriting the previous shape's stroke colour.
            builder.SaveState();
            shape.Render(builder, page);
            builder.RestoreState();
        }
        // The border paints over the shapes.
        if (Border is not null && Border.Side != BorderSide.None)
            RenderBorder(builder);
        builder.RestoreState();
        return builder.Build();
    }

    /// <summary>Draw the configured border around the graph box (local coordinates).
    /// A full box is one rectangle outset by half the stroke width horizontally and
    /// inset by half of it vertically -- (-w/2, w/2, W + w, H - w) -- the shape the
    /// reference emits; partial sides are individual edge lines.</summary>
    private void RenderBorder(ContentStreamBuilder builder)
    {
        builder.SetLineWidth(Border!.Width);
        builder.SetStrokeColor(Border.Color);
        var side = Border.Side;
        if ((side & BorderSide.Box) == BorderSide.Box)
        {
            var half = Border.Width / 2;
            builder.Rectangle(-half, half, Width + Border.Width, Height - Border.Width);
            builder.Stroke();
            return;
        }
        if (side.HasFlag(BorderSide.Bottom))
            builder.MoveTo(0, 0).LineTo(Width, 0).Stroke();
        if (side.HasFlag(BorderSide.Top))
            builder.MoveTo(0, Height).LineTo(Width, Height).Stroke();
        if (side.HasFlag(BorderSide.Left))
            builder.MoveTo(0, 0).LineTo(0, Height).Stroke();
        if (side.HasFlag(BorderSide.Right))
            builder.MoveTo(Width, 0).LineTo(Width, Height).Stroke();
    }
}
