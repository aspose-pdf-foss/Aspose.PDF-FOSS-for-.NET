using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Represents the text markup of a single page, organized into sections.
/// </summary>
public sealed class PageMarkup
{
    private readonly List<MarkupSection> _sections;
    private readonly ParagraphAbsorberOptions _options;
    private bool _isMulticolumn;

    internal PageMarkup(int pageNumber, List<MarkupSection> sections, ParagraphAbsorberOptions options)
    {
        Number = pageNumber;
        _sections = sections;
        _options = options;
    }

    /// <summary>The 1-based page number.</summary>
    public int Number { get; }

    /// <summary>The sections detected on this page.</summary>
    public List<MarkupSection> Sections => _sections;

    /// <summary>Every <see cref="MarkupParagraph"/> on this page, flattened across sections.</summary>
    public List<MarkupParagraph> Paragraphs
    {
        get
        {
            var all = new List<MarkupParagraph>();
            foreach (var s in _sections)
                foreach (var p in s.Paragraphs) all.Add(p);
            return all;
        }
    }

    /// <summary>Every <see cref="TextFragment"/> on this page, flattened across paragraphs.</summary>
    public List<TextFragment> TextFragments
    {
        get
        {
            var all = new List<TextFragment>();
            foreach (var s in _sections)
                foreach (var p in s.Paragraphs)
                    foreach (var f in p.Fragments) all.Add(f);
            return all;
        }
    }

    /// <summary>Bounding rectangle that contains every section on this page.</summary>
    public Rectangle Rectangle
    {
        get
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            bool any = false;
            foreach (var s in _sections)
            {
                if (s.Rectangle is not { } r) continue;
                if (r.LLX < minX) minX = r.LLX;
                if (r.LLY < minY) minY = r.LLY;
                if (r.URX > maxX) maxX = r.URX;
                if (r.URY > maxY) maxY = r.URY;
                any = true;
            }
            return any ? new Rectangle(minX, minY, maxX, maxY) : new Rectangle(0, 0, 0, 0);
        }
    }

    /// <summary>
    /// Gets or sets whether paragraphs may continue across the page's columns.
    /// Sections are untouched; the LAST paragraph of a column's bottom section
    /// absorbs the FIRST paragraph of the next column's top section (the absorbed
    /// paragraph leaves its own section). Switching back restores every section's
    /// common paragraphs.
    /// </summary>
    public bool IsMulticolumnParagraphsAllowed
    {
        get => _isMulticolumn;
        set
        {
            if (_isMulticolumn == value) return;
            _isMulticolumn = value;
            if (_isMulticolumn) JoinColumnContinuations();
            else foreach (var s in _sections) s.RestoreCommonParagraphs();
        }
    }

    /// <summary>
    /// The page's text columns, left to right, each holding its sections top to
    /// bottom. Sections cluster by horizontal overlap (narrowest first); a section
    /// that overlaps two clusters - a headline across the columns - belongs to no
    /// column. Overlap counts from a tenth of the narrower width, so a glyph
    /// poking into the gutter does not fuse two columns.
    /// </summary>
    internal List<List<MarkupSection>> BuildColumns()
    {
        const double OverlapFraction = 0.1;
        var groups = new List<(double left, double right, List<MarkupSection> members)>();
        foreach (var sec in _sections.OrderBy(s => s.Rectangle.URX - s.Rectangle.LLX))
        {
            var r = sec.Rectangle;
            var hits = new List<int>();
            for (var gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                var overlap = Math.Min(r.URX, g.right) - Math.Max(r.LLX, g.left);
                var minW = Math.Min(r.URX - r.LLX, g.right - g.left);
                if (overlap > OverlapFraction * minW) hits.Add(gi);
            }
            if (hits.Count == 0) groups.Add((r.LLX, r.URX, [sec]));
            else if (hits.Count == 1)
            {
                var g = groups[hits[0]];
                g.members.Add(sec);
                groups[hits[0]] = (Math.Min(g.left, r.LLX), Math.Max(g.right, r.URX), g.members);
            }
            // two or more hits: a spanner, member of no column
        }
        groups.Sort((x, y) => x.left.CompareTo(y.left));
        var columns = new List<List<MarkupSection>>();
        foreach (var g in groups)
        {
            g.members.Sort((x, y) => y.Rectangle.URY.CompareTo(x.Rectangle.URY));
            columns.Add(g.members);
        }
        return columns;
    }

    private void JoinColumnContinuations()
    {
        var columns = BuildColumns();
        for (var k = 0; k + 1 < columns.Count; k++)
            JoinSections(columns[k][^1], columns[k + 1][0], Number);
    }

    /// <summary>The bottom of <paramref name="from"/>'s last column continues at the
    /// top of <paramref name="to"/>'s first column.</summary>
    internal static void JoinAcrossPages(PageMarkup from, PageMarkup to)
    {
        var fromCols = from.BuildColumns();
        var toCols = to.BuildColumns();
        if (fromCols.Count == 0 || toCols.Count == 0) return;
        JoinSections(fromCols[^1][^1], toCols[0][0], to.Number);
    }

    /// <summary>Append the first paragraph of <paramref name="dst"/> to the last
    /// paragraph of <paramref name="src"/>: texts join with a line break, the lines
    /// concatenate, the continuation's polygon goes to SecondaryPoints with its page.</summary>
    private static void JoinSections(MarkupSection src, MarkupSection dst, int dstPageNumber)
    {
        if (ReferenceEquals(src, dst) || src.Paragraphs.Count == 0 || dst.Paragraphs.Count == 0) return;
        var head = src.Paragraphs[^1];
        var tail = dst.Paragraphs[0];
        var lines = new List<List<TextFragment>>(head.Lines);
        lines.AddRange(tail.Lines);
        var joined = new MarkupParagraph(head.Text + "\r\n" + tail.Text, head.Points, lines);
        joined.SecondaryPoints.AddRange(head.SecondaryPoints);
        joined.ContinuationPageNumbers.AddRange(head.ContinuationPageNumbers);
        joined.SecondaryPoints.Add(tail.Points);
        joined.ContinuationPageNumbers.Add(dstPageNumber);
        src.ReplaceParagraph(src.Paragraphs.Count - 1, joined);
        dst.RemoveParagraph(0);
    }
}
