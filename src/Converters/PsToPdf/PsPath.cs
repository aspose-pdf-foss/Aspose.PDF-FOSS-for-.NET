using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>What a path segment does.</summary>
internal enum PsSegmentKind
{
    /// <summary>Start a new subpath at the point.</summary>
    Move,

    /// <summary>Straight line to the point.</summary>
    Line,

    /// <summary>Cubic Bezier through two controls to the point.</summary>
    Curve,

    /// <summary>Close the current subpath.</summary>
    Close,
}

/// <summary>One segment of a path, with its points already in device space.</summary>
internal readonly struct PsSegment
{
    /// <summary>Build a segment.</summary>
    public PsSegment(PsSegmentKind kind, double x, double y,
        double x1 = 0, double y1 = 0, double x2 = 0, double y2 = 0)
    {
        Kind = kind;
        X = x;
        Y = y;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    /// <summary>What this segment does.</summary>
    public PsSegmentKind Kind { get; }

    /// <summary>End-point x.</summary>
    public double X { get; }

    /// <summary>End-point y.</summary>
    public double Y { get; }

    /// <summary>First control-point x.</summary>
    public double X1 { get; }

    /// <summary>First control-point y.</summary>
    public double Y1 { get; }

    /// <summary>Second control-point x.</summary>
    public double X2 { get; }

    /// <summary>Second control-point y.</summary>
    public double Y2 { get; }
}

/// <summary>
/// The current path. Points are stored in DEVICE space — the CTM is applied as each
/// segment is added, exactly as PostScript specifies, so that a path built across
/// several different transformations keeps the shape the program intended.
/// </summary>
internal sealed class PsPath
{
    /// <summary>How many straight pieces a flattened Bezier is cut into per unit of
    /// its device-space extent. Chosen so a full-page curve stays visually smooth at
    /// the resolutions the corpus renders at.</summary>
    private const double FlattenStepsPerPoint = 0.4;

    /// <summary>The fewest pieces a flattened curve is ever cut into.</summary>
    private const int MinFlattenSteps = 4;

    /// <summary>The most pieces a flattened curve is ever cut into.</summary>
    private const int MaxFlattenSteps = 200;

    private readonly List<PsSegment> _segments = new();

    /// <summary>The segments, in order.</summary>
    public IReadOnlyList<PsSegment> Segments => _segments;

    /// <summary>Whether nothing has been added.</summary>
    public bool IsEmpty => _segments.Count == 0;

    /// <summary>The current point in device space, or false when there is none.</summary>
    public bool HasCurrentPoint { get; private set; }

    /// <summary>Device-space x of the current point.</summary>
    public double CurrentX { get; private set; }

    /// <summary>Device-space y of the current point.</summary>
    public double CurrentY { get; private set; }

    /// <summary>Device-space x where the current subpath started.</summary>
    public double StartX { get; private set; }

    /// <summary>Device-space y where the current subpath started.</summary>
    public double StartY { get; private set; }

    /// <summary>Begin a subpath.</summary>
    public void MoveTo(double x, double y)
    {
        _segments.Add(new PsSegment(PsSegmentKind.Move, x, y));
        CurrentX = StartX = x;
        CurrentY = StartY = y;
        HasCurrentPoint = true;
    }

    /// <summary>Add a straight segment.</summary>
    public void LineTo(double x, double y)
    {
        if (!HasCurrentPoint) throw new PsErrorSignal("nocurrentpoint");
        _segments.Add(new PsSegment(PsSegmentKind.Line, x, y));
        CurrentX = x;
        CurrentY = y;
    }

    /// <summary>Add a cubic Bezier.</summary>
    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (!HasCurrentPoint) throw new PsErrorSignal("nocurrentpoint");
        _segments.Add(new PsSegment(PsSegmentKind.Curve, x3, y3, x1, y1, x2, y2));
        CurrentX = x3;
        CurrentY = y3;
    }

    /// <summary>Close the current subpath, returning the point to its start.</summary>
    public void ClosePath()
    {
        if (!HasCurrentPoint) return;
        _segments.Add(new PsSegment(PsSegmentKind.Close, StartX, StartY));
        CurrentX = StartX;
        CurrentY = StartY;
    }

    /// <summary>Discard everything.</summary>
    public void Clear()
    {
        _segments.Clear();
        HasCurrentPoint = false;
    }

    /// <summary>A copy that shares nothing.</summary>
    public PsPath Clone()
    {
        var copy = new PsPath();
        copy._segments.AddRange(_segments);
        copy.HasCurrentPoint = HasCurrentPoint;
        copy.CurrentX = CurrentX;
        copy.CurrentY = CurrentY;
        copy.StartX = StartX;
        copy.StartY = StartY;
        return copy;
    }

    /// <summary>Append another path's segments to this one.</summary>
    public void Append(PsPath other)
    {
        if (other is null) return;
        _segments.AddRange(other._segments);
        HasCurrentPoint = other.HasCurrentPoint || HasCurrentPoint;
        if (other.HasCurrentPoint)
        {
            CurrentX = other.CurrentX;
            CurrentY = other.CurrentY;
            StartX = other.StartX;
            StartY = other.StartY;
        }
    }

    /// <summary>A copy with every subpath running the other way, which is what
    /// <c>reversepath</c> leaves behind. A program flowing text along a path reverses
    /// it so the letters face outwards rather than inwards.</summary>
    public PsPath Reversed()
    {
        var reversed = new PsPath();
        var start = 0;
        while (start < _segments.Count)
        {
            var end = start + 1;
            while (end < _segments.Count && _segments[end].Kind != PsSegmentKind.Move) end++;
            AppendReversedRun(reversed, start, end);
            start = end;
        }

        return reversed;
    }

    /// <summary>Write one subpath backwards: the last point becomes the first, and each
    /// piece is re-aimed at the point that used to precede it, its controls swapped.</summary>
    private void AppendReversedRun(PsPath into, int start, int end)
    {
        var closed = end > start && _segments[end - 1].Kind == PsSegmentKind.Close;
        var last = closed ? end - 2 : end - 1;
        if (last < start) return;
        into.MoveTo(_segments[last].X, _segments[last].Y);
        for (var k = last; k > start; k--)
        {
            var s = _segments[k];
            var previous = _segments[k - 1];
            if (s.Kind == PsSegmentKind.Curve)
                into.CurveTo(s.X2, s.Y2, s.X1, s.Y1, previous.X, previous.Y);
            else into.LineTo(previous.X, previous.Y);
        }

        if (closed) into.ClosePath();
    }

    /// <summary>The bounding box of every point in the path, controls included, or
    /// false when the path is empty. This is the box <c>pathbbox</c> reports once the
    /// result is mapped back to user space.</summary>
    public bool TryGetBounds(out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = minY = double.MaxValue;
        maxX = maxY = double.MinValue;
        var any = false;
        for (var k = 0; k < _segments.Count; k++)
        {
            var s = _segments[k];
            if (s.Kind == PsSegmentKind.Close) continue;
            // A move that begins nothing encloses nothing: the point a program sets
            // before asking a glyph for its outline is not part of the outline's box.
            if (s.Kind == PsSegmentKind.Move &&
                (k + 1 >= _segments.Count || _segments[k + 1].Kind == PsSegmentKind.Move))
                continue;
            any = true;
            Include(s.X, s.Y, ref minX, ref minY, ref maxX, ref maxY);
            if (s.Kind != PsSegmentKind.Curve) continue;
            Include(s.X1, s.Y1, ref minX, ref minY, ref maxX, ref maxY);
            Include(s.X2, s.Y2, ref minX, ref minY, ref maxX, ref maxY);
        }

        return any;
    }

    /// <summary>The length of the curve's control polygon, which stands in for how far
    /// it travels.</summary>
    private static double Extent(double x0, double y0, PsSegment s) =>
        Math.Abs(s.X1 - x0) + Math.Abs(s.Y1 - y0) +
        Math.Abs(s.X2 - s.X1) + Math.Abs(s.Y2 - s.Y1) +
        Math.Abs(s.X - s.X2) + Math.Abs(s.Y - s.Y2);

    /// <summary>How many straight pieces keep a curve within the flatness asked for.
    /// A cubic's distance from the chords of an n-piece cut is bounded by three
    /// eighths of its largest second difference over n squared, and that bound is what
    /// this inverts.</summary>
    private static int StepsWithin(double x0, double y0, PsSegment s, double flatness)
    {
        var ax = x0 - 2 * s.X1 + s.X2;
        var ay = y0 - 2 * s.Y1 + s.Y2;
        var bx = s.X1 - 2 * s.X2 + s.X;
        var by = s.Y1 - 2 * s.Y2 + s.Y;
        var second = Math.Sqrt(Math.Max(ax * ax + ay * ay, bx * bx + by * by));
        return (int)Math.Ceiling(Math.Sqrt(SecondDifferenceShare * second / flatness));
    }

    /// <summary>The share of a cubic's largest second difference that its distance
    /// from an n-piece cut is bounded by, once the n squared is divided out: six
    /// eighths, since the second derivative of a cubic is six times that difference.
    /// The bound and not the true error, which is what an implementation can afford to
    /// compute.</summary>
    private const double SecondDifferenceShare = 0.75;

    private static void Include(double x, double y,
        ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }

    /// <summary>The flatness that cuts a curve as finely as the geometry deserves,
    /// used where the result is measured rather than walked.</summary>
    public const double FineFlatness = 0;

    /// <summary>A copy with every curve replaced by straight segments, which is what
    /// <c>flattenpath</c> leaves behind and what <c>pathforall</c> then walks. The
    /// FLATNESS is how far a straight piece may stray from the curve it replaces: a
    /// program that walks the result and does something once per piece — one character
    /// of a string per segment around a circle — counts on getting the few pieces the
    /// tolerance allows and not the many a fine cut would give.</summary>
    public PsPath Flatten(double flatness = FineFlatness)
    {
        var flat = new PsPath();
        double px = 0, py = 0;
        foreach (var s in _segments)
        {
            switch (s.Kind)
            {
                case PsSegmentKind.Move:
                    flat.MoveTo(s.X, s.Y);
                    break;
                case PsSegmentKind.Line:
                    flat.LineTo(s.X, s.Y);
                    break;
                case PsSegmentKind.Close:
                    flat.ClosePath();
                    break;
                default:
                    FlattenCurve(flat, px, py, s, flatness);
                    break;
            }

            px = s.X;
            py = s.Y;
        }

        return flat;
    }

    private static void FlattenCurve(PsPath into, double x0, double y0, PsSegment s,
        double flatness)
    {
        var steps = flatness > 0
            ? StepsWithin(x0, y0, s, flatness)
            : (int)Math.Round(Extent(x0, y0, s) * FlattenStepsPerPoint);
        steps = Math.Min(MaxFlattenSteps, Math.Max(MinFlattenSteps, steps));
        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var u = 1 - t;
            var x = u * u * u * x0 + 3 * u * u * t * s.X1 + 3 * u * t * t * s.X2 + t * t * t * s.X;
            var y = u * u * u * y0 + 3 * u * u * t * s.Y1 + 3 * u * t * t * s.Y2 + t * t * t * s.Y;
            into.LineTo(x, y);
        }
    }
}
