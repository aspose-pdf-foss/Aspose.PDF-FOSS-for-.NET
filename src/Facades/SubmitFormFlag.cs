namespace Aspose.Pdf.Facades;

/// <summary>
/// Format of the data posted by a Submit form action — set per field via
/// <see cref="FormEditor.SetSubmitFlag"/>.
/// </summary>
public enum SubmitFormFlag
{
    /// <summary>Form Data Format (FDF) — field values only.</summary>
    Fdf = 0,
    /// <summary>FDF including comments (annotations).</summary>
    FdfWithComments = 1,
    /// <summary>HTML application/x-www-form-urlencoded payload.</summary>
    Html = 2,
    /// <summary>The entire PDF document.</summary>
    Pdf = 3,
    /// <summary>XML Form Data Format (XFDF) — field values only.</summary>
    Xfdf = 4,
    /// <summary>XFDF including comments (annotations).</summary>
    XfdfWithComments = 5,
}
