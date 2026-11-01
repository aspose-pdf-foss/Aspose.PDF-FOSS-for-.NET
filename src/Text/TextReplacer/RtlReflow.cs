using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>The lines of one paragraph end within this many points of each other on the right.</summary>
    private const double RtlEdgeTolerance = 1.0;

    /// <summary>A line further below than this many ems is not the next line of the paragraph.</summary>
    private const double RtlMaxPitchEms = 2.0;

    /// <summary>The line pitch, in ems, of a one-line paragraph that needs a second line.</summary>
    private const double RtlDefaultPitchEms = 1.2;

    /// <summary>The word space, in ems, put between two lines' words when neither draws one.</summary>
    private const double RtlDefaultSpaceEms = 0.25;

    /// <summary>Runs closer than this, in points, abut; a wider gap is kept as part of the line.</summary>
    private const double RtlAbutTolerance = 0.1;

    /// <summary>A show together with the shows its text object places after it without a Tm of
    /// their own (a mark set off its letter by Td): one unit on the line, moved by a Tm written
    /// before it, and split at its spaces only when it is one show.</summary>
    private sealed class RtlUnit
    {
        public CrossTextOp Head = null!;
        public List<CrossTextOp> Members = new();
        public double X, Right, Y;
        public string Text = "";
    }

    /// <summary>One space-delimited piece of a unit: a word, or the spaces (and the gap) between two.</summary>
    private sealed class RtlPiece
    {
        public RtlUnit? Unit;
        public int Start, End;
        public List<(int byteIndex, double amount)> Kerns = new();
        public double Width, X, Y;
        public bool IsSpace, IsSeat, Rigid;
    }

    /// <summary>
    /// Re-flows the right-to-left paragraph an in-place replacement sits in: the match's
    /// line and the lines below it that end at the same right edge in the same clip box are
    /// broken again at their spaces, in reading order, each line flush with its right edge
    /// and filled leftward to the clip's left edge, a line added below at the paragraph's
    /// own pitch when the words need one; a shorter replacement pulls the next line's words
    /// up. Nothing outside the paragraph moves. Returns the replacement's new extent on its
    /// line, or null when the paragraph is not one this can rewrite (the caller then only
    /// re-seats the line).
    /// </summary>
    internal static (double left, double right, double baseline)? ReflowRightToLeft(
        Page page, double baselineY, double matchX, double oldAdvance)
    {
        var reader = page.Reader;
        if (reader is null || page.RotateDegrees % 360 != 0) return null;
        var streams = GetContentStreams(page, reader);
        if (streams.Count == 0) return null;
        var bytes = CombineStreams(streams);
        var ops = CollectTextOps(bytes, TextAbsorber.ResolveFonts(page.Dict, reader), reader);
        var metrics = new Dictionary<PdfDictionary, FontMetrics?>();
        var units = RtlUnits(ops, metrics, reader);

        var lines = RtlParagraphLines(units, baselineY, matchX, matchX + oldAdvance);
        if (lines is null) return null;
        var pieces = RtlParagraphPieces(lines, metrics, reader, matchX, matchX + oldAdvance);
        var clip = lines[0].units[0].Head.Clip;
        var left = double.IsInfinity(clip.left) ? lines.Min(l => l.units.Min(u => u.X)) : clip.left;
        RtlLayout(pieces, lines, left);
        return RtlRewrite(page, bytes, pieces);
    }

    /// <summary>The upright shows of the page grouped into units, in stream order.</summary>
    private static List<RtlUnit> RtlUnits(List<CrossTextOp> ops, Dictionary<PdfDictionary, FontMetrics?> metrics, PdfReader reader)
    {
        var units = new List<RtlUnit>();
        RtlUnit? cur = null;
        foreach (var o in ops)
        {
            if (o.TmB != 0 || o.TmC != 0 || o.CtmB != 0 || o.CtmC != 0 || o.TmA <= 0 || o.CtmA <= 0 || o.TmD <= 0 || o.CtmD <= 0)
            {
                cur = null;
                continue;
            }
            double x = PageX(o), right = x + RunAdvance(o, metrics, reader);
            if (cur is not null && !o.TmPositioned && o.BtStart == cur.Head.BtStart)
            {
                cur.Members.Add(o);
                cur.X = Math.Min(cur.X, x);
                cur.Right = Math.Max(cur.Right, right);
                cur.Text += o.Text;
                continue;
            }
            cur = new RtlUnit { Head = o, X = x, Right = right, Y = PageY(o), Text = o.Text };
            cur.Members.Add(o);
            units.Add(cur);
        }
        return units;
    }

    /// <summary>The paragraph's lines from the match's line down, each as its units from right to left
    /// with the right edge it is set flush with; null when the match's line cannot be found.</summary>
    private static List<(double y, double right, List<RtlUnit> units)>? RtlParagraphLines(
        List<RtlUnit> units, double baselineY, double matchX, double matchRight)
    {
        var seats = units.Where(u => Math.Abs(u.Y - baselineY) <= RtlLineTolerance && IsSeat(u, matchX, matchRight)).ToList();
        if (seats.Count == 0) return null;
        var em = EmOf(seats[0].Head);
        var clip = seats[0].Head.Clip;
        // Text of the next cell drawn over this one (clipped away) is not part of its line.
        var onLine = units.Where(u => Math.Abs(u.Y - baselineY) <= RtlLineTolerance && SameClip(u.Head.Clip, clip)).ToList();

        // The match's line: the units right of it while they continue the line, the seats, the units left of it.
        var first = new List<RtlUnit>();
        var edge = matchRight;
        foreach (var u in onLine.Where(u => u.X >= matchRight - RtlLineTolerance).OrderBy(u => u.X))
        {
            if (EndsLine(u, u.X - edge, em)) break;
            first.Insert(0, u);
            edge = Math.Max(edge, u.Right);
        }
        first.AddRange(seats.OrderByDescending(u => u.X));
        first.AddRange(UnitsContinuingLeftward(onLine, matchX, em));
        var result = new List<(double y, double right, List<RtlUnit> units)> { (baselineY, edge, first) };

        var ink = InkRight(first, seats, matchRight);
        double y = baselineY, pitch = 0;
        while (true)
        {
            var below = units.Where(u => u.Y < y - RtlLineTolerance && SameClip(u.Head.Clip, clip)
                && u.Right <= edge + RtlEdgeTolerance).ToList();
            if (below.Count == 0) break;
            var nextY = below.Max(u => u.Y);
            var step = y - nextY;
            if (step > RtlMaxPitchEms * em || pitch > 0 && Math.Abs(step - pitch) > RtlLineTolerance) break;
            var line = below.Where(u => Math.Abs(u.Y - nextY) <= RtlLineTolerance).ToList();
            var start = line.Max(u => u.Right);
            var chain = line.Where(u => u.Right >= start - RtlLineTolerance).OrderByDescending(u => u.X).ToList();
            chain.AddRange(UnitsContinuingLeftward(line, chain.Min(u => u.X), em));
            if (chain.All(u => string.IsNullOrWhiteSpace(u.Text))) break;
            if (Math.Abs(InkRight(chain, new(), 0) - ink) > RtlEdgeTolerance) break;
            result.Add((nextY, chain.Max(u => u.Right), chain));
            pitch = step;
            y = nextY;
        }
        return result;
    }

    private static bool IsSeat(RtlUnit u, double matchX, double matchRight) =>
        u.X >= matchX - RtlLineTolerance && u.X < matchRight - RtlLineTolerance && ClipHolds(u.Head, matchX, matchRight);

    /// <summary>Whether the op's clip box shows the middle of the match: text of the next cell shifted
    /// under it (clipped away) is not what the match drew.</summary>
    private static bool ClipHolds(CrossTextOp op, double matchX, double matchRight)
    {
        var mid = (matchX + matchRight) / 2;
        return op.Clip.left <= mid && mid <= op.Clip.right;
    }

    private static double EmOf(CrossTextOp o) => o.FontSize * TmScaleOf(o) * ScaleOf(o);

    /// <summary>A gap of a column's width ends the line, and so does a gap wider than a word space
    /// before something other than right-to-left text or spaces (the next column of a bilingual table).</summary>
    private static bool EndsLine(RtlUnit u, double gap, double em) =>
        gap > RtlColumnGapEms * em
        || gap > RtlRunGapEms * em && !BidiReorderer.ContainsRtl(u.Text) && !string.IsNullOrWhiteSpace(u.Text);

    /// <summary>The units left of <paramref name="lineLeft"/> that continue its line, nearest first.</summary>
    private static List<RtlUnit> UnitsContinuingLeftward(List<RtlUnit> line, double lineLeft, double em)
    {
        var result = new List<RtlUnit>();
        foreach (var u in line.Where(u => u.Right <= lineLeft + RtlLineTolerance && u.X < lineLeft - RtlLineTolerance)
                     .OrderByDescending(u => u.X))
        {
            if (EndsLine(u, lineLeft - u.Right, em)) break;
            lineLeft = Math.Min(lineLeft, u.X);
            result.Add(u);
        }
        return result;
    }

    private static bool SameClip((double left, double bottom, double right, double top) a,
        (double left, double bottom, double right, double top) b) =>
        a.Equals(b)
        || Math.Abs(a.left - b.left) < RtlAbutTolerance && Math.Abs(a.bottom - b.bottom) < RtlAbutTolerance
            && Math.Abs(a.right - b.right) < RtlAbutTolerance && Math.Abs(a.top - b.top) < RtlAbutTolerance;

    /// <summary>The right edge of a line's rightmost unit that draws more than spaces (a seat at the match's old right edge).</summary>
    private static double InkRight(List<RtlUnit> line, List<RtlUnit> seats, double matchRight)
    {
        var ink = double.MinValue;
        foreach (var u in line)
            if (seats.Contains(u)) ink = Math.Max(ink, matchRight);
            else if (!string.IsNullOrWhiteSpace(u.Text)) ink = Math.Max(ink, u.Right);
        return ink;
    }
}
