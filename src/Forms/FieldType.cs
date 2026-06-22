namespace Aspose.Pdf.Forms;

/// <summary>
/// The type of a form field.
/// </summary>
public enum FieldType
{
    Unknown,
    Text,
    Button,
    CheckBox,
    RadioButton,
    Choice,
    Signature,
    /// <summary>List box (single or multi-select).</summary>
    ListBox,
    /// <summary>Combo box (drop-down).</summary>
    ComboBox,
    /// <summary>Barcode field.</summary>
    Barcode,
    /// <summary>Numeric text field.</summary>
    Numeric,
    /// <summary>Date text field.</summary>
    DateTime,
    /// <summary>Radio button group (alias for <see cref="RadioButton"/>).</summary>
    Radio = RadioButton,
}
