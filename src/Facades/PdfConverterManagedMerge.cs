using Aspose.Pdf.Devices;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfConverter
{
    /// <summary>
    /// <see cref="MergeImages"/> where there is no GDI+ (Linux, macOS, the WebAssembly engine): the inputs read by
    /// the library's own decoders (PNG, JPEG, GIF, TIFF), laid out exactly as the GDI+ path lays them out - top-left
    /// in input order for a vertical or horizontal stack, the first input alone centred on a canvas the size of
    /// the largest for Center - on white, and written by the library's own encoders (JPEG at quality 100, PNG,
    /// BMP, TIFF).
    /// </summary>
    internal static Stream MergeImagesManaged(List<Stream> inputImagesStreams,
        Aspose.Pdf.Drawing.ImageFormat outputImageFormat, ImageMergeMode mergeMode)
    {
        var inputs = DecodedInputs(inputImagesStreams);
        var (outW, outH) = mergeMode switch
        {
            ImageMergeMode.Vertical => (inputs.Max(i => i.Width), inputs.Sum(i => i.Height)),
            ImageMergeMode.Horizontal => (inputs.Sum(i => i.Width), inputs.Max(i => i.Height)),
            ImageMergeMode.Center => (inputs.Max(i => i.Width), inputs.Max(i => i.Height)),
            _ => throw new ArgumentOutOfRangeException(nameof(mergeMode)),
        };

        var canvas = new byte[outW * outH * 4];
        canvas.AsSpan().Fill(byte.MaxValue); // white, opaque
        if (mergeMode == ImageMergeMode.Center)
        {
            var first = inputs[0];
            Draw(canvas, outW, first, (outW - first.Width) / 2, (outH - first.Height) / 2);
        }
        else
        {
            var cursor = 0;
            foreach (var input in inputs)
            {
                var (x, y) = mergeMode == ImageMergeMode.Vertical ? (0, cursor) : (cursor, 0);
                Draw(canvas, outW, input, x, y);
                cursor += mergeMode == ImageMergeMode.Vertical ? input.Height : input.Width;
            }
        }

        var encoded = outputImageFormat switch
        {
            Aspose.Pdf.Drawing.ImageFormat.Jpeg => IO.JpegEncoderImpl.Encode(canvas, outW, outH, MergedJpegQuality),
            Aspose.Pdf.Drawing.ImageFormat.Png => IO.PngEncoder.Encode(Rgb(canvas), outW, outH),
            Aspose.Pdf.Drawing.ImageFormat.Bmp => BmpDevice.EncodeBmp(canvas, outW, outH),
            Aspose.Pdf.Drawing.ImageFormat.Tiff => TiffOf(new[] { (canvas, outW, outH) }),
            _ => throw new NotSupportedException(
                $"Merging images into {outputImageFormat} needs GDI+, which is Windows-only; JPEG, PNG, BMP and TIFF are written everywhere."),
        };
        return new MemoryStream(encoded, writable: false);
    }

    /// <summary><see cref="MergeImagesAsTiff"/> where there is no GDI+: one TIFF page per input, LZW-compressed,
    /// each input read by the library's own decoders.</summary>
    internal static Stream MergeImagesAsTiffManaged(List<Stream> inputImagesStreams)
        => new MemoryStream(TiffOf(DecodedInputs(inputImagesStreams)
            .Select(input => (input.Rgba, input.Width, input.Height)).ToArray()), writable: false);

    /// <summary>JPEG quality of a merged picture: the GDI+ path encodes at 100 too.</summary>
    private const int MergedJpegQuality = 100;

    private sealed record Decoded(byte[] Rgba, int Width, int Height);

    private static List<Decoded> DecodedInputs(List<Stream> streams)
    {
        var decoded = new List<Decoded>(streams.Count);
        foreach (var stream in streams)
        {
            if (stream.CanSeek) stream.Position = 0;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            var (rgb, alpha, width, height) = Aspose.Pdf.ImageStamp.DecodeRaster(copy.ToArray())
                ?? throw new ArgumentException(
                    "An input is not an image the library can read without GDI+ (PNG, JPEG, GIF or TIFF).",
                    nameof(streams));
            var rgba = new byte[width * height * 4];
            for (var at = 0; at < width * height; at++)
            {
                rgba[at * 4] = rgb[at * 3];
                rgba[at * 4 + 1] = rgb[at * 3 + 1];
                rgba[at * 4 + 2] = rgb[at * 3 + 2];
                rgba[at * 4 + 3] = alpha?[at] ?? byte.MaxValue;
            }
            decoded.Add(new Decoded(rgba, width, height));
        }
        return decoded;
    }

    /// <summary>An input drawn onto the canvas at (left, top), its alpha composited over what is there.</summary>
    private static void Draw(byte[] canvas, int canvasWidth, Decoded input, int left, int top)
    {
        var canvasHeight = canvas.Length / 4 / canvasWidth;
        for (var y = 0; y < input.Height; y++)
        {
            var cy = top + y;
            if (cy < 0 || cy >= canvasHeight) continue;
            for (var x = 0; x < input.Width; x++)
            {
                var cx = left + x;
                if (cx < 0 || cx >= canvasWidth) continue;
                var from = (y * input.Width + x) * 4;
                var to = (cy * canvasWidth + cx) * 4;
                var a = input.Rgba[from + 3];
                for (var channel = 0; channel < 3; channel++)
                    canvas[to + channel] = (byte)((input.Rgba[from + channel] * a + canvas[to + channel] * (255 - a) + 127) / 255);
                canvas[to + 3] = byte.MaxValue;
            }
        }
    }

    private static byte[] Rgb(byte[] rgba)
    {
        var rgb = new byte[rgba.Length / 4 * 3];
        for (int from = 0, to = 0; from < rgba.Length; from += 4, to += 3)
        {
            rgb[to] = rgba[from];
            rgb[to + 1] = rgba[from + 1];
            rgb[to + 2] = rgba[from + 2];
        }
        return rgb;
    }

    private static byte[] TiffOf((byte[] rgba, int w, int h)[] pages)
    {
        using var output = new MemoryStream();
        TiffDevice.EncodeRgbaImages(pages, output, CompressionType.LZW);
        return output.ToArray();
    }
}
