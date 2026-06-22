#nullable disable

using System.IO;

namespace Aspose.Pdf;

/// <summary>
/// Options for saving a PDF document as HTML.
/// </summary>
/// <remarks>
/// The HTML writer in this build positions text fragments with absolute CSS,
/// inlines images as base64 data URIs and emits link annotations as anchor
/// tags. Most callback / strategy / styling fields are stored as configuration
/// only — they are not consulted by the writer at this time.
/// </remarks>
public class HtmlSaveOptions : UnifiedSaveOptions
{
    // ── Constructors ─────────────────────────────────────────────────────────

    public HtmlSaveOptions() { }

    public HtmlSaveOptions(bool fixedLayout)
    {
        FixedLayout = fixedLayout;
    }

    public HtmlSaveOptions(HtmlDocumentType documentType)
    {
        DocumentType = documentType;
    }

    public HtmlSaveOptions(HtmlDocumentType documentType, bool fixedLayout)
    {
        DocumentType = documentType;
        FixedLayout = fixedLayout;
    }

    // ── Properties ───────────────────────────────────────────────────────────

    /// <summary>Extra width (PDF points) added to each page's right margin
    /// in the output HTML. Stored only.</summary>
    public int AdditionalMarginWidthInPoints { get; set; }

    /// <summary>How many PDF pages are processed per output batch when
    /// SplitIntoPages is in effect. Stored only.</summary>
    public int BatchSize { get; set; }

    /// <summary>Compress SVG graphics if any are present. Stored only.</summary>
    public bool CompressSvgGraphicsIfAny { get; set; }

    /// <summary>Convert marked-content sections to HTML layers. Stored only.</summary>
    public bool ConvertMarkedContentToLayers { get; set; }

    /// <summary>Default font name used when a glyph has no resolvable face. Stored only.</summary>
    public string DefaultFontName { get; set; }

    /// <summary>HTML doc-type emitted by the writer.</summary>
    public HtmlDocumentType DocumentType { get; set; }

    /// <summary>1-based page numbers to render. Null/empty means render all pages.</summary>
    public int[] ExplicitListOfSavedPages { get; set; }

    /// <summary>Use fixed (absolute) layout when emitting text positions.</summary>
    public bool FixedLayout { get; set; } = true;

    /// <summary>Make flow-layout paragraphs span the full page width. Stored only.</summary>
    public bool FlowLayoutParagraphFullWidth { get; set; }

    /// <summary>Font sources consulted when embedding fonts in the HTML output.</summary>
    public Aspose.Pdf.Text.FontSourceCollection FontSources { get; } = new();

    /// <summary>Suppress font-resource errors at conversion time and substitute. Stored only.</summary>
    public bool IgnoreResourceFontErrors { get; set; }

    /// <summary>Glyphs whose font size is at or below this value are dropped
    /// from the output. Null means no filter. Stored only.</summary>
    public System.Nullable<float> IgnoredTextFontSize { get; set; }

    /// <summary>DPI used when rasterising vector or font content. Stored only.</summary>
    public int ImageResolution { get; set; }

    /// <summary>Minimum stroke width below which lines are widened. Stored only.</summary>
    public float MinimalLineWidth { get; set; }

    /// <summary>If true, glyphs are emitted individually rather than grouped into runs. Stored only.</summary>
    public bool PreventGlyphsGrouping { get; set; }

    /// <summary>Render text fragments as raster images instead of selectable text. Stored only.</summary>
    public bool RenderTextAsImage { get; set; }

    /// <summary>Embed full font tables instead of subsetting. Stored only.</summary>
    public bool SaveFullFont { get; set; }

    /// <summary>Group adjacent glyphs into a single text-box for a cleaner DOM. Stored only.</summary>
    public bool SimpleTextboxModeGrouping { get; set; }

    /// <summary>When SplitIntoPages is true, also split the CSS per page. Stored only.</summary>
    public bool SplitCssIntoPages { get; set; }

    /// <summary>Emit one HTML file per page instead of a single concatenated document.</summary>
    public bool SplitIntoPages { get; set; }

    /// <summary>HTML &lt;title&gt; element. Stored only.</summary>
    public string Title { get; set; }

    /// <summary>Path to a PNG intermediate file (debug artifact, parity
    /// with HtmlLoadOptions.ApsIntermediateFileIfAny / XpsIntermediateFileIfAny).
    /// Stored only.</summary>
    public string PngIntermediateFileIfAny { get; set; }

    /// <summary>Path to an XPS intermediate file (debug artifact). Stored only.</summary>
    public string XpsIntermediateFileIfAny { get; set; }

    /// <summary>Path to an APS intermediate file (debug artifact). Stored only.</summary>
    public string ApsIntermediateFileIfAny { get; set; }

    /// <summary>Merge adjacent text fragments into a single run when possible. Stored only.</summary>
    public bool TryMergeFragments { get; set; }

    /// <summary>Honour z-order of overlapping content when emitting HTML. Stored only.</summary>
    public bool UseZOrder { get; set; }

    // ── Public fields (matches Aspose.PDF for .NET reflection shape) ─────────

    public AntialiasingProcessingType AntialiasingProcessing;
    public string CssClassNamesPrefix;
    public CssSavingStrategy CustomCssSavingStrategy;
    public HtmlPageMarkupSavingStrategy CustomHtmlSavingStrategy;
    public ConversionProgressEventHandler CustomProgressHandler;
    public ResourceSavingStrategy CustomResourceSavingStrategy;
    public CssUrlMakingStrategy CustomStrategyOfCssUrlCreation;
    public string[] ExcludeFontNameList;
    public FontEncodingRules FontEncodingStrategy;
    public FontSavingModes FontSavingMode;
    public HtmlMarkupGenerationModes HtmlMarkupGenerationMode;
    public LettersPositioningMethods LettersPositioningMethod;
    public SaveOptions.BorderInfo PageBorderIfAny;
    public SaveOptions.MarginInfo PageMarginIfAny;
    public bool PagesFlowTypeDependsOnViewersScreenSize;
    public PartsEmbeddingModes PartsEmbeddingMode = PartsEmbeddingModes.EmbedAllIntoHtml;
    public RasterImagesSavingModes RasterImagesSavingMode;
    public bool RemoveEmptyAreasOnTopAndBottom;
    public bool SaveShadowedTextsAsTransparentTexts;
    public bool SaveTransparentTexts;
    public string SpecialFolderForAllImages;
    public string SpecialFolderForSvgImages;
    public bool TrySaveTextUnderliningAndStrikeoutingInCss;

    // ── Nested enums ─────────────────────────────────────────────────────────

    /// <summary>Antialiasing pass applied to the resulting HTML.</summary>
    public enum AntialiasingProcessingType
    {
        NoAdditionalProcessing,
        TryCorrectResultHtml,
    }

    /// <summary>Backing-byte enum: the Aspose.PDF for .NET reflection signature is byte-typed.</summary>
    public enum FontEncodingRules : byte
    {
        Default,
        DecreaseToUnicodePriorityLevel,
    }

    /// <summary>How fonts are emitted into the output HTML.</summary>
    public enum FontSavingModes
    {
        AlwaysSaveAsWOFF,
        AlwaysSaveAsTTF,
        AlwaysSaveAsEOT,
        SaveInAllFormats,
        DontSave,
    }

    /// <summary>Granularity of HTML markup produced.</summary>
    public enum HtmlMarkupGenerationModes
    {
        WriteAllHtml,
        WriteOnlyBodyContent,
    }

    /// <summary>Image type produced for embedded raster content.</summary>
    public enum HtmlImageType
    {
        Jpeg,
        Png,
        Bmp,
        Gif,
        Tiff,
        Svg,
        ZippedSvg,
        Unknown,
    }

    /// <summary>Container element that hosts an image.</summary>
    public enum ImageParentTypes
    {
        HtmlPage,
        SvgImage,
    }

    /// <summary>How letter positions are encoded in the output CSS.</summary>
    public enum LettersPositioningMethods
    {
        UseEmUnitsAndCompensationOfRoundingErrorsInCss,
        UsePixelUnitsInCssLetterSpacingForIE,
    }

    /// <summary>Whether and how secondary parts (CSS / images / fonts) are embedded.</summary>
    public enum PartsEmbeddingModes
    {
        EmbedAllIntoHtml,
        EmbedCssOnly,
        NoEmbedding,
    }

    /// <summary>How raster images are saved alongside the HTML.</summary>
    public enum RasterImagesSavingModes
    {
        AsPngImagesEmbeddedIntoSvg,
        AsExternalPngFilesReferencedViaSvg,
        AsEmbeddedPartsOfPngPageBackground,
        DontSave,
    }

    // ── Nested info-bag classes ─────────────────────────────────────────────

    /// <summary>Context passed to a <see cref="CssSavingStrategy"/> callback.</summary>
    public class CssSavingInfo
    {
        public Stream ContentStream;
        public int CssNumber;
        public string SupposedURL;
    }

    /// <summary>Context passed to a <see cref="CssUrlMakingStrategy"/> callback.</summary>
    public class CssUrlRequestInfo
    {
        public bool CustomProcessingCancelled;
    }

    /// <summary>Context passed to a <see cref="HtmlPageMarkupSavingStrategy"/> callback.</summary>
    public class HtmlPageMarkupSavingInfo
    {
        public Stream ContentStream;
        public bool CustomProcessingCancelled;
        public int HtmlHostPageNumber;
        public int PdfHostPageNumber;
        public string SupposedFileName;
    }

    /// <summary>Context passed to a per-image saving callback.</summary>
    public class HtmlImageSavingInfo
    {
        public int HtmlHostPageNumber;
        public HtmlImageType ImageType;
        public ImageParentTypes ParentType;
        public int PdfHostPageNumber;
    }

    // ── Nested delegates (callback strategies) ──────────────────────────────

    /// <summary>Callback invoked for each generated CSS file.</summary>
    public delegate void CssSavingStrategy(CssSavingInfo partSavingInfo);

    /// <summary>Callback that lets the caller customise the URL written into
    /// <c>href</c> attributes of <c>&lt;link&gt;</c> elements that reference CSS.</summary>
    public delegate string CssUrlMakingStrategy(CssUrlRequestInfo cssUrlRequestInfo);

    /// <summary>Callback invoked for each generated HTML page.</summary>
    public delegate void HtmlPageMarkupSavingStrategy(HtmlPageMarkupSavingInfo htmlSavingInfo);

    /// <summary>Callback invoked for each generated resource (image / font); returns the
    /// file name to save the resource as, or null to skip.</summary>
    public delegate string ResourceSavingStrategy(SaveOptions.ResourceSavingInfo resourceSavingInfo);
}
