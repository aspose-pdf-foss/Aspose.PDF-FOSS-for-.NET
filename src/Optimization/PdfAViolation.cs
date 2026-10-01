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

    /// <summary>Value for the log's Code attribute. The reference vocabulary keys its
    /// checks by CODE (<c>7.1:7.2(12.2)</c>), which is finer than the conformance
    /// <see cref="Clause"/> it belongs to (<c>7.1</c>); when null the code repeats the
    /// clause, which is what the single-code PDF/A families do.</summary>
    public string? Code { get; init; }

    /// <summary>Name of the log section this problem is filed under (PDF/UA-1 sorts its
    /// problems into nineteen fixed sections). Null lets the writer place the problem by
    /// its <see cref="Rule"/>, the PDF/A layout.</summary>
    public string? Section { get; init; }

    /// <summary>How the problem is reported. Only <see cref="PdfAProblemSeverity.Error"/>
    /// fails the verdict.</summary>
    public PdfAProblemSeverity Severity { get; init; } = PdfAProblemSeverity.Error;
}
