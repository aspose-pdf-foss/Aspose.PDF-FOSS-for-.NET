namespace Aspose.Pdf.Facades;

/// <summary>
/// The type of an AcroForm field, as used by <see cref="FormEditor.AddField"/>
/// and related facade operations.
/// </summary>
public enum FieldType
{
    /// <summary>Sentinel returned when the field name does not resolve to a known field.</summary>
    InvalidNameOrType = 0,
    /// <summary>Single-line text field.</summary>
    Text = 1,
    /// <summary>Multi-line text field.</summary>
    MultiLineText = 2,
    /// <summary>Check box.</summary>
    CheckBox = 3,
    /// <summary>Radio button.</summary>
    Radio = 4,
    /// <summary>Combo box (drop-down).</summary>
    ComboBox = 5,
    /// <summary>List box.</summary>
    ListBox = 6,
    /// <summary>Push button.</summary>
    PushButton = 7,
    /// <summary>Signature field.</summary>
    Signature = 8,
    /// <summary>Image-button field.</summary>
    Image = 9,
    /// <summary>Numeric text field.</summary>
    Numeric = 10,
    /// <summary>Bar-code field.</summary>
    Barcode = 11,
    /// <summary>Date-time text field.</summary>
    DateTime = 12,
}
