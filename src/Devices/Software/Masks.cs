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
    private static void DrawXObject(RenderContext ctx, string name, GraphicsState state,
        Dictionary<string, PdfDictionary>? extGStates)
    {
        if (ctx.AllXObjects is null) return;
        if (!ctx.AllXObjects.TryGetValue(name, out var xobj)) return;

        var subtype = xobj.Dict.GetName("Subtype");
        if (subtype == "Image")
            DrawImage(ctx, xobj, state);
        else if (subtype == "Form")
            DrawFormXObject(ctx, xobj, state, extGStates);
    }

    /// <summary>
    /// Decode an explicit /Mask stencil image (PDF 32000 §8.9.6.3) into a flat byte[]
    /// of per-pixel alpha values (0=masked/transparent, 255=painted/opaque). The mask
    /// is a 1-bit ImageMask XObject; its sample value 1 masks the base image by default
    /// (/Decode [0 1]) and /Decode [1 0] inverts it. The mask may use any resolution
    /// independent of the base image — it is sampled in the base image's coordinate
    /// space by the blit. Returns null when the entry is absent, is a colour-key array,
    /// or cannot be decoded as a 1-bit stencil.
    /// </summary>
    internal static byte[]? ResolveStencilMaskAlpha(PdfObject? maskRef, IO.PdfReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (maskRef is null) return null;
        // A colour-key /Mask is a PdfArray, not a stream; not handled here.
        var stream = reader.ResolveStream(maskRef);
        if (stream is null) return null;
        var d = stream.Dict;
        var w = (int)d.GetInt("Width");
        var h = (int)d.GetInt("Height");
        if (w <= 0 || h <= 0) return null;
        byte[] decoded;
        try { decoded = reader.DecodeStream(stream); }
        catch { return null; }
        var rowBytes = (w + 7) / 8;
        if (decoded.Length < (long)rowBytes * h) return null; // not the 1-bit stencil we expect
        // Default /Decode [0 1]: sample 1 ⇒ masked. /Decode [1 0] flips it.
        var invert = false;
        if (d.Get("Decode") is PdfArray da && da.Count >= 2)
            invert = NumFrom(da[0]) > NumFrom(da[1]);
        var alpha = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            var rowBase = y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                var bit = (decoded[rowBase + (x >> 3)] >> (7 - (x & 7))) & 1;
                if (invert) bit ^= 1;
                alpha[y * w + x] = bit == 1 ? (byte)0 : (byte)255; // 1 ⇒ masked out
            }
        }
        width = w;
        height = h;
        return alpha;
    }

    // Repair a soft mask's /DecodeParms /Colors to 1 (its true single-component value) when a
    // predictor is active and the producer left it at the parent image's component count.
    // Handles /DecodeParms as a single dict or a per-filter array. Idempotent.
    private static void ForceSoftMaskPredictorColors(PdfObject? decodeParms, IO.PdfReader reader)
    {
        switch (reader.Resolve(decodeParms))
        {
            case PdfDictionary dp:
                if (dp.GetInt("Predictor") > 1 && dp.GetInt("Colors", 1) != 1)
                    dp.Set("Colors", new PdfInteger(1));
                break;
            case PdfArray arr:
                foreach (var el in arr)
                    if (reader.Resolve(el) is PdfDictionary edp
                        && edp.GetInt("Predictor") > 1 && edp.GetInt("Colors", 1) != 1)
                        edp.Set("Colors", new PdfInteger(1));
                break;
        }
    }

    /// <summary>
    /// Decode an /SMask soft-mask image (PDF 32000 §11.6.5.3) into a flat byte[]
    /// of per-pixel alpha values (0=transparent, 255=opaque). The SMask is always
    /// a DeviceGray, 8-bpc image XObject; a /Decode [a b] entry can invert the
    /// mapping. Returns null if the entry is missing or the stream cannot be
    /// decoded as a grayscale image.
    /// </summary>
    internal static byte[]? ResolveSMaskAlpha(PdfObject? smaskRef, IO.PdfReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (smaskRef is null) return null;
        var stream = reader.ResolveStream(smaskRef);
        if (stream is null) return null;
        var d = stream.Dict;
        var w = (int)d.GetInt("Width");
        var h = (int)d.GetInt("Height");
        if (w <= 0 || h <= 0) return null;

        // A soft mask is a single-component DeviceGray image (PDF 32000 §11.6.5.1). Some
        // producers copy the parent image's /DecodeParms onto the mask verbatim, leaving
        // /Colors at the parent's component count (e.g. 4 for a CMYK base image). A PNG/TIFF
        // predictor unfiltered with the wrong /Colors uses the wrong per-row stride and
        // yields fewer bytes than W*H, so the 8-bpc branch below rejects it, the mask is
        // dropped, and the image composites fully opaque (occluding what should show through).
        // Force the mask's own predictor /Colors to its true value of 1.
        ForceSoftMaskPredictorColors(d.Get("DecodeParms"), reader);

        byte[] decoded;
        try { decoded = reader.DecodeStream(stream); }
        catch { return null; }

        // A soft mask compressed with DCTDecode/JPXDecode arrives here still encoded
        // (DecodeStream leaves image-specific filters in place for the renderer to
        // handle). Decode it to a grayscale alpha plane; otherwise the raw codestream
        // bytes are mistaken for 8-bpc samples and the masked image composites to
        // near-black.
        if (decoded.Length > 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
        {
            try
            {
                var (jp, jw, jh, jc) = IO.Filters.JpegDecoder.Decode(decoded);
                width = jw; height = jh;
                return JpegPlaneToAlpha(jp, jw, jh, jc);
            }
            catch { return null; }
        }
        bool smJ2k = (decoded.Length > 3 && decoded[0] == 0xFF && decoded[1] == 0x4F)
            || (decoded.Length > 12 && decoded[0] == 0x00 && decoded[1] == 0x00 && decoded[2] == 0x00
                && decoded[3] == 0x0C && decoded[4] == 0x6A && decoded[5] == 0x50);
        if (smJ2k)
        {
            if (IO.Filters.JpxDecoder.TryDecode(decoded, out var jp, out var jw, out var jh, out var jc))
            {
                width = jw; height = jh;
                return JpegPlaneToAlpha(jp, jw, jh, jc);
            }
            return null;
        }

        var bpc = (int)d.GetInt("BitsPerComponent");
        if (bpc == 0) bpc = 8;

        // Decode the bytes into a W*H byte buffer of alpha values.
        byte[] alpha;
        if (bpc == 8 && decoded.Length >= w * h)
        {
            alpha = new byte[w * h];
            Array.Copy(decoded, alpha, w * h);
        }
        else if (bpc == 1)
        {
            // 1-bpc soft mask: pack 8 alpha bits per byte, MSB-first per row,
            // each row padded to a byte. Convert to 0/255.
            alpha = new byte[w * h];
            var rowBytes = (w + 7) / 8;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var bi = y * rowBytes + x / 8;
                    if (bi >= decoded.Length) break;
                    var bit = (decoded[bi] >> (7 - x % 8)) & 1;
                    alpha[y * w + x] = bit == 1 ? (byte)255 : (byte)0;
                }
            }
        }
        else
        {
            return null;
        }

        // PDF 32000 §11.6.5.3: soft-mask sample values are alpha (0=transparent,
        // max=opaque). A /Decode [1 0] entry reverses that mapping, which a layered
        // scan (a JPEG2000 "text colour" overlay gated by a 1-bpc JBIG2 mask
        // marked /Decode [1 0]) relies on to confine the overlay to the glyph pixels
        // instead of flooding the page.
        if (GrayDecodeInverts(d))
            for (var i = 0; i < alpha.Length; i++) alpha[i] = (byte)(255 - alpha[i]);

        width = w;
        height = h;
        return alpha;
    }

    /// <summary>Reduce a decoded image plane (gray or RGB) to a W×H 8-bit alpha buffer.</summary>
    private static byte[] JpegPlaneToAlpha(byte[] pixels, int w, int h, int comps)
    {
        var alpha = new byte[w * h];
        if (comps <= 1)
        {
            for (int i = 0; i < alpha.Length && i < pixels.Length; i++) alpha[i] = pixels[i];
        }
        else
        {
            for (int i = 0; i < w * h; i++)
            {
                int s = i * comps;
                if (s + 2 >= pixels.Length) break;
                alpha[i] = (byte)((pixels[s] * 299 + pixels[s + 1] * 587 + pixels[s + 2] * 114) / 1000);
            }
        }
        return alpha;
    }
}
