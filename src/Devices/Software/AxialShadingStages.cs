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
    /// <summary>The stages of the axial shading draw: the device rows inside the blend scope.</summary>
    private static void DrawAxialRows(AxialShadingState ash, double x0u, int xEnd, int xStart, double y0u, int yEnd, int yStart)
    {

        // Sample at pixel centres (+0.5) rather than corners. Sampling at the corner
        // means the pixel covering [0, 1) on the y-axis is probed at exactly y=0 —
        // which for a shading BBox of [0, …, max] with strict inequalities just barely
        // lands on the upper edge and gets excluded. Probing at +0.5 keeps the
        // first/last rows inside their BBoxes, the behaviour mainstream
        // viewers exhibit.
        for (var py = yStart; py < yEnd; py++)
        {
            var uy = ash.mbLly + (ash.ctx.PixelH - py - 0.5) * ash.invScale;
            var rowBase = py * ash.ctx.PixelW;
            for (var px = xStart; px < xEnd; px++)
            {
                if (ash.ctx.ClipMask is { } mask && mask[rowBase + px] == 0) continue;

                var ux = ash.mbLlx + (px + 0.5) * ash.invScale;

                if (ash.bboxLocal is not null && ash.inv is not null)
                {
                    var (lx, ly) = TransformPoint(ash.inv, ux, uy);
                    if (lx < ash.bboxLocal[0] || lx > ash.bboxLocal[2] ||
                        ly < ash.bboxLocal[1] || ly > ash.bboxLocal[3])
                        continue;
                }

                var t = ((ux - x0u) * ash.dx + (uy - y0u) * ash.dy) / ash.denom;

                if (t < 0)
                {
                    if (!ash.extendBefore) continue;
                    t = 0;
                }
                else if (t > 1)
                {
                    if (!ash.extendAfter) continue;
                    t = 1;
                }

                ash.input[0] = ash.domLo + t * ash.domLen;
                var col = ash.axial.Function!.Evaluate(ash.input);
                if (col is null) continue;

                var (r, g, b) = ComponentsToRgb(col, ash.csName, ash.axial.TintTransform, ash.axial.AltSpaceName);
                SetPixel(ash.ctx, px, py, r, g, b, ash.alpha);
            }
        }
    }
}
