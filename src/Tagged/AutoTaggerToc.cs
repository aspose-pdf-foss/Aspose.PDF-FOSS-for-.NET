using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // A table of contents' entry ends in leader dots running on to the page it names (a number, or a roman one for front
    // matter); at least this many entries on a page, their page numbers ending within this many points of one another,
    // make a table of contents.
    private static readonly Regex TocTail = new(@"(?:\.\s?){4,}\s*(?:\d{1,4}|[ivxlcdm]{1,7})\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private const int MinTocEntries = 3;
    private const double TocEdgeTolerance = 3.0;

    /// <summary>Marks the lines of a page's table of contents: lines ending in leader dots and a page number, enough of
    /// them ending at one edge. Each is an entry of its own - read whole, its number, title and page together, never
    /// parted into columns or taken as a table's rows.</summary>
    private static void MarkTocLines(List<Line> content)
    {
        var tails = content.Where(l => l.Frags.Count > 0 && TocTail.IsMatch(string.Concat(l.Frags.Select(f => f.Text)))).ToList();
        foreach (var edge in tails.GroupBy(l => System.Math.Round(l.Frags[^1].R / TocEdgeTolerance)))
        {
            var entries = tails.Where(l => System.Math.Abs(l.Frags[^1].R - edge.First().Frags[^1].R) <= TocEdgeTolerance).ToList();
            if (entries.Count < MinTocEntries) continue;
            foreach (var l in entries) l.Toc = true;
        }
    }
}
