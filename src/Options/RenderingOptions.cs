namespace Aspose.Pdf;

/// <summary>
/// Rendering options used by the page-to-image converters (<see cref="Aspose.Pdf.Devices.PngDevice"/>,
/// <see cref="Aspose.Pdf.Devices.JpegDevice"/>, <see cref="Aspose.Pdf.Facades.PdfConverter"/>, …).
/// Property values are stored on this options object; the active subset depends on the device.
/// Most flags here describe legacy rendering knobs and are not currently honoured.
/// </summary>
public sealed class RenderingOptions
{
    /// <summary>Analyse fonts on the page before rendering.</summary>
    public bool AnalyzeFonts { get; set; }

    /// <summary>Bar-code optimization.</summary>
    public bool BarcodeOptimization { get; set; }

    /// <summary>Convert fonts to Unicode TrueType form before rendering.</summary>
    public bool ConvertFontsToUnicodeTTF { get; set; }

    /// <summary>Override default font name used when a referenced font cannot be resolved.</summary>
    public string? DefaultFontName { get; set; }

    /// <summary>Extra units added to the rendered image height.</summary>
    public float HeightExtraUnits { get; set; }

    /// <summary>If true, the renderer suppresses exceptions raised by malformed font resources.</summary>
    public bool IgnoreResourceFontErrors { get; set; }

    /// <summary>Use high-quality bicubic interpolation when scaling images.</summary>
    public bool InterpolationHighQuality { get; set; }

    /// <summary>Maximum entries in the font cache. Zero means unbounded.</summary>
    public int MaxFontsCacheSize { get; set; }

    /// <summary>Maximum entries in the symbol cache. Zero means unbounded.</summary>
    public int MaxSymbolsCacheSize { get; set; }

    /// <summary>Crop the rendered image to the visible content.</summary>
    public bool OptimizeDimensions { get; set; }

    /// <summary>Scale images on the page so they fit the page width.</summary>
    public bool ScaleImagesToFitPageWidth { get; set; }

    /// <summary>Use native rendering for system fonts.</summary>
    public bool SystemFontsNativeRendering { get; set; }

    /// <summary>Enable font hinting.</summary>
    public bool UseFontHinting { get; set; }

    /// <summary>Use the new imaging engine.</summary>
    public bool UseNewImagingEngine { get; set; }

    /// <summary>Extra units added to the rendered image width.</summary>
    public float WidthExtraUnits { get; set; }
}
