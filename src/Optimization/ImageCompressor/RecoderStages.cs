using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Optimization;

internal static partial class ImageCompressor
{
    /// <summary>The lossy leg of the image re-encode: build RGB, try a palette, then JPEG.</summary>
    private static bool TryRecodeLossyJpeg(PdfStream stream, PdfReader reader, byte[] decoded, int width, int height, int quality, HashSet<PdfStream> maskStreams, bool hasColorKeyMask)
    {
        if (!stream.Dict.GetBool("ImageMask") && !maskStreams.Contains(stream) && !hasColorKeyMask)
        {
            var rgb = TryBuildRgb(decoded, width, height, stream, reader);
            if (rgb is not null)
            {
                // Lossless palette re-encode first: for flat-colour graphics it beats both
                // JPEG and plain Flate while preserving every sample exactly.
                if (TryPalettize(stream, rgb, width, height)) return false;

                byte[] jpeg;
                try
                {
                    jpeg = JpegEncoderImpl.Encode((int x, int y) =>
                    {
                        var idx = (y * width + x) * 3;
                        return (rgb[idx], rgb[idx + 1], rgb[idx + 2]);
                    }, width, height, quality);
                }
                catch
                {
                    jpeg = [];
                }

                if (jpeg.Length > 0 && jpeg.Length < stream.RawData.Length)
                {
                    stream.ReplaceData(jpeg);
                    stream.Dict.Set("Filter", new PdfName("DCTDecode"));
                    stream.Dict.Set("Length", new PdfInteger(jpeg.Length));
                    stream.Dict.Set("ColorSpace", new PdfName("DeviceRGB"));
                    stream.Dict.Set("BitsPerComponent", new PdfInteger(8));
                    // A custom /Decode array or /DecodeParms no longer matches the
                    // re-encoded DeviceRGB sample stream.
                    stream.Dict.Remove("DecodeParms");
                    stream.Dict.Remove("Decode");
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>An already-JPEG image: a lossless palette re-encode, else a lower-quality re-encode.</summary>
    private static bool RecodeExistingJpeg(PdfStream stream, PdfReader reader, string? filterName, int width, int height, int quality, HashSet<PdfStream> maskStreams)
    {
        if (filterName == "DCTDecode")
        {
            if (stream.Dict.Get("Filter") is PdfArray) return false;     // only the sole-filter case: RawData is raw JPEG
            if (stream.Dict.GetBool("ImageMask") || maskStreams.Contains(stream)) return false;
            // A colour-key /Mask matches EXACT sample values; palettizing rewrites samples
            // into palette indices, so such images must keep their original encoding.
            if (reader.Resolve(stream.Dict.Get("Mask")) is not PdfArray &&
                TryPalettizeJpeg(stream, width, height))
                return false;
            if (quality >= 75) return false;                             // default/high quality: leave JPEGs intact
            TryReencodeJpeg(stream, width, height, quality);
            return false;
        }
        return true;
    }
}
