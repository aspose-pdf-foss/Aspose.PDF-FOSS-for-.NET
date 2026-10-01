#nullable disable

namespace Aspose.Pdf
{
    /// <summary>A converter that reduces a bitmap to 1-, 4- or 8-bit indexed colour. The library defines this interface but never calls it.</summary>
    public interface IIndexBitmapConverter
    {
        System.Drawing.Bitmap Get1BppImage(System.Drawing.Bitmap src);
        System.Drawing.Bitmap Get4BppImage(System.Drawing.Bitmap src);
        System.Drawing.Bitmap Get8BppImage(System.Drawing.Bitmap src);
    }

    /// <summary>Raw pixel data with its size and pixel layout, used to add an uncompressed bitmap to a page's image resources.</summary>
    public class BitmapInfo
    {
        /// <summary>Creates an empty bitmap description with no pixels and a size of 0 x 0.</summary>
        public BitmapInfo() { }

        public BitmapInfo(byte[] pixelBytes, int width, int height, PixelFormat format)
        {
            PixelBytes = pixelBytes;
            Width = width;
            Height = height;
            Format = format;
        }

        /// <summary>Gets the image width in pixels.</summary>
        public int Width { get; }
        /// <summary>Gets the image height in pixels.</summary>
        public int Height { get; }
        public byte[] PixelBytes { get; }
        public PixelFormat Format { get; }
        public enum PixelFormat { Bgra32, Bgr24, Gray8, Rgba32, Rgb24, Argb32 }
    }
}
