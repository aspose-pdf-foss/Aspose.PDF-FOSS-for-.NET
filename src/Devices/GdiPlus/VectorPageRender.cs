using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer
{
    /// <summary>True while the page is drawn as drawing commands onto a caller's surface rather
    /// than into pixels.</summary>
    private bool _vectorTarget;

    /// <summary>Set while drawing for a printer when the page shows content the reference prints
    /// only as an image.</summary>
    private bool _vectorNeedsImage;

    /// <summary>While <see cref="_vectorTarget"/>: the mapping from the page's layout pixels to the
    /// printer surface's own units, hundredths of an inch.</summary>
    private System.Drawing.Drawing2D.Matrix? _layoutToSheet;

    /// <summary>
    /// Draws the page as drawing commands onto <paramref name="target"/>, filling
    /// <paramref name="destination"/> in the target's current units and transform - the way a
    /// page is printed, so the printer receives outlines and images rather than a bitmap of them.
    /// </summary>
    /// <remarks>
    /// <para>The page is drawn exactly as a render at <paramref name="dpi"/> draws it, in that
    /// pixel space, inside a container that maps it onto the destination. The renderer sets and
    /// resets its transform freely; inside a container both are relative to the container.</para>
    /// <para>Some content is composited by reading pixels back - transparency groups, blend modes,
    /// soft masks, the exact-render text compositor - and pixels never reach a printer. So the page
    /// is first drawn into a discarded metafile over a blank backing canvas; if any of that content
    /// wrote to the canvas, nothing is drawn and the caller prints the page as an image.</para>
    /// <para>So does a page that shows Type 3 text: the reference prints such a page as one image
    /// of it, whether its glyphs are outlines or bitmaps. Sent as drawing commands, a four-page
    /// form set in Type 3 bitmap fonts reached the XPS writer as 74,000 glyph-sized images and
    /// 27 MB, and printed in 11.5 seconds, where the reference's four page images took 470 KB and
    /// 2.7 seconds.</para>
    /// </remarks>
    /// <returns>False, with nothing drawn, when the page needs pixel compositing.</returns>
    [SupportedOSPlatform("windows")]
    internal bool TryRenderPageToGraphics(Page page, Graphics target, RectangleF destination, int dpi)
    {
        var crop = SoftwarePageRenderer.EffectiveCropRect(page);
        var rot = ((page.RotateDegrees % 360) + 360) % 360;
        var extent = new SizeF(
            (float)(rot is 90 or 270 ? crop.Height : crop.Width),
            (float)(rot is 90 or 270 ? crop.Width : crop.Height));
        var pixelW = Math.Max(1, SoftwarePageRenderer.PagePixels(extent.Width, dpi));
        var pixelH = Math.Max(1, SoftwarePageRenderer.PagePixels(extent.Height, dpi));

        using var canvas = new Bitmap(pixelW, pixelH, PixelFormat.Format32bppArgb);
        _vectorNeedsImage = false;
        using (var reference = new Bitmap(1, 1))
        using (var referenceGraphics = Graphics.FromImage(reference))
        {
            var hdc = referenceGraphics.GetHdc();
            try
            {
                using var discard = new Metafile(hdc, new RectangleF(0, 0, pixelW, pixelH),
                    MetafileFrameUnit.Pixel, EmfType.EmfPlusOnly);
                using var discardGraphics = Graphics.FromImage(discard);
                DrawPageInContainer(page, discardGraphics, new RectangleF(0, 0, pixelW, pixelH), canvas, extent, dpi);
            }
            finally
            {
                referenceGraphics.ReleaseHdc(hdc);
            }
        }
        if (_vectorNeedsImage || AnyPixelWritten(canvas)) return false;

        DrawPageInContainer(page, target, destination, canvas, extent, dpi);
        return true;
    }

    /// <summary>One vector draw of the page into <paramref name="destination"/> on
    /// <paramref name="target"/>, with <paramref name="canvas"/> as the backing pixels.</summary>
    /// <param name="page">The page to draw.</param>
    /// <param name="target">The graphics surface that receives the drawing.</param>
    /// <param name="destination">The rectangle on <paramref name="target"/> the page is fitted into.</param>
    /// <param name="canvas">The bitmap backing <paramref name="target"/>.</param>
    /// <param name="extent">The page's visible size in points, turned by its rotation.</param>
    /// <param name="dpi">Output resolution in dots per inch; the drawing scale is dpi / 72.</param>
    [SupportedOSPlatform("windows")]
    private void DrawPageInContainer(Page page, Graphics target, RectangleF destination, Bitmap canvas,
        SizeF extent, int dpi)
    {
        var scale = dpi / 72.0;
        var gp = new GdiPixelPageRenderState
        {
            page = page,
            pixelW = canvas.Width,
            pixelH = canvas.Height,
            xScale = scale,
            yScale = scale,
        };
        BeginGdiPageRender(gp);

        var outer = target.Save();
        // The mapping goes on the OUTER transform with a plain container opened under it. The
        // container overload taking a source rectangle does not scale by the two rectangles on a
        // printer surface - it measures the source in the device's own pixels. The scale is the
        // page's exact extent, not the canvas, which is truncated to whole pixels.
        target.TranslateTransform(destination.X, destination.Y);
        target.ScaleTransform((float)(destination.Width / (extent.Width * scale)),
            (float)(destination.Height / (extent.Height * scale)));
        // The render flips the page about the canvas's height, which drops the page's partial last
        // pixel; that partial pixel is the page's to keep here, or everything sits that fraction of
        // a layout pixel high. A page 842 points tall laid out at 600 DPI is 7016.67 pixels on a
        // 7016-pixel canvas: every edge came out 0.11 hundredths of an inch above the reference's
        // print, and 0.125 for 841.89 points, while a Letter page's whole 6600 matched it exactly.
        target.TranslateTransform(0, (float)(extent.Height * scale - canvas.Height));
        _layoutToSheet = target.Transform;
        var container = target.BeginContainer();
        _vectorTarget = true;
        try
        {
            RenderGdiPageContent(gp, canvas, target);
        }
        finally
        {
            _vectorTarget = false;
            _layoutToSheet.Dispose();
            _layoutToSheet = null;
            target.EndContainer(container);
            target.Restore(outer);
            _reader.ClearCacheExcept(gp.cachedBefore);
            EndGdiPageRender();
        }
    }

    /// <summary>Whether anything painted into a canvas that started fully transparent.</summary>
    private static bool AnyPixelWritten(Bitmap canvas)
    {
        var data = canvas.LockBits(new System.Drawing.Rectangle(0, 0, canvas.Width, canvas.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[data.Width * 4];
            for (var y = 0; y < data.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                for (var a = 3; a < row.Length; a += 4)
                    if (row[a] != 0) return true;
            }
            return false;
        }
        finally
        {
            canvas.UnlockBits(data);
        }
    }
}
