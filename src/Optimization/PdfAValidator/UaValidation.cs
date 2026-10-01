using System.Linq;
﻿using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

/// <summary>
/// Collects what one PDF/UA-1 validation pass finds. Only an <see
/// cref="PdfAProblemSeverity.Error"/> reaches the issue list that decides the verdict —
/// warnings and manual-check notes are recorded in the log and leave the document valid.
/// </summary>
internal sealed class UaReport(List<string> issues, List<PdfAViolation> violations)
{
    public void Add(UaProblemKind kind, string rule, string description,
        int? page = null, string? objectId = null)
    {
        if (kind.Severity == PdfAProblemSeverity.Error) issues.Add(description);
        violations.Add(new PdfAViolation
        {
            Rule = rule,
            Description = description,
            Section = kind.Section,
            Clause = kind.Clause,
            Code = kind.Code,
            Severity = kind.Severity,
            Convertable = kind.Convertable,
            PageNumber = page,
            ObjectId = objectId,
        });
    }
}

internal static partial class PdfAValidator
{
    /// <summary>
    /// Validate a document against PDF/UA-1 (ISO 14289-1) — accessibility, not a PDF/A
    /// conformance level, so it has its own requirement set and its own log vocabulary.
    /// </summary>
    /// <summary>The problems a PDF/UA-1 CONVERSION reports about the document it is handed:
    /// the same requirement set validation applies, with the two differences a conversion
    /// makes - it embeds the faces the source left out, and it creates the XMP packet a
    /// source without one lacks, so the title it will then carry is empty rather than
    /// absent.</summary>
    public static PdfAValidationResult ReportPdfUaConversion(Document document) =>
        ValidatePdfUa(document, forConversion: true);

    private static PdfAValidationResult ValidatePdfUa(Document document, bool forConversion = false)
    {
        var issues = new List<string>();
        var violations = new List<PdfAViolation>();
        var report = new UaReport(issues, violations);

        CheckUaCatalog(document, report);
        CheckUaMetadata(document, report, forConversion);
        CheckUaFonts(document, report, forConversion);
        CheckUaAnnotations(document, report);
        var structure = ReadStructureTree(document);
        CheckUaParentTree(document, structure, report);
        CheckUaInlineStructureElements(document, structure, report);
        CheckUaTables(document, structure, report);
        CheckUaContent(document, report);

        // Colour contrast can never be settled mechanically, so every UA log carries the
        // note. Reading order is listed the same way, but only for a document that draws
        // text at all — there is nothing to read in a document that draws none.
        report.Add(UaProblems.ColorContrast, "UaColorContrast", "Color contrast");
        if (DrawsText(document))
            report.Add(UaProblems.LogicalReadingOrder, "UaReadingOrder",
                "Ensure text content is tagged in logical reading order");

        return new PdfAValidationResult
        {
            IsValid = issues.Count == 0,
            Format = PdfFormat.PDF_UA_1,
            Issues = issues,
            Violations = violations,
        };
    }

    /// <summary>The catalog-level requirements: the document declares itself tagged, carries
    /// a structure tree with no suspect content, and asks the viewer to show its title.</summary>
    private static void CheckUaCatalog(Document document, UaReport report)
    {
        var reader = document.Reader;
        var catalog = document.Catalog;

        if (!document.IsTagged)
            report.Add(UaProblems.DocumentNotMarkedAsTagged, "TaggedPdf",
                "Document is not marked as tagged");

        if (!document.HasStructTree)
            report.Add(UaProblems.StructureTreeMissing, "StructureTree", "Structure tree missing");

        if (reader.ResolveDict(catalog.Get("MarkInfo"))?.Get("Suspects") is PdfBoolean { Value: true })
            report.Add(UaProblems.SuspectsIsSet, "Suspects", "'Suspects' entry is set");

        if (reader.ResolveDict(catalog.Get("ViewerPreferences")) is null)
            report.Add(UaProblems.ViewerPreferencesMissing, "ViewerPreferences",
                "'ViewerPreferences' dictionary missing");

        if (!document.DisplayDocTitle)
            report.Add(UaProblems.DisplayDocTitleNotSet, "DisplayDocTitle",
                "'DisplayDocTitle' entry is not set");
    }

    /// <summary>The XMP requirements: the packet itself, the title inside it, and the
    /// PDF/UA identifier.</summary>
    /// <remarks>The title is read from the XMP packet alone, never from /Info: a document
    /// with an /Info title but no packet still reports its title as missing. With no packet
    /// there is nothing to read at all, so the packet, the title and the identifier are all
    /// reported. The identifier is judged by the ENTRY, not its value - a document that
    /// declares an empty pdfuaid:part has stated its intent and is not reported.</remarks>
    private static void CheckUaMetadata(Document document, UaReport report, bool forConversion)
    {
        if (!document.HasMetadata)
        {
            report.Add(UaProblems.XmpMetadataMissing, "Metadata", "XMP metadata missing in document");
            // A conversion creates the packet, so the title it goes on to carry is empty
            // rather than absent, and the identifier it writes is never missing.
            if (forConversion)
            {
                report.Add(UaProblems.TitleEmptyInXmp, "DocumentTitle",
                    "Title is empty in document's XMP metadata");
                return;
            }
            report.Add(UaProblems.TitleMissingInXmp, "DocumentTitle",
                "Title missing in document's XMP metadata");
            report.Add(UaProblems.PdfUaIdentifierMissing, "PdfUaIdentifier", "PDF/UA identifier missing");
            return;
        }

        // ContainsKey asks the PACKET; the value getters fall back to /Info, which
        // would hide a title the packet does not actually carry.
        var metadata = document.Metadata;
        if (!metadata.ContainsKey("dc:title"))
            report.Add(UaProblems.TitleMissingInXmp, "DocumentTitle",
                "Title missing in document's XMP metadata");
        else if (metadata.Get("dc:title") is { Length: 0 })
            report.Add(UaProblems.TitleEmptyInXmp, "DocumentTitle",
                "Title is empty in document's XMP metadata");

        if (!metadata.ContainsKey("pdfuaid:part"))
            report.Add(UaProblems.PdfUaIdentifierMissing, "PdfUaIdentifier", "PDF/UA identifier missing");
    }

    /// <summary>The annotation requirements PDF/UA-1 &#167;7.18 states.</summary>
    private static void CheckUaAnnotations(Document document, UaReport report)
    {
        foreach (var page in document.Pages)
            CheckUaLinkAnnotations(document, page, report);
    }

    /// <summary>The content requirements: ISO 14289-1 §7.1 (14.8 in ISO 32000) — all real
    /// content shall be tagged.</summary>
    /// <remarks>A document THIS instance just UA-converted is exempt: the conversion's
    /// auto-tagger repairs the structure level while content-stream MCIDs are not yet
    /// emitted, and its own post-validation treats the repaired dimension as fixed (the
    /// PdfAConversionApplied precedent).</remarks>
    private static void CheckUaContent(Document document, UaReport report)
    {
        if (document.LastConvertedFormat == PdfFormat.PDF_UA_1) return;
        var reportedUntagged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in document.Pages)
            CheckUaUntaggedContent(page, report, reportedUntagged);
    }

    /// <summary>True when any page draws text. Stops at the first show-text operator —
    /// this only decides whether the reading-order note belongs on the log.</summary>
    private static bool DrawsText(Document document)
    {
        foreach (var page in document.Pages)
        {
            IEnumerable<Operator> ops;
            try { ops = page.Contents; } catch { continue; }
            foreach (var op in ops)
                if (op is Aspose.Pdf.Operators.TextShowOperator) return true;
        }
        return false;
    }
}
