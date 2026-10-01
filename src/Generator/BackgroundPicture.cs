using Aspose.Pdf.Drawing;

namespace Aspose.Pdf;

/// <summary>One of the boxes a block is drawn in, from the outside in (CSS
/// <c>background-origin</c> and <c>background-clip</c>).</summary>
public enum BackgroundBoxArea
{
    /// <summary>Out to the border's outer edge.</summary>
    Border,

    /// <summary>Inside the border.</summary>
    Padding,

    /// <summary>Inside the border and the padding.</summary>
    Content,
}

/// <summary>A picture painted as a block's background (CSS <c>background-image</c>), over
/// its background colour and under its border and content.
///
/// The picture has a size in its <see cref="Origin"/> box -- its natural size, or as
/// <see cref="Sizing"/>, <see cref="Width"/> and <see cref="Height"/> give it -- and a
/// place there, from the edges <see cref="HorizontalEdge"/> and <see cref="VerticalEdge"/>
/// name by the offsets given. From that place it repeats across and up as
/// <see cref="RepeatAcross"/> and <see cref="RepeatUp"/> say, every copy that reaches
/// into the <see cref="Clip"/> box being drawn, and nothing outside that box shows. A
/// block that breaks across pages draws its pictures in each part as in a whole box.
///
/// A picture is an image, drawn one point per pixel at its natural size; a form,
/// whose natural size is its bounding box; or a picture without a natural size (a
/// gradient), drawn by a caller for the origin box it is given and scaled from there to
/// its size.</summary>
public sealed class BackgroundPicture
{
    /// <summary>A background picture of an image.</summary>
    public BackgroundPicture(ImageXObject image)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        XObject = image.Stream;
    }

    /// <summary>A background picture of a form.</summary>
    public BackgroundPicture(XForm form)
    {
        if (form is null) throw new ArgumentNullException(nameof(form));
        form.FlushContentEdits();
        XObject = form.Stream;
    }

    /// <summary>A background picture without a natural size: <paramref name="drawForArea"/>
    /// makes the form for an origin box of the width and height it is given, with that
    /// box as its bounding box.</summary>
    public BackgroundPicture(Func<double, double, XForm> drawForArea)
    {
        if (drawForArea is null) throw new ArgumentNullException(nameof(drawForArea));
        DrawForArea = (width, height) =>
        {
            var form = drawForArea(width, height);
            form.FlushContentEdits();
            return form.Stream;
        };
    }

    /// <summary>A background picture of an image or form stream the caller made.</summary>
    internal BackgroundPicture(Core.PdfStream xobject)
    {
        XObject = xobject ?? throw new ArgumentNullException(nameof(xobject));
    }

    /// <summary>A background picture without a natural size, whose form stream the
    /// caller makes for the origin box it is given.</summary>
    internal static BackgroundPicture ForArea(Func<double, double, Core.PdfStream> drawForArea) => new(drawForArea);

    private BackgroundPicture(Func<double, double, Core.PdfStream> drawForArea)
    {
        DrawForArea = drawForArea ?? throw new ArgumentNullException(nameof(drawForArea));
    }

    /// <summary>The image or form, null for a picture drawn for its area.</summary>
    internal Core.PdfStream? XObject { get; }

    /// <summary>What draws a picture without a natural size.</summary>
    internal Func<double, double, Core.PdfStream>? DrawForArea { get; }

    /// <summary>How the size is found: as given (the default), or contained in or
    /// covering the origin box.</summary>
    public CssBackgroundSizing Sizing { get; set; } = CssBackgroundSizing.Explicit;

    /// <summary>The width when <see cref="Sizing"/> is explicit; null keeps the natural
    /// width, or follows a given height in proportion.</summary>
    public double? Width { get; set; }

    /// <summary><see cref="Width"/> is a percentage of the origin box's width.</summary>
    public bool WidthIsPercent { get; set; }

    /// <summary>The height when <see cref="Sizing"/> is explicit; null keeps the natural
    /// height, or follows a given width in proportion.</summary>
    public double? Height { get; set; }

    /// <summary><see cref="Height"/> is a percentage of the origin box's height.</summary>
    public bool HeightIsPercent { get; set; }

    /// <summary>The edge the picture is placed from across: left (the default), right,
    /// or centred.</summary>
    public CssBackgroundEdge HorizontalEdge { get; set; } = CssBackgroundEdge.Start;

    /// <summary>The edge the picture is placed from up and down: top (the default),
    /// bottom, or centred.</summary>
    public CssBackgroundEdge VerticalEdge { get; set; } = CssBackgroundEdge.Start;

    /// <summary>How far in from <see cref="HorizontalEdge"/>.</summary>
    public double HorizontalOffset { get; set; }

    /// <summary><see cref="HorizontalOffset"/> is a percentage of the room the picture
    /// leaves across the origin box.</summary>
    public bool HorizontalOffsetIsPercent { get; set; }

    /// <summary>How far in from <see cref="VerticalEdge"/>.</summary>
    public double VerticalOffset { get; set; }

    /// <summary><see cref="VerticalOffset"/> is a percentage of the room the picture
    /// leaves up and down the origin box.</summary>
    public bool VerticalOffsetIsPercent { get; set; }

    /// <summary>How the picture repeats across.</summary>
    public CssBackgroundRepeat RepeatAcross { get; set; } = CssBackgroundRepeat.Repeat;

    /// <summary>How the picture repeats up and down.</summary>
    public CssBackgroundRepeat RepeatUp { get; set; } = CssBackgroundRepeat.Repeat;

    /// <summary>The box the picture is sized and placed in; the padding box by default.</summary>
    public BackgroundBoxArea Origin { get; set; } = BackgroundBoxArea.Padding;

    /// <summary>The box outside which the picture does not show; the border box by default.</summary>
    public BackgroundBoxArea Clip { get; set; } = BackgroundBoxArea.Border;

    /// <summary>The PDF blend mode the picture is painted with (<c>Multiply</c>,
    /// <c>Screen</c>, ...); null paints it normally.</summary>
    public string? BlendMode { get; set; }
}
