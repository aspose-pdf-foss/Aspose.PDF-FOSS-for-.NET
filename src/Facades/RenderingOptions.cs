namespace Aspose.Pdf.Facades;

/// <summary>
/// Rendering options used by <see cref="PdfConverter"/> and other facade classes.
/// Properties are stored for callers to inspect; the current rendering pipeline
/// does not honour them.
/// </summary>
public sealed class RenderingOptions
{
    /// <summary>Use the new imaging engine. Stored only; not currently honoured.</summary>
    public bool UseNewImagingEngine { get; set; }

    /// <summary>Use anti-aliasing. Stored only; not currently honoured.</summary>
    public bool UseAntiAliasing { get; set; } = true;

    /// <summary>Use font hinting. Stored only; not currently honoured.</summary>
    public bool UseFontHinting { get; set; }

    /// <summary>High-quality interpolation flag. Stored only; not currently honoured.</summary>
    public bool InterpolationHighQuality { get; set; }

    /// <summary>Default font name override. Stored only; not currently honoured.</summary>
    public string? DefaultFontName { get; set; }

    /// <summary>Bar code optimization. Stored only; not currently honoured.</summary>
    public bool BarcodeOptimization { get; set; }

    /// <summary>Smooth content rendering. Stored only; not currently honoured.</summary>
    public bool ScaleImagesToFitPageWidth { get; set; }

    /// <summary>System fonts substitution. Stored only; not currently honoured.</summary>
    public bool SystemFontsNativeRendering { get; set; }

    /// <summary>Ignore resource-font errors and keep rendering. Stored only; not currently honoured.</summary>
    public bool IgnoreResourceFontErrors { get; set; }

    /// <summary>Optimize rendered output dimensions. Stored only; not currently honoured.</summary>
    public bool OptimizeDimensions { get; set; }
}
