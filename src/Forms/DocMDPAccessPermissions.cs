namespace Aspose.Pdf.Forms;

/// <summary>/DocMDP /TransformParams /P access-permission levels per
/// PDF 32000-1 §12.8.2.2. Determines what changes a downstream user may
/// make to a certified document without invalidating the certification.</summary>
public enum DocMDPAccessPermissions
{
    /// <summary>No changes are permitted.</summary>
    NoChanges = 1,

    /// <summary>Only form fill-in and signature creation are permitted.</summary>
    FillingInForms = 2,

    /// <summary>Form fill-in, signature creation, and annotation
    /// modification are permitted.</summary>
    AnnotationModification = 3,
}
