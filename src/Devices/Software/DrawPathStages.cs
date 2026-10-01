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
        if (dp.ctx.SoftMaskAlpha is not null || !string.Equals(dp.state.BlendMode, "Normal", StringComparison.Ordinal))
            return false;
        var (lx, ly, hx, hy) = PathDeviceBounds(dp.segments, dp.ctm, dp.ctx);
        double w = hx - lx, h = hy - ly;
        if (!(w > 0 && h > 0) || Math.Min(w, h) >= 1) return false;
        var coverage = Math.Max(Math.Min(w, h), SubPixelFillCoverageFloor);
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
            false, dp.clip, dp.state.BlendMode, knockout: dp.ctx.IsKnockoutGroup);
        return true;
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
                r, g, b, (byte)(dp.state.FillAlpha * 255), dp.evenOdd, dp.clip, dp.state.BlendMode,
                knockout: dp.ctx.IsKnockoutGroup, softMask: dp.ctx.SoftMaskAlpha);
        }
    }
}
