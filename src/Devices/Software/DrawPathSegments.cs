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
    /// <summary>Strokes every segment of the path through the dash-aware emitter, which carries the dash position across segments: lines, closes, cubics flattened, and rectangles as four edges.</summary>
    private static void StrokePathSegments(DrawPathState dp)
    {
        // Every stroked segment of the path goes through here, so the dash walk keeps
        // its position ACROSS segments - a polyline dashes on around its corners
        // instead of restarting at every vertex.
        void EmitStroke(double ex0, double ey0, double ex1, double ey1)
        {
            if (dp.dashPx is null)
            {
                ScanlineFiller.StrokeLine(dp.ctx.Pixels, dp.ctx.PixelW, dp.ctx.PixelH,
                    ex0, ey0, ex1, ey1, dp.r, dp.g, dp.b, dp.strokeA, dp.lw, dp.clip,
                    blendMode: dp.ctx.CurrentBlendMode, knockout: dp.ctx.IsKnockoutGroup, softMask: dp.ctx.SoftMaskAlpha);
                return;
            }
            var segDx = ex1 - ex0; var segDy = ey1 - ey0;
            var segLen = Math.Sqrt(segDx * segDx + segDy * segDy);
            if (segLen <= 1e-12) return;
            var ux = segDx / segLen; var uy = segDy / segLen;
            var t = 0.0;
            while (t < segLen)
            {
                var take = Math.Min(dp.dashPx[dp.dashIdx] - dp.dashPos, segLen - t);
                if (dp.dashOn && take > 0)
                    ScanlineFiller.StrokeLine(dp.ctx.Pixels, dp.ctx.PixelW, dp.ctx.PixelH,
                        ex0 + ux * t, ey0 + uy * t, ex0 + ux * (t + take), ey0 + uy * (t + take),
                        dp.r, dp.g, dp.b, dp.strokeA, dp.lw, dp.clip,
                        blendMode: dp.ctx.CurrentBlendMode, knockout: dp.ctx.IsKnockoutGroup, softMask: dp.ctx.SoftMaskAlpha);
                t += take;
                dp.dashPos += take;
                if (dp.dashPos >= dp.dashPx[dp.dashIdx] - 1e-9)
                {
                    dp.dashIdx = (dp.dashIdx + 1) % dp.dashPx.Length;
                    dp.dashPos = 0;
                    dp.dashOn = !dp.dashOn;
                }
            }
        }

        dp.sx = 0;
        dp.sy = 0;
        dp.stX = 0;
        dp.stY = 0;
        foreach (var seg in dp.segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo:
                    (dp.sx, dp.sy) = Transform(dp, seg.X1, seg.Y1);
                    dp.stX = dp.sx; dp.stY = dp.sy;
                    break;
                case PathOp.LineTo:
                    var (slx, sly) = Transform(dp, seg.X1, seg.Y1);
                    EmitStroke(dp.sx, dp.sy, slx, sly);
                    dp.sx = slx; dp.sy = sly;
                    break;
                case PathOp.Close:
                    EmitStroke(dp.sx, dp.sy, dp.stX, dp.stY);
                    dp.sx = dp.stX; dp.sy = dp.stY;
                    break;
                // Cubic bezier control points all live in user space — transform each
                // through the CTM, then flatten in device space so the per-segment
                // tolerance is in pixels (not user units, which can be sub-pixel after
                // a small `cm` scale).
                case PathOp.CurveTo:
                    var (c1x, c1y) = Transform(dp, seg.X1, seg.Y1);
                    var (c2x, c2y) = Transform(dp, seg.X2, seg.Y2);
                    var (c3x, c3y) = Transform(dp, seg.X3, seg.Y3);
                    StrokeCubic(dp.ctx, dp.sx, dp.sy, c1x, c1y, c2x, c2y, c3x, c3y, EmitStroke);
                    dp.sx = c3x; dp.sy = c3y;
                    break;
                case PathOp.CurveToV:
                    // First control point coincides with current point.
                    var (v2x, v2y) = Transform(dp, seg.X1, seg.Y1);
                    var (v3x, v3y) = Transform(dp, seg.X2, seg.Y2);
                    StrokeCubic(dp.ctx, dp.sx, dp.sy, dp.sx, dp.sy, v2x, v2y, v3x, v3y, EmitStroke);
                    dp.sx = v3x; dp.sy = v3y;
                    break;
                case PathOp.CurveToY:
                    // Second control point coincides with endpoint; the `y`
                    // operator stores its endpoint in X2/Y2.
                    var (y1x, y1y) = Transform(dp, seg.X1, seg.Y1);
                    var (y3x, y3y) = Transform(dp, seg.X2, seg.Y2);
                    StrokeCubic(dp.ctx, dp.sx, dp.sy, y1x, y1y, y3x, y3y, y3x, y3y, EmitStroke);
                    dp.sx = y3x; dp.sy = y3y;
                    break;
                case PathOp.Rect:
                    var (sr1, sr2) = Transform(dp, seg.X1, seg.Y1);
                    var (sr3, sr4) = Transform(dp, seg.X1 + seg.X2, seg.Y1);
                    var (sr5, sr6) = Transform(dp, seg.X1 + seg.X2, seg.Y1 + seg.Y2);
                    var (sr7, sr8) = Transform(dp, seg.X1, seg.Y1 + seg.Y2);
                    EmitStroke(sr1, sr2, sr3, sr4);
                    EmitStroke(sr3, sr4, sr5, sr6);
                    EmitStroke(sr5, sr6, sr7, sr8);
                    EmitStroke(sr7, sr8, sr1, sr2);
                    dp.sx = sr1; dp.sy = sr2;
                    dp.stX = sr1; dp.stY = sr2;
                    break;
            }
        }
    }
}
