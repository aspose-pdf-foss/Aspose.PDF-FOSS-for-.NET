#nullable disable

namespace Aspose.Pdf
{
    public interface IIndexBitmapConverter
    {
        System.Drawing.Bitmap Get1BppImage(System.Drawing.Bitmap src);
        System.Drawing.Bitmap Get4BppImage(System.Drawing.Bitmap src);
        System.Drawing.Bitmap Get8BppImage(System.Drawing.Bitmap src);
    }

    public class BitmapInfo
    {
        public BitmapInfo() { }

        public BitmapInfo(byte[] pixelBytes, int width, int height, PixelFormat format)
        {
            PixelBytes = pixelBytes;
            Width = width;
            Height = height;
            Format = format;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] PixelBytes { get; }
        public PixelFormat Format { get; }
        public enum PixelFormat { Bgra32, Bgr24, Gray8, Rgba32, Rgb24, Argb32 }
    }
}
