namespace Aspose.Pdf.Facades;

/// <summary>
/// Layout strategy used by <see cref="PdfConverter.MergeImages"/> when
/// combining multiple input images into a single output image.
/// </summary>
public enum ImageMergeMode
{
    /// <summary>Stack inputs vertically.</summary>
    Vertical = 0,
    /// <summary>Stack inputs horizontally.</summary>
    Horizontal = 1,
    /// <summary>Place each input centered, overlapping previous content.</summary>
    Center = 2,
}
