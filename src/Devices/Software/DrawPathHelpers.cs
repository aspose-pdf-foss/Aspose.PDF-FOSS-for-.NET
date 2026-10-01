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
// The device-space transform of the software path draw.
    // Transform point from PDF user space to pixel coordinates
    private static (double px, double py) Transform(DrawPathState dp, double x, double y)
    {
        var tx = dp.ctm[0] * x + dp.ctm[2] * y + dp.ctm[4];
        var ty = dp.ctm[1] * x + dp.ctm[3] * y + dp.ctm[5];
        return ((tx - dp.ctx.MediaBox.LLX) * dp.ctx.Scale,
            dp.ctx.PixelH - (ty - dp.ctx.MediaBox.LLY) * dp.ctx.Scale);
    }
}
