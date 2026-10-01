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
    internal static (byte[]? result, int width, int height) ResolveStencilMaskAlpha(PdfObject? maskRef, IO.PdfReader reader)
    {
        int width = default;
        int height = default;
        width = 0;
        height = 0;
        if (maskRef is null) return (null, width, height);
        // A colour-key /Mask is a PdfArray, not a stream; not handled here.
        var stream = reader.ResolveStream(maskRef);
        if (stream is null) return (null, width, height);
        var d = stream.Dict;
        var w = (int)d.GetInt("Width");
        var h = (int)d.GetInt("Height");
        if (w <= 0 || h <= 0) return (null, width, height);
        byte[] decoded;
        try { decoded = reader.DecodeStream(stream); }
        catch { return (null, width, height); }
        var rowBytes = (w + 7) / 8;
        if (decoded.Length < (long)rowBytes * h) return (null, width, height); // not the 1-bit stencil we expect
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
        return (alpha, width, height);
    }

    /// <summary>The /Colors a soft mask's predictor declares (1 when no predictor is active).</summary>
    private static int SoftMaskPredictorColors(PdfObject? decodeParms, IO.PdfReader reader)
    {
        var parms = reader.Resolve(decodeParms) switch
        {
            PdfDictionary dp => dp,
            PdfArray arr => arr.Select(reader.Resolve).OfType<PdfDictionary>()
                .FirstOrDefault(p => p.GetInt("Predictor") > 1),
            _ => null,
        };
        return parms is not null && parms.GetInt("Predictor") > 1 ? (int)parms.GetInt("Colors", 1) : 1;
    }

    /// <summary>
    /// Decode a soft mask's samples. A soft mask is a one-component image, yet a producer
    /// may carry its parent image's /Colors into the mask's predictor (4 for a CMYK parent).
    /// Which stride the bytes were actually filtered through shows in the decoded length:
    /// when the declared stride yields exactly the mask's own sample count the producer
    /// filtered through that stride (its rows simply do not align with the image rows), and
    /// when it does not, the samples were filtered at the mask's own stride and the decode
    /// is repeated with /Colors 1. The document's dictionaries stay untouched either way.
    /// </summary>
    private static byte[]? DecodeSoftMaskSamples(PdfStream stream, IO.PdfReader reader, long expectedBytes)
    {
        byte[] declared;
        try { declared = reader.DecodeStream(stream); }
        catch { return null; }
        if (declared.Length == expectedBytes || SoftMaskPredictorColors(stream.Dict.Get("DecodeParms"), reader) == 1)
            return declared;
        byte[] single;
        try { single = reader.DecodeStreamWithPredictorColors(stream, 1); }
        catch { return declared; }
        return single.Length == expectedBytes || declared.Length < expectedBytes ? single : declared;
    }

    /// <summary>
    /// Decode an /SMask soft-mask image (PDF 32000 §11.6.5.3) into a flat byte[]
    /// of per-pixel alpha values (0=transparent, 255=opaque). The SMask is always
    /// a DeviceGray, 8-bpc image XObject; a /Decode [a b] entry can invert the
    /// mapping. Returns null if the entry is missing or the stream cannot be
    /// decoded as a grayscale image.
    /// </summary>
    internal static (byte[]? result, int width, int height) ResolveSMaskAlpha(PdfObject? smaskRef, IO.PdfReader reader)
    {
        int width = default;
        int height = default;
        width = 0;
        height = 0;
        if (smaskRef is null) return (null, width, height);
        var stream = reader.ResolveStream(smaskRef);
        if (stream is null) return (null, width, height);
        var d = stream.Dict;
        var w = (int)d.GetInt("Width");
        var h = (int)d.GetInt("Height");
        if (w <= 0 || h <= 0) return (null, width, height);

        var bpc = (int)d.GetInt("BitsPerComponent");
        if (bpc == 0) bpc = 8;

        // A soft mask is a single-component DeviceGray image (PDF 32000 §11.6.5.1); its
        // sample count follows from its own geometry, whatever /Colors its predictor declares.
        var decoded = DecodeSoftMaskSamples(stream, reader, (long)((w * bpc + 7) / 8) * h);
        if (decoded is null) return (null, width, height);

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
                return (JpegPlaneToAlpha(jp, jw, jh, jc), width, height);
            }
            catch { return (null, width, height); }
        }
        bool smJ2k = (decoded.Length > 3 && decoded[0] == 0xFF && decoded[1] == 0x4F)
            || (decoded.Length > 12 && decoded[0] == 0x00 && decoded[1] == 0x00 && decoded[2] == 0x00
                && decoded[3] == 0x0C && decoded[4] == 0x6A && decoded[5] == 0x50);
        if (smJ2k)
        {
            if (IO.Filters.JpxDecoder.TryDecode(decoded) is (var jp, var jw, var jh, var jc))
            {
                width = jw; height = jh;
                return (JpegPlaneToAlpha(jp, jw, jh, jc), width, height);
            }
            return (null, width, height);
        }

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
            return (null, width, height);
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
        return (alpha, width, height);
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
