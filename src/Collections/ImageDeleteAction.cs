namespace Aspose.Pdf;

/// <summary>
/// Action taken by <see cref="XImageCollection.Delete(int, ImageDeleteAction)"/>
/// when the image being removed is still referenced from the page content.
/// </summary>
public enum ImageDeleteAction
{
    /// <summary>Take no special action.</summary>
    None = 0,
    /// <summary>Refuse to delete when the image is still referenced by content streams.</summary>
    Check = 1,
    /// <summary>Delete even when references exist; references become dangling.</summary>
    ForceDelete = 2,
    /// <summary>Drop the resource entry but leave the content-stream operators intact.</summary>
    KeepContents = 3,
}
