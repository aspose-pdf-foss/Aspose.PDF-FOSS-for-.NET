namespace Aspose.Pdf;

/// <summary>
/// PDF format/conformance level for validation and conversion.
/// </summary>
public enum PdfFormat
{
    /// <summary>Standard PDF (no conformance).</summary>
    Pdf,

    /// <summary>PDF/A-1a (ISO 19005-1, Level A).</summary>
    PDF_A_1A,

    /// <summary>PDF/A-1b (ISO 19005-1, Level B).</summary>
    PDF_A_1B,

    /// <summary>PDF/A-2a (ISO 19005-2, Level A).</summary>
    PDF_A_2A,

    /// <summary>PDF/A-2b (ISO 19005-2, Level B).</summary>
    PDF_A_2B,

    /// <summary>PDF/A-3a (ISO 19005-3, Level A).</summary>
    PDF_A_3A,

    /// <summary>PDF/A-3b (ISO 19005-3, Level B).</summary>
    PDF_A_3B,

    /// <summary>PDF/A-2u (ISO 19005-2, Level U — Unicode text).</summary>
    PDF_A_2U,

    /// <summary>PDF/A-3u (ISO 19005-3, Level U — Unicode text).</summary>
    PDF_A_3U,

    /// <summary>PDF/A-4 (ISO 19005-4).</summary>
    PDF_A_4,

    /// <summary>PDF/A-4e (ISO 19005-4, engineering documents).</summary>
    PDF_A_4E,

    /// <summary>PDF/A-4f (ISO 19005-4, embedded files).</summary>
    PDF_A_4F,

    /// <summary>ZUGFeRD (electronic invoicing, based on PDF/A-3).</summary>
    ZUGFeRD,

    /// <summary>PDF/UA-1 (ISO 14289-1, universal accessibility).</summary>
    PDF_UA_1,

    /// <summary>PDF/X-1a (ISO 15930-1).</summary>
    PDF_X_1A,

    /// <summary>PDF/X-3 (ISO 15930-3).</summary>
    PDF_X_3,

    /// <summary>PDF 1.0.</summary>
    v_1_0,

    /// <summary>PDF 1.1.</summary>
    v_1_1,

    /// <summary>PDF 1.2.</summary>
    v_1_2,

    /// <summary>PDF 1.3.</summary>
    v_1_3,

    /// <summary>PDF 1.4.</summary>
    v_1_4,

    /// <summary>PDF 1.5.</summary>
    v_1_5,

    /// <summary>PDF 1.6.</summary>
    v_1_6,

    /// <summary>PDF 1.7 (ISO 32000-1).</summary>
    v_1_7,

    /// <summary>PDF 2.0 (ISO 32000-2).</summary>
    v_2_0,

    /// <summary>PDF/E-1 (engineering, ISO 24517-1).</summary>
    PDF_E_1,

    /// <summary>PDF/X-1a:2001 (ISO 15930-1, 2001 edition).</summary>
    PDF_X_1A_2001,

    /// <summary>PDF/X-4 (ISO 15930-7).</summary>
    PDF_X_4,
}

/// <summary>
/// Action to take when transparency is encountered during PDF/A conversion.
/// </summary>
public enum ConvertTransparencyAction
{
    /// <summary>Default handling — remove transparency.</summary>
    Default,
    /// <summary>Mask transparent areas.</summary>
    Mask,
}

/// <summary>
/// Action to take when a conversion error is found.
/// </summary>
public enum ConvertErrorAction
{
    /// <summary>Delete problematic elements.</summary>
    Delete,

    /// <summary>Do nothing (log only).</summary>
    None,
}
