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

    public GraphInfo? Top { get; set; }
    public GraphInfo? Bottom { get; set; }
    public GraphInfo? Left { get; set; }
    public GraphInfo? Right { get; set; }

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
