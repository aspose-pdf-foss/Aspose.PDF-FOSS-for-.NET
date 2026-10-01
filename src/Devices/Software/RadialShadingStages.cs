using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer : IPageRenderer
{
    /// <summary>The stages of the radial shading draw: one device row at a time.</summary>
    private static void DrawRadialRow(RadialShadingState rs, int py, int xStart, int xEnd, double x0u, double y0u)
    {
        var uy = rs.mbLly + (rs.ctx.PixelH - py - 0.5) * rs.invScale;
        var rowBase = py * rs.ctx.PixelW;
        for (var px = xStart; px < xEnd; px++)
        {
            if (rs.ctx.ClipMask is { } mask && mask[rowBase + px] == 0) continue;

            var ux = rs.mbLlx + (px + 0.5) * rs.invScale;

            if (rs.bboxLocal is not null && rs.inv is not null)
            {
                var (lx, ly) = TransformPoint(rs.inv, ux, uy);
                if (lx < rs.bboxLocal[0] || lx > rs.bboxLocal[2] ||
                    ly < rs.bboxLocal[1] || ly > rs.bboxLocal[3])
                    continue;
            }

            var fx = ux - x0u;
            var fy = uy - y0u;

            // (fx - t*cdx)^2 + (fy - t*cdy)^2 = (r0 + t*dr)^2
            // qa*t^2 - 2*qb*t + qc = 0, pick the larger root in [0, 1].
            var qa = rs.cdx * rs.cdx + rs.cdy * rs.cdy - rs.dr * rs.dr;
            var qb = fx * rs.cdx + fy * rs.cdy + rs.r0 * rs.dr;
            var qc = fx * fx + fy * fy - rs.r0 * rs.r0;

            double t;
            if (Math.Abs(qa) < 1e-12)
            {
                if (Math.Abs(qb) < 1e-12) continue;
                t = qc / (2 * qb);
            }
            else
            {
                var disc = qb * qb - qa * qc;
                if (disc < 0) continue;
                var sq = Math.Sqrt(disc);
                var t1 = (qb + sq) / qa;
                var t2 = (qb - sq) / qa;
                // Pick the larger valid root that gives a non-negative radius.
                t = double.NaN;
                foreach (var candidate in new[] { t1, t2 })
                {
                    if (double.IsNaN(candidate)) continue;
                    if (rs.r0 + candidate * rs.dr < 0) continue;
                    if (double.IsNaN(t) || candidate > t) t = candidate;
                }
                if (double.IsNaN(t)) continue;
            }

            if (t < 0)
            {
                if (!rs.extendBefore) continue;
                t = 0;
            }
            else if (t > 1)
            {
                if (!rs.extendAfter) continue;
                t = 1;
            }

            rs.input[0] = rs.domLo + t * rs.domLen;
            var col = rs.radial.Function!.Evaluate(rs.input);
            if (col is null) continue;

            var (r, g, b) = ComponentsToRgb(col, rs.csName, rs.radial.TintTransform, rs.radial.AltSpaceName);
            SetPixel(rs.ctx, px, py, r, g, b, rs.alpha);
        }
    }
}
