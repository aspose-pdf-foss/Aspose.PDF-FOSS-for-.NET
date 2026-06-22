namespace Aspose.Pdf.Optimization;

/// <summary>
/// Image-compression sub-options for <see cref="OptimizationOptions"/>.
/// </summary>
public class ImageCompressionOptions
{
    /// <summary>The default constructor.</summary>
    public ImageCompressionOptions() { }

    /// <summary>If this flag is set to true images will be compressed in the document.
    /// Compression level is specified with <see cref="ImageQuality"/> property.</summary>
    public bool CompressImages { get; set; }

    /// <summary>Specifies level of image compression when <see cref="CompressImages"/> flag is used.</summary>
    public int ImageQuality { get; set; } = 75;

    /// <summary>Specifies maximum resolution of images. If image has higher resolution
    /// it will be scaled.</summary>
    public int MaxResolution { get; set; }

    /// <summary>If this flag set to true and <see cref="CompressImages"/> is true images
    /// will be resized if image resolution is greater then specified <see cref="MaxResolution"/>.</summary>
    public bool ResizeImages
    {
        get => MaxResolution > 0;
        set { if (!value) MaxResolution = 0; }
    }

    /// <summary>Gets or sets encoding used to store images. Stored only;
    /// the original encoding is preserved regardless of the value.</summary>
    public ImageEncoding Encoding { get; set; } = ImageEncoding.Unchanged;

    /// <summary>Determines algorithm version (standard, fast, or mixed compression options).
    /// Stored only; all values are treated the same.</summary>
    public ImageCompressionVersion Version { get; set; } = ImageCompressionVersion.Standard;
}
