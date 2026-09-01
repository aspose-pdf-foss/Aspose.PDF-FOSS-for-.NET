using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

/// <summary>
/// Describes a single PDF/A compliance violation.
/// </summary>
internal sealed class PdfAViolation
{
    /// <summary>Short rule identifier (e.g., "FontEmbedding", "Transparency").</summary>
    public required string Rule { get; init; }

    /// <summary>Human-readable description of the violation.</summary>
    public required string Description { get; init; }

    /// <summary>Page number where the violation was found, or null for document-level issues.</summary>
    public int? PageNumber { get; init; }

    /// <summary>Explicit conformance clause for the log's Clause attribute; when null the
    /// log writer derives one from <see cref="Rule"/>.</summary>
    public string? Clause { get; init; }

    /// <summary>Whether conversion can repair this violation. An unconvertable violation
    /// (an implementation limit baked into the content) makes the conversion report
    /// failure.</summary>
    public bool Convertable { get; init; } = true;

    /// <summary>Value for the log's ObjectID attribute (an object number, or the
    /// document's permanent file ID for whole-document refusals); omitted when null.</summary>
    public string? ObjectId { get; init; }
}
