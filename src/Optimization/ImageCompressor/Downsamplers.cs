using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Optimization;

internal static partial class ImageCompressor
{
    /// <summary>How an over-resolved image is brought down to the target resolution.</summary>
    private enum Resample { None, Jpeg, Bilevel, PackedGray, Samples, Indexed }

    private const double PointsPerInch = 72.0;

    // Below this target resolution box-averaging a fax scan to grey destroys the very text it
    // exists to carry, for a negligible saving over its existing G4 compression.
    private const int BilevelLegibilityFloorDpi = 60;

    /// <summary>The resolution of an image of the given pixel size where it is drawn largest:
    /// the higher of its two axes'.</summary>
    private static double DrawnDpi(ImageJob job, int width, int height) =>
        Math.Max(width * PointsPerInch / job.DrawnWidth, height * PointsPerInch / job.DrawnHeight);

    /// <summary>The reader's part of the downsample pass: the route and target size follow
    /// from the dictionary and the drawn size alone, and only an image that will be resampled
    /// is decoded. An image no page draws has no resolution to judge and is left as it is.</summary>
    private static ImageJob? PrepareDownsample(PdfStream stream, PdfReader reader, int maxDpi, int quality,
        HashSet<PdfStream> maskStreams, Dictionary<PdfStream, (double Width, double Height)> drawnSizes)
    {
        if (!drawnSizes.TryGetValue(stream, out var drawn) || drawn.Width <= 0 || drawn.Height <= 0) return null;
        var job = DescribeImage(stream, reader, maskStreams, quality, maxDpi);
        if (job is null) return null;
        (job.DrawnWidth, job.DrawnHeight) = drawn;
        job.GrayOrCcitt = IsGrayOrCcitt(stream, reader);
        job.Indexed = reader.Resolve(stream.Dict.Get("ColorSpace")) is PdfArray { Count: > 0 } cs
            && reader.Resolve(cs[0]) is PdfName { Value: "Indexed" or "I" };
        job.Route = RouteFor(job);
        if (job.Route == Resample.None) return null;
        // PDF samples: 0 = black, 1 = white for DeviceGray, unless /Decode [1 0] inverts it.
        if (job.Route == Resample.Bilevel && reader.Resolve(stream.Dict.Get("Decode")) is PdfArray dec && dec.Count >= 2)
        {
            var d0 = dec[0] is PdfInteger di ? di.Value : (dec[0] as PdfReal)?.Value ?? 0;
            job.InvertBilevel = d0 >= 0.5;
        }
        job.Decoded = SafeDecode(stream, reader);
        return job;
    }

    /// <summary>Pick the resample for an image and set its target size. A JPEG's target follows
    /// from its frame and is decided by the recode; every other image is routed by depth.</summary>
    private static Resample RouteFor(ImageJob job)
    {
        if (job.FilterName is "DCTDecode") return Resample.Jpeg;

        var estimatedDpi = DrawnDpi(job, job.Width, job.Height);
        if (estimatedDpi <= job.MaxDpi) return Resample.None;
        var scaleFactor = job.MaxDpi / estimatedDpi;
        job.NewWidth = Math.Max(1, (int)(job.Width * scaleFactor));
        job.NewHeight = Math.Max(1, (int)(job.Height * scaleFactor));
        // Don't bother if reduction is trivial
        if (job.NewWidth >= job.Width && job.NewHeight >= job.Height) return Resample.None;

        // A palette image (often one the compression pass just made) keeps its palette.
        if (job.Indexed) return job.Bpc is 1 or 2 or 4 or 8 ? Resample.Indexed : Resample.None;

        // Bilevel (1-bit) scans — typically high-resolution CCITT G4 fax images — are by far
        // the largest objects in scanned documents, and shrink dramatically once down-rezzed,
        // while the target resolution still holds legible text.
        if (job.Bpc == 1 && !job.ImageMask && job.GrayOrCcitt)
            return job.MaxDpi < BilevelLegibilityFloorDpi ? Resample.None : Resample.Bilevel;

        // Sub-byte grayscale (2- or 4-bit DeviceGray, e.g. quantized scans) is unpacked to one
        // byte per pixel first: fewer, wider samples more than offset the 2→8 bit growth, and
        // the generic 8-bit path cannot read the packed rows.
        if (job.Bpc is 2 or 4 && job.Components == 1 && !job.ImageMask && job.ColorSpaceName == "DeviceGray")
            return Resample.PackedGray;

        return job.Bpc == 8 && job.Components > 0 ? Resample.Samples : Resample.None;
    }

    private static ImageRecode? Downsample(ImageJob job)
    {
        if (job.Decoded is not { } decoded) return null;
        return job.Route switch
        {
            Resample.Jpeg => DownsampleJpeg(job, decoded),
            Resample.Bilevel => DownsampleBilevel(job, decoded),
            Resample.PackedGray => DownsamplePackedGray(job, decoded),
            Resample.Samples => DownsampleSamples(job, decoded),
            Resample.Indexed => DownsampleIndexed(job, decoded),
            _ => null,
        };
    }

    /// <summary>A palette image at 1, 2, 4 or 8 bits: its samples are palette entries, which
    /// cannot be averaged, so each target pixel takes the entry at the centre of its block, at
    /// the same depth and under the same palette.</summary>
    private static ImageRecode? DownsampleIndexed(ImageJob job, byte[] decoded)
    {
        var bpc = job.Bpc;
        var rowBytes = (job.Width * bpc + 7) / 8;
        if (decoded.Length < rowBytes * job.Height) return null;
        var newRowBytes = (job.NewWidth * bpc + 7) / 8;
        var packed = new byte[newRowBytes * job.NewHeight];
        var mask = (1 << bpc) - 1;
        for (var y = 0; y < job.NewHeight; y++)
        {
            var srcRow = (int)((2L * y + 1) * job.Height / (2 * job.NewHeight)) * rowBytes;
            for (var x = 0; x < job.NewWidth; x++)
            {
                var srcBit = (int)((2L * x + 1) * job.Width / (2 * job.NewWidth)) * bpc;
                var sample = (decoded[srcRow + (srcBit >> 3)] >> (8 - bpc - (srcBit & 7))) & mask;
                var dstBit = x * bpc;
                packed[y * newRowBytes + (dstBit >> 3)] |= (byte)(sample << (8 - bpc - (dstBit & 7)));
            }
        }
        return new ImageRecode(Compress(packed), "FlateDecode", null, null, job.NewWidth, job.NewHeight);
    }

    /// <summary>True for a single-component (grayscale) image, including CCITT fax streams
    /// whose /ColorSpace may be absent (G4 fax is implicitly bilevel gray).</summary>
    private static bool IsGrayOrCcitt(PdfStream stream, PdfReader reader)
    {
        var filterName = GetFilterName(stream);
        if (filterName is "CCITTFaxDecode" or "CCF") return true;
        var csObj = reader.Resolve(stream.Dict.Get("ColorSpace"));
        return csObj is PdfName { Value: "DeviceGray" or "G" or "CalGray" };
    }

    /// <summary>
    /// Decode a JPEG XObject, box-filter it down to the target DPI when it is over-resolved,
    /// and re-encode at the requested quality. Only grayscale/RGB JPEGs are handled (no
    /// managed CMYK-JPEG re-encoder). Kept only when the result is smaller.
    /// </summary>
    private static ImageRecode? DownsampleJpeg(ImageJob job, byte[] raw)
    {
        // The target size follows from the frame's size alone, so the frame is decoded no
        // larger than the reduction needs: at a half, a quarter or an eighth when the target
        // is that much smaller, and box-averaged the rest of the way.
        var (frameWidth, frameHeight) = JpegDecoder.FrameSize(raw);
        if (frameWidth <= 0 || frameHeight <= 0) return null;
        var estimatedDpi = DrawnDpi(job, frameWidth, frameHeight);
        var scale = estimatedDpi > job.MaxDpi ? job.MaxDpi / estimatedDpi : 1.0;
        var newWidth = Math.Max(1, (int)(frameWidth * scale));
        var newHeight = Math.Max(1, (int)(frameHeight * scale));

        int w, h, comp;
        byte[] px;
        try
        {
            (px, w, h, comp) = JpegDecoder.Decode(raw, Devices.SoftwarePageRenderer.CmykDecodeInverts(job.Stream.Dict),
                scaleDenominator: JpegDecoder.ScaleDenominatorFor(frameWidth, frameHeight, newWidth, newHeight));
        }
        catch
        {
            return null;
        }
        if (w <= 0 || h <= 0 || (comp != 1 && comp != 3) || px.Length < w * h * comp) return null;

        if (Shown(job, comp == 3 ? px : ExpandGrayToRgb(px, w * h), job.Components) is not { } srcRgb) return null;
        var rgb = newWidth == w && newHeight == h
            ? srcRgb
            : BoxFilterDownsample(srcRgb, w, h, 3, newWidth, newHeight);

        var jpeg = EncodeRgbJpeg(rgb, newWidth, newHeight, job.Quality);
        if (jpeg.Length == 0 || jpeg.Length >= job.Raw.Length) return null;
        return new ImageRecode(jpeg, "DCTDecode", new PdfName("DeviceRGB"), 8, newWidth, newHeight, DropDecode: true);
    }

    /// <summary>
    /// Expand a 1-bit image to a byte per pixel, box-filter it down to the target size,
    /// threshold the averaged greys back to 1-bit, and re-store as Flate. Bilevel scans must
    /// stay bilevel — re-encoding their sharp edges as a JPEG bloats rather than shrinks them.
    /// Honours a /Decode [1 0] inversion. Kept only when smaller.
    /// </summary>
    private static ImageRecode? DownsampleBilevel(ImageJob job, byte[] decoded)
    {
        var width = job.Width;
        var height = job.Height;
        var rowBytes = (width + 7) / 8;
        if (decoded.Length < rowBytes * height) return null;

        var gray = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var rowOff = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var bit = (decoded[rowOff + (x >> 3)] >> (7 - (x & 7))) & 1;
                if (job.InvertBilevel) bit ^= 1;
                gray[y * width + x] = bit != 0 ? (byte)255 : (byte)0;
            }
        }

        var newWidth = job.NewWidth;
        var newHeight = job.NewHeight;
        var down = BoxFilterDownsample(gray, width, height, 1, newWidth, newHeight);

        // Threshold back to 1-bit and pack (MSB first), 1 = white.
        var newRowBytes = (newWidth + 7) / 8;
        var packed = new byte[newRowBytes * newHeight];
        for (var y = 0; y < newHeight; y++)
        {
            var rowOff = y * newRowBytes;
            for (var x = 0; x < newWidth; x++)
            {
                if (down[y * newWidth + x] >= 128)
                    packed[rowOff + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
            }
        }

        var compressed = Compress(packed);
        if (compressed.Length >= job.Raw.Length) return null;
        return new ImageRecode(compressed, "FlateDecode", new PdfName("DeviceGray"), 1, newWidth, newHeight, DropDecode: true);
    }

    /// <summary>A packed 2- or 4-bit gray image, unpacked to a byte per pixel, box-filtered and
    /// re-stored as 8-bit gray Flate.</summary>
    private static ImageRecode? DownsamplePackedGray(ImageJob job, byte[] packed)
    {
        var width = job.Width;
        var height = job.Height;
        var bpc = job.Bpc;
        var rowBytes = (width * bpc + 7) / 8;
        if (packed.Length < rowBytes * height) return null;
        var maxVal = (1 << bpc) - 1;
        var gray = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var rowOff = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var bitPos = x * bpc;
                var b = packed[rowOff + (bitPos >> 3)];
                var shift = 8 - bpc - (bitPos & 7);
                var sample = (b >> shift) & maxVal;
                gray[y * width + x] = (byte)(sample * 255 / maxVal);
            }
        }

        var down = BoxFilterDownsample(gray, width, height, 1, job.NewWidth, job.NewHeight);
        return new ImageRecode(Compress(down), "FlateDecode", null, 8, job.NewWidth, job.NewHeight);
    }

    /// <summary>An 8-bit image of any component count, box-filtered and re-stored as Flate.</summary>
    private static ImageRecode? DownsampleSamples(ImageJob job, byte[] decoded)
    {
        var expectedSize = job.Width * job.Height * job.Components;
        if (decoded.Length < expectedSize) return null;
        var downsampled = BoxFilterDownsample(decoded, job.Width, job.Height, job.Components, job.NewWidth, job.NewHeight);
        return new ImageRecode(Compress(downsampled), "FlateDecode", null, null, job.NewWidth, job.NewHeight);
    }

    /// <summary>The reader's part of the grayscale pass: an 8-bit DeviceRGB image that is not
    /// a JPEG (which cannot easily be re-encoded).</summary>
    private static ImageJob? PrepareGrayscale(PdfStream stream, PdfReader reader)
    {
        var job = DescribeImage(stream, reader, [], 0, 0);
        if (job is null || job.ColorSpaceName != "DeviceRGB" || job.Bpc != 8 || job.FilterName is "DCTDecode")
            return null;
        job.Decoded = SafeDecode(stream, reader);
        return job;
    }

    /// <summary>RGB to grayscale by the ITU-R BT.601 luma coefficients, re-stored as Flate.</summary>
    private static ImageRecode? ToGrayscale(ImageJob job)
    {
        var pixels = job.Width * job.Height;
        if (job.Decoded is not { } decoded || decoded.Length < pixels * 3) return null;

        var gray = new byte[pixels];
        for (var i = 0; i < pixels; i++)
        {
            var r = decoded[i * 3];
            var g = decoded[i * 3 + 1];
            var b = decoded[i * 3 + 2];
            gray[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
        }
        return new ImageRecode(Compress(gray), "FlateDecode", new PdfName("DeviceGray"));
    }
}
