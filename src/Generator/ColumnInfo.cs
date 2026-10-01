namespace Aspose.Pdf;

/// <summary>Describes a multi-column layout for a floating box, a table of contents or generated page content: how many columns, their widths and the gap between them.</summary>
public sealed class ColumnInfo
{
    /// <summary>Creates a single-column layout with no explicit widths and zero spacing.</summary>
    public ColumnInfo() { }

    /// <summary>Gets or sets the column widths in points as a space- or comma-separated list, e.g. <c>"150 150 150"</c>. When fewer widths than <c>ColumnCount</c> are given, the columns share the available width evenly. Defaults to an empty string.</summary>
    public string? ColumnWidths { get; set; } = string.Empty;
    /// <summary>Gets or sets the gap between adjacent columns in points, as a string (e.g. <c>"12"</c>). Defaults to <c>"0"</c>.</summary>
    public string? ColumnSpacing { get; set; } = "0";
    /// <summary>Gets or sets the number of columns. Defaults to 1.</summary>
    public int ColumnCount { get; set; } = 1;
}
