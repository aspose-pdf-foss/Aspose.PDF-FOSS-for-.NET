namespace Aspose.Pdf.Facades;

/// <summary>
/// Attribute flags applied to a form field via <see cref="FormEditor.SetFieldAttribute"/>.
/// </summary>
public enum PropertyFlag
{
    /// <summary>Sentinel value indicating "no flag".</summary>
    InvalidFlag = 0,
    /// <summary>The field is read-only — its value cannot be modified by the user.</summary>
    ReadOnly = 1,
    /// <summary>The field must have a value when the form is submitted.</summary>
    Required = 2,
    /// <summary>The field is excluded from form submission.</summary>
    NoExport = 3,
}
