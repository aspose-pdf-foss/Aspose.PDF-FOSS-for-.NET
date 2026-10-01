using System;
using System.Collections.Generic;
using System.Linq;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    /// <summary>A drawing: a cluster of painted paths and shadings read as one figure, with the
    /// box they cover. Its member operations are marked as the figure's content, as is the
    /// text standing inside the box (its labels); <see cref="Rep"/> names it among the page's figures.</summary>
    private sealed class Drawing
    {
        public Aspose.Pdf.Rectangle Box = new(0, 0, 0, 0);
        public List<int> Ops = [];
        public int Rep;
    }

    // Paths whose boxes come within this many points of each other belong to one drawing (the
    // bars of a chart stand apart; its axes join them).
    private const double DrawingGap = 12.0;
    // A drawing holds at least this many painted paths (a frame round a paragraph, an underline
    // are fewer; a bullet drawn as a square is a drawing only as its line's mark); a path smaller
    // than this many points both ways is a dot, no part of one (a line is: an axis, a rule of a diagram).
    private const int MinDrawingPaints = 6;
    private const double MinPaintSize = 2.0;
    // Fewer paths make a drawing when their outlines hold this many curves: a logo or an emblem
    // is a few shapes filled whole (a frame, a rule or a box has no curves; a rounded frame four).
    private const int MinDrawingCurves = 16;
    // A drawing is at least this many points on each side, and covers less than this share of
    // the page (a page background is larger).
    private const double MinDrawingSide = 24.0;
    private const double MaxDrawingPageShare = 0.6;
    // A line inside a drawing's box running at least this share of its width, in the body size,
    // is prose; this many such lines make the box a framed or shaded text block, no drawing.
    private const double DrawingLabelShare = 0.6;
    private const int ProseLinesInDrawing = 3;
    // A diagram's label is a mark of at most this many characters ("X1", "Ym"): a line of such marks is no prose.
    private const int MaxLabelMarkChars = 3;
    // A drawing overlapping a table's region by this share of its own area is the table's ruling and shading.
    private const double DrawingInTableShare = 0.5;
    // A chart's labels stand beside its paths - the years under the axis, the unit left of it: a
    // short line within this many points of the paths, of at most this many words and no larger
    // than the text, is a label of the drawing. A caption ("Figure 1. ...") is not.
    private const double LabelReach = 30.0;
    private const int MaxLabelWords = 4;
    private static readonly System.Text.RegularExpressions.Regex CaptionStart = new(
        @"^\s*(Fig(ure)?|Table|Chart|Exhibit|Source)(?![A-Za-z])", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Find the page's drawings - clusters of painted paths that are neither ruling
    /// nor a frame nor a background - and list each among the page's figures, its member
    /// operations named by it, the lines inside its box taken out of the page's text as its labels.</summary>
    private static void FindDrawings(PageWork pw, List<Line> rows, double bodySize)
    {
        var rect = pw.Page.GetPageRect(true);
        var pageArea = Math.Max(1, rect.Width * rect.Height);
        var members = new List<int>();
        // Dots - a dashed connector drawn dot by dot - join a drawing's parts, and count toward none of its paths.
        var dots = new HashSet<int>();
        for (var i = 0; i < pw.Ops.Count; i++)
        {
            var op = pw.Ops[i];
            // (paint no reader sees - a text box's white ground on the white page - draws no part of a drawing)
            if (op.Kind is not (ContentOpKind.PathPaint or ContentOpKind.Shading) || op.Unseen) continue;
            var w = op.Urx - op.Llx;
            var h = op.Ury - op.Lly;
            if (w * h >= MaxDrawingPageShare * pageArea) continue;
            if (w < MinPaintSize && h < MinPaintSize) dots.Add(i);
            members.Add(i);
        }
        if (members.Count == 0) return;

        // Union-find over the boxes: two paths within the gap of each other share a drawing.
        var parent = Enumerable.Range(0, members.Count).ToArray();
        int Root(int k) { while (parent[k] != k) k = parent[k] = parent[parent[k]]; return k; }
        for (var a = 0; a < members.Count; a++)
            for (var b = a + 1; b < members.Count; b++)
                if (Near(pw.Ops[members[a]], pw.Ops[members[b]])) parent[Root(a)] = Root(b);
        // Clusters whose boxes stand as near, or side by side in a band of rows a few ems apart (a diagram's columns,
        // a connector left open between them), are one drawing: its parts before any is judged too small to be one.
        for (var joined = true; joined;)
        {
            joined = false;
            var boxes = Enumerable.Range(0, members.Count).GroupBy(Root)
                .Select(g => (Root: g.Key, Box: BoxOf(pw, g.Select(k => members[k])))).ToList();
            for (var a = 0; a < boxes.Count && !joined; a++)
                for (var b = a + 1; b < boxes.Count && !joined; b++)
                    if (SideBySide(boxes[a].Box, boxes[b].Box, bodySize))
                    {
                        parent[boxes[a].Root] = boxes[b].Root;
                        joined = true;
                    }
        }
        foreach (var cluster in Enumerable.Range(0, members.Count).GroupBy(Root))
        {
            var ops = cluster.Select(k => members[k]).OrderBy(k => k).ToList();
            // A cluster of dots alone is no drawing, nor are its dots its paths; what it joins is.
            var paths = ops.Where(k => !dots.Contains(k)).ToList();
            if (paths.Count == 0) continue;
            var box = new Aspose.Pdf.Rectangle(ops.Min(k => pw.Ops[k].Llx), ops.Min(k => pw.Ops[k].Lly), ops.Max(k => pw.Ops[k].Urx), ops.Max(k => pw.Ops[k].Ury));
            // A few small paths standing just before a line's text - a box to tick, a bullet drawn as a square - are a mark
            // of that line, however few and small (MarksLine).
            var mark = box.Width >= MinPaintSize && box.Height >= MinPaintSize
                       && rows.Any(l => l.Running == 0 && MarksLine((box.LLY, box.LLX, box.Width, box.Height, paths[0]), l));
            if (!mark && paths.Count < MinDrawingPaints && paths.Sum(k => pw.Ops[k].Curves) < MinDrawingCurves) continue;
            if (!mark && (box.Width < MinDrawingSide || box.Height < MinDrawingSide) || box.Width * box.Height >= MaxDrawingPageShare * pageArea) continue;
            if (pw.Tables.Any(t => Overlap(box, t.region) >= DrawingInTableShare * box.Width * box.Height)) continue;
            if (IsFrame(pw, paths, box) || (!mark && RulesOneWay(pw, paths))) continue;
            var inside = rows.Where(l => l.Running == 0 && InsideBox(l, box.LLX, box.LLY, box.URX, box.URY)).ToList();
            // (labels across it - "X1 ... Xm" over a diagram's columns, each a mark of a few characters - are no prose)
            var prose = inside.Count(l => Math.Abs(l.Size - bodySize) <= 0.5 && Width(l) >= DrawingLabelShare * box.Width
                                          && l.Frags.Any(f => f.Text.Trim().Length > MaxLabelMarkChars));
            if (prose >= ProseLinesInDrawing) continue;
            // A mark has no labels: the line it marks is text of the page's.
            var labels = mark ? [] : rows.Where(l => l.Running == 0 && !inside.Contains(l) && IsLabelOf(l, box, bodySize, rows)).ToList();
            inside.AddRange(labels);
            foreach (var l in labels)
                box = new Aspose.Pdf.Rectangle(Math.Min(box.LLX, l.MinX), Math.Min(box.LLY, l.Y), Math.Max(box.URX, l.Frags[^1].R), Math.Max(box.URY, l.Y + l.Size));
            var drawing = new Drawing { Box = box, Ops = ops, Rep = ops[0] };
            pw.Drawings.Add(drawing);
            foreach (var k in ops) pw.Ops[k].Drawing = pw.Drawings.Count - 1;
            pw.Figures.Add((box.LLY, box.LLX, box.Width, box.Height, drawing.Rep));
            foreach (var l in inside) rows.Remove(l);
        }
        pw.Figures = pw.Figures.OrderByDescending(f => f.y).ToList();
    }

    // A straight rule is at most this share of its length thick; a frame's rules run along this share of its box's sides.
    private const double RuleThicknessShare = 0.1;
    private const double FrameSideShare = 0.9;
    // A frame's rule is at most this many points thick: its middle stands at most this far in from the frame's box.
    private const double FrameReach = 6.0;

    /// <summary>Whether a cluster of paths is a frame: straight rules only, across and down, running along all four
    /// sides of its box (a box drawn round a picture or text, the lines under links inside it) - no drawing.</summary>
    private static bool IsFrame(PageWork pw, List<int> ops, Aspose.Pdf.Rectangle box)
    {
        var paths = ops.Select(k => pw.Ops[k]).ToList();
        if (paths.Any(p => p.Curves > 0 || Math.Min(p.Urx - p.Llx, p.Ury - p.Lly) > RuleThicknessShare * Math.Max(p.Urx - p.Llx, p.Ury - p.Lly)))
            return false;
        var across = paths.Where(p => p.Urx - p.Llx > p.Ury - p.Lly).ToList();
        var down = paths.Where(p => p.Ury - p.Lly > p.Urx - p.Llx).ToList();
        if (across.Count == 0 || down.Count == 0) return false;
        // How much of a side the outermost rules along it cover; they stand within a rule's thickness of the box's edge
        // (a path's box leaves its stroke's width out: the rules down run past the rules across by half of it).
        static double Middle(double a, double b) => (a + b) / 2;
        bool Side(List<ContentOp> rules, Func<ContentOp, double> at, double edge, Func<ContentOp, (double L, double R)> span, double length)
        {
            var outer = rules.OrderBy(p => Math.Abs(at(p) - edge)).First();
            if (Math.Abs(at(outer) - edge) > FrameReach) return false;
            return Union(rules.Where(p => Math.Abs(at(p) - at(outer)) <= MinPaintSize).Select(span)).Sum(iv => iv.R - iv.L) >= FrameSideShare * length;
        }
        return Side(across, p => Middle(p.Lly, p.Ury), box.LLY, p => (p.Llx, p.Urx), box.Width)
               && Side(across, p => Middle(p.Lly, p.Ury), box.URY, p => (p.Llx, p.Urx), box.Width)
               && Side(down, p => Middle(p.Llx, p.Urx), box.LLX, p => (p.Lly, p.Ury), box.Height)
               && Side(down, p => Middle(p.Llx, p.Urx), box.URX, p => (p.Lly, p.Ury), box.Height);
    }

    /// <summary>Whether a cluster of paths is straight rules only, all running one way (the lines under a column's links, a
    /// rule over its next entry) - ruling, no drawing: a chart's or a diagram's lines run both ways, or curve, or are filled.</summary>
    private static bool RulesOneWay(PageWork pw, List<int> ops)
    {
        var paths = ops.Select(k => pw.Ops[k]).ToList();
        if (paths.Any(p => p.Curves > 0 || Math.Min(p.Urx - p.Llx, p.Ury - p.Lly) > RuleThicknessShare * Math.Max(p.Urx - p.Llx, p.Ury - p.Lly)))
            return false;
        return paths.All(p => p.Urx - p.Llx > p.Ury - p.Lly) || paths.All(p => p.Ury - p.Lly > p.Urx - p.Llx);
    }

    // A line of prose this many of a label's size over it, starting this near (points) where it starts, makes it the
    // last line of that paragraph.
    private const double LabelParagraphPitches = 1.6;
    private const double LabelParagraphAlign = 1.5;

    /// <summary>Whether a line is a label standing beside a drawing's paths.</summary>
    private static bool IsLabelOf(Line line, Aspose.Pdf.Rectangle box, double bodySize, List<Line> rows)
    {
        if (line.Frags.Count == 0 || line.Size > bodySize + 0.5 || Words(line) > MaxLabelWords) return false;
        var text = string.Concat(line.Frags.Select(f => f.Text));
        if (CaptionStart.IsMatch(text)) return false;
        // The last line of a paragraph standing over the drawing is the paragraph's: a line of prose over it, a pitch or
        // two up, starts where it does.
        if (rows.Any(o => o != line && o.Running == 0 && o.Y > line.Y && o.Y - line.Y <= LabelParagraphPitches * Math.Max(line.Size, 1)
                          && Math.Abs(o.MinX - line.MinX) <= LabelParagraphAlign && Words(o) > MaxLabelWords))
            return false;
        double dx = Math.Max(0, Math.Max(box.LLX - line.Frags[^1].R, line.MinX - box.URX));
        double dy = Math.Max(0, Math.Max(box.LLY - (line.Y + line.Size), line.Y - box.URY));
        return dx <= LabelReach && dy <= LabelReach;
    }

    // Parts of a drawing standing side by side in a band of rows - sharing this share of the lower one's height - stand at
    // most this many of the body's size apart across.
    private const double BandShare = 0.5;
    private const double PartReachEms = 2.0;

    private static Aspose.Pdf.Rectangle BoxOf(PageWork pw, IEnumerable<int> ops)
    {
        var list = ops.ToList();
        return new Aspose.Pdf.Rectangle(list.Min(k => pw.Ops[k].Llx), list.Min(k => pw.Ops[k].Lly), list.Max(k => pw.Ops[k].Urx), list.Max(k => pw.Ops[k].Ury));
    }

    /// <summary>Whether two clusters' boxes stand within the drawing gap of each other, or side by side in a band of rows
    /// a few ems apart (a diagram's columns).</summary>
    private static bool SideBySide(Aspose.Pdf.Rectangle a, Aspose.Pdf.Rectangle b, double bodySize)
    {
        var across = Math.Max(0, Math.Max(a.LLX - b.URX, b.LLX - a.URX));
        var down = Math.Max(0, Math.Max(a.LLY - b.URY, b.LLY - a.URY));
        if (across <= DrawingGap && down <= DrawingGap) return true;
        var shared = Math.Min(a.URY, b.URY) - Math.Max(a.LLY, b.LLY);
        return shared >= BandShare * Math.Min(a.Height, b.Height) && Math.Min(a.Height, b.Height) > MinPaintSize
               && across <= PartReachEms * Math.Max(bodySize, 1);
    }

    private static bool Near(ContentOp a, ContentOp b)
        => a.Llx - DrawingGap <= b.Urx && b.Llx - DrawingGap <= a.Urx && a.Lly - DrawingGap <= b.Ury && b.Lly - DrawingGap <= a.Ury;

    private static double Overlap(Aspose.Pdf.Rectangle a, Aspose.Pdf.Rectangle b)
        => Math.Max(0, Math.Min(a.URX, b.URX) - Math.Max(a.LLX, b.LLX)) * Math.Max(0, Math.Min(a.URY, b.URY) - Math.Max(a.LLY, b.LLY));

    /// <summary>Whether a line stands inside a box (a figure's labels).</summary>
    private static bool InsideBox(Line line, double llx, double lly, double urx, double ury)
    {
        if (line.Frags.Count == 0) return false;
        var middle = line.Y + line.Size / 2;
        return line.MinX >= llx - LabelTolerance && line.Frags[^1].R <= urx + LabelTolerance && middle >= lly && middle <= ury;
    }

    /// <summary>The drawing whose box holds the point, or -1.</summary>
    private static int DrawingAt(PageWork pw, double x, double y)
    {
        for (var d = 0; d < pw.Drawings.Count; d++)
        {
            var b = pw.Drawings[d].Box;
            if (x >= b.LLX - LabelTolerance && x <= b.URX + LabelTolerance && y >= b.LLY - LabelTolerance && y <= b.URY + LabelTolerance) return d;
        }
        return -1;
    }
}
