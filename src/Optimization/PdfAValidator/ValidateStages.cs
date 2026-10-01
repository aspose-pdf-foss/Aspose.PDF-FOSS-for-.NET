using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

internal static partial class PdfAValidator
{
    /// <summary>Per page: transparency, font embedding, colour spaces and annotations; then the trailer's file ID.</summary>
    private static void CheckPdfAPagesAndTrailer(Document document, PdfFormat format, bool isPdfA1, List<string> issues, List<PdfAViolation> violations)
    {
        var hasOutputIntent = HasOutputIntent(document);

        // 7. Per-page checks
        foreach (var page in document.Pages)
        {
            // Transparency check (enhanced)
            CheckTransparency(document, page, isPdfA1, issues, violations);

            // Font embedding check
            CheckFontEmbedding(document, page, isPdfA1, issues, violations);

            // Color space validation
            CheckColorSpaces(document, page, hasOutputIntent, issues, violations);

            // Annotation restrictions
            CheckAnnotations(document, page, format, issues, violations);
        }

        // 8. File trailer must have /ID array
        var trailer = document.Reader.Trailer;
        if (trailer.Get("ID") is null)
        {
            issues.Add("Missing file ID in trailer (required for PDF/A).");
            violations.Add(new PdfAViolation
            {
                Rule = "FileId",
                Description = "Missing file ID in trailer (required for PDF/A).",
            });
        }
    }

    /// <summary>Level A needs a tagged document with a structure tree; prohibited actions; a PDF version below 1.3 fails.</summary>
    private static void CheckPdfALevelAndVersion(Document document, PdfFormat format, List<string> issues, List<PdfAViolation> violations)
    {
        if (format is PdfFormat.PDF_A_1A or PdfFormat.PDF_A_2A or PdfFormat.PDF_A_3A)
        {
            if (!document.IsTagged)
            {
                issues.Add("Document is not tagged (required for PDF/A Level A).");
                violations.Add(new PdfAViolation
                {
                    Rule = "TaggedPdf",
                    Description = "Document is not tagged (required for PDF/A Level A).",
                });
            }

            if (!document.HasStructTree)
            {
                issues.Add("Missing structure tree (required for PDF/A Level A).");
                violations.Add(new PdfAViolation
                {
                    Rule = "StructureTree",
                    Description = "Missing structure tree (required for PDF/A Level A).",
                });
            }
        }

        // 4. Check for prohibited actions (OpenAction and annotation actions)
        CheckActions(document, issues, violations);

        // 5. PDF version check
        var version = document.PdfVersion;
        if (version is not null)
        {
            // 1.3, matching what conversion leaves behind — a converted document must
            // not fail the validation of the very format it was just converted to.
            if (string.Compare(version, "1.3", StringComparison.Ordinal) < 0)
            {
                issues.Add($"PDF version {version} is below 1.3 (minimum for PDF/A).");
                violations.Add(new PdfAViolation
                {
                    Rule = "PdfVersion",
                    Description = $"PDF version {version} is below 1.3 (minimum for PDF/A).",
                });
            }
        }
    }

    /// <summary>File-level rules: no xref stream under PDF/A-1, the metadata checks, ZUGFeRD XMP presence, no encryption.</summary>
    private static void CheckPdfAFileLevel(Document document, PdfFormat format, bool isPdfA1, List<string> issues, List<PdfAViolation> violations)
    {
        if (isPdfA1 && document.Reader.XRefTable.UsedXrefStream
            && !document.PdfAConversionApplied)
        {
            issues.Add("The xref stream is prohibited in PDF/A-1; the file must use a cross-reference table.");
            violations.Add(new PdfAViolation
            {
                Rule = "XrefStream",
                Description = "The xref stream is prohibited in PDF/A-1; the file must use a cross-reference table.",
            });
        }

        // 1. Must have XMP metadata
        CheckMetadata(document, format, issues, violations);

        // 1b. ZUGFeRD: the invoice profile requires the ZUGFeRD XMP extension schema
        // (zf:DocumentType/DocumentFileName/... in urn:ferd:pdfa:CrossIndustryDocument).
        // A PDF/A-3 file without that block is not a ZUGFeRD invoice.
        if (format == PdfFormat.ZUGFeRD)
        {
            var zfMeta = document.HasMetadata ? document.Metadata : null;
            var hasZf = zfMeta is not null
                && zfMeta.Keys.Any(k => k.StartsWith("zf:", StringComparison.Ordinal));
            if (!hasZf)
            {
                issues.Add("ZUGFeRD XMP metadata missing.");
                violations.Add(new PdfAViolation
                {
                    Rule = "ZUGFeRDXmp",
                    Description = "ZUGFeRD XMP metadata missing.",
                });
            }
        }

        // 2. Must not be encrypted
        if (document.IsEncrypted)
        {
            issues.Add("Document is encrypted (not allowed in PDF/A).");
            violations.Add(new PdfAViolation
            {
                Rule = "Encryption",
                Description = "Document is encrypted (not allowed in PDF/A).",
            });
        }
    }

    /// <summary>A document claiming a PDF/A part and conformance must claim the requested ones and carry an output intent; null when the claim is consistent.</summary>
    private static PdfAValidationResult? CheckPdfAClaim(Document document, PdfFormat format)
    {
        var (reqPart, reqConf) = format switch
        {
            PdfFormat.PDF_A_1A => ("1", "A"),
            PdfFormat.PDF_A_1B => ("1", "B"),
            PdfFormat.PDF_A_2A => ("2", "A"),
            PdfFormat.PDF_A_2B => ("2", "B"),
            PdfFormat.PDF_A_2U => ("2", "U"),
            PdfFormat.PDF_A_3A => ("3", "A"),
            PdfFormat.PDF_A_3B => ("3", "B"),
            PdfFormat.PDF_A_3U => ("3", "U"),
            PdfFormat.ZUGFeRD => ("3", "B"),
            PdfFormat.PDF_A_4 or PdfFormat.PDF_A_4E or PdfFormat.PDF_A_4F => ("4", (string?)null),
            _ => ((string?)null, (string?)null),
        };
        var claimPart = document.HasMetadata ? document.Metadata?.Get("pdfaid:part") : null;
        var claimConf = document.HasMetadata ? document.Metadata?.Get("pdfaid:conformance") : null;
        if (reqPart is not null && !string.IsNullOrEmpty(claimPart)
            && (claimPart != reqPart || (reqConf is not null && claimConf != reqConf)))
            return new PdfAValidationResult { IsValid = false, Format = format };

        // OUTPUT-INTENT GATE (silent like the claim gate): a matched claim without a
        // /GTS_PDFA1 output intent fails without logging.
        if (!string.IsNullOrEmpty(claimPart) && !HasPdfAOutputIntent(document))
            return new PdfAValidationResult { IsValid = false, Format = format };
        return null;
    }
}
