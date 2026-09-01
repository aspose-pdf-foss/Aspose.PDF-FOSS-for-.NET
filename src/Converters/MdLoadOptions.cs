namespace Aspose.Pdf;

/// <summary>
/// Options for loading Markdown files as PDF documents.
/// </summary>
public sealed class MdLoadOptions : LoadOptions
{
    /// <summary>Page size info (width, height, margins).</summary>
    public PageSizeInfo PageInfo { get; set; } = new PageSizeInfo();

    /// <summary>Custom CSS styles to apply to the rendered content.</summary>
    public string? CssStyles { get; set; }

    /// <summary>When set, a <c>@page</c> rule found in a <c>&lt;style&gt;</c> block of the
    /// markdown source takes priority over <see cref="PageInfo"/> for the page geometry.</summary>
    public bool IsPriorityCssPageRule { get; set; }
}

/// <summary>
/// Page size configuration for document loading.
/// </summary>
public sealed class PageSizeInfo
{
    /// <summary>Page width in points. Defaults to ISO A4.</summary>
    public double Width { get; set; } = 595.276;

    /// <summary>Page height in points. Defaults to ISO A4.</summary>
    public double Height { get; set; } = 841.89;

    /// <summary>Page margins.</summary>
    public MarginInfo? Margin { get; set; }
}
