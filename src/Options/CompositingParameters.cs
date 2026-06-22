namespace Aspose.Pdf;

/// <summary>Image-blending parameters consumed by
/// <see cref="Facades.PdfFileMend.AddImage(System.IO.Stream, int, float, float, float, float, CompositingParameters)"/>.
/// Carries a blend mode, an image-filter hint, and a mask flag; behaviour
/// is "stored only" in this build — the FOSS image-write path treats every
/// AddImage call as <see cref="BlendMode.Normal"/>.</summary>
public sealed class CompositingParameters
{
    /// <summary>Construct with just a blend mode (filter type defaults to
    /// <see cref="ImageFilterType.Flate"/>, isMasked defaults to false).</summary>
    public CompositingParameters(BlendMode blendMode)
        : this(blendMode, ImageFilterType.Flate, isMasked: false) { }

    /// <summary>Construct with blend mode + filter type (isMasked defaults to false).</summary>
    public CompositingParameters(BlendMode blendMode, ImageFilterType filterType)
        : this(blendMode, filterType, isMasked: false) { }

    /// <summary>Construct with blend mode + filter type + mask flag.</summary>
    public CompositingParameters(BlendMode blendMode, ImageFilterType filterType, bool isMasked)
    {
        BlendMode = blendMode;
        FilterType = filterType;
        IsMasked = isMasked;
    }

    /// <summary>Blend mode applied when compositing the image onto the page.</summary>
    public BlendMode BlendMode { get; }

    /// <summary>Image-filter hint used when re-encoding the image into the
    /// PDF resource dictionary.</summary>
    public ImageFilterType FilterType { get; }

    /// <summary>True when the image carries an alpha mask that should be
    /// honoured during compositing.</summary>
    public bool IsMasked { get; }
}

/// <summary>PDF blend modes (Table 136 of PDF 32000-1:2008).
/// FOSS recognises the values; only <see cref="Normal"/> is honoured at
/// render time.</summary>
public enum BlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity,
    Compatible,
}

// ImageFilterType is already declared in src/Stubs/TypeStubs.cs at the
// Aspose.Pdf namespace level (Jpeg2000 / Flate / Jpeg / CCITTFax — matches
// Aspose.PDF for .NET). CompositingParameters references that existing enum.
