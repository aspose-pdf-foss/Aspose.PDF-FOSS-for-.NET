namespace Aspose.Pdf;

/// <summary>
/// Options for loading SVG files as PDF documents.
/// </summary>
public sealed class SvgLoadOptions : LoadOptions
{
    /// <summary>SVG conversion engine selection.</summary>
    public ConversionEngines ConversionEngine { get; set; } = ConversionEngines.NewEngine;

    /// <summary>Adjust PDF page size to SVG size.</summary>
    public bool AdjustPageSize { get; set; } = true;

    /// <summary>Target page layout. Width/Height of 0 (the default) mean "derive from
    /// the SVG content"; margins grow the derived page and offset the artwork. All
    /// values are read as CSS px (×0.75 pt), matching the SVG root-length rule.</summary>
    public PageInfo PageInfo { get; set; } = new PageInfo { Width = 0, Height = 0 };

    /// <summary>Available conversion engines.</summary>
    public enum ConversionEngines
    {
        /// <summary>Legacy engine.</summary>
        LegacyEngine,
        /// <summary>New engine (default).</summary>
        NewEngine,
    }
}
