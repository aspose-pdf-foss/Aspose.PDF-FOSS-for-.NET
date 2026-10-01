using System;
using System.Collections.Generic;
using System.Linq;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // Edges closer than this share of the text size (and never closer than the floor) line up.
    private const double EdgeToleranceEms = 0.25;
    private const double EdgeToleranceFloor = 2.0;
    // A line is short of its column when it ends this many tolerances before the column's
    // right edge, and set in from it when it starts this many after the left edge.
    private const double InsetTolerances = 4.0;
    // Justified text: at least this many lines, all but the last ending at one right edge.
    private const int JustifiedMinLines = 3;
    // Justified text may hang its punctuation past its edge (a stop, a comma, most of a hyphen): a line ending in a
    // letter then ends up to this many ems short of the lines ending in one, and still runs to the edge. Ragged lines
    // end further off than that.
    private const double HangingEms = 0.3;
    // An indent beyond this many ems of the text size is no paragraph's: the block stands in a
    // column the page analysis did not see, and its lines start where that column does.
    private const double MaxBlockIndentEms = 8.0;

    /// <summary>How a paragraph or heading sits in its column: its alignment (a standard
    /// /TextAlign name, null for Start), how far its lines start in from the column's left
    /// edge, and how much further (or less) its first line starts.</summary>
    private readonly record struct ParagraphLayout(LS.AttributeName? Align, double StartIndent, double TextIndent)
    {
        /// <summary>The space between this block and the one above it, beyond the gap between
        /// two lines of body text.</summary>
        public double SpaceBefore { get; init; }

        /// <summary>How far the block's lines end in from the column's right edge (a block set
        /// to a narrower measure, an abstract); 0 when they run to the edge.</summary>
        public double EndIndent { get; init; }
    }

    /// <summary>The layout of a block's lines within their column: the column's left edge is
    /// where the page's lines in it start furthest left, its right edge the column's right edge
    /// (the page's text margin for a single column).</summary>
    private static ParagraphLayout? LayoutOf(PageAnalysisState pa, List<Line> lines)
    {
        var body = lines.Where(l => l.Frags.Count > 0).ToList();
        if (body.Count == 0) return null;
        var (left, right) = ColumnBounds(pa, body[0]);
        if (right - left <= 0) return null;
        var size = body.Max(l => l.Size);
        var tol = Math.Max(EdgeToleranceFloor, EdgeToleranceEms * size);
        double Start(Line l) => l.Frags[0].X;
        double End(Line l) => l.Frags[^1].R;

        var starts = body.Select(Start).ToList();
        var ends = body.Select(End).ToList();
        var centre = (left + right) / 2;
        var inset = InsetTolerances * tol;
        // Centred lines are ragged at both ends; lines all starting at one x and ending at
        // another are a justified block set to a narrower measure.
        var centred = body.All(l => Math.Abs((Start(l) + End(l)) / 2 - centre) <= tol)
                      && starts.Min() - left > inset && right - ends.Max() > inset
                      && (body.Count == 1 || ends.Max() - ends.Min() > tol || starts.Max() - starts.Min() > tol)
                      && !(body.Count == 1 && StartsWithANeighbour(pa, body[0], centre, tol));
        // Right-aligned: one line ending at the column's edge, or lines ending at one edge of
        // their own while starting ragged (a list of names set flush right).
        // A lone line starting where the column's paragraphs start their first lines is such a first line, set justified
        // to the edge (a run-in heading filling it), not flush right.
        var rightAligned = starts.Min() - left > inset
                           && (body.Count == 1 ? Math.Abs(ends[0] - right) <= tol && !StartsAsAParagraph(pa, body[0], left, right, tol)
                               : ends.Max() - ends.Min() <= tol && starts.Max() - starts.Min() > tol);
        // Lines ending together at the column's edge, the last starting further in than the first, are set flush right
        // however near the column's left edge the first starts (a column found from these very lines starts there);
        // a paragraph's last line ends short of the edge. So are such lines whose first starts further in than a first
        // line is ever set in (a short name over a long title): no paragraph's first line, filling it to the edge.
        rightAligned |= body.Count > 1 && ends.Max() - ends.Min() <= tol && Math.Abs(ends.Max() - right) <= tol
                        && (starts[^1] - starts[0] > tol || starts[0] - starts.Skip(1).Min() > MaxIndentEms * size);
        // Justified: all lines but the last end at one edge - the column's, or the block's own
        // narrower one when its lines start in from the column too.
        var blockRight = body.Count >= JustifiedMinLines ? ends.Take(ends.Count - 1).Max() : right;
        var narrower = blockRight < right - tol && starts.Min() - left > tol;
        var hang = Math.Max(tol, HangingEms * size);
        var justified = body.Count >= JustifiedMinLines
                        && ends.Take(ends.Count - 1).All(e => Math.Abs(e - blockRight) <= hang)
                        && blockRight - ends[^1] > hang && (blockRight >= right - tol || narrower);
        // In a justified column so is a paragraph of fewer lines, or one going on into the next column or onto the next
        // page (its last line here as full as the others): its lines but the last run to their own column's edge.
        justified |= body.Count >= 2 && IsJustifiedColumn(pa, body[0])
                     && body.Take(body.Count - 1).All(l => ColumnBounds(pa, l).Right - End(l) <= hang && Start(l) - ColumnBounds(pa, l).Left <= MaxIndentEms * size);
        var endIndent = justified && narrower ? right - blockRight : 0;

        LS.AttributeName? align = centred ? LS.AttributeName.TextAlign_Center
            : rightAligned ? LS.AttributeName.TextAlign_End
            : justified ? LS.AttributeName.TextAlign_Justify
            : null;
        if (centred || rightAligned) return new ParagraphLayout(align, 0, 0);

        // The paragraph's own left edge is where its lines after the first start.
        var edge = body.Count > 1 ? starts.Skip(1).Min() : starts[0];
        var startIndent = edge - left > tol ? edge - left : 0;
        var textIndent = body.Count > 1 && Math.Abs(starts[0] - edge) > tol ? starts[0] - edge : 0;
        if (startIndent > MaxBlockIndentEms * size) startIndent = 0;
        if (Math.Abs(textIndent) > MaxBlockIndentEms * size) textIndent = 0;
        if (align is null && startIndent == 0 && textIndent == 0 && endIndent == 0) return null;
        return new ParagraphLayout(align, startIndent, textIndent) { EndIndent = Math.Round(endIndent, 1) };
    }

    /// <summary>Whether a lone line starts where a paragraph of its column starts its first line: another line of the
    /// column starts there and runs to the column's edge, and the line after it starts at the column's left edge.</summary>
    private static bool StartsAsAParagraph(PageAnalysisState pa, Line line, double left, double right, double tol)
    {
        var column = pa.lines.Where(l => l.Running == 0 && l.Frags.Count > 0 && l.ColumnRight == line.ColumnRight).ToList();
        for (var i = 0; i + 1 < column.Count; i++)
        {
            var (first, next) = (column[i], column[i + 1]);
            if (!ReferenceEquals(first, line) && next.Y < first.Y && Math.Abs(first.Frags[0].X - line.Frags[0].X) <= tol
                && Math.Abs(first.Frags[^1].R - right) <= tol && Math.Abs(next.Frags[0].X - left) <= tol)
                return true;
        }
        return false;
    }

    // A line this many pitches above or below a lone line is its neighbour.
    private const double NeighbourPitches = 3.0;

    /// <summary>Whether a lone line starts where a neighbouring line of its column starts, one not centred itself: two
    /// entries set flush left one under the other, the longer one's middle near the column's by chance.</summary>
    private static bool StartsWithANeighbour(PageAnalysisState pa, Line line, double centre, double tol)
    {
        var reach = NeighbourPitches * Math.Max(pa.pitch, line.Size);
        return pa.lines.Any(l => !ReferenceEquals(l, line) && l.Frags.Count > 0 && l.Running == 0
                                 && Math.Abs(l.Y - line.Y) <= reach
                                 && l.MinX < line.Frags[^1].R && l.Frags[^1].R > line.MinX
                                 && Math.Abs(l.Frags[0].X - line.Frags[0].X) <= tol
                                 && Math.Abs((l.Frags[0].X + l.Frags[^1].R) / 2 - centre) > tol);
    }

    /// <summary>How much further below the block above this one starts than a body line starts
    /// below the line before it: the space the author put between them. Nothing for the first
    /// block of a page or of a column.</summary>
    private static double SpaceBefore(PageAnalysisState pa, List<Line> lines)
    {
        if (lines.Count == 0 || lines[0].ColumnStart || lines[0].Frags.Count == 0) return 0;
        var first = lines[0];
        // The block above is in its column, or stands over it across (a title across the columns).
        return SpaceAbove(pa, first.Y + first.Size, first.Size,
            l => l.ColumnRight == first.ColumnRight || (l.Frags.Count > 0 && l.MinX < first.Frags[^1].R && l.Frags[^1].R > first.MinX),
            r => first.MinX < r.URX && first.Frags[^1].R > r.LLX);
    }

    /// <summary>The space between a block whose top edge is at <paramref name="top"/> and the
    /// block emitted before it, beyond the whitespace between two body lines; nothing when
    /// that block does not stand above it: a block of lines in another column
    /// (<paramref name="sameColumn"/>), a table whose region the block is not under
    /// (<paramref name="overlaps"/>), a figure (placed later).</summary>
    private static double SpaceAbove(PageAnalysisState pa, double top, double size,
        Func<Line, bool> sameColumn, Func<Aspose.Pdf.Rectangle, bool> overlaps)
    {
        if (pa.result.Count == 0) return 0;
        double bottom;
        if (pa.blockLines[^1] is { Count: > 0 } above)
        {
            // The block above stands in one of boxes set side by side: the block under the boxes stands under the foot
            // of the one over it.
            var band = pa.frames.FirstOrDefault(f => InsideBox(above[^1], f.LLX, f.LLY, f.URX, f.URY));
            var box = band is null ? null
                : pa.frames.FirstOrDefault(f => overlaps(f) && Math.Abs(f.LLY - band.LLY) <= FrameRuleReach && f.LLY > top);
            if (box is not null) bottom = box.LLY;
            else if (!sameColumn(above[^1])) return 0;
            else bottom = above[^1].Y;
        }
        else if (pa.result[^1].Kind == BlockKind.Table && overlaps(pa.tables[pa.result[^1].TableIndex].region))
        {
            // The table ends where its last row does: a reader sets the rows as tall as they stood, down to that edge.
            bottom = pa.tableBottoms[pa.result[^1].TableIndex];
        }
        else
        {
            return 0;
        }
        var usual = Math.Max(pa.pitch - pa.bodySize, 0);
        var extra = bottom - top - usual;
        return extra > Math.Max(EdgeToleranceFloor, EdgeToleranceEms * size) ? Math.Round(extra, 1) : 0;
    }

    /// <summary>State a table cell's width, its column's (the Layout /Width, ISO 32000-1
    /// §14.8.5.4.3), so a reader can lay the table out in its proportions.</summary>
    private static void StateWidth(LS.StructureElement cell, double width)
    {
        if (width <= 0) return;
        var a = new LS.StructureAttribute(LS.AttributeKey.Width);
        a.SetNumberValue(Math.Round(width, 1));
        cell.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout).SetAttribute(a);
    }

    // A rule runs along a cell's edge when rules within this many points of the edge cover this
    // share of it; such an edge is drawn this thick (the rules found are lines, their widths unread).
    private const double BorderReach = 1.5;
    private const double BorderCover = 0.6;
    private const double BorderRuleThickness = 0.5;

    /// <summary>The rules of a table, each moved onto the grid edge it draws: the row edge (column edge)
    /// nearest it, when it lies within half the row (column) beside that edge and across the table.
    /// A table read from its text has its edges midway between the text's rows, where no rule stands.</summary>
    private static List<PageContentScan.Rule> SnapRules(List<PageContentScan.Rule> rules, List<double> colX, List<double> rowY)
    {
        var snapped = new List<PageContentScan.Rule>();
        if (colX.Count < 2 || rowY.Count < 2) return snapped;
        foreach (var rule in rules)
        {
            var (edges, across) = rule.Horizontal ? (rowY, colX) : (colX, rowY);
            if (Math.Min(rule.To, Math.Max(across[0], across[^1])) - Math.Max(rule.From, Math.Min(across[0], across[^1])) <= 0) continue;
            var k = Enumerable.Range(0, edges.Count).OrderBy(i => Math.Abs(edges[i] - rule.At)).First();
            var beside = Math.Min(k > 0 ? Math.Abs(edges[k] - edges[k - 1]) : double.MaxValue, k < edges.Count - 1 ? Math.Abs(edges[k + 1] - edges[k]) : double.MaxValue);
            if (Math.Abs(edges[k] - rule.At) <= Math.Max(BorderReach, beside / 2))
                snapped.Add(rule with { At = edges[k] });
        }
        return snapped;
    }

    /// <summary>State where a table cell is ruled: /BorderStyle Solid with a /BorderThickness per edge
    /// (before, after, start, end: top, bottom, left, right), 0 for an edge no rule runs along; /BorderStyle
    /// None for a cell no rule touches.</summary>
    // A cell is shaded when fills of one colour cover this share of it; a fill that is no shade is lighter than this in
    // every channel (white, the paper's).
    private const double ShadedShare = 0.8;
    private const double PaperShade = 0.98;

    // Text ending or starting within this many of its size of where two fills meet runs on across them.
    private const double RunsToEdgeEms = 1.0;

    /// <summary>The edges of the shades filled inside a table's region, as rules not drawn: fills of one colour meeting
    /// edge to edge are one shade (a band painted in pieces), parted by no edge between them - unless a rule of the page
    /// is drawn where they meet (a column's edge, drawn in the rows under a head row shaded cell by cell) and no text of
    /// <paramref name="lines"/> runs up to the meeting edge (a worksheet row's leader dots run on to its amount's shade):
    /// then each is a shade of its own; a null region: the page's.</summary>
    private static IEnumerable<PageContentScan.Rule> ShadeEdges(PageWork pw, Aspose.Pdf.Rectangle? table, List<Line> lines)
    {
        bool RuledAt(bool horizontal, double at) => pw.Rules.Any(r => r.Horizontal == horizontal && r.Drawn && Math.Abs(r.At - at) <= GridLineTolerance
                                                                      && (table is null || (r.From <= table.URX && r.To >= table.LLX)));
        bool RunsTo(double x, double bottom, double top) => lines.Any(l => l.Baseline >= bottom && l.Baseline <= top
            && l.Frags.Any(f => !string.IsNullOrWhiteSpace(f.Text) && (Math.Abs(f.R - x) <= RunsToEdgeEms * f.Size || Math.Abs(f.X - x) <= RunsToEdgeEms * f.Size)));
        bool PartedAcross(double x, double bottom, double top) => RuledAt(false, x) && !RunsTo(x, bottom, top);
        var shades = new List<(string Colour, double L, double B, double R, double T)>();
        foreach (var o in pw.Ops.Where(o => o.Kind == ContentOpKind.PathPaint && o.Fill is { } f && f.Any(v => v < PaperShade)
                                            && (table is null || (o.Llx >= table.LLX - GridLineTolerance && o.Urx <= table.URX + GridLineTolerance
                                                                  && o.Lly >= table.LLY - GridLineTolerance && o.Ury <= table.URY + GridLineTolerance))))
        {
            var shade = (Colour: string.Join(",", o.Fill!.Select(v => Math.Round(v, 3))), L: o.Llx, B: o.Lly, R: o.Urx, T: o.Ury);
            // Join the shades of its colour it meets along a whole side, again and again.
            for (var joined = true; joined;)
            {
                joined = false;
                for (var k = 0; k < shades.Count; k++)
                {
                    var s = shades[k];
                    if (s.Colour != shade.Colour) continue;
                    bool Near(double a, double b) => Math.Abs(a - b) <= GridLineTolerance;
                    var (bottom, top) = (Math.Max(s.B, shade.B), Math.Min(s.T, shade.T));
                    var across = Near(s.B, shade.B) && Near(s.T, shade.T)
                                 && (Near(s.R, shade.L) && !PartedAcross(shade.L, bottom, top) || Near(s.L, shade.R) && !PartedAcross(shade.R, bottom, top));
                    var down = Near(s.L, shade.L) && Near(s.R, shade.R)
                               && (Near(s.T, shade.B) && !RuledAt(true, shade.B) || Near(s.B, shade.T) && !RuledAt(true, shade.T));
                    if (!across && !down) continue;
                    shade = (shade.Colour, Math.Min(s.L, shade.L), Math.Min(s.B, shade.B), Math.Max(s.R, shade.R), Math.Max(s.T, shade.T));
                    shades.RemoveAt(k);
                    joined = true;
                    break;
                }
            }
            shades.Add(shade);
        }
        return shades.Where(s => s.R - s.L >= MinRuleLength && s.T - s.B >= MinRuleLength).SelectMany(s => new[]
        {
            new PageContentScan.Rule(false, s.L, s.B, s.T, Drawn: false), new PageContentScan.Rule(false, s.R, s.B, s.T, Drawn: false),
            new PageContentScan.Rule(true, s.B, s.L, s.R, Drawn: false), new PageContentScan.Rule(true, s.T, s.L, s.R, Drawn: false),
        });
    }

    /// <summary>State a cell's shade (the Layout /BackgroundColor): the colour the fills inside its table cover most of it
    /// with (a fill reaching outside the table is the page's, no cell's).</summary>
    private static void StateBackground(LS.StructureElement cell, PageWork pw, Aspose.Pdf.Rectangle box, Aspose.Pdf.Rectangle table)
    {
        if (ShadeOf(pw, box, table) is { } fill) StateBackground(cell, fill);
    }

    /// <summary>States a block's shade (the Layout /BackgroundColor).</summary>
    private static void StateBackground(LS.StructureElement element, double[] fill)
    {
        var color = new LS.StructureAttribute(LS.AttributeKey.BackgroundColor);
        color.SetArrayNumberValue(fill.Select(v => (double?)Math.Round(v, 3)).ToArray());
        element.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout).SetAttribute(color);
    }

    /// <summary>The colour the fills standing within <paramref name="within"/> cover most of <paramref name="box"/> with, when
    /// they cover enough of it to shade it; null otherwise.</summary>
    private static double[]? ShadeOf(PageWork pw, Aspose.Pdf.Rectangle box, Aspose.Pdf.Rectangle within)
    {
        var area = box.Width * box.Height;
        if (area <= 0) return null;
        var table = within;
        var shade = pw.Ops.Where(o => o.Kind == ContentOpKind.PathPaint && o.Fill is { } f && f.Any(v => v < PaperShade)
                                      && o.Llx >= table.LLX - GridLineTolerance && o.Urx <= table.URX + GridLineTolerance
                                      && o.Lly >= table.LLY - GridLineTolerance && o.Ury <= table.URY + GridLineTolerance)
            .GroupBy(o => string.Join(",", o.Fill!.Select(v => Math.Round(v, 3))))
            .Select(g => (Fill: g.First().Fill!, Cover: g.Sum(o => Math.Max(0, Math.Min(o.Urx, box.URX) - Math.Max(o.Llx, box.LLX))
                                                                    * Math.Max(0, Math.Min(o.Ury, box.URY) - Math.Max(o.Lly, box.LLY)))))
            .OrderByDescending(x => x.Cover).FirstOrDefault();
        return shade.Fill is null || shade.Cover < ShadedShare * area ? null : shade.Fill;
    }

    private static void StateBorders(LS.StructureElement cell, List<PageContentScan.Rule> rules,
        double left, double right, double top, double bottom)
    {
        bool Ruled(bool horizontal, double at, double from, double to) =>
            rules.Where(r => r.Drawn && r.Horizontal == horizontal && Math.Abs(r.At - at) <= BorderReach)
                .Sum(r => Math.Max(0, Math.Min(r.To, to) - Math.Max(r.From, from))) >= BorderCover * (to - from);
        var sides = new[] { Ruled(true, top, left, right), Ruled(true, bottom, left, right), Ruled(false, left, bottom, top), Ruled(false, right, bottom, top) };
        var layout = cell.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout);
        var style = new LS.StructureAttribute(LS.AttributeKey.BorderStyle);
        style.SetNameValue(sides.Any(s => s) ? LS.AttributeName.BorderStyle_Solid : LS.AttributeName.BorderStyle_None);
        layout.SetAttribute(style);
        if (!sides.Any(s => s)) return;
        var thickness = new LS.StructureAttribute(LS.AttributeKey.BorderThickness);
        thickness.SetArrayNumberValue(sides.Select(s => (double?)(s ? BorderRuleThickness : 0)).ToArray());
        layout.SetAttribute(thickness);
    }

    /// <summary>State a table cell's height, its row's (the Layout /Height), so a reader can
    /// lay the rows out as they stood.</summary>
    private static void StateHeight(LS.StructureElement cell, double height)
    {
        if (height <= 0) return;
        var a = new LS.StructureAttribute(LS.AttributeKey.Height);
        a.SetNumberValue(Math.Round(height, 1));
        cell.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout).SetAttribute(a);
    }

    /// <summary>State a layout as the element's standard Layout attributes (ISO 32000-1
    /// §14.8.5.4): /TextAlign, /SpaceBefore, /StartIndent, /EndIndent and /TextIndent.</summary>
    private static void StateLayout(LS.StructureElement element, ParagraphLayout? layout)
    {
        if (layout is not { } l) return;
        var attributes = element.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout);
        if (l.Align is { } align)
        {
            var a = new LS.StructureAttribute(LS.AttributeKey.TextAlign);
            a.SetNameValue(align);
            attributes.SetAttribute(a);
        }
        if (l.StartIndent != 0)
        {
            var a = new LS.StructureAttribute(LS.AttributeKey.StartIndent);
            a.SetNumberValue(Math.Round(l.StartIndent, 1));
            attributes.SetAttribute(a);
        }
        if (l.SpaceBefore > 0)
        {
            var a = new LS.StructureAttribute(LS.AttributeKey.SpaceBefore);
            a.SetNumberValue(l.SpaceBefore);
            attributes.SetAttribute(a);
        }
        if (l.TextIndent != 0)
        {
            var a = new LS.StructureAttribute(LS.AttributeKey.TextIndent);
            a.SetNumberValue(Math.Round(l.TextIndent, 1));
            attributes.SetAttribute(a);
        }
        if (l.EndIndent > 0)
        {
            var a = new LS.StructureAttribute(LS.AttributeKey.EndIndent);
            a.SetNumberValue(l.EndIndent);
            attributes.SetAttribute(a);
        }
    }
}
