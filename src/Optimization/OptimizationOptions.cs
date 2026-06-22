namespace Aspose.Pdf.Optimization;

/// <summary>
/// Class which describes document optimization algorithm. Instance of this class may be
/// used as parameter of <c>OptimizeResources()</c> method.
/// </summary>
public class OptimizationOptions
{
    /// <summary>Eliminate unreferenced document objects.</summary>
    public bool RemoveUnusedObjects { get; set; } = true;

    /// <summary>Delete unused resources from the document.</summary>
    public bool RemoveUnusedStreams { get; set; } = true;

    /// <summary>Consolidate duplicate resource streams into single objects.</summary>
    public bool LinkDuplicateStreams { get; set; } = true;

    /// <summary>Pack PDF objects into Object Streams (PDF 1.5+ /ObjStm) with compression.
    /// Stored only; the writer always emits xref + objects in the legacy format.</summary>
    public bool CompressObjects { get; set; }

    /// <summary>Maximum image resolution; higher resolution images are scaled down.
    /// Note: the public reference spells this 'MaxResoultion' (typo in the original
    /// Aspose.PDF for .NET API); retained for source compatibility. Forwards to
    /// <see cref="ImageCompressionOptions.MaxResolution"/>.</summary>
    public int MaxResoultion
    {
        get => ImageCompressionOptions.MaxResolution;
        set => ImageCompressionOptions.MaxResolution = value;
    }

    /// <summary>Allow reusing page content streams across identical pages.
    /// Stored only; page-content deduplication is not currently implemented.</summary>
    public bool AllowReusePageContent { get; set; }

    /// <summary>Remove font embedding from Standard-14 fonts (built into PDF viewers).</summary>
    public bool UnembedFonts { get; set; }

    /// <summary>Convert fonts into subsets (remove unused glyphs).</summary>
    public bool SubsetFonts { get; set; }

    /// <summary>Strip page-piece information and other private (application-specific)
    /// information from the document. Stored only; private-info entries are not
    /// currently removed.</summary>
    public bool RemovePrivateInfo { get; set; }

    /// <summary>Image-compression sub-options.</summary>
    public ImageCompressionOptions ImageCompressionOptions { get; } = new();

    /// <summary>Resize images that exceed <see cref="MaxResoultion"/>. Forwards to
    /// <see cref="ImageCompressionOptions.CompressImages"/> — when on, images
    /// above the max resolution are downsampled.</summary>
    public bool ResizeImages
    {
        get => ImageCompressionOptions.CompressImages;
        set => ImageCompressionOptions.CompressImages = value;
    }

    /// <summary>Specifies the encoder used for image processing. Forwards to
    /// <see cref="ImageCompressionOptions.Encoding"/>.</summary>
    public ImageEncoding ImageEncoding
    {
        get => ImageCompressionOptions.Encoding;
        set => ImageCompressionOptions.Encoding = value;
    }

    // ---- Internal optimization knobs ----
    // Read by Document.OptimizeResources() and the in-tree tests
    // (which see internals via [InternalsVisibleTo]).

    internal bool CompressImages
    {
        get => ImageCompressionOptions.CompressImages;
        set => ImageCompressionOptions.CompressImages = value;
    }

    internal int ImageQuality
    {
        get => ImageCompressionOptions.ImageQuality;
        set => ImageCompressionOptions.ImageQuality = value;
    }

    internal int MaxImageDpi
    {
        get => ImageCompressionOptions.MaxResolution;
        set => ImageCompressionOptions.MaxResolution = value;
    }

    internal bool ConvertImagesToGrayscale { get; set; }

    internal bool RemoveDuplicateImages { get; set; }

    internal bool SubsetEmbeddedFonts { get; set; }

    internal bool RemoveMetadata { get; set; }

    internal static OptimizationOptions Default => new();

    /// <summary>Factory returning an optimization strategy with all non-destructive
    /// options enabled.</summary>
    public static OptimizationOptions All() => new()
    {
        RemoveUnusedObjects = true,
        RemoveUnusedStreams = true,
        LinkDuplicateStreams = true,
        CompressImages = true,
        ImageQuality = 50,
        MaxImageDpi = 150,
        ConvertImagesToGrayscale = true,
        RemoveDuplicateImages = true,
        UnembedFonts = true,
        SubsetFonts = true,
        SubsetEmbeddedFonts = true,
        RemoveMetadata = true,
    };
}

/// <summary>Strategy version for image compression. All values are treated
/// the same; the property is stored for API parity.</summary>
public enum ImageCompressionVersion
{
    /// <summary>Standard algorithm. Default value.</summary>
    Standard = 0,
    /// <summary>Improved algorithm faster then standard but applicable not for all cases.</summary>
    Fast = 2,
    /// <summary>Use fast algorithm when possible and standard for other cases. May be
    /// slower then "Fast" but may produce better compression.</summary>
    Mixed = 3,
}

/// <summary>How image streams are re-encoded during optimization.
/// Stored only; images are not currently re-encoded.</summary>
public enum ImageEncoding
{
    /// <summary>Don't change encoding.</summary>
    Unchanged = 0,
    /// <summary>JPEG (DCT) encoding.</summary>
    Jpeg = 1,
    /// <summary>Flate encoding.</summary>
    Flate = 2,
    /// <summary>JPEG2000 (JPX) encoding.</summary>
    Jpeg2000 = 3,
}
