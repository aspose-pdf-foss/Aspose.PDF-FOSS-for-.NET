using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using Aspose.Pdf.Devices;
using GdiImageFormat = System.Drawing.Imaging.ImageFormat;
// The public colour API is Aspose.Pdf.Color (the bare Color in this namespace); the GDI+
// Rectangle used for LockBits is aliased since bare Rectangle would resolve to Aspose.Pdf.Rectangle.
using GdiRectangle = System.Drawing.Rectangle;

namespace Aspose.Pdf.Comparison
{
    /// <summary>
    /// Compares two PDF pages/documents graphically by rendering them to rasters and highlighting
    /// the pixels that differ. Differences can be written as an image overlay (the source page with
    /// changed pixels painted in <see cref="Color"/>) or collected into a PDF report.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class GraphicalPdfComparer
    {
        /// <summary>Bytes per pixel of the rasters this comparer works with (24bpp RGB).</summary>
        internal const int BytesPerPixel = 3;

        // Pages are rendered at SuperSampleFactor x the requested resolution and box-downsampled
        // with a darkening coverage gamma. GDI+ path-fill anti-aliasing blends glyph coverage
        // linearly in sRGB, which leaves text edges lighter than a rasteriser that
        // applies a Windows-style font gamma to true coverage. Rendering high then downsampling
        // with the gamma reproduces that heavier edge ramp so the rasters line up within the
        // comparison tolerance. Confined to the comparer — the shared page renderer is untouched.
        private const int SuperSampleFactor = 3;
        private const double AntiAliasGamma = 3.0;

        private double _threshold;

        /// <summary>Rendering resolution used to rasterise the pages. Defaults to 150 DPI.</summary>
        public Resolution Resolution { get; set; } = new Resolution(150);

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

            using Bitmap b1 = RenderPage(page1);
            using Bitmap b2 = RenderPage(page2);

            int w = b1.Width;
            int h = b1.Height;

            var rect1 = new GdiRectangle(0, 0, w, h);
            var data1 = b1.LockBits(rect1, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var data2 = b2.LockBits(new GdiRectangle(0, 0, b2.Width, b2.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            byte[] s1, s2;
            int st1 = data1.Stride, st2 = data2.Stride;
            try
            {
                s1 = new byte[st1 * h];
                System.Runtime.InteropServices.Marshal.Copy(data1.Scan0, s1, 0, s1.Length);
                s2 = new byte[st2 * b2.Height];
                System.Runtime.InteropServices.Marshal.Copy(data2.Scan0, s2, 0, s2.Length);
            }
            finally
            {
                b1.UnlockBits(data1);
                b2.UnlockBits(data2);
            }

            var source = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var srcData = source.LockBits(rect1, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            int srcStride = srcData.Stride;
            var diff = new int[w * h];
            int tol = (int)Math.Round(_threshold / 100.0 * 255.0);
            int w2 = b2.Width, h2 = b2.Height;
            try
            {
                var srcBytes = new byte[srcStride * h];
                for (int y = 0; y < h; y++)
                {
                    int r1row = y * st1;
                    int r2row = y * st2;
                    int orow = y * srcStride;
                    int drow = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int p1 = r1row + x * 4; // BGRA
                        byte b1B = s1[p1], b1G = s1[p1 + 1], b1R = s1[p1 + 2];

                        int oi = orow + x * 3;
                        srcBytes[oi] = b1B; srcBytes[oi + 1] = b1G; srcBytes[oi + 2] = b1R;

                        if (x < w2 && y < h2)
                        {
                            int p2 = r2row + x * 4;
                            byte b2B = s2[p2], b2G = s2[p2 + 1], b2R = s2[p2 + 2];
                            int d = Math.Abs(b1R - b2R);
                            int dg = Math.Abs(b1G - b2G);
                            int db = Math.Abs(b1B - b2B);
                            if (dg > d) d = dg;
                            if (db > d) d = db;
                            diff[drow + x] = d > tol
                                ? (b2R << 16) | (b2G << 8) | b2B
                                : ImagesDifference.Same;
                        }
                        else
                        {
                            diff[drow + x] = ImagesDifference.Same;
                        }
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(srcBytes, 0, srcData.Scan0, srcBytes.Length);
            }
            finally
            {
                source.UnlockBits(srcData);
            }

            return new ImagesDifference(source, diff, srcStride, h);
        }

        /// <summary>
        /// Compare two pages and write the highlighted overlay (source page with differing pixels
        /// painted <see cref="Color"/>) to an image file. Output format follows the file extension.
        /// </summary>
        public void ComparePagesToImage(Page page1, Page page2, string resultImagePath)
        {
            using ImagesDifference difference = GetDifference(page1, page2);
            using Bitmap overlay = difference.Compose(ImagesDifference.ModeOverlay, Color, Color.Black);
            overlay.Save(resultImagePath, FormatFromExtension(resultImagePath));
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
            for (int i = 1; i <= count; i++)
            {
                using ImagesDifference difference = GetDifference(document1.Pages[i], document2.Pages[i]);
                using Bitmap overlay = difference.Compose(ImagesDifference.ModeOverlay, Color, Color.Black);
                string path = Path.Combine(targetDirectory, fileNamePrefix + i + extension);
                overlay.Save(path, imageFormat);
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

        // The result PDF always uses a fixed A4 page box (integer points), independent of the
        // source page size, DPI, or overlay aspect ratio: the overlay image is stretched to fill it.
        private const double ResultPageWidth = 595;
        private const double ResultPageHeight = 842;

        private void AppendOverlayPage(Document doc, Page page1, Page page2)
        {
            using ImagesDifference difference = GetDifference(page1, page2);
            using Bitmap overlay = difference.Compose(ImagesDifference.ModeOverlay, Color, Color.Black);

            Page page = doc.Pages.Add();
            page.SetPageSize(ResultPageWidth, ResultPageHeight);
            page.PageInfo.Margin = new MarginInfo(0, 0, 0, 0);

            using var ms = new MemoryStream();
            overlay.Save(ms, GdiImageFormat.Png);
            ms.Position = 0;
            page.AddImage(ms, new Rectangle(0, 0, ResultPageWidth, ResultPageHeight));
        }

        private Bitmap RenderPage(Page page)
        {
            var res = Resolution ?? new Resolution(150);
            var hiRes = new Resolution(res.X * SuperSampleFactor, res.Y * SuperSampleFactor);
            var device = new PngDevice(hiRes);
            using Bitmap hi = device.GetBitmap(page);
            return DownsampleWithGamma(hi, SuperSampleFactor, AntiAliasGamma);
        }

        /// <summary>
        /// Box-downsample a supersampled render by <paramref name="ss"/> in each axis, mapping the
        /// averaged ink coverage of each output pixel through <paramref name="gamma"/> so anti-aliased
        /// edges darken the way Windows-style font-gamma AA does. Coverage is taken
        /// per channel relative to a white background, so solid fills (coverage 0 or 1) are unchanged
        /// and only partial-coverage edge pixels shift.
        /// </summary>
        private static Bitmap DownsampleWithGamma(Bitmap hi, int ss, double gamma)
        {
            int w = hi.Width / ss, h = hi.Height / ss;
            var hiData = hi.LockBits(new GdiRectangle(0, 0, hi.Width, hi.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int hiStride = hiData.Stride;
            byte[] src;
            try
            {
                src = new byte[hiStride * hi.Height];
                System.Runtime.InteropServices.Marshal.Copy(hiData.Scan0, src, 0, src.Length);
            }
            finally
            {
                hi.UnlockBits(hiData);
            }

            var outBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var outData = outBmp.LockBits(new GdiRectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int outStride = outData.Stride;
                var dst = new byte[outStride * h];
                double invGamma = 1.0 / gamma;
                int area = ss * ss;
                for (int y = 0; y < h; y++)
                {
                    int orow = y * outStride;
                    for (int x = 0; x < w; x++)
                    {
                        int oi = orow + x * 4;
                        for (int c = 0; c < 3; c++) // B, G, R
                        {
                            double coverage = 0.0;
                            for (int yy = 0; yy < ss; yy++)
                            {
                                int srow = (y * ss + yy) * hiStride;
                                for (int xx = 0; xx < ss; xx++)
                                    coverage += 1.0 - src[srow + (x * ss + xx) * 4 + c] / 255.0;
                            }
                            coverage /= area;
                            double shaped = Math.Pow(coverage, invGamma);
                            dst[oi + c] = (byte)Math.Round(255.0 * (1.0 - shaped));
                        }
                        dst[oi + 3] = 255; // opaque
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(dst, 0, outData.Scan0, dst.Length);
            }
            finally
            {
                outBmp.UnlockBits(outData);
            }

            return outBmp;
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
