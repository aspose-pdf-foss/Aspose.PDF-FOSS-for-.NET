using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer
{
    /// <summary>The stages of the image blit: the plain (unmasked, non-overprint) draw.</summary>
    private void BlitImagePlain(Bitmap bmp, System.Drawing.Drawing2D.Matrix world, PointF[] dest, float[] e, bool magnified, double alpha)
    {
        // WrapMode.TileFlipXY: at the image boundary a high-quality (bicubic)
        // resample otherwise samples the pixels *outside* the source — which are
        // transparent since the page backdrop is bare paper — bleeding partial
        // alpha and a darkened colour into the edge row/column. Over the former
        // opaque-white backdrop this went unnoticed; on the coverage-alpha page it
        // flattens to off-white (e.g. an opaque white scan edge lands at 254
        // not 255). Clamping the sampler to the edge texel keeps the border
        // exact. The alpha branch also carries the /ca image opacity via a matrix.
        using var ia = new ImageAttributes();
        ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
        if (PrintedPageImage)
        {
            if (alpha < 0.999)
                ia.SetColorMatrix(new ColorMatrix { Matrix33 = (float)Math.Max(0.0, Math.Min(1.0, alpha)) });
            DrawPrintedImage(_g, bmp, world, dest, ia);
            return;
        }
        if (alpha < 0.999)
        {
            var cm = new ColorMatrix { Matrix33 = (float)Math.Max(0.0, Math.Min(1.0, alpha)) };
            ia.SetColorMatrix(cm);
        }
        // LAW (the border, magnified only): an axis-aligned magnified
        // image covers device pixels [ceil(x0), ceil(x1)) × [ceil(y0), ceil(y1))
        // of its UNSEATED extent, painted hard — probed on a 300 dpi ladder of
        // sub-pixel left edges (50.0/50.17/…/50.83 start at columns 50/51/51/51/
        // 51/51), witnessed on a scan whose banner at 87.5 px starts hard at 88.
        // Enforced as a CLIP around the seated blit: the seat owns the interior
        // phase (it measured exact), the clip owns the border, and neither
        // disturbs the other — the source-rewindow and destination-translation
        // forms of this rule were both tried and measured worse. The seated
        // content edge sits half a pixel inside the clip on each side, and the
        // sampler's mirrored edge texel carries full strength to the clip line.
        // A MINIFIED border stays soft (its half-covered edge pixels match the
        // reference as-is — clipping them was measured and lost the match).
        Region? savedClip = null;
        if (magnified && Math.Abs(e[1]) < 1e-4f && Math.Abs(e[2]) < 1e-4f
            && e[0] > 0f && e[3] < 0f)
        {
            var hardL = (float)Math.Ceiling(e[4]);
            var hardT = (float)Math.Ceiling(e[5] + e[3]);
            var hardR = (float)Math.Ceiling(e[4] + e[0]);
            var hardB = (float)Math.Ceiling(e[5]);
            if (hardR - hardL >= 1 && hardB - hardT >= 1)
            {
                savedClip = _g.Clip;
                // The hard rect is in device pixels; intersect it under an
                // identity transform, then restore the blit's world matrix.
                using (var id = new GdiMatrix())
                {
                    _g.Transform = id;
                    _g.IntersectClip(new RectangleF(hardL, hardT, hardR - hardL, hardB - hardT));
                }
                _g.Transform = world;
            }
        }
        try
        {
            _g.DrawImage(bmp, dest, new RectangleF(0, 0, bmp.Width, bmp.Height),
                GraphicsUnit.Pixel, ia);
        }
        finally
        {
            if (savedClip is not null) { _g.Clip = savedClip; savedClip.Dispose(); }
        }
    }
}
