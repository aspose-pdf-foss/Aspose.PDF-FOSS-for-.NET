using System.Drawing;
using System.Drawing.Imaging;
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

    /// <summary>While drawing for a printer: how many paths the page has filled so far.</summary>
    private int _vectorFills;

    /// <summary>True during the probe draw that decides how the page is printed: nothing drawn
    /// there reaches a page, so images are neither decoded nor drawn.</summary>
    private bool _vectorProbe;

    /// <summary>While <see cref="_vectorTarget"/>: the page's layout size in pixels, which the
    /// surface's backing bitmap has when the page is rendered into pixels.</summary>
    private int _vectorPixelW, _vectorPixelH;

    /// <summary>The size of the pixel grid the page is laid out on: the backing bitmap's, or the
    /// layout size when the page is drawn for a printer, whose backing bitmap is a placeholder.</summary>
    private (int W, int H) BackingExtent => _vectorTarget ? (_vectorPixelW, _vectorPixelH) : (_bitmap.Width, _bitmap.Height);

    /// <summary>
    /// Called where the page is composited through its pixels - a blend, a soft mask, a
    /// transparency group - which a printer never receives: true, with the page marked for printing
    /// as an image of itself, when the page is being drawn for a printer. Replaces the former test
    /// of whether anything had been written to a page-sized backing canvas, which cost that canvas
    /// - 135 MB at 600 DPI for a Letter page - allocated, scanned and freed for every printed page.
    /// </summary>
    private bool DeferToPageImage()
    {
        if (!_vectorTarget) return false;
        _vectorNeedsImage = true;
        return true;
    }

    /// <summary>
    /// The most filled paths a page may hold and still be sent to a printer as drawing commands;
    /// over it the page goes as an image of itself.
    /// </summary>
    /// <remarks>
    /// A printer driver's cost follows the number of paths it is handed, and it is paid once more
    /// in the spooler, which takes dense pages one at a time: measured against the XPS writer with
    /// six-page documents of small filled shapes, four jobs at a time, 250 fills a page printed at
    /// the same rate either way, 1,000 at 24 jobs a minute as commands against 56 as 150-DPI
    /// images, 2,000 at 16 against 55, 4,000 at 7 against 33, 8,000 at 6 against 96. Text set as
    /// outlines reaches that density on an ordinary page - a six-page release with 2,200 to 6,200
    /// filled glyphs a page took 58 seconds a job as commands and under 2 as images. Pages under
    /// the limit keep their outlines, which land on the printer's own grid as the reference's do.
    /// </remarks>
    private const int DenseFillLimit = 1000;

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
    /// is first drawn as a probe onto a one-pixel surface, with no image decoded or drawn; if any
    /// of that content was reached (<see cref="DeferToPageImage"/>), nothing is drawn and the caller
    /// prints the page as an image.</para>
    /// <para>So does a page dense with filled paths (<see cref="DenseFillLimit"/>), and a page that
    /// shows Type 3 text: the reference prints such a page as one image of it, whether its glyphs
    /// are outlines or bitmaps. Sent as drawing commands, a four-page
    /// form set in Type 3 bitmap fonts reached the XPS writer as 74,000 glyph-sized images and
    /// 27 MB, and printed in 11.5 seconds, where the reference's four page images took 470 KB and
    /// 2.7 seconds.</para>
    /// </remarks>
    /// <returns>False, with nothing drawn, when the page needs pixel compositing.</returns>
    [SupportedOSPlatform("windows")]
    internal bool TryRenderPageToGraphics(Page page, Graphics target, RectangleF destination, int dpi)
    {
        ImageDevice.LayOutPendingParagraphs(page);
        var crop = SoftwarePageRenderer.EffectiveCropRect(page);
        var rot = ((page.RotateDegrees % 360) + 360) % 360;
        var extent = new SizeF(
            (float)(rot is 90 or 270 ? crop.Height : crop.Width),
            (float)(rot is 90 or 270 ? crop.Width : crop.Height));
        var pixelW = Math.Max(1, SoftwarePageRenderer.PagePixels(extent.Width, dpi));
        var pixelH = Math.Max(1, SoftwarePageRenderer.PagePixels(extent.Height, dpi));

        // The backing bitmap stands in for the page's pixels, which a printer has none of: every
        // path that would read or write them defers the page to an image instead.
        using var canvas = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        _vectorNeedsImage = false;
        _vectorFills = 0;
        _vectorPixelW = pixelW;
        _vectorPixelH = pixelH;
        _vectorProbe = true;
        try
        {
            using var probe = Graphics.FromImage(canvas);
            DrawPageInContainer(page, probe, new RectangleF(0, 0, pixelW, pixelH), canvas, extent, dpi);
        }
        finally
        {
            _vectorProbe = false;
        }
        if (_vectorNeedsImage || _vectorFills > DenseFillLimit) return false;

        DrawPageInContainer(page, target, destination, canvas, extent, dpi);
        return true;
    }

    /// <summary>One vector draw of the page into <paramref name="destination"/> on
    /// <paramref name="target"/>, with <paramref name="canvas"/> standing in for the backing pixels.</summary>
    /// <param name="page">The page to draw.</param>
    /// <param name="target">The graphics surface that receives the drawing.</param>
    /// <param name="destination">The rectangle on <paramref name="target"/> the page is fitted into.</param>
    /// <param name="canvas">The placeholder bitmap the renderer holds as the page's pixels.</param>
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
            pixelW = _vectorPixelW,
            pixelH = _vectorPixelH,
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
        // The render flips the page about the layout's height in whole pixels, which drops the
        // page's partial last pixel; that partial pixel is the page's to keep here, or everything
        // sits that fraction of a layout pixel high. A page 842 points tall laid out at 600 DPI is
        // 7016.67 pixels on a 7016-pixel grid: every edge came out 0.11 hundredths of an inch above
        // the reference's print, and 0.125 for 841.89 points, while a Letter page's whole 6600
        // matched it exactly.
        target.TranslateTransform(0, (float)(extent.Height * scale - gp.pixelH));
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
}
