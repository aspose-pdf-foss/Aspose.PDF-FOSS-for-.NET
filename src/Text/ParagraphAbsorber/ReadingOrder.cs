namespace Aspose.Pdf.Text;

public sealed partial class ParagraphAbsorber
{
    /// <summary>The page's sections in reading order. A band of white across the whole group divides it into
    /// parts read top to bottom; a group no such band divides but a gutter running down all of it - the columns
    /// of a page - is read column by column, left to right; each part is ordered the same way in turn. Sections
    /// that overlap whichever way are ordered by their bottom edges, level ones left to right.</summary>
    private static List<MarkupSection> InReadingOrder(List<MarkupSection> sections)
    {
        var ordered = new List<MarkupSection>(sections.Count);
        OrderByCuts(sections, ordered);
        return ordered;
    }

    private static void OrderByCuts(List<MarkupSection> group, List<MarkupSection> ordered)
    {
        if (group.Count > 1)
        {
            var parts = SplitAtGaps(group, s => s.Rectangle.LLY, s => s.Rectangle.URY, fromHigh: true);
            if (parts.Count == 1) parts = SplitAtGaps(group, s => s.Rectangle.LLX, s => s.Rectangle.URX, fromHigh: false);
            if (parts.Count > 1)
            {
                foreach (var part in parts) OrderByCuts(part, ordered);
                return;
            }
        }
        // Sections are ordered by their BOTTOM edge, top-to-bottom (LLY descending), ties left-to-right.
        group.Sort((x, y) =>
        {
            var dy = y.Rectangle.LLY.CompareTo(x.Rectangle.LLY);
            return dy != 0 ? dy : x.Rectangle.LLX.CompareTo(y.Rectangle.LLX);
        });
        OrderLevelSectionsLeftToRight(group);
        ordered.AddRange(group);
    }

    /// <summary>The group divided where no section covers a stretch of one axis: the runs of sections whose
    /// extents (<paramref name="low"/> to <paramref name="high"/>) overlap or touch, from the high end of the
    /// axis or the low one.</summary>
    private static List<List<MarkupSection>> SplitAtGaps(List<MarkupSection> group, Func<MarkupSection, double> low,
        Func<MarkupSection, double> high, bool fromHigh)
    {
        var sorted = fromHigh ? group.OrderByDescending(high).ToList() : group.OrderBy(low).ToList();
        var parts = new List<List<MarkupSection>>();
        var edge = 0.0;
        foreach (var section in sorted)
        {
            var open = parts.Count > 0 && (fromHigh ? high(section) >= edge : low(section) <= edge);
            if (!open) parts.Add(new List<MarkupSection>());
            parts[^1].Add(section);
            edge = !open ? (fromHigh ? low(section) : high(section))
                : fromHigh ? Math.Min(edge, low(section)) : Math.Max(edge, high(section));
        }
        return parts;
    }
}
