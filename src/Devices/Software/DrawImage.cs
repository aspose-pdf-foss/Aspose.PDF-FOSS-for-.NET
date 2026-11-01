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

        (di.px, di.py, di.pw, di.ph) = DeviceImageRect(di.ctx, di.destX, di.topPdfY, di.destW, di.destH);

        di.flipY = di.ctm[3] < 0;
        di.flipX = di.ctm[0] < 0;

        if (!BlitStencilImageMask(di)) return;

        if (!DecodeCompressedImage(di)) return;

        BlitDecodedImage(di);
    }

    /// <summary>
    /// The device pixels an upright image covers: each EDGE rounded to the pixel grid, the far
    /// ones included. Rounding the width and height on their own let the far edge of one image
    /// and the near edge of the next - the same line on the page - land a pixel apart: a photo
    /// set as one-row strips of 2.5 pixels each showed a white row between every two strips.
    /// </summary>
    private static (int px, int py, int pw, int ph) DeviceImageRect(RenderContext ctx, double destX, double topPdfY,
        double destW, double destH)
    {
        var left = (int)Math.Round((destX - ctx.MediaBox.LLX) * ctx.Scale);
        var right = (int)Math.Round((destX + destW - ctx.MediaBox.LLX) * ctx.Scale);
        // Rows are counted up the page here. A line half way up a row leaves that row to both
        // images it parts: the clip mask takes a row by its centre, and two computations of one
        // line differ in their last bits, so either slice's clip may be the one that selects it.
        var top = (int)Math.Floor((topPdfY - ctx.MediaBox.LLY) * ctx.Scale + HalfPixel + DeviceEdgeSlack);
        var bottom = (int)Math.Floor((topPdfY - destH - ctx.MediaBox.LLY) * ctx.Scale + HalfPixel - DeviceEdgeSlack);
        // A rectangle that is non-empty in user space covers at least one device pixel.
        return (left, ctx.PixelH - top, Math.Max(1, right - left), Math.Max(1, top - bottom));
    }

    /// <summary>How far, in device pixels, an edge may lie off a row's centre and count as on it.</summary>
    private const double DeviceEdgeSlack = 1.0 / 1024;

    private const double HalfPixel = 0.5;
}
