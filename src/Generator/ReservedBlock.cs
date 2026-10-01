namespace Aspose.Pdf;

/// <summary>A block of the flow whose content its caller lays out and paints itself: the
/// flow reserves the room, the caller fills it. Where the block stands, the flow asks
/// <see cref="LayOut"/> how much of the room left on the page its next part takes; it keeps
/// that height, and when the part says the block continues, it breaks the page and asks
/// again for the next part at the top of the new one. Once the document is saved,
/// <see cref="Parts"/> says which page each part landed on and where, so the caller can
/// paint it there.
///
/// It stands wherever a paragraph does - on the page, in a <see cref="BoxBlock"/> or a list
/// item - and the room it is offered is the region there: narrowed by the boxes around it,
/// its bottom raised by the room they keep under their paragraphs. Its own margins, border
/// and padding are the caller's to include in the heights it answers with; the flow adds
/// nothing around it.</summary>
public sealed class ReservedBlock : BaseParagraph
{
    private readonly List<ReservedPart> _parts = new();

    /// <summary>Lays the next part out in the room offered: returns the part, or null when
    /// nothing of the block fits there. A block that fits nothing is asked again at the top
    /// of the next page; one that fits nothing even there is laid no further.</summary>
    public Func<ReservedRoom, ReservedPart?>? LayOut { get; set; }

    /// <summary>The narrowest and the widest the block can be laid out at, for a table column
    /// sized by what its cells hold; null leaves the column to the cell's other content.</summary>
    public Func<(double Min, double Max)>? MeasureWidths { get; set; }

    /// <summary>The parts the flow placed, in order, each with the page it landed on and
    /// the rectangle it took; filled when the document's pages are laid out.</summary>
    public IReadOnlyList<ReservedPart> Parts => _parts;

    internal void ClearParts() => _parts.Clear();

    internal void AddPart(ReservedPart part) => _parts.Add(part);
}

/// <summary>The room a <see cref="ReservedBlock"/>'s part is offered: the region's left edge
/// and width, and the cursor's height with the lowest the part may reach under it, all in
/// the page's own points (y up).</summary>
public readonly struct ReservedRoom
{
    /// <summary>Creates the room of one part.</summary>
    public ReservedRoom(int index, double left, double width, double top, double bottom, bool isPageTop)
    {
        Index = index;
        Left = left;
        Width = width;
        Top = top;
        Bottom = bottom;
        IsPageTop = isPageTop;
    }

    /// <summary>Which part this is: 0 for the first.</summary>
    public int Index { get; }

    /// <summary>The region's left edge.</summary>
    public double Left { get; }

    /// <summary>The region's width.</summary>
    public double Width { get; }

    /// <summary>Where the part starts: the flow's cursor.</summary>
    public double Top { get; }

    /// <summary>The lowest the part may reach.</summary>
    public double Bottom { get; }

    /// <summary>Whether nothing stands above the part on its page: a part that fits nothing
    /// here fits nothing anywhere, and should be placed as far as it goes.</summary>
    public bool IsPageTop { get; }
}

/// <summary>One part of a <see cref="ReservedBlock"/>: the height it takes from the top of its
/// room, whether the block continues on the next page, and the caller's own state for
/// painting it; the flow adds the page it landed on and its rectangle.</summary>
public sealed class ReservedPart
{
    /// <summary>The height the part takes, down from the room's top.</summary>
    public double Height { get; set; }

    /// <summary>Whether the block goes on after this part, on the next page.</summary>
    public bool Continues { get; set; }

    /// <summary>Whatever the caller needs to paint the part; the flow keeps it as it is.</summary>
    public object? State { get; set; }

    /// <summary>The page the part landed on; set when the pages are laid out.</summary>
    public Page? Page { get; internal set; }

    /// <summary>The comment the flow wrote into the page's content where the part stands, so
    /// the caller can paint the part in that place rather than over everything after it:
    /// "% " followed by this, on a line of its own.</summary>
    public string? Marker { get; internal set; }

    /// <summary>The rectangle the part took on its page: the room's width, its height.</summary>
    public Rectangle? Rect { get; internal set; }
}
