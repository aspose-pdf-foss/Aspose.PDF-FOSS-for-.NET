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
    private static void DrawImage(RenderContext ctx, PdfStream xobjStream, GraphicsState state)
    {
        var di = new DrawImageState();
        di.ctx = ctx;
        di.xobjStream = xobjStream;
        di.state = state;
        di.dict = di.xobjStream.Dict;
        di.imgW = (int)di.dict.GetInt("Width");
        di.imgH = (int)di.dict.GetInt("Height");
        if (di.imgW <= 0 || di.imgH <= 0) return;

        // Skip an image whose optional-content group/membership is hidden by the
        // document's default configuration (PDF 32000 §8.11.4.4).
        if (IsOcHidden(di.dict.Get("OC"), di.ctx.Reader, di.ctx.OcgHidden)) return;

        // Inherit blend mode and fill-alpha (CA/ca via /gs) for this draw. Set on the
        // context up front; Blit* paths read it via SetPixel.
        di.ctx.CurrentBlendMode = di.state.BlendMode;
        di.ctx.SoftMaskAlpha = di.state.SoftMask is { } sm__ ? ResolveSoftMaskAlpha(di.ctx, sm__) : null;

        try { di.decoded = di.ctx.Reader.DecodeStream(di.xobjStream); }
        catch { return; }

        di.isImageMask = di.dict.Get("ImageMask") is PdfBoolean imb && imb.Value;

        ResolveImageMasks(di);

        if (!TryBlitAffineImage(di)) return;

        di.destX = Math.Min(di.ctm[4], di.ctm[4] + di.ctm[0]);
        di.destW = Math.Abs(di.ctm[0]);
        di.destH = Math.Abs(di.ctm[3]);
        di.topPdfY = Math.Max(di.ctm[5], di.ctm[5] + di.ctm[3]);
        if (di.destW < 0.01) di.destW = di.imgW;
        if (di.destH < 0.01) di.destH = di.imgH;

        di.px = (int)Math.Round((di.destX - di.ctx.MediaBox.LLX) * di.ctx.Scale);
        di.py = di.ctx.PixelH - (int)Math.Round((di.topPdfY - di.ctx.MediaBox.LLY) * di.ctx.Scale);
        di.pw = Math.Max(1, (int)Math.Round(di.destW * di.ctx.Scale));
        di.ph = Math.Max(1, (int)Math.Round(di.destH * di.ctx.Scale));

        di.flipY = di.ctm[3] < 0;
        di.flipX = di.ctm[0] < 0;

        if (!BlitStencilImageMask(di)) return;

        if (!DecodeCompressedImage(di)) return;

        BlitDecodedImage(di);
    }
}
