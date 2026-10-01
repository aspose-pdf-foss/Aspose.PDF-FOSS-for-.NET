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
    /// The per-pixel difference between two rendered pages, as <see cref="GraphicalPdfComparer.GetDifference"/>
    /// computes it. The rasters are managed 24-bit RGB rows; the <see cref="Bitmap"/> views are built on demand
    /// and need the platform's GDI+, which the file and PDF outputs of the comparer do not.
    /// </summary>
    public sealed class ImagesDifference : IDisposable
    {
        /// <summary>The difference entry of a pixel the two pages agree on.</summary>
        internal const int Same = -1;

        /// <summary>Bytes per pixel of the rasters this class holds (24bpp RGB).</summary>
        internal const int BytesPerPixel = 3;

        /// <summary>Every raster row is padded to a multiple of this many bytes, as a 24bpp platform bitmap's is.</summary>
        internal const int RowAlignment = 4;

        // Compose modes for <see cref="Compose"/>.
        internal const int ModeDestination = 0; // reconstruct the destination page
        internal const int ModeMask = 1;        // fg where different, bg where identical
        internal const int ModeOverlay = 2;     // source page with differing pixels painted fg

        private const int ChannelMask = 0xFF;
        private const int RedShift = 16;
        private const int GreenShift = 8;

        private readonly byte[] _source;
        private bool _disposed;

        /// <summary>Wraps the source raster (RGB rows of <paramref name="width"/> pixels) and the
        /// per-pixel difference against the destination.</summary>
        internal ImagesDifference(byte[] sourceRgb, int width, int[] difference, int height)
        {
            _source = sourceRgb;
            Width = width;
            Difference = difference;
            Height = height;
        }

        /// <summary>The first page as rendered (GDI+ view; see <see cref="SourceRgb"/> for the raster).</summary>
        [SupportedOSPlatform("windows")]
        public Bitmap SourceImage => ToBitmap(_source, Width, Height);

        /// <summary>
        /// One entry per pixel, row by row: <c>-1</c> where the two pages agree, otherwise the
        /// destination page's colour as <c>0xRRGGBB</c>.
        /// </summary>
        public int[] Difference { get; }

        /// <summary>Bytes per row of the source raster: the pixels, padded to <see cref="RowAlignment"/>.</summary>
        public int Stride => StrideFor(Width);

        /// <summary>The padded row length of a raster <paramref name="width"/> pixels wide.</summary>
        internal static int StrideFor(int width) =>
            (width * BytesPerPixel + RowAlignment - 1) / RowAlignment * RowAlignment;

        /// <summary>Height of the rasters in pixels.</summary>
        public int Height { get; }

        /// <summary>Width of the rasters in pixels.</summary>
        internal int Width { get; }

        /// <summary>The first page's raster: RGB rows, <see cref="Stride"/> bytes each.</summary>
        internal byte[] SourceRgb => _source;

        /// <summary>The second page, rebuilt from the source and the difference.</summary>
        [SupportedOSPlatform("windows")]
        public Bitmap GetDestinationImage()
        {
            return ToBitmap(Compose(ModeDestination, Color.Black, Color.Black), Width, Height);
        }

        /// <summary>A mask: <paramref name="color"/> where the pages differ, <paramref name="backgroundColor"/> elsewhere.</summary>
        [SupportedOSPlatform("windows")]
        public Bitmap DifferenceToImage(Color color, Color backgroundColor)
        {
            return ToBitmap(Compose(ModeMask, color, backgroundColor), Width, Height);
        }

        /// <summary>The raster a compose mode describes: RGB rows of <see cref="Stride"/> bytes.</summary>
        internal byte[] Compose(int mode, Color fg, Color bg)
        {
            int w = Width, h = Height;
            var dst = new byte[Stride * h];
            for (int y = 0; y < h; y++)
            {
                int row = y * Stride;
                int drow = y * w;
                for (int x = 0; x < w; x++)
                {
                    int di = Difference[drow + x];
                    int oi = row + x * BytesPerPixel;
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
                            r = _source[oi]; g = _source[oi + 1]; b = _source[oi + 2];
                        }
                    }
                    else if (mode == ModeDestination)
                    {
                        r = (byte)((di >> RedShift) & ChannelMask);
                        g = (byte)((di >> GreenShift) & ChannelMask);
                        b = (byte)(di & ChannelMask);
                    }
                    else
                    {
                        // Mask foreground / overlay highlight colour.
                        r = fg.R; g = fg.G; b = fg.B;
                    }
                    dst[oi] = r; dst[oi + 1] = g; dst[oi + 2] = b;
                }
            }
            return dst;
        }

        /// <summary>A GDI+ bitmap over an RGB raster (Windows only; the managed outputs never need it).</summary>
        [SupportedOSPlatform("windows")]
        internal static Bitmap ToBitmap(byte[] rgb, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var data = bitmap.LockBits(new GdiRectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var rows = new byte[data.Stride * height];
                for (int y = 0; y < height; y++)
                {
                    int src = y * StrideFor(width);
                    int dst = y * data.Stride;
                    for (int x = 0; x < width; x++)
                    {
                        // GDI+ rows are BGR.
                        rows[dst + x * BytesPerPixel] = rgb[src + x * BytesPerPixel + 2];
                        rows[dst + x * BytesPerPixel + 1] = rgb[src + x * BytesPerPixel + 1];
                        rows[dst + x * BytesPerPixel + 2] = rgb[src + x * BytesPerPixel];
                    }
                }
                Marshal.Copy(rows, 0, data.Scan0, rows.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        /// <summary>Nothing unmanaged is held; kept for the callers that dispose the result.</summary>
        public void Dispose()
        {
            _disposed = true;
        }

        /// <summary>Whether <see cref="Dispose"/> was called.</summary>
        internal bool IsDisposed => _disposed;
    }
}
