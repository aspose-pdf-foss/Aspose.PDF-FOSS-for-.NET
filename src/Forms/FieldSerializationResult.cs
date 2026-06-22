namespace Aspose.Pdf;

/// <summary>
/// Per-field outcome of an <see cref="Aspose.Pdf.Forms.Form.ExportToJson"/> or
/// <see cref="Aspose.Pdf.Forms.Form.ImportFromJson"/> call.
/// </summary>
public sealed class FieldSerializationResult
{
    /// <summary>Errors raised for this field, if any.</summary>
    public HashSet<string> ErrorMessages { get; } = new();

    /// <summary>Warnings raised for this field, if any.</summary>
    public HashSet<string> WarningMessages { get; } = new();

    /// <summary>Full name (dotted path) of the field.</summary>
    public string FieldFullName { get; internal set; } = string.Empty;

    /// <summary>Final outcome — Success / Warning / Error.</summary>
    public FieldSerializationStatus FieldSerializationStatus { get; internal set; }
}
