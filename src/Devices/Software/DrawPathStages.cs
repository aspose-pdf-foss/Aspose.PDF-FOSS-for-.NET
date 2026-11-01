using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>Strokes the path: the stroke colour and device line width, the dash pattern in device pixels with its phase, then every segment (lines, closes, cubics flattened, rectangles) through the dash-aware emitter.</summary>
    private static void StrokePath(DrawPathState dp)
    {
        dp.r = (byte)(dp.state.StrokeR * 255);
        dp.g = (byte)(dp.state.StrokeG * 255);
        dp.b = (byte)(dp.state.StrokeB * 255);
        // The stroking constant alpha (ExtGState /CA, PDF 32000 §11.6.4.4) applies to every
        // stroke. The per-segment emitter used to paint at 255 whatever the state said, so a
        // chart frame set at CA 0.2 came out as a solid gray rule.
        dp.strokeA = (byte)Math.Round(Compat.Clamp(dp.state.StrokeAlpha, 0, 1) * 255);
        // A pen wide enough to show its caps and joins is drawn as the region it covers;
        // a hairline keeps the per-segment emitter and its pixel-grid hinting.
        if (TryStrokeOutline(dp)) return;
        dp.ctmScale = Math.Sqrt(Math.Abs(dp.ctm[0] * dp.ctm[3] - dp.ctm[1] * dp.ctm[2]));
        dp.lw = dp.state.LineWidth * dp.ctmScale * dp.ctx.Scale;
        if (dp.lw < 1) dp.lw = 1;

        dp.dashPx = null;
        dp.dashOn = true;
        dp.dashIdx = 0;
        dp.dashPos = 0.0;
        if (dp.state.DashArray is { Length: > 0 } da)
        {
            dp.dashPx = new double[da.Length];
            var total = 0.0;
            for (var di = 0; di < da.Length; di++)
            {
                var el = Math.Max(da[di], dp.state.LineWidth) * dp.ctmScale * dp.ctx.Scale;
                dp.dashPx[di] = Math.Max(el, 1.0);
                total += dp.dashPx[di];
            }
            if (total <= 0) dp.dashPx = null;
            else
            {
                // Consume the phase cyclically to find the starting element and the
                // offset inside it; the pattern starts ON at element 0 (§8.4.3.6).
                var phase = Math.Max(0, dp.state.DashPhase) * dp.ctmScale * dp.ctx.Scale % total;
                while (phase >= dp.dashPx[dp.dashIdx])
                {
                    phase -= dp.dashPx[dp.dashIdx];
                    dp.dashIdx = (dp.dashIdx + 1) % dp.dashPx.Length;
                    dp.dashOn = !dp.dashOn;
                }
                dp.dashPos = phase;
            }
        }

        StrokePathSegments(dp);
        StrokeDegenerateDots(dp);
    }

    /// <summary>The fewest device pixels across a degenerate subpath's dot is drawn, as the
    /// hairline emitter draws its lines at least a pixel wide.</summary>
    private const double MinimumDotPx = 1.0;

    /// <summary>Paint the mark a round or projecting cap leaves where a subpath has no length
    /// (see <see cref="PenGeometry.DegenerateSubpathPoints"/>): the hairline emitter draws
    /// nothing along a segment of no length, so the i-dots and colons of thin stroked
    /// lettering vanished. The pen's outline path paints these itself.</summary>
    private static void StrokeDegenerateDots(DrawPathState dp)
    {
        if (PenGeometry.DegenerateSubpathPoints(dp.segments, dp.state.LineCap) is not { } dots) return;
        var pen = PenMap.For(dp.ctm, dp.ctx.Scale);
        if (!(pen.MaxStretch > 0)) return;
        var half = Math.Max(dp.state.LineWidth, MinimumDotPx / pen.MaxStretch) / 2;
        var spec = new StrokeSpec { Half = half, Cap = dp.state.LineCap, ArcSteps = ArcStepsFor(half * pen.MaxStretch) };
        var et = new EdgeTable();
        foreach (var (x, y) in dots)
            foreach (var polygon in Dot(pen.ToPen(Transform(dp, x, y)), spec))
                AddPenPolygon(et, polygon, pen);
        ScanlineFiller.Fill(et, dp.ctx.Pixels, dp.ctx.PixelW, dp.ctx.PixelH,
            dp.r, dp.g, dp.b, dp.strokeA, false, dp.clip, dp.ctx.CurrentBlendMode,
            knockout: dp.ctx.IsKnockoutGroup, softMask: dp.ctx.SoftMaskAlpha);
    }

    /// <summary>The least share of a pixel a sub-pixel fill is drawn with.</summary>
    private const double SubPixelFillCoverageFloor = 0.125;

    /// <summary>
    /// A flat fill thinner than one device pixel - a 0.12 pt table rule - draws as a bar at
    /// least one pixel across whose alpha is its geometric coverage (floored so a vanishing
    /// rule stays faintly visible): the law the GDI+ renderer applies. The scanline filler
    /// samples four sub-scanlines per row, so such a rule caught one of them or none and
    /// came out either a quarter-strength line or nothing - never its true coverage.
    /// Only a plain fill qualifies (no pattern, soft mask or blend mode), as there.
    /// </summary>
    private static bool TryFillSubPixelBar(DrawPathState dp)
    {
        if (dp.ctx.SoftMaskAlpha is not null || !string.Equals(dp.ctx.CurrentBlendMode, "Normal", StringComparison.Ordinal))
            return false;
        var (lx, ly, hx, hy) = PathDeviceBounds(dp.segments, dp.ctm, dp.ctx);
        double w = hx - lx, h = hy - ly;
        if (!(w > 0 && h > 0) || Math.Min(w, h) >= 1) return false;
        // A path of several subpaths - a row of dots - is thin as a whole, but its dots are its marks:
        // each draws its own bar, not one bar over the whole row, which read as a solid rule.
        var parts = Subpaths(dp.segments);
        if (parts.Count == 1)
        {
            FillSubPixelBar(dp, lx, ly, w, h);
            return true;
        }
        foreach (var part in parts)
        {
            var (px0, py0, px1, py1) = PathDeviceBounds(part, dp.ctm, dp.ctx);
            FillSubPixelBar(dp, px0, py0, px1 - px0, py1 - py0);
        }
        return true;
    }

    /// <summary>One bar at least a pixel across at (<paramref name="lx"/>, <paramref name="ly"/>), its alpha the
    /// geometric coverage of a <paramref name="w"/> by <paramref name="h"/> box, floored; nothing for an empty box.</summary>
    private static void FillSubPixelBar(DrawPathState dp, double lx, double ly, double w, double h)
    {
        if (!(w > 0 && h > 0)) return;
        var coverage = Math.Max(Math.Min(Math.Min(w, h), 1), SubPixelFillCoverageFloor);
        var bw = Math.Max(w, 1);
        var bh = Math.Max(h, 1);
        var et = new EdgeTable();
        et.AddLine(lx, ly, lx + bw, ly);
        et.AddLine(lx + bw, ly, lx + bw, ly + bh);
        et.AddLine(lx + bw, ly + bh, lx, ly + bh);
        et.AddLine(lx, ly + bh, lx, ly);
        var a = (byte)Math.Round(Compat.Clamp(dp.state.FillAlpha * coverage, 0, 1) * 255);
        ScanlineFiller.Fill(et, dp.ctx.Pixels, dp.ctx.PixelW, dp.ctx.PixelH,
            (byte)(dp.state.FillR * 255), (byte)(dp.state.FillG * 255), (byte)(dp.state.FillB * 255), a,
            false, dp.clip, dp.ctx.CurrentBlendMode, knockout: dp.ctx.IsKnockoutGroup);
    }

    /// <summary>The path's subpaths: each opens at a move or a rectangle and runs to the next one.</summary>
    private static List<IReadOnlyList<PathCommand>> Subpaths(IReadOnlyList<PathCommand> segments)
    {
        var parts = new List<IReadOnlyList<PathCommand>>();
        List<PathCommand>? current = null;
        foreach (var seg in segments)
        {
            if (current is null || seg.Op is PathOp.MoveTo or PathOp.Rect)
            {
                current = new List<PathCommand>();
                parts.Add(current);
            }
            current.Add(seg);
        }
        return parts;
    }

    /// <summary>Fills the path interior: a tiling or shading pattern by name, else the flat fill colour through the scanline filler under the clip, blend mode, knockout and soft mask.</summary>
    private static void FillPathInterior(DrawPathState dp)
    {
        if (dp.state.FillPatternName is { } patName)
        {
            // Pattern fill: build a stencil from the path and paint the pattern's
            // content stream through it instead of a solid RGBA blit.
            FillWithPattern(dp.ctx, dp.edgeTable, dp.evenOdd, patName, dp.state);
        }
        else if (!TryFillSubPixelBar(dp))
        {
            var r = (byte)(dp.state.FillR * 255);
            var g = (byte)(dp.state.FillG * 255);
            var b = (byte)(dp.state.FillB * 255);
            ScanlineFiller.Fill(dp.edgeTable, dp.ctx.Pixels, dp.ctx.PixelW, dp.ctx.PixelH,
                r, g, b, (byte)(dp.state.FillAlpha * 255), dp.evenOdd, dp.clip, dp.ctx.CurrentBlendMode,
                knockout: dp.ctx.IsKnockoutGroup, softMask: dp.ctx.SoftMaskAlpha);
        }
    }
}
