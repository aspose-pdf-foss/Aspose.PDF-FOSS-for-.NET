using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
// The public colour API is Aspose.Pdf.Color (the bare Color in this namespace); the GDI+
// Rectangle used for LockBits is aliased since bare Rectangle would resolve to Aspose.Pdf.Rectangle.
using GdiRectangle = System.Drawing.Rectangle;

namespace Aspose.Pdf.Comparison
{
    /// <summary>
    /// Result of a graphical comparison of two rendered PDF pages: the first (source) page
    /// rasterised to a 24bpp bitmap, plus a per-pixel record of where the second (destination)
    /// page differs from it. Produced by <see cref="GraphicalPdfComparer.GetDifference"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class ImagesDifference : IDisposable
    {
        /// <summary>Sentinel stored in <see cref="Difference"/> for pixels identical in both pages.</summary>
        internal const int Same = -1;

        // Compose modes for <see cref="Compose"/>.
        internal const int ModeDestination = 0; // reconstruct the destination page
        internal const int ModeMask = 1;        // fg where different, bg where identical
        internal const int ModeOverlay = 2;     // source page with differing pixels painted fg

        private Bitmap _source;
        private bool _disposed;

        internal ImagesDifference(Bitmap sourceImage, int[] difference, int stride, int height)
        {
            _source = sourceImage;
            Difference = difference;
            Stride = stride;
            Height = height;
        }

        /// <summary>The first (source) page rendered to a 24bpp RGB bitmap.</summary>
        public Bitmap SourceImage => _source;

        /// <summary>
        /// Per-pixel difference record, row-major (index = y * <c>Width</c> + x). A value of
        /// <c>-1</c> means the pixel is identical in both pages; any other value is the packed
        /// <c>0xRRGGBB</c> colour of the destination page at that pixel.
        /// </summary>
        public int[] Difference { get; }

        /// <summary>Row stride (in bytes) of <see cref="SourceImage"/> at 24bpp.</summary>
        public int Stride { get; }

        /// <summary>Pixel height of the compared images.</summary>
        public int Height { get; }

        private int Width => _source.Width;

        /// <summary>
        /// Reconstruct the second (destination) page image: identical pixels are copied from the
        /// source, differing pixels take their recorded destination colour.
        /// </summary>
        public Bitmap GetDestinationImage()
        {
            return Compose(ModeDestination, Color.Black, Color.Black);
        }

        /// <summary>
        /// Produce a difference mask: <paramref name="color"/> where the two pages differ and
        /// <paramref name="backgroundColor"/> where they are identical.
        /// </summary>
        public Bitmap DifferenceToImage(Color color, Color backgroundColor)
        {
            return Compose(ModeMask, color, backgroundColor);
        }

        /// <summary>
        /// Build a 24bpp bitmap from the source raster and the difference record.
        /// </summary>
        internal Bitmap Compose(int mode, Color fg, Color bg)
        {
            int w = Width, h = Height;

            var srcRect = new GdiRectangle(0, 0, w, h);
            var srcData = _source.LockBits(srcRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            byte[] src;
            int srcStride = srcData.Stride;
            try
            {
                src = new byte[srcStride * h];
                Marshal.Copy(srcData.Scan0, src, 0, src.Length);
            }
            finally
            {
                _source.UnlockBits(srcData);
            }

            var outBmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var outData = outBmp.LockBits(srcRect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                int outStride = outData.Stride;
                var dst = new byte[outStride * h];
                for (int y = 0; y < h; y++)
                {
                    int srow = y * srcStride;
                    int orow = y * outStride;
                    int drow = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int di = Difference[drow + x];
                        byte r, g, b;
                        if (di == Same)
                        {
                            if (mode == ModeMask)
                            {
                                r = bg.R; g = bg.G; b = bg.B;
                            }
                            else
                            {
                                // Destination and overlay keep the source colour for identical pixels.
                                int si = srow + x * 3;
                                b = src[si]; g = src[si + 1]; r = src[si + 2];
                            }
                        }
                        else if (mode == ModeDestination)
                        {
                            r = (byte)((di >> 16) & 0xFF);
                            g = (byte)((di >> 8) & 0xFF);
                            b = (byte)(di & 0xFF);
                        }
                        else
                        {
                            // Mask foreground / overlay highlight colour.
                            r = fg.R; g = fg.G; b = fg.B;
                        }

                        int oi = orow + x * 3;
                        dst[oi] = b; dst[oi + 1] = g; dst[oi + 2] = r;
                    }
                }
                Marshal.Copy(dst, 0, outData.Scan0, dst.Length);
            }
            finally
            {
                outBmp.UnlockBits(outData);
            }

            return outBmp;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _source?.Dispose();
            _source = null!;
        }
    }
}
