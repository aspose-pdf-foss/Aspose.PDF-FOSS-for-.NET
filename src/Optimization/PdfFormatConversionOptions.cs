using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

/// <summary>
/// Options for converting a PDF document to a specific PDF/A conformance level.
/// </summary>
public sealed class PdfFormatConversionOptions
{
    /// <summary>Initializes a new instance with the specified target format.</summary>
    public PdfFormatConversionOptions(PdfFormat format)
    {
        TargetFormat = format;
        ErrorAction = ConvertErrorAction.Delete;
    }

    /// <summary>Initializes a new instance with the specified target format and error action.</summary>
    public PdfFormatConversionOptions(PdfFormat format, ConvertErrorAction action)
    {
        TargetFormat = format;
        ErrorAction = action;
    }

    /// <summary>Initializes a new instance with a log stream, target format, and error action.</summary>
    public PdfFormatConversionOptions(Stream outputLogStream, PdfFormat format, ConvertErrorAction action)
    {
        LogStream = outputLogStream;
        TargetFormat = format;
        ErrorAction = action;
    }

    /// <summary>Initializes a new instance with a log file path and target format.</summary>
    public PdfFormatConversionOptions(string outputLogFileName, PdfFormat format)
        : this(outputLogFileName, format, ConvertErrorAction.Delete)
    {
    }

    /// <summary>Initializes a new instance with a log file path, target format, and error action.</summary>
    public PdfFormatConversionOptions(string outputLogFileName, PdfFormat format, ConvertErrorAction action)
    {
        LogFileName = outputLogFileName;
        TargetFormat = format;
        ErrorAction = action;
    }

    /// <summary>Initializes a new instance with a log file path, target format, error action and transparency action.</summary>
    public PdfFormatConversionOptions(string outputLogFileName, PdfFormat format, ConvertErrorAction action, ConvertTransparencyAction transparencyAction)
        : this(outputLogFileName, format, action)
    {
        TransparencyAction = transparencyAction;
    }

    /// <summary>Target PDF/A conformance level.</summary>
    public PdfFormat TargetFormat { get; set; }

    /// <summary>Target PDF/A conformance level (alias for <see cref="TargetFormat"/>).</summary>
    public PdfFormat Format
    {
        get => TargetFormat;
        set => TargetFormat = value;
    }

    /// <summary>Action to take when a violation is found: fix it or log only.</summary>
    public ConvertErrorAction ErrorAction { get; set; }

    /// <summary>Whether to optimize file size during conversion.</summary>
    public bool OptimizeFileSize { get; set; }

    /// <summary>Action to take when transparency is encountered.</summary>
    public ConvertTransparencyAction TransparencyAction { get; set; }

    /// <summary>Log file path for writing conversion results.</summary>
    public string? LogFileName { get; set; }

    /// <summary>Stream for logging conversion results.</summary>
    public Stream? LogStream { get; set; }

    /// <summary>Log of violations found (and optionally fixed) during conversion.
    /// Internal — not part of the public API surface.</summary>
    internal List<PdfAViolation> ConversionLog { get; } = new();

    /// <summary>Path to an ICC color profile file for PDF/X conversion.</summary>
    public string? IccProfileFileName { get; set; }

    /// <summary>Output intent for PDF/X conversion.</summary>
    public OutputIntent? OutputIntent { get; set; }

    // ── Additional options ────────────────────────────────────────────────

    /// <summary>Strategy for collapsing adjacent text segments during PDF/A conversion.</summary>
    public enum SegmentAlignStrategy : byte
    {
        None = 0,
        RestoreSegmentBounds = 1,
    }

    /// <summary>Strategy for removing or excluding fonts during conversion.</summary>
    public enum RemoveFontsStrategy : byte
    {
        SubsetFonts = 0,
        RemoveDuplicatedFonts = 1,
        RemoveSimilarFontsWithDifferentWidths = 2,
    }

    /// <summary>Strategy for processing Private-Use-Area Unicode characters.</summary>
    public enum PuaProcessingStrategy
    {
        None = 0,
        SubstitutePuaSymbols = 1,
        SurroundPuaTextWithEmptyActualText = 2,
    }

    /// <summary>Segment alignment strategy applied during conversion.</summary>
    public SegmentAlignStrategy AlignStrategy;

    /// <summary>When true, the converter aligns adjacent text fragments according to <see cref="AlignStrategy"/>.</summary>
    public bool AlignText { get; set; }

    /// <summary>Auto-tagging configuration.</summary>
    public AutoTaggingSettings AutoTaggingSettings { get; set; } = new AutoTaggingSettings();

    /// <summary>How soft masks are converted.</summary>
    public ConvertSoftMaskAction ConvertSoftMaskAction { get; set; } = ConvertSoftMaskAction.Default;

    /// <summary>Default conversion options targeting PDF/A-1B with Delete-on-error.</summary>
    public static PdfFormatConversionOptions Default => new(PdfFormat.PDF_A_1B, ConvertErrorAction.Delete);

    /// <summary>Strategy for removing fonts from the converted output.</summary>
    public RemoveFontsStrategy ExcludeFontsStrategy { get; set; } = RemoveFontsStrategy.SubsetFonts;

    /// <summary>Font-embedding behaviour.</summary>
    public FontEmbeddingOptions FontEmbeddingOptions { get; } = new FontEmbeddingOptions();

    /// <summary>When true, image-stream conversion runs asynchronously where possible. Stored only.</summary>
    public bool IsAsyncImageStreamsConversionMode { get; set; }

    /// <summary>When true, the converter operates in low-memory mode. Stored only.</summary>
    public bool IsLowMemoryMode { get; set; }

    /// <summary>When true, the converter transfers Info-dict entries to the output. Stored only.</summary>
    public bool IsTransferInfo { get; set; }

    /// <summary>Relaxed PDF/A specification flags.</summary>
    public PdfANonSpecificationFlags NonSpecificationCases { get; } = new PdfANonSpecificationFlags();

    /// <summary>List of font names that could not be embedded during conversion.</summary>
    public string[] NotAccessibleFonts { get; internal set; } = System.Array.Empty<string>();

    /// <summary>Private-Use-Area processing strategy.</summary>
    public PuaProcessingStrategy PuaTextProcessingStrategy { get; set; } = PuaProcessingStrategy.None;

    /// <summary>Re-encoding strategy for symbolic fonts.</summary>
    public PdfASymbolicFontEncodingStrategy SymbolicFontEncodingStrategy { get; set; } = new PdfASymbolicFontEncodingStrategy();

    /// <summary>ToUnicode CMap generation rules.</summary>
    public ToUnicodeProcessingRules UnicodeProcessingRules { get; set; } = new ToUnicodeProcessingRules();
}
