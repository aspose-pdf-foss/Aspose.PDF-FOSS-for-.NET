using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Turns a parsed glyph outline into path commands. TrueType contours are quadratic
/// and may omit an on-curve point between two control points, in which case the
/// implied point is their midpoint; each quadratic is raised to the cubic form PDF
/// and PostScript both write.
/// </summary>
internal static class PsGlyphOutline
{
    /// <summary>Emit a glyph's contours as PDF path operators, scaled by
    /// <paramref name="scale"/> and with y negated when
    /// <paramref name="flipY"/> is set — which is what a font matrix with a negative
    /// vertical term expects the stored outline to be.</summary>
    public static void AppendToContent(StringBuilder content, GlyphOutline outline,
        double scale, bool flipY)
    {
        if (outline is null) return;
        var sign = flipY ? -1.0 : 1.0;
        foreach (var contour in outline.Contours)
            AppendContour(content, contour, scale, sign);
    }

    /// <summary>Append a glyph's contours to a device-space path, mapping every point
    /// through <paramref name="place"/>.</summary>
    public static void AppendToPath(PsPath path, GlyphOutline outline,
        Func<double, double, (double X, double Y)> place)
    {
        if (outline is null || path is null) return;
        foreach (var contour in outline.Contours)
            AppendContourToPath(path, contour, place);
    }

    /// <summary>The on-curve points of one contour, in order, with the implied
    /// midpoints filled in and each segment tagged with its control point.</summary>
    private static List<Segment> Resolve(ContourPoint[] contour)
    {
        var segments = new List<Segment>();
        if (contour is null || contour.Length == 0) return segments;
        var start = FirstOnCurve(contour);
        if (start is null) return segments;
        segments.Add(new Segment(start.Value, null));
        var n = contour.Length;
        var startIndex = StartIndex(contour);
        ContourPoint? pendingControl = null;
        for (var step = 1; step <= n; step++)
        {
            var p = contour[(startIndex + step) % n];
            if (p.OnCurve)
            {
                segments.Add(new Segment((p.X, p.Y), pendingControl));
                pendingControl = null;
                continue;
            }

            if (pendingControl is null)
            {
                pendingControl = p;
                continue;
            }

            // Two controls in a row imply an on-curve point halfway between them.
            var mid = ((pendingControl.Value.X + p.X) / 2, (pendingControl.Value.Y + p.Y) / 2);
            segments.Add(new Segment(mid, pendingControl));
            pendingControl = p;
        }

        segments.Add(new Segment(start.Value, pendingControl));
        return segments;
    }

    private static (double X, double Y)? FirstOnCurve(ContourPoint[] contour)
    {
        foreach (var p in contour)
            if (p.OnCurve)
                return (p.X, p.Y);
        // An all-control contour starts at the midpoint of its last and first points.
        var a = contour[contour.Length - 1];
        var b = contour[0];
        return ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }

    private static int StartIndex(ContourPoint[] contour)
    {
        for (var i = 0; i < contour.Length; i++)
            if (contour[i].OnCurve)
                return i;
        return contour.Length - 1;
    }

    private static void AppendContour(StringBuilder content, ContourPoint[] contour,
        double scale, double sign)
    {
        var segments = Resolve(contour);
        if (segments.Count < 2) return;
        var first = segments[0].End;
        Move(content, first.X * scale, first.Y * scale * sign);
        var current = first;
        for (var i = 1; i < segments.Count; i++)
        {
            var s = segments[i];
            if (s.Control is null)
                Line(content, s.End.X * scale, s.End.Y * scale * sign);
            else
                Curve(content, current, s.Control.Value, s.End, scale, sign);
            current = s.End;
        }

        content.Append("h\n");
    }

    private static void AppendContourToPath(PsPath path, ContourPoint[] contour,
        Func<double, double, (double X, double Y)> place)
    {
        var segments = Resolve(contour);
        if (segments.Count < 2) return;
        var first = place(segments[0].End.X, segments[0].End.Y);
        path.MoveTo(first.X, first.Y);
        var current = segments[0].End;
        for (var i = 1; i < segments.Count; i++)
        {
            var s = segments[i];
            if (s.Control is null)
            {
                var p = place(s.End.X, s.End.Y);
                path.LineTo(p.X, p.Y);
            }
            else
            {
                var c = Cubic(current, s.Control.Value, s.End);
                var c1 = place(c.C1X, c.C1Y);
                var c2 = place(c.C2X, c.C2Y);
                var e = place(s.End.X, s.End.Y);
                path.CurveTo(c1.X, c1.Y, c2.X, c2.Y, e.X, e.Y);
            }

            current = s.End;
        }

        path.ClosePath();
    }

    /// <summary>A quadratic raised to a cubic: each control sits two thirds of the way
    /// from its end point towards the quadratic control.</summary>
    private static (double C1X, double C1Y, double C2X, double C2Y) Cubic(
        (double X, double Y) from, ContourPoint control, (double X, double Y) to)
    {
        const double TwoThirds = 2.0 / 3.0;
        return (from.X + TwoThirds * (control.X - from.X),
            from.Y + TwoThirds * (control.Y - from.Y),
            to.X + TwoThirds * (control.X - to.X),
            to.Y + TwoThirds * (control.Y - to.Y));
    }

    private static void Curve(StringBuilder content, (double X, double Y) from,
        ContourPoint control, (double X, double Y) to, double scale, double sign)
    {
        var c = Cubic(from, control, to);
        content.Append(N(c.C1X * scale)).Append(' ').Append(N(c.C1Y * scale * sign)).Append(' ')
            .Append(N(c.C2X * scale)).Append(' ').Append(N(c.C2Y * scale * sign)).Append(' ')
            .Append(N(to.X * scale)).Append(' ').Append(N(to.Y * scale * sign)).Append(" c\n");
    }

    private static void Move(StringBuilder content, double x, double y) =>
        content.Append(N(x)).Append(' ').Append(N(y)).Append(" m\n");

    private static void Line(StringBuilder content, double x, double y) =>
        content.Append(N(x)).Append(' ').Append(N(y)).Append(" l\n");

    private static string N(double value) => PsPageWriter.N(value);

    private readonly struct Segment
    {
        public Segment((double X, double Y) end, ContourPoint? control)
        {
            End = end;
            Control = control;
        }

        public (double X, double Y) End { get; }

        public ContourPoint? Control { get; }
    }
}
