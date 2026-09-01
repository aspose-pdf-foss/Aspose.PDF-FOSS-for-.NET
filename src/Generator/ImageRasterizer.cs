using System.IO;
using Aspose.Pdf.Converters;
using Aspose.Pdf.Devices;

namespace Aspose.Pdf;

/// <summary>
/// Converts a vector image source (SVG) into raster bytes so it can be embedded
/// through <see cref="Page.AddImage(byte[], Rectangle)"/>, which only accepts
/// raster formats. The SVG is converted to a one-page <see cref="Document"/>
/// sized to the SVG viewport, then that page is rendered to a PNG.
/// </summary>
internal static class ImageRasterizer
{
    /// <summary>Render DPI for the rasterised SVG (2× the 72-dpi base for crispness).</summary>
    private const int RasterDpi = 144;

    /// <summary>Rasterise SVG bytes to a PNG, or null if conversion fails.</summary>
    public static byte[]? RasterizeSvg(byte[] svgData) => RasterizeSvg(svgData, out _, out _);

    /// <summary>Rasterise a sizeless SVG the way a page-filling cell placement needs it:
    /// the artwork is aspect-fit to a portrait A4-proportion canvas (width-fit for
    /// square/wide viewBoxes), centred, on a TRANSPARENT background. The caller then
    /// stretches this canvas over the destination rect, so the artwork's on-page
    /// scale ends up (rectW / canvasW) × (rectH / canvasH) — a square viewBox in a
    /// tall rect renders as a portrait ellipse occupying pageW/pageH of the rect's
    /// height, centred, with the cell borders visible through the empty bands.
    /// Falls back to the plain raster off-Windows or on failure.</summary>
    public static byte[]? RasterizeSvgOnPageCanvas(byte[] svgData)
    {
        var probe = RasterizeSvg(svgData, out var natW, out var natH);
        if (probe is null || natW <= 0 || natH <= 0) return probe;
        const double pageW = 595.3, pageH = 841.9;
        var fit = System.Math.Min(pageW / natW, pageH / natH);
        var art = RasterizeSvgSized(svgData, natW * fit, natH * fit) ?? probe;
        try
        {
            return ComposeOnCanvas(art, pageW, pageH, fitArtwork: false,
                whiteIsTransparent: true) ?? probe;
        }
        catch
        {
            return probe;
        }
    }


    /// <summary>Rasterise SVG bytes to a PNG at an explicit viewport size in points,
    /// overriding any root width/height. The viewBox maps onto the forced viewport
    /// with independent x/y scales, so artwork with a different aspect is STRETCHED
    /// to fill the box (a circle in a tall box renders as an ellipse) rather than
    /// letterboxed. Returns null on failure.</summary>
    public static byte[]? RasterizeSvgSized(byte[] svgData, double widthPt, double heightPt)
    {
        if (widthPt <= 0 || heightPt <= 0) return RasterizeSvg(svgData);
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(svgData);
            var m = System.Text.RegularExpressions.Regex.Match(text, "<svg\\b[^>]*>");
            if (!m.Success) return RasterizeSvg(svgData);
            var tag = System.Text.RegularExpressions.Regex.Replace(
                m.Value, "\\s+(width|height)\\s*=\\s*(\"[^\"]*\"|'[^']*')", "");
            tag = tag.Insert("<svg".Length, FormattableString.Invariant(
                $" width=\"{widthPt:0.##}\" height=\"{heightPt:0.##}\""));
            text = text.Remove(m.Index, m.Length).Insert(m.Index, tag);
            return RasterizeSvg(System.Text.Encoding.UTF8.GetBytes(text));
        }
        catch
        {
            return RasterizeSvg(svgData);
        }
    }

    /// <summary>Rasterise SVG bytes onto a canvas of the given box aspect (in points),
    /// with the artwork aspect-fit and CENTERED — a vector source is letterboxed
    /// inside its Fix box instead of stretched. Returns null on failure
    /// (callers fall back to the plain raster). Windows-only compositing.</summary>
    public static byte[]? RasterizeSvgOnCanvas(byte[] svgData, double boxWpt, double boxHpt)
    {
        if (boxWpt <= 0 || boxHpt <= 0)
            return RasterizeSvg(svgData);
        var art = RasterizeSvg(svgData, out var natW, out var natH);
        if (art is null || natW <= 0 || natH <= 0) return art;
        try
        {
            return ComposeOnCanvas(art, boxWpt, boxHpt, fitArtwork: true,
                whiteIsTransparent: false, natW: natW, natH: natH) ?? art;
        }
        catch
        {
            return art;
        }
    }


    /// <summary>
    /// Place a rasterised artwork on a transparent canvas of the given size in points.
    /// Managed throughout: this used to be two System.Drawing routines, so off Windows
    /// both callers fell back to the bare raster - the artwork filled its box edge to edge
    /// instead of being letterboxed inside it, and the cell borders that should show
    /// through the empty bands were covered over.
    /// <paramref name="fitArtwork"/> scales the artwork to fit the canvas (aspect kept,
    /// centred); without it the artwork is placed at its rendered size. 
    /// <paramref name="whiteIsTransparent"/> drops the white background the page render
    /// bakes in, so what sits under the canvas stays visible.
    /// </summary>
    private static byte[]? ComposeOnCanvas(byte[] art, double canvasWpt, double canvasHpt,
        bool fitArtwork, bool whiteIsTransparent, double natW = 0, double natH = 0)
    {
        var canvasW = (int)System.Math.Round(canvasWpt * RasterDpi / 72.0);
        var canvasH = (int)System.Math.Round(canvasHpt * RasterDpi / 72.0);
        if (canvasW <= 0 || canvasH <= 0 || (long)canvasW * canvasH > 64_000_000)
            return null;

        var (srcPix, srcW, srcH, srcHasAlpha) = Facades.PdfFileMend.DecodePng(art);
        if (srcW <= 0 || srcH <= 0 || srcPix.Length == 0) return null;
        var srcComps = srcHasAlpha ? 4 : 3;
        if (srcPix.Length < (long)srcW * srcH * srcComps) return null;

        int drawW = srcW, drawH = srcH;
        if (fitArtwork && natW > 0 && natH > 0)
        {
            var fit = System.Math.Min(canvasWpt / natW, canvasHpt / natH);
            drawW = (int)System.Math.Round(natW * fit * RasterDpi / 72.0);
            drawH = (int)System.Math.Round(natH * fit * RasterDpi / 72.0);
        }
        if (drawW <= 0 || drawH <= 0) return null;
        var offX = (canvasW - drawW) / 2;
        var offY = (canvasH - drawH) / 2;

        var canvas = new byte[(long)canvasW * canvasH * 4];   // RGBA, cleared = transparent
        for (var y = 0; y < drawH; y++)
        {
            var cy = offY + y;
            if (cy < 0 || cy >= canvasH) continue;
            // Nearest-neighbour source row/column: the artwork is rendered at 2x the base
            // resolution already, so the resample is a mild reduction and the extra weight
            // of a filtered scale is not worth the blur it puts on vector edges.
            var sy = drawH == srcH ? y : (int)((long)y * srcH / drawH);
            if (sy >= srcH) sy = srcH - 1;
            for (var x = 0; x < drawW; x++)
            {
                var cx = offX + x;
                if (cx < 0 || cx >= canvasW) continue;
                var sx = drawW == srcW ? x : (int)((long)x * srcW / drawW);
                if (sx >= srcW) sx = srcW - 1;
                var si = (sy * srcW + sx) * srcComps;
                byte r = srcPix[si], g = srcPix[si + 1], b = srcPix[si + 2];
                byte a = srcComps == 4 ? srcPix[si + 3] : (byte)255;
                if (whiteIsTransparent && r == 255 && g == 255 && b == 255) a = 0;
                var di = (cy * canvasW + cx) * 4;
                canvas[di] = r; canvas[di + 1] = g; canvas[di + 2] = b; canvas[di + 3] = a;
            }
        }
        return IO.PngEncoder.Encode(canvas, canvasW, canvasH, colorType: 6);
    }

    /// <summary>Rasterise SVG bytes to a PNG and report the SVG viewport size in
    /// points, or null if conversion fails. The natural point size lets layout code
    /// size the vector image by its authored dimensions instead of raster pixels.</summary>
    public static byte[]? RasterizeSvg(byte[] svgData, out double naturalWidthPt, out double naturalHeightPt)
    {
        naturalWidthPt = 0;
        naturalHeightPt = 0;
        try
        {
            var doc = SvgToPdfConverter.ConvertForImage(svgData);
            if (doc.Pages.Count == 0) return null;
            var page = doc.Pages[1];
            naturalWidthPt = page.Width;
            naturalHeightPt = page.Height;
            using var ms = new MemoryStream();
            var device = new PngDevice(new Resolution(RasterDpi));
            device.Process(page, ms);
            var bytes = ms.ToArray();
            return bytes.Length > 0 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }
}
