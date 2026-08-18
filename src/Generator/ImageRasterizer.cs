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
        if (!OperatingSystem.IsWindows()) return probe;
        const double pageW = 595.3, pageH = 841.9;
        var fit = System.Math.Min(pageW / natW, pageH / natH);
        var art = RasterizeSvgSized(svgData, natW * fit, natH * fit) ?? probe;
        try
        {
            return ComposeOnTransparentCanvasWindows(art, pageW, pageH) ?? probe;
        }
        catch
        {
            return probe;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[]? ComposeOnTransparentCanvasWindows(byte[] art, double canvasWpt, double canvasHpt)
    {
        var canvasW = (int)System.Math.Round(canvasWpt * RasterDpi / 72.0);
        var canvasH = (int)System.Math.Round(canvasHpt * RasterDpi / 72.0);
        if (canvasW <= 0 || canvasH <= 0 || (long)canvasW * canvasH > 64_000_000)
            return null;
        using var srcMs = new MemoryStream(art);
        using var src = new System.Drawing.Bitmap(srcMs);
        // The page render bakes a white background in; the canvas must stay
        // see-through outside the artwork so borders under the blit survive.
        src.MakeTransparent(System.Drawing.Color.White);
        using var canvas = new System.Drawing.Bitmap(canvasW, canvasH);
        using (var g = System.Drawing.Graphics.FromImage(canvas))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.DrawImage(src, (canvasW - src.Width) / 2, (canvasH - src.Height) / 2,
                src.Width, src.Height);
        }
        using var outMs = new MemoryStream();
        canvas.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
        return outMs.ToArray();
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
        if (!OperatingSystem.IsWindows() || boxWpt <= 0 || boxHpt <= 0)
            return RasterizeSvg(svgData);
        var art = RasterizeSvg(svgData, out var natW, out var natH);
        if (art is null || natW <= 0 || natH <= 0) return art;
        try
        {
            return CompositeOnCanvasWindows(art, natW, natH, boxWpt, boxHpt) ?? art;
        }
        catch
        {
            return art;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[]? CompositeOnCanvasWindows(byte[] art, double natW, double natH,
        double boxWpt, double boxHpt)
    {
        var canvasW = (int)System.Math.Round(boxWpt * RasterDpi / 72.0);
        var canvasH = (int)System.Math.Round(boxHpt * RasterDpi / 72.0);
        if (canvasW <= 0 || canvasH <= 0 || (long)canvasW * canvasH > 64_000_000)
            return null;
        var fit = System.Math.Min(boxWpt / natW, boxHpt / natH);
        var drawW = (int)System.Math.Round(natW * fit * RasterDpi / 72.0);
        var drawH = (int)System.Math.Round(natH * fit * RasterDpi / 72.0);
        using var srcMs = new MemoryStream(art);
        using var src = new System.Drawing.Bitmap(srcMs);
        using var canvas = new System.Drawing.Bitmap(canvasW, canvasH);
        using (var g = System.Drawing.Graphics.FromImage(canvas))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, (canvasW - drawW) / 2, (canvasH - drawH) / 2, drawW, drawH);
        }
        using var outMs = new MemoryStream();
        canvas.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
        return outMs.ToArray();
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
