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

public sealed partial class GdiPlusPageRenderer : IPageRenderer
{
    private static partial class ImageDecoder
    {
        /// <summary>Each output row takes its alpha from the soft mask, the stencil or the colour key, un-matting the colour where the mask says it was composited.</summary>
        private static void ApplyMaskRows(byte[] bgra, int w, int h, byte[] outBgra, int outW, int outH, bool upscale,
            bool haveSMask, bool haveStencil, bool haveColorKey, bool unmatte, byte mB, byte mG, byte mR,
            byte[]? alpha, int sw, int sh, byte[]? stencil, int stw, int sth, int[]? colorKey)
        {
            for (int y = 0; y < outH; y++)
            {
                for (int x = 0; x < outW; x++)
                {
                    var o = (y * outW + x) * 4;
                    if (upscale)
                    {
                        var bo = ((y * h / outH) * w + (x * w / outW)) * 4;
                        outBgra[o + 0] = bgra[bo + 0];
                        outBgra[o + 1] = bgra[bo + 1];
                        outBgra[o + 2] = bgra[bo + 2];
                    }
                    int a = 255;
                    if (haveSMask)
                    {
                        var sy = sh == outH ? y : (int)((long)y * sh / outH);
                        var sx = sw == outW ? x : (int)((long)x * sw / outW);
                        var ai = sy * sw + sx;
                        a = ai < alpha!.Length ? alpha[ai] : 255;
                    }
                    if (haveStencil)
                    {
                        var ty = sth == outH ? y : (int)((long)y * sth / outH);
                        var tx = stw == outW ? x : (int)((long)x * stw / outW);
                        var ti = ty * stw + tx;
                        if (ti < stencil!.Length) a = a * stencil[ti] / 255;
                    }
                    if (haveColorKey && a > 0)
                    {
                        // outBgra holds B,G,R at o..o+2 (post-conversion device colour).
                        int pb = outBgra[o + 0], pg = outBgra[o + 1], pr = outBgra[o + 2];
                        if (pr >= colorKey![0] && pr <= colorKey[1]
                            && pg >= colorKey[2] && pg <= colorKey[3]
                            && pb >= colorKey[4] && pb <= colorKey[5])
                            a = 0;
                    }
                    if (unmatte && a > 0)
                    {
                        outBgra[o + 0] = Unmatte(outBgra[o + 0], mB, (byte)a);
                        outBgra[o + 1] = Unmatte(outBgra[o + 1], mG, (byte)a);
                        outBgra[o + 2] = Unmatte(outBgra[o + 2], mR, (byte)a);
                    }
                    outBgra[o + 3] = (byte)a;
                }
            }
        }

        /// <summary>A JPEG-encoded image decodes through the baseline reader; null hands the stream back to the generic path.</summary>
        private static Bitmap? TryDecodeJpegImage(PdfDictionary dict, PdfReader reader, byte[] decoded, SoftwarePageRenderer.ImageColorSpaceInfo csInfo, bool opSim, bool replicateChroma)
        {
            if (decoded.Length > 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
            {
                // ⚠ Routing this through the PLATFORM codec was tried, to see whether
                // handing JPEGs to GDI+/WIC would match the expected render: it moves the
                // output by at most 2 levels and does not close the gap, so the expected
                // render is not produced that way either. The mechanism was removed rather
                // than left dormant.
                try
                {
                    var (pixels, jw, jh, comps) = Aspose.Pdf.IO.Filters.JpegDecoder.Decode(decoded,
                        SoftwarePageRenderer.CmykDecodeInverts(dict), replicateChroma);
                    byte[]? bgraJ;
                    if (comps == 1 && csInfo.TintTransform is not null && csInfo.TintComponents <= 1)
                        bgraJ = SeparationToBgra(pixels, jw, jh,
                            (opSim ? SoftwarePageRenderer.BuildSeparationOverprintLut(csInfo, DecodeInverts(dict)) : SoftwarePageRenderer.BuildSeparationLut(csInfo, DecodeInverts(dict))));
                    else if (comps > 1 && csInfo.TintTransform is not null && csInfo.TintComponents == comps)
                        // Multi-colorant /DeviceN JPEG: raw ink tuples through the tint transform.
                        bgraJ = DeviceNToBgra(pixels, jw, jh, csInfo);
                    else
                        bgraJ = comps == 1 ? GrayToBgra(pixels, jw, jh) : RgbToBgra(pixels, jw, jh);
                    if (bgraJ is null) return null;
                    var (mb, mw, mh) = ApplyMasks(dict, reader, bgraJ, jw, jh);
                    return FromBgra(mb, mw, mh);
                }
                catch { return null; }
            }
            return null;
        }
    }
}

