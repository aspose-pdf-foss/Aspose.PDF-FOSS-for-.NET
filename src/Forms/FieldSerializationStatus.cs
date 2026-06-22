namespace Aspose.Pdf;

/// <summary>
/// Outcome of serializing a single form field through
/// <see cref="Aspose.Pdf.Forms.Form.ExportToJson"/> or
/// <see cref="Aspose.Pdf.Forms.Form.ImportFromJson"/>.
/// </summary>
public enum FieldSerializationStatus
{
    /// <summary>The field was serialized without issue.</summary>
    Success = 0,
    /// <summary>Serialization completed but raised one or more warnings.</summary>
    Warning = 1,
    /// <summary>Serialization failed for this field; see <see cref="FieldSerializationResult.ErrorMessages"/>.</summary>
    Error = 2,
}
