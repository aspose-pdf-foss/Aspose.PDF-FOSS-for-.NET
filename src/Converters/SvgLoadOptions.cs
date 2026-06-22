namespace Aspose.Pdf.Converters;

/// <summary>
/// Options for loading SVG files as PDF documents.
/// </summary>
public sealed class SvgLoadOptions : LoadOptions
{
    /// <summary>SVG conversion engine selection.</summary>
    public ConversionEngines ConversionEngine { get; set; } = ConversionEngines.NewEngine;

    /// <summary>Adjust PDF page size to SVG size.</summary>
    public bool AdjustPageSize { get; set; } = true;

    /// <summary>Page margin info.</summary>
    public MarginInfo? PageInfo { get; set; }

    /// <summary>Available conversion engines.</summary>
    public enum ConversionEngines
    {
        /// <summary>Legacy engine.</summary>
        LegacyEngine,
        /// <summary>New engine (default).</summary>
        NewEngine,
    }
}
