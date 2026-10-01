namespace Aspose.Pdf.Text;

/// <summary>A paragraph's orphan and widow control at a break, the same wherever its lines stand -
/// on a page of the flow or in a table cell.</summary>
internal static class LineControls
{
    /// <summary>Whether the options could ask anything of a paragraph's breaks.</summary>
    public static bool Any(TextFormattingOptions? options) =>
        options is not null && (options.MinOrphanLines > 1 || options.MinWidowLines > 1);

    /// <summary>How many of a paragraph's remaining lines stay before a break under its line
    /// controls (<see cref="TextFormattingOptions.MinOrphanLines"/>,
    /// <see cref="TextFormattingOptions.MinWidowLines"/>), given how many fit: all that fit when
    /// the controls allow it; fewer, so that enough are carried over, when at most
    /// <see cref="TextFormattingOptions.MaxWidowLinesMoved"/> must move and the orphan floor (or
    /// one line) stays behind; none - the whole paragraph moves on - when too few of its first
    /// lines would stand before the break, or when widows cannot be made up and the paragraph asks
    /// to move whole then. A paragraph that may not move whole (it stands at the top of its region
    /// already, or has moved once) keeps what fits.</summary>
    public static int Kept(TextFormattingOptions? options, int fit, int remaining, bool first, bool mayMoveWhole)
    {
        if (options is null || fit <= 0 || fit >= remaining) return fit;
        if (first && fit < options.MinOrphanLines) return mayMoveWhole ? 0 : fit;
        var carried = remaining - fit;
        if (options.MinWidowLines <= 0 || carried >= options.MinWidowLines) return fit;
        var move = options.MinWidowLines - carried;
        if (move <= options.MaxWidowLinesMoved && fit - move >= Math.Max(1, options.MinOrphanLines))
            return fit - move;
        return first && options.MoveWholeOnWidowViolation && mayMoveWhole ? 0 : fit;
    }
}
