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
    private static void DrawPath(RenderContext ctx, IReadOnlyList<PathCommand> segments,
        string op, GraphicsState state)
    {
        var dp = new DrawPathState();
        dp.ctx = ctx;
        dp.segments = segments;
        dp.op = op;
        dp.state = state;
        if (dp.segments.Count == 0) return;

        dp.doFill = dp.op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
        dp.doStroke = dp.op is "S" or "s" or "B" or "B*" or "b" or "b*";
        dp.evenOdd = dp.op is "f*" or "B*" or "b*";

        if (!dp.doFill && !dp.doStroke) return;
        if (!PathGeometrySane(dp.segments, dp.state.Ctm, dp.ctx)) return;

        // Inherit the active blend mode for the fill/stroke pixels.
        dp.ctx.CurrentBlendMode = dp.state.BlendMode;
        dp.ctx.SoftMaskAlpha = dp.state.SoftMask is { } sm__ ? ResolveSoftMaskAlpha(dp.ctx, sm__) : null;

        dp.ctm = dp.state.Ctm;

        dp.edgeTable = BuildPathEdgeTable(dp.segments, dp.ctm, dp.ctx);

        dp.clip = dp.state.ClipMask;

        if (dp.doFill)
        {
            FillPathInterior(dp);
        }

        if (dp.doStroke)
        {
            StrokePath(dp);
        }
    }
}
