namespace Aspose.Pdf;

/// <summary>A box that holds other paragraphs: CSS's block container. Its paragraphs
/// flow inside it exactly as they flow on the page -- text, lists, pictures and boxes
/// nested in it -- only narrowed to its content box, and it paints its background and
/// border around whatever part of them each page holds.
///
/// From the outside in: the <see cref="BaseParagraph.Margin"/>, the <see cref="Border"/>,
/// the <see cref="Padding"/>, then the content box the paragraphs fill. Margins are
/// space around the box and never collapse with a neighbour's or a child's. The box is
/// as wide as its region less its margins unless <see cref="Width"/> sizes its content
/// box, and then it stands where <see cref="BaseParagraph.HorizontalAlignment"/> puts it;
/// it is as tall as its paragraphs, or <see cref="MinHeight"/> when that is taller.
///
/// A box its page cannot finish continues on the next: the part left behind is closed
/// under the content it holds (its bottom padding and border drawn, its corners
/// rounded), and the part that continues opens again with its top border and padding,
/// and its top margin when <see cref="TopMarginAfterBreak"/> asks for it. While the box
/// is open the flow keeps room for the bottom margin, border and padding under its
/// paragraphs, so the box always closes on the page it leaves. A box whose first
/// paragraph cannot start on the page paints nothing there and opens on the next.
///
/// Unlike a <see cref="FloatingBox"/> it never leaves the flow and never takes a fixed
/// size from outside: it is the flow's own, around the paragraphs it holds.</summary>
public sealed class BoxBlock : BaseParagraph
{
    /// <summary>What the box holds, in order.</summary>
    public Paragraphs Paragraphs { get; } = new();

    /// <summary>The rules around the padding; null for none. Each side is as wide as
    /// its own rule and painted in its own style (see <see cref="RulePainter"/>).</summary>
    public BorderInfo? Border { get; set; }

    /// <summary>The space between the border and the paragraphs; null for none.</summary>
    public MarginInfo? Padding { get; set; }

    /// <summary>The colour filling the box out to its border's outer edge; null for none.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>The opacity the background is painted at, 0 to 1; null paints it at its
    /// colour's own alpha.</summary>
    public double? BackgroundOpacity { get; set; }

    /// <summary>The box's rounded corners; null leaves them square. The background fills
    /// inside the rounded outline and the border inside the ring it leaves.</summary>
    public CornerRadii? CornerRadii { get; set; }

    /// <summary>Pictures painted over the background colour and under the border, the
    /// first on top (see <see cref="BackgroundPicture"/>); each part of a box that
    /// breaks across pages draws them as a whole box.</summary>
    public List<BackgroundPicture> BackgroundPictures { get; } = new();

    /// <summary>The width of the content box in points, or -- when
    /// <see cref="WidthIsFraction"/> is set -- the fraction of the region's width it
    /// takes; 0 (the default) fills the region less the margins, border and padding.</summary>
    public double Width { get; set; }

    /// <summary><see cref="Width"/> is a fraction of the region's width, not points.</summary>
    public bool WidthIsFraction { get; set; }

    /// <summary>The least height of the content box in points on the page the box starts;
    /// a box whose paragraphs are shorter is closed that far under its content top.</summary>
    public double MinHeight { get; set; }

    /// <summary>Whether the part that continues on the next page opens with the box's
    /// top margin again, as the box itself did; off (the default) it opens flush at the
    /// page's content top, the way a fragmented CSS box does.</summary>
    public bool TopMarginAfterBreak { get; set; }

    /// <summary>Whether a box that would break across pages starts on the next page
    /// instead, whole. A box taller than a page breaks anyway, and so does one holding
    /// a table, a list or a picture, whose height only laying them out tells.</summary>
    public bool IsKeptTogether { get; set; }

    /// <summary>A rule stroked across the content box through its vertical middle, from
    /// its left edge to its right, painted over the background and border: the line a
    /// separator draws. Its <see cref="GraphInfo.LineWidth"/>, colour (its alpha the
    /// stroke's opacity), dashes and caps are the stroke's. A box holding nothing but
    /// the rule is as tall as <see cref="MinHeight"/> makes it; null (the default) draws none.</summary>
    public GraphInfo? MiddleRule { get; set; }
}
