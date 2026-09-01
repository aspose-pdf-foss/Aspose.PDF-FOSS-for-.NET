using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

/// <summary>
/// Result of a PDF/A validation check.
/// </summary>
internal sealed class PdfAValidationResult
{
    /// <summary>Whether the document passes validation.</summary>
    public bool IsValid { get; init; }

    /// <summary>Alias for <see cref="IsValid"/>.</summary>
    public bool IsCompliant => IsValid;

    /// <summary>The target format that was checked.</summary>
    public PdfFormat Format { get; init; }

    /// <summary>List of issues found (simple string descriptions for backward compat).</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>Structured list of violations with rule identifiers and page numbers.</summary>
    public IReadOnlyList<PdfAViolation> Violations { get; init; } = [];
}
