using System.Text;

namespace Aspose.Pdf.Text;

public sealed partial class ParagraphAbsorber
{
    /// <summary>
    /// Groups fragments into horizontal lines based on Y-coordinate proximity.
    /// </summary>
    private static List<TextLine> GroupIntoLines(List<TextFragment> fragments)
    {
        var gl = new GroupLinesState();
        gl.fragments = fragments;
        gl.lines = new List<TextLine>();
        gl.sorted = gl.fragments.OrderByDescending(f => GetY(f)).ThenBy(f => GetX(f)).ToList();

        foreach (var frag in gl.sorted)
        {
            GroupFragmentIntoLine(gl, frag);
        }

        // Sort fragments within each line left-to-right
        foreach (var line in gl.lines)
            line.Fragments.Sort((a, b) => GetX(a).CompareTo(GetX(b)));

        gl.splitLines = new List<TextLine>();
        foreach (var line in gl.lines)
        {
            SplitGroupedLine(gl, line);
        }

        return gl.splitLines;
    }

    /// <summary>
    /// Groups lines into paragraphs within a section.
    /// Rules: vertical gaps below the section threshold never
    /// split a paragraph; a line starts a new one only on a content trigger, and
    /// every trigger requires the line to lead with a capital letter:
    ///  - T-indent : the line starts more than ~0.55·F right of the paragraph's
    ///               left edge;
    ///  - T-numeric: the first token is a pure number, the previous line ends with
    ///               a period, and a capital follows the number;
    ///  - T-space  : the text begins with literal whitespace before the capital and
    ///               the previous line stops ≥ ~1.5 em short of the block's right edge.
    /// </summary>
    /// <summary>A line whose ink stops more than this many em before the section's
    /// right edge is SHORT (probed: a 4 em gap is short, 3 em is not; 2.9 em kept a
    /// heading pair together, 4.6 em split one).</summary>
    private const double ShortLineGapEm = 3.5;

    /// <summary>The smallest left-edge shift that, after a short line, opens a
    /// paragraph (probed: 1 pt splits at 10 pt; 0.06 pt does not).</summary>
    private const double MinShiftPt = 1.0;

    /// <summary>The same threshold scaled for large faces, where a glyph's side
    /// bearing alone moves a left edge by a few tenths of a point.</summary>
    private const double MinShiftEm = 0.1;

    /// <summary>The shift a non-capital lead needs after a short line (a half-em
    /// page number stays, a 26 em table row leaves).</summary>
    private const double LargeShiftEm = 2.0;

    private static List<MarkupParagraph> GroupIntoParagraphs(List<TextLine> lines, double pageBodyRight = double.NaN)
    {
        var gp = new ParagraphGroupState();
        gp.lines = lines;
        gp.pageBodyRight = pageBodyRight;
        if (gp.lines.Count == 0) return [];

        if (GridDebug)
            foreach (var l in gp.lines)
                Console.Error.WriteLine($"[line] mid={l.MidY:F3} y={l.MinY:F3}..{l.MaxY:F3} x={l.MinX:F1} n={l.Fragments.Count} '{LineText(l)[..Math.Min(20, LineText(l).Length)]}'");
        gp.paragraphs = new List<MarkupParagraph>();
        gp.currentLines = new List<TextLine> { gp.lines[0] };
        gp.paraLeft = gp.lines[0].MinX;
        gp.sectionRight = gp.lines.Max(l => l.MaxX);
        gp.bodyRight = double.IsNaN(gp.pageBodyRight) ? gp.lines.Max(l => l.MaxX) : gp.pageBodyRight;

        for (var i = 1; i < gp.lines.Count; i++)
        {
            GroupParagraphLine(gp, i);
        }

        if (gp.currentLines.Count > 0)
            gp.paragraphs.Add(BuildParagraph(gp.currentLines));

        return gp.paragraphs;
    }

    /// <summary>Re-join a paragraph's per-line fragments into text, consulting the page's
    /// standalone space glyphs: a space is inserted between two fragments when either the
    /// plain horizontal-gap heuristic fires or a space glyph sits between them (documents
    /// that draw every inter-word space as its own overlapping run leave a near-zero gap).</summary>
    private static string AssembleTextWithSpaces(
        List<List<TextFragment>> lines, List<TextFragment> spaces)
    {
        static bool SpaceGlyphAt(List<TextFragment> spaces, Rectangle anchor, double fromX, double toX)
        {
            foreach (var s in spaces)
            {
                var r = s.Rectangle!;
                var cx = (r.LLX + r.URX) / 2;
                if (cx < fromX - 1) continue;
                if (cx > toX + r.Width) break;              // spaces sorted by LLX
                if (r.LLY < anchor.URY && r.URY > anchor.LLY) return true;
            }
            return false;
        }

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            for (var fi = 0; fi < line.Count; fi++)
            {
                var f = line[fi];
                if (fi > 0 && f.Rectangle is not null)
                {
                    var prevFrag = line[fi - 1];
                    if (prevFrag.Rectangle is not null)
                    {
                        var gap = f.Rectangle.LLX - prevFrag.Rectangle.URX;
                        var spaceGlyph = gap <= f.FontSize * 0.15
                            && SpaceGlyphAt(spaces, f.Rectangle, prevFrag.Rectangle.URX, f.Rectangle.LLX);
                        // A positive word-sized gap ALWAYS gets a boundary space —
                        // even when the left fragment already ends with one
                        // ("queries  or" is emitted there); the space-glyph
                        // re-insertion keeps the duplicate guard.
                        if (gap > f.FontSize * 0.15
                            || (spaceGlyph && !prevFrag.Text.EndsWith(" ") && !f.Text.StartsWith(" ")))
                            sb.Append(' ');
                    }
                }
                sb.Append(f.Text);
            }
            // Standalone space glyphs at the end of a line are dropped (a trailing
            // space is kept only when it is part of the last word's own run).
            sb.Append("\r\n");
        }
        var text = sb.ToString();
        if (text.EndsWith("\r\n"))
            text = text[..^2];
        return text;
    }

    /// <summary>Punctuation that closes what precedes it and never takes a space
    /// in front of it.</summary>
    private static bool IsClosingPunctuation(char c) =>
        c is ',' or '.' or ';' or ':' or '!' or '?' or ')' or ']' or '}'
          or '%' or '’' or '”' or '»';

    /// <summary>A one-space fragment covering the pen gap between two drawn runs,
    /// seated on the gap itself and carrying the following run's text state.</summary>
    private static TextFragment GapSpaceFragment(TextFragment left, TextFragment right)
    {
        var l = left.Rectangle!;
        var r = right.Rectangle!;
        var rect = new Rectangle(l.URX, System.Math.Min(l.LLY, r.LLY),
                                 r.LLX, System.Math.Max(l.URY, r.URY));
        return new TextFragment(" ", rect, right.TextState);
    }

    private static MarkupParagraph BuildParagraph(List<TextLine> lines)
    {
        var sb = new StringBuilder();
        var lineFragments = new List<List<TextFragment>>();

        foreach (var line in lines)
        {
            var frags = new List<TextFragment>(line.Fragments.Count);
            lineFragments.Add(frags);

            for (var fi = 0; fi < line.Fragments.Count; fi++)
            {
                var f = line.Fragments[fi];
                // Insert space between fragments when there's a horizontal gap
                if (fi > 0 && f.Rectangle is not null)
                {
                    var prevFrag = line.Fragments[fi - 1];
                    if (prevFrag.Rectangle is not null)
                    {
                        var gap = f.Rectangle.LLX - prevFrag.Rectangle.URX;
                        if (gap > f.FontSize * 0.15)
                        {
                            sb.Append(' ');
                            // The word gap becomes a real fragment spanning it, so a caller
                            // walking Lines and concatenating fragment text reads the same
                            // string as the paragraph's own Text. A source that draws each
                            // glyph as its own run (and leaves the inter-word spacing to the
                            // pen) otherwise reads back as one unbroken word.
                            //
                            // Two gaps are NOT word spaces and get no fragment: one whose
                            // boundary the source already spells with a space character of
                            // its own (adding another would double it), and one between runs
                            // set at DIFFERENT sizes — that is a tracked heading or a raised
                            // initial being positioned, not a space between words.
                            var alreadySpaced = prevFrag.Text.EndsWith(" ", StringComparison.Ordinal)
                                || f.Text.StartsWith(" ", StringComparison.Ordinal);
                            var sameSize = System.Math.Abs(prevFrag.FontSize - f.FontSize) < 0.01;
                            // Nor does a gap in front of closing punctuation: a slanted or
                            // swashed final glyph leaves room after its box that the comma
                            // sits in, and no space belongs there.
                            var leadsPunctuation = f.Text.Length > 0 && IsClosingPunctuation(f.Text[0]);
                            if (!alreadySpaced && sameSize && !leadsPunctuation)
                                frags.Add(GapSpaceFragment(prevFrag, f));
                        }
                    }
                }
                frags.Add(f);
                sb.Append(f.Text);
            }

            sb.Append("\r\n");
        }

        var points = OutlinePolygon(lines);

        var text = sb.ToString();
        if (text.EndsWith("\r\n"))
            text = text[..^2];

        return new MarkupParagraph(text, points, lineFragments);
    }

    /// <summary>Two consecutive line edges closer than this share one polygon
    /// edge (the lower line's value for the right side, the upper line's for the
    /// left). Edges 0.25 pt apart merge; edges 1.79 pt apart step.</summary>
    private const double OutlineEdgeTolerance = 1.0;

    /// <summary>
    /// The paragraph polygon reported: the rectilinear OUTLINE of the
    /// stacked line boxes, counter-clockwise from the bottom line's lower-left
    /// corner - along the bottom line, up the right side stepping in or out where
    /// a line's right edge moves, across the top line, and down the left side
    /// stepping at a first-line indent. A plain paragraph yields the four corners
    /// LL, LR, UR, UL; a short last line or an indented first line adds a step.
    /// </summary>
    private static Point[] OutlinePolygon(List<TextLine> lines)
    {
        // A line's vertical extent in the outline is its LEFTMOST fragment's box: a
        // bullet glyph seated 1.6 pt above its item text sets both the item's
        // lower-left corner and the step above it (a "Place: ..." item's
        // corner sits at the bullet's bottom, not the text's).
        static (double bottom, double top) Extent(TextLine l)
        {
            TextFragment? leftmost = null;
            foreach (var f in l.Fragments)
                if (leftmost is null || (f.Rectangle?.LLX ?? GetX(f)) < (leftmost.Rectangle?.LLX ?? GetX(leftmost)))
                    leftmost = f;
            return leftmost?.Rectangle is { } r ? (r.LLY, r.URY) : (l.MinY, l.MaxY);
        }

        // lines are top-to-bottom; walk the right side bottom-up, the left side top-down.
        var pts = new List<Point>();
        var bottom = lines[^1];
        var top = lines[0];

        var right = bottom.MaxX;
        pts.Add(new Point(bottom.MinX, Extent(bottom).bottom));
        pts.Add(new Point(right, Extent(bottom).bottom));
        for (var i = lines.Count - 1; i > 0; i--)
        {
            var next = lines[i - 1];
            if (Math.Abs(next.MaxX - right) < OutlineEdgeTolerance) continue;
            pts.Add(new Point(right, Extent(lines[i]).top));
            right = next.MaxX;
            pts.Add(new Point(right, Extent(next).bottom));
        }
        pts.Add(new Point(right, Extent(top).top));

        var left = top.MinX;
        pts.Add(new Point(left, Extent(top).top));
        for (var i = 0; i < lines.Count - 1; i++)
        {
            var next = lines[i + 1];
            if (Math.Abs(next.MinX - left) < OutlineEdgeTolerance) continue;
            pts.Add(new Point(left, Extent(lines[i]).bottom));
            left = next.MinX;
            pts.Add(new Point(left, Extent(next).top));
        }
        // The walk closes on the first point; a left step right above the bottom
        // line would duplicate it, so the start corner takes the bottom run's edge.
        if (Math.Abs(left - pts[0].X) >= OutlineEdgeTolerance)
            pts[0] = new Point(left, pts[0].Y);
        return pts.ToArray();
    }

    private static double GetX(TextFragment f) =>
        f.PositionOrNull?.XIndent ?? f.Rectangle?.LLX ?? 0;

    private static double GetY(TextFragment f) =>
        f.PositionOrNull?.YIndent ?? f.Rectangle?.LLY ?? 0;

    private static double Median(List<double> list)
    {
        if (list.Count == 0) return 0;
        var sorted = list.OrderBy(x => x).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }

    /// <summary>
    /// Represents a horizontal line of text.
    /// </summary>
    internal sealed class TextLine
    {
        public List<TextFragment> Fragments { get; } = [];
        public double MidY { get; private set; }
        public double MidX { get; private set; }
        public double MinY { get; private set; }
        public double MaxY { get; private set; }
        public double MinX { get; private set; }
        public double MaxX { get; private set; }
        public double AvgFontSize { get; private set; }

        public void Recalc()
        {
            MinX = Fragments.Min(f => f.Rectangle?.LLX ?? GetX(f));
            MaxX = Fragments.Max(f => f.Rectangle?.URX ?? GetX(f));
            MinY = Fragments.Min(f => f.Rectangle?.LLY ?? GetY(f));
            MaxY = Fragments.Max(f => f.Rectangle?.URY ?? (GetY(f) + f.FontSize));
            MidY = (MinY + MaxY) / 2;
            MidX = (MinX + MaxX) / 2;
            // Use actual rendered height for effective font size (handles scaled fonts with FontSize=1)
            var avgHeight = Fragments
                .Where(f => f.Rectangle is not null && f.Rectangle.Height > 0)
                .Select(f => f.Rectangle!.Height)
                .DefaultIfEmpty(12)
                .Average();
            var avgFontSz = Fragments.Average(f => f.FontSize > 0 ? f.FontSize : 12);
            AvgFontSize = Math.Max(avgHeight, avgFontSz);
        }

        private static double GetX(TextFragment f) =>
            f.PositionOrNull?.XIndent ?? f.Rectangle?.LLX ?? 0;
        private static double GetY(TextFragment f) =>
            f.PositionOrNull?.YIndent ?? f.Rectangle?.LLY ?? 0;
    }
}
