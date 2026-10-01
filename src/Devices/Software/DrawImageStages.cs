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
    /// <summary>Software image draw: the decoded samples blitted by colour space and bit depth.</summary>
    private static void BlitDecodedImage(DrawImageState di)
    {
        // Indexed: unpack bit-packed palette indices and look up RGB per pixel.
        // 4-bpc indexed is common for palette-based screenshots; 8-bpc indexed also appears.
        if (di.csInfo.Palette is not null)
        {
            BlitIndexed(di.ctx, di.decoded, di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, di.bpc, di.csInfo, di.flipY, di.flipX,
                di.smask, di.smaskW, di.smaskH, di.state.FillAlpha);
            return;
        }

        // Raw 8-bpc samples take the same /Matte correction as the JPEG path above.
        if (di.bpc == 8 && (di.cs == "DeviceRGB" || di.cs == "DeviceGray"))
            UnpremultiplyMatte(di.decoded, di.cs == "DeviceRGB" ? 3 : 1, di.dict.Get("SMask"), di.smask, di.ctx.Reader);

        if (di.bpc == 8 && (di.cs == "DeviceRGB" || di.cs == "DeviceGray"))
            (di.smask, di.smaskW, di.smaskH) = FoldColorKeyMask(di.dict, di.ctx.Reader, di.decoded, di.imgW, di.imgH, di.cs == "DeviceRGB" ? 3 : 1,
                di.smask, di.smaskW, di.smaskH);

        // Render raw pixel data
        if (di.cs == "DeviceRGB" && di.bpc == 8 && di.decoded.Length >= di.imgW * di.imgH * 3)
            BlitRGB(di.ctx, di.decoded, di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX, di.overprint);
        else if (di.cs == "DeviceCMYK" && di.bpc == 8 && di.decoded.Length >= di.imgW * di.imgH * 4)
            BlitCMYK(di.ctx, di.decoded, di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.overprint);
        else if (di.cs == "DeviceGray" && di.bpc == 8 && di.decoded.Length >= di.imgW * di.imgH)
            BlitGray(di.ctx, di.decoded, di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX);
        else if (di.cs == "DeviceGray" && (di.bpc == 2 || di.bpc == 4))
            BlitGray(di.ctx, UnpackGraySamples(di.decoded, di.imgW, di.imgH, di.bpc, GrayDecodeInverts(di.dict)), di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX);
        // A colour image may be bit-packed too: /DeviceRGB with /BitsPerComponent 1 is
        // three bits per pixel, not one, and falling through to the bilevel branch below
        // read each row at a third of its stride and painted streaks.
        else if (di.cs == "DeviceRGB" && di.bpc is 1 or 2 or 4)
            BlitRGB(di.ctx, UnpackComponentSamples(di.decoded, di.imgW, di.imgH, di.bpc, 3), di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX);
        else if (di.cs == "DeviceCMYK" && di.bpc is 1 or 2 or 4)
            BlitCMYK(di.ctx, UnpackComponentSamples(di.decoded, di.imgW, di.imgH, di.bpc, 4), di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha);
        else if (di.csInfo.TintTransform is not null && (di.bpc is 1 or 2 or 4 or 8))
            // Single-colorant /Separation (or /DeviceN) image: map each sample through the
            // tint transform. A 1-bpc /Separation/Black image (sample 1 ⇒ full ink) would
            // otherwise reach BlitBilevel and render inverted (e.g. a white-on-black
            // graphic comes out black-on-white).
            BlitRGB(di.ctx, SeparationSamplesToRgb(di.decoded, di.imgW, di.imgH, di.bpc, BuildSeparationLut(di.csInfo, GrayDecodeInverts(di.dict))),
                di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX);
        else if (di.bpc == 1)
            BlitBilevel(di.ctx, di.decoded, di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, GrayDecodeInverts(di.dict));
        // No fallback — a solid gray rectangle over an unrecognised image is false
        // content. Leaving the area white (page background) is less damaging
        // than painting a false rectangle over the area's real content.
    }

    /// <summary>Software image draw: a JPEG or JPEG 2000 stream decoded and blitted.</summary>
    private static bool DecodeCompressedImage(DrawImageState di)
    {
        di.bpc = (int)di.dict.GetInt("BitsPerComponent");
        if (di.bpc == 0) di.bpc = 8;
        di.csInfo = ResolveImageColorSpace(di.dict.Get("ColorSpace"), di.ctx.Reader);
        di.cs = di.csInfo.BaseName;


        // Decode JPEG images

        di.overprint = di.state.OverprintFill
                        && (di.csInfo.TintTransform is not null || di.cs == "DeviceCMYK");
        if (di.decoded.Length > 2 && di.decoded[0] == 0xFF && di.decoded[1] == 0xD8)
        {
            try
            {
                var jpeg = IO.Filters.JpegDecoder.Decode(di.decoded, CmykDecodeInverts(di.dict));
                if (jpeg.components is 1 or >= 3)
                    UnpremultiplyMatte(jpeg.pixels, jpeg.components >= 3 ? 3 : 1,
                        di.dict.Get("SMask"), di.smask, di.ctx.Reader);
                // A single-component DCT image in a /Separation or /DeviceN space carries TINT
                // values, not grey levels: high tint = more ink = DARKER, the opposite of
                // DeviceGray. The raw-sample path below already runs them through the tint
                // transform; this one did not, so a pale PANTONE duotone photo blitted straight
                // as grey and came out near-black — inverted.
                (di.smask, di.smaskW, di.smaskH) = FoldColorKeyMask(di.dict, di.ctx.Reader, jpeg.pixels, jpeg.width, jpeg.height,
                    jpeg.components, di.smask, di.smaskW, di.smaskH);
                if (jpeg.components == 1 && di.csInfo.TintTransform is not null)
                {
                    // Under PDF/X the spot sample is a colorant COVERAGE: tint 0 must come out
                    // paper WHITE so the overprint multiply leaves the backdrop untouched. The
                    // composited (non-PDF/X) LUT would hand back the plate's tint-0 wash instead.
                    var sepLut = di.overprint && di.ctx.PdfXOverprintSim
                        ? BuildSeparationOverprintLut(di.csInfo, GrayDecodeInverts(di.dict))
                        : BuildSeparationLut(di.csInfo, GrayDecodeInverts(di.dict));
                    var sepRgb = SeparationSamplesToRgb(jpeg.pixels, jpeg.width, jpeg.height, 8, sepLut);
                    BlitRGB(di.ctx, sepRgb, jpeg.width, jpeg.height, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX, di.overprint);
                }
                else if (jpeg.components == 1)
                    BlitGray(di.ctx, jpeg.pixels, jpeg.width, jpeg.height, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX, di.overprint);
                else if (jpeg.components >= 3)
                    BlitRGB(di.ctx, jpeg.pixels, jpeg.width, jpeg.height, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX, di.overprint);
                return false;
            }
            catch
            {
                // No fallback — leave area as page background rather than painting a
                // false gray rectangle over the area's real content.
                return false;
            }
        }

        di.isJ2kFile = di.decoded.Length > 12
            && di.decoded[0] == 0x00 && di.decoded[1] == 0x00 && di.decoded[2] == 0x00 && di.decoded[3] == 0x0C
            && di.decoded[4] == 0x6A && di.decoded[5] == 0x50;
        di.isJ2kCodestream = di.decoded.Length > 4 && di.decoded[0] == 0xFF && di.decoded[1] == 0x4F;
        if (di.isJ2kFile || di.isJ2kCodestream)
        {
            if (IO.Filters.JpxDecoder.TryDecode(di.decoded) is (var jp, var jw, var jh, var jc))
            {
                // Single-component JPX under /Indexed: the samples are palette
                // indices, not gray levels — look them up before blitting.
                if (jc == 1 && di.csInfo.Palette is not null)
                {
                    var rgbIdx = DecodeIndexedToRgb(jp, jw, jh, 8, di.csInfo);
                    if (rgbIdx is not null)
                        BlitRGB(di.ctx, rgbIdx, jw, jh, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX);
                }
                else if (jc >= 3)
                    BlitRGB(di.ctx, jp, jw, jh, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX);
                else
                    BlitGray(di.ctx, jp, jw, jh, di.px, di.py, di.pw, di.ph, di.smask, di.smaskW, di.smaskH, di.state.FillAlpha, di.flipY, di.flipX);
            }
            return false;
        }
        return true;
    }

    /// <summary>Software image draw: an image mask painted, through a fill pattern when one is set.</summary>
    private static bool BlitStencilImageMask(DrawImageState di)
    {
        if (di.isImageMask)
        {
            // /Decode [a b]: when a > b (e.g. [1 0]) the bit-to-opacity mapping is
            // inverted from the default. PDF 32000 §8.9.5.1: Decode component values
            // map source samples to colour-component values; for ImageMasks the
            // default is [0 1] (sample 0 ⇒ paint, 1 ⇒ transparent) and [1 0] flips it.
            var invertDecode = false;
            if (di.dict.Get("Decode") is PdfArray decodeArr && decodeArr.Count >= 2)
                invertDecode = NumFrom(decodeArr[0]) > NumFrom(decodeArr[1]);
            // A mask painted while a /Pattern fill is active (e.g. PowerPoint masks a
            // gradient pattern through a stencil) shows the pattern, not a flat colour;
            // painting the stale solid fill over-inks it dark. Fill the stencil with the
            // pattern instead.
            if (di.state.FillPatternName is not null)
            {
                DrawImageMaskWithPattern(di.ctx, di.decoded, di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, di.state, invertDecode);
                return false;
            }
            DrawImageMask(di.ctx, di.decoded, di.imgW, di.imgH, di.px, di.py, di.pw, di.ph, di.state, invertDecode, di.flipY, di.flipX);
            return false;
        }
        return true;
    }

    /// <summary>Software image draw: a rotated or skewed image inverse-mapped through its matrix.</summary>
    private static bool TryBlitAffineImage(DrawImageState di)
    {
        di.ctm = di.state.Ctm;

        // A rotated or skewed image CTM (e.g. any image on a /Rotate 90|270 page) cannot
        // be represented by the axis-aligned blit paths below — they sample the source on
        // a straight x/y scale, so they place the image at the wrong size and orientation
        // (e.g. a /Rotate 270 page drew its CCITT text mask 3.7x off-canvas and
        // the page rendered blank). Decode the image to RGBA once and inverse-map each
        // destination pixel through the CTM. Axis-aligned images keep their optimised paths.
        if (Math.Abs(di.ctm[1]) > 1e-4 || Math.Abs(di.ctm[2]) > 1e-4)
        {
            // A rotated/skewed mask painted while a /Pattern fill is active shows the
            // pattern through the stencil, not a flat colour (see DrawImageMaskWithPattern).
            if (di.isImageMask && di.state.FillPatternName is not null)
            {
                var inv = di.dict.Get("Decode") is PdfArray dm && dm.Count >= 2 && NumFrom(dm[0]) > NumFrom(dm[1]);
                DrawImageMaskWithPatternAffine(di.ctx, di.decoded, di.imgW, di.imgH, di.ctm, di.state, inv);
                return false;
            }
            var aff = DecodeImageToRgba(di.ctx, di.dict, di.decoded, di.imgW, di.imgH, di.isImageMask, di.smask, di.smaskW, di.smaskH, di.state);
            if (aff is not null) BlitRgbaAffine(di.ctx, aff.Value.rgba, aff.Value.w, aff.Value.h, di.ctm, di.state.FillAlpha);
            return false;
        }
        return true;
    }

    /// <summary>Software image draw: the soft mask and stencil mask resolved and combined.</summary>
    private static void ResolveImageMasks(DrawImageState di)
    {
        (di.smask, di.smaskW, di.smaskH) = ResolveSMaskAlpha(di.dict.Get("SMask"), di.ctx.Reader);

        (di.stencil, di.stencilW, di.stencilH) = ResolveStencilMaskAlpha(di.dict.Get("Mask"), di.ctx.Reader);
        if (di.stencil is not null)
        {
            if (di.smask is null)
            {
                di.smask = di.stencil; di.smaskW = di.stencilW; di.smaskH = di.stencilH;
            }
            else
            {
                // Combine on the SMask grid (both are sampled in base-image coords).
                var combined = new byte[di.smaskW * di.smaskH];
                for (int y = 0; y < di.smaskH; y++)
                    for (int x = 0; x < di.smaskW; x++)
                    {
                        var sxr = x * di.stencilW / di.smaskW;
                        var syr = y * di.stencilH / di.smaskH;
                        combined[y * di.smaskW + x] = (byte)(di.smask[y * di.smaskW + x] * di.stencil[syr * di.stencilW + sxr] / 255);
                    }
                di.smask = combined;
            }
        }
    }
}
