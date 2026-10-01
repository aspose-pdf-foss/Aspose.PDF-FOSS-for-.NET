using System;
using System.IO;
using Aspose.Pdf.Devices;
using GdiImageFormat = System.Drawing.Imaging.ImageFormat;

// An image format here is a NAME the caller hands over (ImageFormat.Png); no GDI+ call is made on it, and the
// managed codecs write the file on every platform.
#pragma warning disable CA1416
namespace Aspose.Pdf.Comparison
{
    /// <summary>
    /// Compares two PDF pages/documents graphically by rendering them to rasters and highlighting
    /// the pixels that differ. Differences can be written as an image overlay (the source page with
    /// changed pixels painted in <see cref="Color"/>) or collected into a PDF report. Rendering,
    /// comparison and the image and PDF outputs are managed code; only the <see cref="ImagesDifference"/>
    /// bitmap views need the platform's GDI+.
    /// </summary>
    public class GraphicalPdfComparer
    {
        /// <summary>Bytes per pixel of the rasters this comparer works with (24bpp RGB).</summary>
        internal const int BytesPerPixel = ImagesDifference.BytesPerPixel;

        /// <summary>Bytes per pixel of what the page renderer answers (RGBA).</summary>
        private const int RenderedBytesPerPixel = 4;

        // Pages are rendered at SuperSampleFactor x the requested resolution and box-downsampled
        // with a darkening coverage gamma. GDI+ path-fill anti-aliasing blends glyph coverage
        // linearly in sRGB, which leaves text edges lighter than a rasteriser that
        // applies a Windows-style font gamma to true coverage. Rendering high then downsampling
        // with the gamma reproduces that heavier edge ramp so the rasters line up within the
        // comparison tolerance. Confined to the comparer — the shared page renderer is untouched.
        private const int SuperSampleFactor = 3;
        private const double AntiAliasGamma = 3.0;

        private const int DefaultDpi = 150;
        private const double PercentScale = 100.0;
        private const double ChannelMax = 255.0;
        private const int JpegQuality = 100;
        private const int RedShift = 16;
        private const int GreenShift = 8;

        // The result PDF always uses a fixed A4 page box (integer points), independent of the
        // source page size, DPI, or overlay aspect ratio: the overlay image is stretched to fill it.
        private const double ResultPageWidth = 595;
        private const double ResultPageHeight = 842;

        private double _threshold;

        /// <summary>Rendering resolution used to rasterise the pages. Defaults to 150 DPI.</summary>
        public Resolution Resolution { get; set; } = new Resolution(DefaultDpi);

        /// <summary>Colour used to highlight differing pixels in image output. Defaults to red.</summary>
        public Color Color { get; set; } = Color.Red;

        /// <summary>
        /// Tolerance, as a percentage (0..100), of per-channel colour difference below which two
        /// pixels are considered identical. 0 (the default) treats any colour difference as a change.
        /// </summary>
        public double Threshold
        {
            get { return _threshold; }
            set { _threshold = value; }
        }

        /// <summary>Creates a comparer with default settings (150 DPI, red highlight, exact threshold).</summary>
        public GraphicalPdfComparer()
        {
        }

        /// <summary>
        /// Render both pages and compute their per-pixel difference.
        /// </summary>
        public ImagesDifference GetDifference(Page page1, Page page2)
        {
            if (page1 == null) throw new ArgumentNullException(nameof(page1));
            if (page2 == null) throw new ArgumentNullException(nameof(page2));

            var (s1, w, h) = RenderPage(page1);
            var (s2, w2, h2) = RenderPage(page2);
            var diff = new int[w * h];
            int tol = (int)Math.Round(_threshold / PercentScale * ChannelMax);
            int stride1 = ImagesDifference.StrideFor(w), stride2 = ImagesDifference.StrideFor(w2);
            for (int y = 0; y < h; y++)
            {
                int row1 = y * stride1;
                int row2 = y * stride2;
                int drow = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (x >= w2 || y >= h2)
                    {
                        diff[drow + x] = ImagesDifference.Same;
                        continue;
                    }
                    int p1 = row1 + x * BytesPerPixel;
                    int p2 = row2 + x * BytesPerPixel;
                    byte r2 = s2[p2], g2 = s2[p2 + 1], b2 = s2[p2 + 2];
                    int d = Math.Abs(s1[p1] - r2);
                    int dg = Math.Abs(s1[p1 + 1] - g2);
                    int db = Math.Abs(s1[p1 + 2] - b2);
                    if (dg > d) d = dg;
                    if (db > d) d = db;
                    diff[drow + x] = d > tol ? (r2 << RedShift) | (g2 << GreenShift) | b2 : ImagesDifference.Same;
                }
            }
            return new ImagesDifference(s1, w, diff, h);
        }

        /// <summary>
        /// Compare two pages and write the highlighted overlay (source page with differing pixels
        /// painted <see cref="Color"/>) to an image file. Output format follows the file extension.
        /// </summary>
        public void ComparePagesToImage(Page page1, Page page2, string resultImagePath)
        {
            using ImagesDifference difference = GetDifference(page1, page2);
            File.WriteAllBytes(resultImagePath, Overlay(difference, FormatFromExtension(resultImagePath)));
        }

        /// <summary>
        /// Compare two documents page by page, writing one highlighted overlay image per page into
        /// <paramref name="targetDirectory"/>. Files are named
        /// <c>&lt;fileNamePrefix&gt;&lt;pageIndex&gt;.&lt;ext&gt;</c> (1-based).
        /// </summary>
        public void CompareDocumentsToImages(Document document1, Document document2, string targetDirectory, string fileNamePrefix, GdiImageFormat imageFormat)
        {
            if (document1 == null) throw new ArgumentNullException(nameof(document1));
            if (document2 == null) throw new ArgumentNullException(nameof(document2));

            int count = Math.Min(document1.Pages.Count, document2.Pages.Count);
            string extension = ExtensionForFormat(imageFormat);
            Directory.CreateDirectory(targetDirectory);
            for (int i = 1; i <= count; i++)
            {
                using ImagesDifference difference = GetDifference(document1.Pages[i], document2.Pages[i]);
                string path = Path.Combine(targetDirectory, fileNamePrefix + i + extension);
                File.WriteAllBytes(path, Overlay(difference, imageFormat));
            }
        }

        /// <summary>
        /// Compare two pages and append the highlighted overlay as a page in a PDF written to
        /// <paramref name="resultPdfPath"/>.
        /// </summary>
        public void ComparePagesToPdf(Page page1, Page page2, string resultPdfPath)
        {
            using var doc = new Document();
            AppendOverlayPage(doc, page1, page2);
            doc.Save(resultPdfPath);
        }

        /// <summary>
        /// Compare two pages and append the highlighted overlay as a page in <paramref name="pdfDocument"/>.
        /// </summary>
        public void ComparePagesToPdf(Page page1, Page page2, Document pdfDocument)
        {
            if (pdfDocument == null) throw new ArgumentNullException(nameof(pdfDocument));
            AppendOverlayPage(pdfDocument, page1, page2);
        }

        /// <summary>
        /// Compare two documents page by page and write a single PDF whose pages hold the
        /// highlighted overlays, to <paramref name="resultPdfPath"/>.
        /// </summary>
        public void CompareDocumentsToPdf(Document document1, Document document2, string resultPdfPath)
        {
            if (document1 == null) throw new ArgumentNullException(nameof(document1));
            if (document2 == null) throw new ArgumentNullException(nameof(document2));

            using var doc = new Document();
            int count = Math.Min(document1.Pages.Count, document2.Pages.Count);
            for (int i = 1; i <= count; i++)
            {
                AppendOverlayPage(doc, document1.Pages[i], document2.Pages[i]);
            }
            doc.Save(resultPdfPath);
        }

        private void AppendOverlayPage(Document doc, Page page1, Page page2)
        {
            using ImagesDifference difference = GetDifference(page1, page2);
            Page page = doc.Pages.Add();
            page.SetPageSize(ResultPageWidth, ResultPageHeight);
            page.PageInfo.Margin = new MarginInfo(0, 0, 0, 0);
            using var ms = new MemoryStream(Overlay(difference, GdiImageFormat.Png));
            page.AddImage(ms, new Rectangle(0, 0, ResultPageWidth, ResultPageHeight));
        }

        /// <summary>The source page with its differing pixels painted <see cref="Color"/>, encoded as <paramref name="format"/>.</summary>
        private byte[] Overlay(ImagesDifference difference, GdiImageFormat format)
        {
            var rgb = difference.Compose(ImagesDifference.ModeOverlay, Color, Color.Black);
            return RasterCodec.Encode(rgb, difference.Width, difference.Height, format, JpegQuality, Resolution ?? new Resolution(DefaultDpi));
        }

        /// <summary>The page as an RGB raster at <see cref="Resolution"/>, supersampled and downsampled with the coverage gamma.</summary>
        private (byte[] Rgb, int Width, int Height) RenderPage(Page page)
        {
            var res = Resolution ?? new Resolution(DefaultDpi);
            var hiRes = new Resolution(res.X * SuperSampleFactor, res.Y * SuperSampleFactor);
            var rendered = new PngDevice(hiRes).Render(page);
            return DownsampleWithGamma(rendered, SuperSampleFactor, AntiAliasGamma);
        }

        /// <summary>
        /// Box-downsample a supersampled RGBA render by <paramref name="ss"/> in each axis, mapping the
        /// averaged ink coverage of each output pixel through <paramref name="gamma"/> so anti-aliased
        /// edges darken the way Windows-style font-gamma AA does. Coverage is taken
        /// per channel relative to a white background, so solid fills (coverage 0 or 1) are unchanged
        /// and only partial-coverage edge pixels shift.
        /// </summary>
        private static (byte[] Rgb, int Width, int Height) DownsampleWithGamma(RgbaBuffer hi, int ss, double gamma)
        {
            int w = hi.Width / ss, h = hi.Height / ss;
            int hiStride = hi.Width * RenderedBytesPerPixel;
            var src = hi.Data;
            int stride = ImagesDifference.StrideFor(w);
            var dst = new byte[stride * h];
            double invGamma = 1.0 / gamma;
            int area = ss * ss;
            for (int y = 0; y < h; y++)
            {
                int orow = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int oi = orow + x * BytesPerPixel;
                    for (int c = 0; c < BytesPerPixel; c++) // R, G, B
                    {
                        double coverage = 0.0;
                        for (int yy = 0; yy < ss; yy++)
                        {
                            int srow = (y * ss + yy) * hiStride;
                            for (int xx = 0; xx < ss; xx++)
                                coverage += 1.0 - src[srow + (x * ss + xx) * RenderedBytesPerPixel + c] / ChannelMax;
                        }
                        coverage /= area;
                        double shaped = Math.Pow(coverage, invGamma);
                        dst[oi + c] = (byte)Math.Round(ChannelMax * (1.0 - shaped));
                    }
                }
            }
            return (dst, w, h);
        }

        private static GdiImageFormat FormatFromExtension(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
            switch (ext)
            {
                case ".jpg":
                case ".jpeg": return GdiImageFormat.Jpeg;
                case ".bmp": return GdiImageFormat.Bmp;
                case ".gif": return GdiImageFormat.Gif;
                case ".tif":
                case ".tiff": return GdiImageFormat.Tiff;
                default: return GdiImageFormat.Png;
            }
        }

        private static string ExtensionForFormat(GdiImageFormat format)
        {
            if (Equals(format, GdiImageFormat.Jpeg)) return ".jpg";
            if (Equals(format, GdiImageFormat.Bmp)) return ".bmp";
            if (Equals(format, GdiImageFormat.Gif)) return ".gif";
            if (Equals(format, GdiImageFormat.Tiff)) return ".tiff";
            return ".png";
        }
    }
}
