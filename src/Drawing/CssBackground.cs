namespace Aspose.Pdf.Drawing;

/// <summary>How a background picture is sized in its area (CSS <c>background-size</c>).</summary>
public enum CssBackgroundSizing
{
    /// <summary>As given: a width, a height, both or neither (the natural size).</summary>
    Explicit,

    /// <summary>As large as fits entirely inside the area, keeping its proportions.</summary>
    Contain,

    /// <summary>As small as covers the whole area, keeping its proportions.</summary>
    Cover,
}

/// <summary>Where a background picture sits along one axis (CSS <c>background-position</c>).</summary>
public enum CssBackgroundEdge
{
    /// <summary>Measured from the left or top edge.</summary>
    Start,

    /// <summary>Measured from the right or bottom edge.</summary>
    End,

    /// <summary>In the middle; a centred picture takes no offset.</summary>
    Center,
}

/// <summary>How a background picture repeats along one axis (CSS <c>background-repeat</c>).</summary>
public enum CssBackgroundRepeat
{
    NoRepeat,
    Repeat,

    /// <summary>Resized so a whole number of copies fills the area.</summary>
    Round,

    /// <summary>As many whole copies as fit, spread with even gaps.</summary>
    Space,
}

/// <summary>A picture placed in an area: x and y of its bottom-left corner, its width and height.</summary>
public struct CssBackgroundTile
{
    public float X;
    public float Y;
    public float Width;
    public float Height;
}

/// <summary>
/// The CSS background geometry of one picture in one area: how large it is, where it
/// sits, and how a rounded or spaced repeat reshapes and spreads it.
/// </summary>
public static class CssBackground
{
    /// <summary>
    /// The size of the picture in an area. A picture with a natural size keeps its
    /// proportions for contain, cover and a side not given; one without (a gradient)
    /// fills the area on any side not given. A length that is a percentage is of the
    /// area's side.
    /// </summary>
    public static double[] Size(double? naturalWidth, double? naturalHeight, CssBackgroundSizing sizing,
        double? width, bool widthIsPercent, double? height, bool heightIsPercent, double areaWidth, double areaHeight)
    {
        if (naturalWidth is not { } nw || naturalHeight is not { } nh)
        {
            if (sizing != CssBackgroundSizing.Explicit) return [areaWidth, areaHeight];
            return [Resolve(width, widthIsPercent, areaWidth) ?? areaWidth, Resolve(height, heightIsPercent, areaHeight) ?? areaHeight];
        }

        switch (sizing)
        {
            case CssBackgroundSizing.Contain:
            {
                var scale = Math.Min(areaWidth / nw, areaHeight / nh);
                return [nw * scale, nh * scale];
            }
            case CssBackgroundSizing.Cover:
            {
                var scale = Math.Max(areaWidth / nw, areaHeight / nh);
                return [nw * scale, nh * scale];
            }
        }

        var w = Resolve(width, widthIsPercent, areaWidth);
        var h = Resolve(height, heightIsPercent, areaHeight);
        if (w is { } gw && h is { } gh) return [gw, gh];
        if (w is { } ow) return [ow, ow * nh / nw];
        if (h is { } oh) return [oh * nw / nh, oh];
        return [nw, nh];
    }

    private static double? Resolve(double? length, bool isPercent, double of) =>
        length is { } value ? isPercent ? value * of / 100 : value : null;

    /// <summary>
    /// Where the picture sits along an axis <paramref name="full"/> long: the offset from
    /// the start edge, the length less the offset from the end edge, or the middle. A
    /// centred picture given an offset is at 0. A percentage offset is of the length.
    /// </summary>
    public static double Position(CssBackgroundEdge edge, double offset, bool offsetIsPercent, double full)
    {
        var shift = offsetIsPercent ? offset * full / 100 : offset;
        return edge switch
        {
            CssBackgroundEdge.End => full - shift,
            CssBackgroundEdge.Center => shift == 0 ? full / 2 : 0,
            _ => shift,
        };
    }

    /// <summary>
    /// Reshapes and moves the picture for a rounded or spaced repeat, and answers the gap
    /// between spaced copies across and up.
    ///
    /// Round first: the picture is resized so that as many whole copies as the area holds
    /// (one more when at least half a copy is left over, and never none) fill it, keeping
    /// its top edge; the other side keeps the proportions unless it is rounded too or its
    /// size was given. Then space: when two or more copies fit, the picture moves to the
    /// area's left (or top) edge and the gap spreads what is left evenly; when fewer fit,
    /// the gap is the larger of the spaces left beside it. Computed in single precision.
    /// </summary>
    public static float[] Arrange(ref CssBackgroundTile tile, CssBackgroundRepeat acrossRepeat, CssBackgroundRepeat upRepeat,
        bool widthGiven, bool heightGiven, CssBackgroundTile area)
    {
        if (acrossRepeat == CssBackgroundRepeat.Round)
        {
            var ratio = Ratio(area.Width, tile.Width);
            var proportion = tile.Height / tile.Width;
            tile.Width = area.Width / ratio;
            if (upRepeat != CssBackgroundRepeat.Round && !heightGiven)
            {
                tile.Y += tile.Height - tile.Width * proportion;
                tile.Height = tile.Width * proportion;
            }
        }
        if (upRepeat == CssBackgroundRepeat.Round)
        {
            var ratio = Ratio(area.Height, tile.Height);
            var proportion = tile.Width / tile.Height;
            tile.Y += tile.Height - area.Height / ratio;
            tile.Height = area.Height / ratio;
            if (acrossRepeat != CssBackgroundRepeat.Round && !widthGiven) tile.Width = tile.Height * proportion;
        }

        float across = 0, up = 0;
        if (acrossRepeat == CssBackgroundRepeat.Space)
        {
            if (tile.Width * 2 <= area.Width)
            {
                tile.X = area.X;
                across = Gap(area.Width, tile.Width);
            }
            else
            {
                across = Math.Max(area.X + area.Width - (tile.X + tile.Width), tile.X - area.X);
            }
        }
        if (upRepeat == CssBackgroundRepeat.Space)
        {
            if (tile.Height * 2 <= area.Height)
            {
                tile.Y = area.Y + area.Height - tile.Height;
                up = Gap(area.Height, tile.Height);
            }
            else
            {
                up = Math.Max(area.Y + area.Height - (tile.Y + tile.Height), tile.Y - area.Y);
            }
        }
        return [across, up];
    }

    /// <summary>How many whole copies a rounded repeat fits: the whole number that fit, one
    /// more when at least half a copy is left, and at least one.</summary>
    private static int Ratio(float areaSize, float tileSize)
    {
        var ratio = (int)Math.Floor(areaSize / tileSize);
        if (areaSize - ratio * tileSize >= tileSize / 2) ratio++;
        return ratio == 0 ? 1 : ratio;
    }

    /// <summary>The even gap between as many whole copies as fit.</summary>
    private static float Gap(float areaSize, float tileSize)
    {
        var count = (int)Math.Floor(areaSize / tileSize);
        return count > 1 ? (areaSize - count * tileSize) / (count - 1) : 0;
    }
}
