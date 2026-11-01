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

                if (ColorAt(ash, t, px) is not { } rgb) continue;
                SetPixel(ash.ctx, px, py, rgb.r, rgb.g, rgb.b, ash.alpha);
            }
        }
    }

    /// <summary>The shading's colour at <paramref name="t"/>, from the function and tint
    /// transform once per distinct t: the last t, then the one this column had on the row
    /// before (see the state's note). Null where the function gives no colour.</summary>
    private static (byte r, byte g, byte b)? ColorAt(AxialShadingState ash, double t, int px)
    {
#pragma warning disable S1244 // the same t gives the same colour: an exact repeat is what is looked for
        if (t == ash.lastT) return ash.lastRgb;
        ash.colT ??= Enumerable.Repeat(double.NaN, ash.ctx.PixelW).ToArray();
        if (ash.colRgb.Length == 0) ash.colRgb = new (byte r, byte g, byte b)?[ash.ctx.PixelW];
        if (t != ash.colT[px])
#pragma warning restore S1244
        {
            ash.input[0] = ash.domLo + t * ash.domLen;
            var col = EvaluateInPlace(ash.axial.Function!, ash.input, ref ash.output);
            ash.colRgb[px] = col is null ? null : ComponentsToRgb(col, ash.csName, ash.axial.TintTransform, ash.axial.AltSpaceName);
            ash.colT[px] = t;
        }
        ash.lastT = t;
        ash.lastRgb = ash.colRgb[px];
        return ash.lastRgb;
    }

    /// <summary>The function's outputs at <paramref name="input"/>[0], in <paramref name="output"/> once
    /// its size is known (the first evaluation sizes it) and the function fills it whole; a function
    /// whose output count varies is evaluated the allocating way every time.</summary>
    private static double[]? EvaluateInPlace(Functions.PdfFunction function, double[] input, ref double[] output)
    {
        if (output.Length > 0 && function.EvaluateInto(input[0], output) == output.Length) return output;
        var col = function.Evaluate(input);
        if (col is not null && output.Length == 0) output = new double[col.Length];
        return col;
    }
}
