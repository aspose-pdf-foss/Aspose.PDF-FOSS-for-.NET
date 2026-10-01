namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>A border drawn around the paragraph's BLOCK when it flows on a
    /// page: around its padding and line boxes, inside its <see cref="BaseParagraph.Margin"/>.
    /// Each side is a band of its own width; the text starts inside the left band,
    /// wraps to the width that is left inside the left and right bands, and the
    /// block grows by the top and bottom bands. A <see cref="TextState.BackgroundColor"/>
    /// asked for as a block background covers the whole bordered box, bands
    /// included. Null (the default) draws none. Honoured by the flow only; a
    /// fragment placed at a position of its own ignores it.</summary>
    public BorderInfo? BlockBorder { get; set; }

    /// <summary>Space between the block's border (or its edge) and its line boxes,
    /// on each side. Null (the default) is none. Flow only.</summary>
    public MarginInfo? BlockPadding { get; set; }

    /// <summary>The width of the block's CONTENT -- its line boxes -- in points;
    /// padding and border add outside it. Zero (the default) takes the write
    /// region's width less the margins. A narrower block stands where
    /// <see cref="BlockHorizontalAlignment"/> puts it. Flow only.</summary>
    public double BlockWidth { get; set; }

    /// <summary>The most the block's content may be wide; the block shrinks to it
    /// when the region is wider. Zero (the default) is no limit. Flow only.</summary>
    public double BlockMaxWidth { get; set; }

    /// <summary>The height of the block's content box, in points; padding and
    /// border add outside it. Lines that do not fit are DROPPED, and the flow
    /// continues under the box whatever its content took. Zero (the default) is
    /// the content's own height. Flow only.</summary>
    public double BoxHeight { get; set; }

    /// <summary>The least the block's content box may be tall. Zero (the default)
    /// is no floor. Flow only.</summary>
    public double BoxMinHeight { get; set; }

    /// <summary>The most the block's content box may be tall: a taller content is
    /// cut to it, lines beyond dropped, and a declared <see cref="BoxHeight"/> above
    /// it is brought down to it; the floor wins over it. Zero (the default) is no cap.</summary>
    public double BoxMaxHeight { get; set; }

    /// <summary>The least the block's content box may be wide: a narrower declared
    /// or fitted width grows to it. Zero (the default) is no floor. Flow only.</summary>
    public double BlockMinWidth { get; set; }

    /// <summary>Where a block narrower than its region stands across it. Left
    /// (the default) hugs the region's left edge; this is the BLOCK's place, not
    /// the alignment of the text inside it.</summary>
    public HorizontalAlignment BlockHorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Where a SINGLE line stands inside a content box taller than it is.
    /// Top (the default) seats it at the top; Bottom puts its font descent flush
    /// with the box's bottom; Center halves that drop. Several lines always seat at
    /// the top.</summary>
    public VerticalAlignment BlockVerticalAlignment { get; set; } = VerticalAlignment.Top;

    /// <summary>Lays the fragment as a block box even when no other block property
    /// is set -- what makes the flow honour its <see cref="BaseParagraph.Margin"/>'s
    /// LEFT and RIGHT, which a plain flow fragment has never taken. Off by default,
    /// so a caller who set a left margin years ago sees nothing move.</summary>
    public bool BlockBoxed { get; set; }

    /// <summary>Rounds the block's corners (see <see cref="CornerRadii"/>): the
    /// background is painted inside the rounded outline and the
    /// <see cref="BlockBorder"/> inside the ring between it and the inner outline.
    /// A reach given as a fraction is of the bordered box's own width or height.
    /// Null (the default) leaves the corners square. Flow only.</summary>
    public CornerRadii? BlockCornerRadii { get; set; }

    /// <summary>Pictures painted over the block's background and under its border and
    /// text, the first on top (see <see cref="BackgroundPicture"/>). Empty (the default)
    /// paints none. Flow only.</summary>
    public List<BackgroundPicture> BlockBackgroundPictures { get; } = new();

    /// <summary>True when any block-box property is set, so the flow lays the
    /// fragment as a box.</summary>
    internal bool HasBlockBox =>
        BlockBoxed || BlockBorder is not null || BlockCornerRadii is not null || BlockBackgroundPictures.Count > 0 || BlockPadding is not null || BlockWidth > 0 || BlockMaxWidth > 0
        || BoxHeight > 0 || BoxMinHeight > 0 || BoxMaxHeight > 0 || BlockMinWidth > 0
        || BlockHorizontalAlignment != HorizontalAlignment.Left
        || BlockVerticalAlignment != VerticalAlignment.Top;
}
