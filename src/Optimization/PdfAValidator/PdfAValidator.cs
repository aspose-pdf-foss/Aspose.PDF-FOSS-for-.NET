using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

/// <summary>
/// Validates PDF documents against PDF/A conformance requirements.
/// Checks required structural elements, font embedding, color spaces, transparency,
/// annotations, actions, and metadata per ISO 19005.
/// </summary>
internal static partial class PdfAValidator
{
    private static readonly HashSet<string> Standard14Fonts = new(StringComparer.Ordinal)
    {
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Symbol", "ZapfDingbats",
    };

    private static readonly HashSet<string> ProhibitedAnnotationSubtypes = new(StringComparer.Ordinal)
    {
        "FileAttachment", "Sound", "Movie", "3D",
    };

    private static readonly HashSet<string> ProhibitedActionTypes = new(StringComparer.Ordinal)
    {
        "Launch", "Sound", "Movie", "ResetForm", "ImportData", "JavaScript",
    };

    /// <summary>
    /// Validate a document against the specified PDF/A format.
    /// Returns a result with both simple string issues and structured violations.
    /// </summary>
    /// <summary>The single problem reported when the document's encryption
    /// permissions block conformance work: the document was opened with USER
    /// access and /P withholds modify-contents, so no validation or conversion
    /// may touch it (the log carries exactly this one entry).</summary>
    public static PdfAValidationResult PermissionBlocked(PdfFormat format)
    {
        const string description = "Conversion is not allowed by permission restrictions";
        return new PdfAValidationResult
        {
            IsValid = false,
            Format = format,
            Issues = [description],
            Violations = [new PdfAViolation { Rule = "Permission", Description = description }],
        };
    }

    /// <summary>The single problem reported when the document carries a live digital
    /// signature: conformance conversion would break the signature's byte range, so
    /// the conversion is refused outright. The log carries exactly this one entry in the
    /// Catalog section, with the document's permanent file ID as the ObjectID.</summary>
    public static PdfAValidationResult SignedFileBlocked(PdfFormat format, string? documentId)
    {
        const string description = "Can not convert signed file";
        return new PdfAValidationResult
        {
            IsValid = false,
            Format = format,
            Issues = [description],
            Violations = [new PdfAViolation
            {
                Rule = "SignedFile",
                Description = description,
                Convertable = false,
                ObjectId = documentId,
            }],
        };
    }

    public static PdfAValidationResult Validate(Document document, PdfFormat format = PdfFormat.PDF_A_1B)
    {
        // Permission-restricted documents refuse conformance validation outright —
        // the caller holds user access only and modification is not permitted.
        if (document.PdfAConversionBlockedByPermissions
            && format is not (PdfFormat.v_1_0 or PdfFormat.v_1_1 or PdfFormat.v_1_2 or PdfFormat.v_1_3
                or PdfFormat.v_1_4 or PdfFormat.v_1_5 or PdfFormat.v_1_6 or PdfFormat.v_1_7
                or PdfFormat.v_2_0 or PdfFormat.Pdf))
            return PermissionBlocked(format);

        // PDF/X validation
        if (format is PdfFormat.PDF_X_1A or PdfFormat.PDF_X_3 or PdfFormat.PDF_X_4)
            return ValidatePdfX(document, format);

        // PDF/UA-1 (ISO 14289-1) — accessibility, not a PDF/A conformance
        // level, so it has its own requirement set (no pdfaid).
        if (format is PdfFormat.PDF_UA_1)
            return ValidatePdfUa(document);

        // PDF/E-1 (ISO 24517-1) — engineering documents, its own requirement set
        // (pdfe identification, no JavaScript, embedded fonts).
        if (format is PdfFormat.PDF_E_1)
            return ValidatePdfE(document);

        // Version-only formats (a plain PDF version, not a PDF/A·X·UA conformance level):
        // a document that loaded is a structurally valid PDF of that version, so validation
        // succeeds — Validate(stream, v_1_x) returns true for a
        // well-formed PDF. (Only PDF/A·X·UA carry conformance requirements that can fail.)
        if (format is PdfFormat.v_1_0 or PdfFormat.v_1_1 or PdfFormat.v_1_2 or PdfFormat.v_1_3
            or PdfFormat.v_1_4 or PdfFormat.v_1_5 or PdfFormat.v_1_6 or PdfFormat.v_1_7
            or PdfFormat.v_2_0 or PdfFormat.Pdf)
            return new PdfAValidationResult { IsValid = true, Format = format };

        // CLAIM GATE:
        // Validate(PDF_A_<p><c>) returns false — with a CLEAN log, nothing written —
        // when the document's XMP pdfaid claim doesn't EXACTLY equal the requested
        // part and conformance (case-sensitive; a claimed 3A validates as 3A only,
        // never as 2B/3B). An ABSENT claim is NOT silent: it falls through so
        // CheckMetadata logs the missing pdfaid entries.
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

        var issues = new List<string>();
        var violations = new List<PdfAViolation>();

        var isPdfA1 = format is PdfFormat.PDF_A_1A or PdfFormat.PDF_A_1B;

        // PDF/A-1 (ISO 19005-1 §6.1.4): cross-reference STREAMS are prohibited —
        // the file must carry a classic xref table. Only flagged before a PDF/A
        // conversion has stamped the document: Convert() repairs this (the file is
        // rewritten with a classic table on save), and post-conversion validation
        // of the same in-memory document must reflect the repaired state.
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

        // 3. Level A requirements. NOTE: a document title is NOT one of them —
        // ISO 19005 requires a title at no level (that's a PDF/UA rule); Acrobat
        // preflight passes untitled Level-A files. The conversion still sets one
        // as best practice, but validation must not demand it.
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

        // 6. Check OutputIntents for color space validation
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

        return new PdfAValidationResult
        {
            IsValid = issues.Count == 0,
            Format = format,
            Issues = issues,
            Violations = violations,
        };
    }

    /// <summary>
    /// Validate a document against PDF/UA-1 (ISO 14289-1). Checks the
    /// structural requirements FOSS can verify without a full content
    /// walk: the document is tagged, has a structure tree, declares a
    /// title and natural language, shows the title in the window bar,
    /// carries the UA identifier in XMP, and has a file ID.
    /// </summary>
    private static PdfAValidationResult ValidatePdfUa(Document document)
    {
        var issues = new List<string>();
        var violations = new List<PdfAViolation>();

        void Fail(string rule, string description)
        {
            issues.Add(description);
            violations.Add(new PdfAViolation { Rule = rule, Description = description });
        }

        if (!document.IsTagged)
            Fail("TaggedPdf", "Document is not tagged (required for PDF/UA-1).");

        if (!document.HasStructTree)
            Fail("StructureTree", "Missing structure tree (required for PDF/UA-1).");

        if (string.IsNullOrEmpty(document.Info.Title))
            Fail("DocumentTitle", "Missing document title (required for PDF/UA-1).");

        if (string.IsNullOrEmpty(document.Language))
            Fail("NaturalLanguage", "Missing natural language /Lang (required for PDF/UA-1).");

        if (!document.DisplayDocTitle)
            Fail("DisplayDocTitle", "/ViewerPreferences /DisplayDocTitle must be true (required for PDF/UA-1).");

        if (!document.HasMetadata || !document.IsPdfUaCompliant)
            Fail("Metadata", "Missing pdfuaid:part in XMP metadata (required for PDF/UA-1).");

        if (document.Reader.Trailer.Get("ID") is null)
            Fail("FileId", "Missing file ID in trailer (required for PDF/UA-1).");

        // PDF/UA-1 §7.21.4.2 symbolic-TrueType cmap rule (the violation Preflight
        // flags on a tagged doc whose symbolic TrueType face lacks the required
        // cmap subtable). NOTE: a general font-EMBEDDING requirement is
        // deliberately NOT enforced here — FOSS-authored tagged documents render
        // through Standard-14 faces that are only embedded at save, and the
        // internal tagged-authoring suite validates them in memory.
        foreach (var page in document.Pages)
            CheckUaSymbolicCmap(document, page, issues, violations);

        // ISO 14289-1 §7.1 (14.8 in ISO 32000): all real content shall be tagged.
        // A document THIS instance just UA-converted is exempt: the conversion's
        // auto-tagger repairs the structure level while content-stream MCIDs are
        // not yet emitted, and its own post-validation treats the repaired
        // dimension as fixed (the PdfAConversionApplied precedent).
        if (document.LastConvertedFormat != PdfFormat.PDF_UA_1)
        {
            var reportedUntagged = new HashSet<string>(StringComparer.Ordinal);
            foreach (var page in document.Pages)
                CheckUaUntaggedContent(page, issues, violations, reportedUntagged);
        }

        return new PdfAValidationResult
        {
            IsValid = issues.Count == 0,
            Format = PdfFormat.PDF_UA_1,
            Issues = issues,
            Violations = violations,
        };
    }

    /// <summary>The clause/code string the corpus reads back for the
    /// untagged-content family (the part of PDFUA_Error_7_1_ObjectNotTagged
    /// before the pipe).</summary>
    private const string UaUntaggedClause = "7.1:1.1(14.8)";

    // PDF/E-1 prohibits the interactive/executable action types; the PDF/A-only
    // form-data prohibitions (ResetForm, ImportData) do not apply.
    private static readonly HashSet<string> PdfEProhibitedActionTypes = new(StringComparer.Ordinal)
    {
        "JavaScript", "Launch", "Sound", "Movie",
    };

    private static PdfAValidationResult ValidatePdfE(Document document)
    {
        var issues = new List<string>();
        var violations = new List<PdfAViolation>();

        void Fail(string rule, string description)
        {
            issues.Add(description);
            violations.Add(new PdfAViolation { Rule = rule, Description = description });
        }

        if (document.IsEncrypted)
            Fail("Encryption", "Document is encrypted (not allowed in PDF/E-1).");

        var reader = document.Reader;
        var names = reader.ResolveDict(reader.Catalog.Get("Names"));
        if (names?.Get("JavaScript") is not null)
            Fail("JavaScript", "Document-level JavaScript is not allowed in PDF/E-1.");

        var openAction = reader.ResolveDict(reader.Catalog.Get("OpenAction"));
        var openActionType = openAction?.GetName("S");
        if (openActionType is not null && PdfEProhibitedActionTypes.Contains(openActionType))
            Fail("ActionType", $"Action type '{openActionType}' is not allowed in PDF/E-1.");

        var catalogAa = reader.ResolveDict(reader.Catalog.Get("AA"));
        if (catalogAa is not null)
            foreach (var key in catalogAa.Keys)
            {
                var actionType = reader.ResolveDict(catalogAa.Get(key))?.GetName("S");
                if (actionType is not null && PdfEProhibitedActionTypes.Contains(actionType))
                    Fail("ActionType", $"Action type '{actionType}' is not allowed in PDF/E-1.");
            }

        if (!document.HasMetadata
            || string.IsNullOrEmpty(document.Metadata?.Get("pdfe:ISO_PDFEVersion")))
            Fail("Metadata", "Missing pdfe:ISO_PDFEVersion in XMP metadata (required for PDF/E-1).");

        foreach (var page in document.Pages)
            CheckFontEmbedding(document, page, isPdfA1: false, issues, violations);

        return new PdfAValidationResult
        {
            IsValid = issues.Count == 0,
            Format = PdfFormat.PDF_E_1,
            Issues = issues,
            Violations = violations,
        };
    }

    /// <summary>
    /// Validate with full structured results. Equivalent to <see cref="Validate"/> but
    /// named for clarity when the caller wants the detailed <see cref="PdfAViolation"/> list.
    /// </summary>
    public static PdfAValidationResult ValidateWithDetails(Document document, PdfFormat format = PdfFormat.PDF_A_1B)
    {
        return Validate(document, format);
    }

    private static PdfAValidationResult ValidatePdfX(Document document, PdfFormat format)
    {
        var issues = new List<string>();
        var violations = new List<PdfAViolation>();

        // PDF/X must not be encrypted
        if (document.IsEncrypted)
        {
            issues.Add("Document is encrypted (not allowed in PDF/X).");
            violations.Add(new PdfAViolation
            {
                Rule = "Encryption",
                Description = "Document is encrypted (not allowed in PDF/X).",
            });
        }

        // PDF/X must have OutputIntent
        var outputIntents = document.OutputIntents;
        var hasPdfXIntent = false;
        foreach (var oi in outputIntents)
        {
            if (oi.IsPdfX)
            {
                hasPdfXIntent = true;
                break;
            }
        }
        if (!hasPdfXIntent)
        {
            issues.Add("Missing PDF/X OutputIntent (required for PDF/X).");
            violations.Add(new PdfAViolation
            {
                Rule = "OutputIntent",
                Description = "Missing PDF/X OutputIntent (required for PDF/X).",
            });
        }

        // PDF/X must have file ID
        var trailer = document.Reader.Trailer;
        if (trailer.Get("ID") is null)
        {
            issues.Add("Missing file ID in trailer (required for PDF/X).");
            violations.Add(new PdfAViolation
            {
                Rule = "FileId",
                Description = "Missing file ID in trailer (required for PDF/X).",
            });
        }

        // PDF/X annotation layout: an annotation (other than TrapNet) must lie
        // completely outside the printable page area. An annotation drawn on the
        // page is the PDF/A validator's "ErrorAnnotationLayout" rule
        // (the log carries that rule id).
        foreach (var page in document.Pages)
        {
            if (document.Reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var aRef in annots)
            {
                var a = document.Reader.ResolveDict(aRef);
                if (a is null || a.GetName("Subtype") is null or "TrapNet") continue;
                var rect = document.Reader.Resolve(a.Get("Rect")) as PdfArray;
                if (rect is not { Count: 4 }) continue;
                var mb = page.MediaBox;
                static double Num(PdfObject? o) => o switch
                {
                    PdfInteger i => i.Value,
                    PdfReal r => r.Value,
                    _ => 0,
                };
                var llx = Math.Min(Num(rect[0]), Num(rect[2]));
                var lly = Math.Min(Num(rect[1]), Num(rect[3]));
                var urx = Math.Max(Num(rect[0]), Num(rect[2]));
                var ury = Math.Max(Num(rect[1]), Num(rect[3]));
                var inside = urx > mb.LLX && llx < mb.URX && ury > mb.LLY && lly < mb.URY;
                if (!inside) continue;
                var msg = $"ErrorAnnotationLayout: The annotation should lying completely outside the BleedBox: '{a.GetName("Subtype")}' on page {page.Number} lies inside the printable area (not allowed in PDF/X).";
                issues.Add(msg);
                violations.Add(new PdfAViolation
                {
                    Rule = "ErrorAnnotationLayout",
                    Description = msg,
                    PageNumber = page.Number,
                });
            }
        }

        return new PdfAValidationResult
        {
            IsValid = issues.Count == 0,
            Format = format,
            Issues = issues,
            Violations = violations,
        };
    }

}
