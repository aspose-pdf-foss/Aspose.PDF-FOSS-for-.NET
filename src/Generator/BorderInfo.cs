namespace Aspose.Pdf;

/// <summary>
/// Specifies which sides of a border to draw.
/// </summary>
[Flags]
public enum BorderSide
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8,
    Box = Left | Right | Top | Bottom,
    All = Box,
}

/// <summary>
/// Represents border information for table cells and rows.
/// </summary>
public sealed class BorderInfo
{
    public BorderSide Side { get; set; }
    public double Width { get; set; } = 1;
    public Color Color { get; set; } = Color.Black;

    // Per-side stroke styling. The public getters lazily materialise a GraphInfo that
    // inherits this border's Width, so callers can write e.g. `border.Right.DashArray = ...`
    // on a border that was created with only a width/colour (matching the generator,
    // where these sides are never null). The Raw* accessors expose the backing field without
    // forcing creation, so the renderer can tell an explicitly-styled side from a plain one.
    private GraphInfo? _top, _bottom, _left, _right;

    public GraphInfo Top { get => _top ??= NewSide(); set => _top = value; }
    public GraphInfo Bottom { get => _bottom ??= NewSide(); set => _bottom = value; }
    public GraphInfo Left { get => _left ??= NewSide(); set => _left = value; }
    public GraphInfo Right { get => _right ??= NewSide(); set => _right = value; }

    internal GraphInfo? RawTop => _top;
    internal GraphInfo? RawBottom => _bottom;
    internal GraphInfo? RawLeft => _left;
    internal GraphInfo? RawRight => _right;

    private GraphInfo NewSide() => new GraphInfo { LineWidth = (float)Width };

    public double RoundedBorderRadius { get; set; }

    public BorderInfo() { }

    public BorderInfo(BorderSide borderSide) { Side = borderSide; }

    public BorderInfo(BorderSide borderSide, double width)
    {
        Side = borderSide; Width = width;
    }

    public BorderInfo(BorderSide borderSide, float borderWidth)
    {
        Side = borderSide; Width = borderWidth;
    }

    public BorderInfo(BorderSide borderSide, double width, Color color)
    {
        Side = borderSide; Width = width; Color = color;
    }

    public BorderInfo(BorderSide borderSide, float borderWidth, Color borderColor)
    {
        Side = borderSide; Width = borderWidth; Color = borderColor;
    }

    public BorderInfo(BorderSide borderSide, Color borderColor)
    {
        Side = borderSide; Color = borderColor;
    }

    public BorderInfo(BorderSide borderSide, GraphInfo info)
    {
        Side = borderSide;
        Width = info.LineWidth;
        Top = info;
        Bottom = info;
        Left = info;
        Right = info;
    }

    public object Clone() => MemberwiseClone();
}
