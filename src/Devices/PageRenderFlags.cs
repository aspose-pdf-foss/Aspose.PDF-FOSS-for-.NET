namespace Aspose.Pdf.Devices;

/// <summary>
/// Ambient per-thread switches consulted by the page renderers.
/// </summary>
internal static class PageRenderFlags
{
    /// <summary>When set, glyph painting is skipped entirely (as if every run were
    /// text-rendering-mode 3). The PDF→HTML PNG-page-background save renders each
    /// page's GRAPHICS to the background raster while the text lives on as real
    /// HTML spans — the background PNGs must carry no text ink.
    /// Per-thread so parallel renders elsewhere stay unaffected; always reset in a
    /// finally block by the setter.</summary>
    [System.ThreadStatic] internal static bool SuppressText;
}
