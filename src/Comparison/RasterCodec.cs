using System.IO;
using Aspose.Pdf.Devices;
using GdiImageFormat = System.Drawing.Imaging.ImageFormat;

// An image format here is a NAME the caller hands over (ImageFormat.Png); no GDI+ call is made on it, and the
// managed codecs write the file on every platform.
#pragma warning disable CA1416
namespace Aspose.Pdf.Comparison
{
    /// <summary>Encodes a 24-bit RGB raster with the library's own codecs, in the format a caller names.</summary>
    internal static class RasterCodec
    {
        private const int RgbBytesPerPixel = 3;
        private const int RgbaBytesPerPixel = 4;
        private const byte Opaque = 255;
        private const int PngColorTypeRgb = 2;
        private const int PngBitDepth = 8;

        /// <summary>The raster (rows padded as <see cref="ImagesDifference.Stride"/> says) as a file of
        /// <paramref name="format"/> (PNG for any format the codecs do not write).</summary>
        internal static byte[] Encode(byte[] padded, int width, int height, GdiImageFormat format, int jpegQuality, Resolution resolution)
        {
            var rgb = Tight(padded, width, height);
            if (Equals(format, GdiImageFormat.Jpeg))
                return IO.JpegEncoderImpl.Encode(WithAlpha(rgb, width, height), width, height, jpegQuality, (int)resolution.X, (int)resolution.Y);
            if (Equals(format, GdiImageFormat.Bmp))
                return BmpDevice.EncodeBmp(WithAlpha(rgb, width, height), width, height);
            if (Equals(format, GdiImageFormat.Gif))
                return GifDevice.EncodeGif(WithAlpha(rgb, width, height), width, height);
            if (Equals(format, GdiImageFormat.Tiff))
            {
                using var tiff = new MemoryStream();
                TiffDevice.EncodeRgbaImage(WithAlpha(rgb, width, height), width, height, tiff, CompressionType.LZW);
                return tiff.ToArray();
            }
            return IO.PngEncoder.Encode(rgb, width, height, PngColorTypeRgb, PngBitDepth);
        }

        /// <summary>The padded RGB rows as the tightly packed rows the codecs read.</summary>
        private static byte[] Tight(byte[] padded, int width, int height)
        {
            int stride = ImagesDifference.StrideFor(width), row = width * RgbBytesPerPixel;
            if (stride == row) return padded;
            var rgb = new byte[row * height];
            for (int y = 0; y < height; y++)
                System.Array.Copy(padded, y * stride, rgb, y * row, row);
            return rgb;
        }

        /// <summary>The RGB rows as the RGBA rows the other codecs read, every pixel opaque.</summary>
        private static byte[] WithAlpha(byte[] rgb, int width, int height)
        {
            var rgba = new byte[width * height * RgbaBytesPerPixel];
            for (int pixel = 0; pixel < width * height; pixel++)
            {
                int src = pixel * RgbBytesPerPixel;
                int dst = pixel * RgbaBytesPerPixel;
                rgba[dst] = rgb[src];
                rgba[dst + 1] = rgb[src + 1];
                rgba[dst + 2] = rgb[src + 2];
                rgba[dst + 3] = Opaque;
            }
            return rgba;
        }
    }
}
