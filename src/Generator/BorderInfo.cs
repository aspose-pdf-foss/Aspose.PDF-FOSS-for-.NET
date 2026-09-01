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

    public GraphInfo Top { get => _top ??= NewSide(); set { _top = value; TopAssigned = true; } }
    public GraphInfo Bottom { get => _bottom ??= NewSide(); set { _bottom = value; BottomAssigned = true; } }
    public GraphInfo Left { get => _left ??= NewSide(); set { _left = value; LeftAssigned = true; } }
    public GraphInfo Right { get => _right ??= NewSide(); set { _right = value; RightAssigned = true; } }

    internal GraphInfo? RawTop => _top;
    internal GraphInfo? RawBottom => _bottom;
    internal GraphInfo? RawLeft => _left;
    internal GraphInfo? RawRight => _right;

    // A side whose GraphInfo was explicitly ASSIGNED draws even when the Side
    // flags don't name it (the generator treats `border.Top = graphInfo` as
    // enabling the top side). Reading the lazy getter does not count.
    internal bool TopAssigned { get; private set; }
    internal bool BottomAssigned { get; private set; }
    internal bool LeftAssigned { get; private set; }
    internal bool RightAssigned { get; private set; }

    private GraphInfo NewSide() => new GraphInfo { LineWidth = (float)Width };

    /// <summary>True when any side draws: named by <see cref="Side"/> or given an
    /// explicit GraphInfo (a `new BorderInfo()` whose four sides were assigned is a
    /// full box even though Side stays None).</summary>
    internal bool HasAnySide => Side != BorderSide.None
        || TopAssigned || BottomAssigned || LeftAssigned || RightAssigned;

    /// <summary>The sides that draw: <see cref="Side"/> plus every explicitly assigned side.</summary>
    internal BorderSide EffectiveSides => Side
        | (TopAssigned ? BorderSide.Top : BorderSide.None)
        | (BottomAssigned ? BorderSide.Bottom : BorderSide.None)
        | (LeftAssigned ? BorderSide.Left : BorderSide.None)
        | (RightAssigned ? BorderSide.Right : BorderSide.None);

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

    /// <summary>A copy of this border with every side's <see cref="GraphInfo.IsDoubled"/>
    /// cleared — the single rules a doubled border strokes around its inner box. The
    /// Side flags and the per-side assignment record are kept exactly as they are, so
    /// the copy draws the same sides the original does.</summary>
    internal BorderInfo WithoutDoubling()
    {
        var copy = (BorderInfo)MemberwiseClone();
        copy._top = Undoubled(_top);
        copy._bottom = Undoubled(_bottom);
        copy._left = Undoubled(_left);
        copy._right = Undoubled(_right);
        return copy;

        static GraphInfo? Undoubled(GraphInfo? gi)
        {
            if (gi is null || !gi.IsDoubled) return gi;
            var c = (GraphInfo)gi.Clone();
            c.IsDoubled = false;
            return c;
        }
    }
}
