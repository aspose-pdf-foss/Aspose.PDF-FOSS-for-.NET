namespace Aspose.Pdf;

/// <summary>
/// Outcome of serializing a single form field through
/// <c>Form.ExportToJson</c> or
/// <c>Form.ImportFromJson</c>.
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
