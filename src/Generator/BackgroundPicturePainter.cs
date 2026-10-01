using Aspose.Pdf.Content;
using Aspose.Pdf.Drawing;

namespace Aspose.Pdf;

/// <summary>A background picture made ready for one box on one page: the box it is cut
/// to, the page's names for it and for its blend state, the scale from its own units to
/// its drawn size, and where each copy of it stands (bottom-left corners).</summary>
internal sealed record BackgroundPictureLayer(
    Rectangle Clip, string XObjectName, string? BlendState, double ScaleX, double ScaleY,
    IReadOnlyList<(double X, double Y)> Copies);

/// <summary>Sizes, places and repeats a block's <see cref="BackgroundPicture"/>s in the
/// block's boxes, and paints them.</summary>
internal static class BackgroundPicturePainter
{
    /// <summary>The pictures made ready for <paramref name="box"/> (the border box) on
    /// <paramref name="page"/>, whose resources take their xobjects and blend states;
    /// listed in painting order, the last picture first so the first ends on top. Null
    /// when there are none.</summary>
    internal static IReadOnlyList<BackgroundPictureLayer>? Prepare(IReadOnlyList<BackgroundPicture> pictures,
        Page page, Rectangle box, (double Left, double Bottom, double Right, double Top) bands, MarginInfo? padding)
    {
        if (pictures.Count == 0) return null;
        var paddingBox = Inset(box, bands.Left, bands.Bottom, bands.Right, bands.Top);
        var contentBox = Inset(paddingBox, padding?.Left ?? 0, padding?.Bottom ?? 0, padding?.Right ?? 0, padding?.Top ?? 0);
        Rectangle Area(BackgroundBoxArea area) => area switch
        {
            BackgroundBoxArea.Padding => paddingBox,
            BackgroundBoxArea.Content => contentBox,
            _ => box,
        };

        var layers = new List<BackgroundPictureLayer>(pictures.Count);
        for (var i = pictures.Count - 1; i >= 0; i--)
        {
            var picture = pictures[i];
            if (Layer(picture, page, Area(picture.Origin), Area(picture.Clip)) is { } layer) layers.Add(layer);
        }
        return layers;
    }

    /// <summary>Each layer in its own graphics state: cut to its box, blended when it
    /// asks, and every copy drawn through its own transform.</summary>
    internal static void Paint(ContentStreamBuilder builder, IReadOnlyList<BackgroundPictureLayer> layers)
    {
        foreach (var layer in layers)
        {
            builder.SaveState();
            builder.Rectangle(layer.Clip.LLX, layer.Clip.LLY, layer.Clip.Width, layer.Clip.Height).Clip();
            if (layer.BlendState is not null) builder.SetExtGState(layer.BlendState);
            foreach (var (x, y) in layer.Copies)
            {
                builder.SaveState();
                builder.SetMatrix(layer.ScaleX, 0, 0, layer.ScaleY, x, y);
                builder.DrawXObject(layer.XObjectName);
                builder.RestoreState();
            }
            builder.RestoreState();
        }
    }

    private static BackgroundPictureLayer? Layer(BackgroundPicture picture, Page page, Rectangle origin, Rectangle clip)
    {
        if (origin.Width <= 0 || origin.Height <= 0) return null;
        var (stream, natural, unit) = Drawn(picture, origin);
        if (stream is null) return null;

        var size = CssBackground.Size(natural?.Width, natural?.Height, picture.Sizing,
            picture.Width, picture.WidthIsPercent, picture.Height, picture.HeightIsPercent, origin.Width, origin.Height);
        var (width, height) = (size[0], size[1]);
        if (width <= 0 || height <= 0) return null;
        var left = origin.LLX + CssBackground.Position(picture.HorizontalEdge, picture.HorizontalOffset,
            picture.HorizontalOffsetIsPercent, origin.Width - width);
        var top = origin.URY - CssBackground.Position(picture.VerticalEdge, picture.VerticalOffset,
            picture.VerticalOffsetIsPercent, origin.Height - height);
        var tile = new CssBackgroundTile { X = (float)left, Y = (float)(top - height), Width = (float)width, Height = (float)height };

        var (across, up) = (picture.RepeatAcross, picture.RepeatUp);
        var area = new CssBackgroundTile { X = (float)origin.LLX, Y = (float)origin.LLY, Width = (float)origin.Width, Height = (float)origin.Height };
        var gaps = CssBackground.Arrange(ref tile, across, up,
            picture.Sizing == CssBackgroundSizing.Explicit && picture.Width is not null,
            picture.Sizing == CssBackgroundSizing.Explicit && picture.Height is not null, area);

        var columns = Steps(tile.X, tile.Width, tile.Width + gaps[0], across != CssBackgroundRepeat.NoRepeat, clip.LLX, clip.URX, towardsStart: false);
        var rows = Steps(tile.Y, tile.Height, tile.Height + gaps[1], up != CssBackgroundRepeat.NoRepeat, clip.LLY, clip.URY, towardsStart: true);
        var copies = new List<(double X, double Y)>(columns.Count * rows.Count);
        foreach (var y in rows)
            foreach (var x in columns)
                copies.Add((x - unit.LLX * tile.Width / unit.Width, y - unit.LLY * tile.Height / unit.Height));

        var name = Text.TextParagraph.EnsureXObject(page, stream);
        var blend = picture.BlendMode is { Length: > 0 } mode ? Text.TextParagraph.EnsureBlendExtGState(page, mode) : null;
        return new BackgroundPictureLayer(clip, name, blend, tile.Width / unit.Width, tile.Height / unit.Height, copies);
    }

    /// <summary>What a copy of the picture draws, its natural size (null for none), and
    /// the box one unit of its own space covers: an image's unit square, a form's
    /// bounding box, a picture drawn for the origin box that box at the origin.</summary>
    private static (Core.PdfStream? Stream, (double Width, double Height)? Natural, Rectangle Unit) Drawn(
        BackgroundPicture picture, Rectangle origin)
    {
        if (picture.DrawForArea is { } draw)
        {
            return (draw(origin.Width, origin.Height), null, new Rectangle(0, 0, origin.Width, origin.Height));
        }
        var stream = picture.XObject!;
        if (stream.Dict.Get("Subtype") is Core.PdfName { Value: "Form" })
        {
            var bbox = FormBox(stream);
            return bbox.Width > 0 && bbox.Height > 0 ? (stream, (bbox.Width, bbox.Height), bbox) : (null, null, bbox);
        }
        var pixels = (Number(stream.Dict.Get("Width")), Number(stream.Dict.Get("Height")));
        return pixels.Item1 > 0 && pixels.Item2 > 0 ? (stream, pixels, new Rectangle(0, 0, 1, 1)) : (null, null, new Rectangle(0, 0, 1, 1));
    }

    private static Rectangle FormBox(Core.PdfStream stream)
    {
        if (stream.Dict.Get("BBox") is not Core.PdfArray { Count: 4 } box) return new Rectangle(0, 0, 0, 0);
        double At(int i) => Number(box[i]);
        return new Rectangle(Math.Min(At(0), At(2)), Math.Min(At(1), At(3)), Math.Max(At(0), At(2)), Math.Max(At(1), At(3)));
    }

    private static double Number(Core.PdfObject? value) => value switch
    {
        Core.PdfInteger i => i.Value,
        Core.PdfReal r => r.Value,
        _ => 0,
    };

    /// <summary>Where the copies stand along one axis: the placed copy first, then one
    /// step on and one step back in turn, onward across (or down) and back left (or up),
    /// each only while it still reaches into the clip from <paramref name="low"/> to
    /// <paramref name="high"/>. A picture that does not repeat has only the placed copy.
    /// Down the page is towards the start of the axis, so rows step on by going lower.</summary>
    private static List<double> Steps(double placed, double length, double step, bool repeats, double low, double high, bool towardsStart)
    {
        var steps = new List<double> { placed };
        if (!repeats || step <= 0) return steps;
        bool Reaches(double at) => at < high - ReachTolerance && at + length > low + ReachTolerance;
        var onward = towardsStart ? -step : step;
        bool forward = true, back = true;
        for (var k = 1; forward || back; k++)
        {
            if (forward)
            {
                var at = placed + k * onward;
                if (Reaches(at)) steps.Add(at);
                else forward = false;
            }
            if (back)
            {
                var at = placed - k * onward;
                if (Reaches(at)) steps.Add(at);
                else back = false;
            }
        }
        return steps;
    }

    /// <summary>How far a copy must reach into the clip to count: less is the rounding of
    /// a size worked out in single precision, a copy that only touches the clip's edge.</summary>
    private const double ReachTolerance = 1e-3;

    private static Rectangle Inset(Rectangle r, double left, double bottom, double right, double top) =>
        new(r.LLX + left, r.LLY + bottom, Math.Max(r.LLX + left, r.URX - right), Math.Max(r.LLY + bottom, r.URY - top));
}
