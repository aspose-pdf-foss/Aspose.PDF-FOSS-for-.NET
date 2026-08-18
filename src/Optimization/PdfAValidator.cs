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
}

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

/// <summary>
/// Validates PDF documents against PDF/A conformance requirements.
/// Checks required structural elements, font embedding, color spaces, transparency,
/// annotations, actions, and metadata per ISO 19005.
/// </summary>
internal static class PdfAValidator
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
        if (format is PdfFormat.PDF_X_1A or PdfFormat.PDF_X_3)
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

        return new PdfAValidationResult
        {
            IsValid = issues.Count == 0,
            Format = PdfFormat.PDF_UA_1,
            Issues = issues,
            Violations = violations,
        };
    }

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

    private static void CheckMetadata(Document document, PdfFormat format,
        List<string> issues, List<PdfAViolation> violations)
    {
        if (!document.HasMetadata)
        {
            issues.Add("Missing XMP metadata stream (required for PDF/A).");
            violations.Add(new PdfAViolation
            {
                Rule = "Metadata",
                Description = "Missing XMP metadata stream (required for PDF/A).",
            });
            return;
        }

        if (document.Metadata is not null)
        {
            var pdfaIdPart = document.Metadata.Get("pdfaid:part");
            if (string.IsNullOrEmpty(pdfaIdPart))
            {
                issues.Add("Missing pdfaid:part in XMP metadata.");
                violations.Add(new PdfAViolation
                {
                    Rule = "MetadataPdfAId",
                    Description = "Missing pdfaid:part in XMP metadata.",
                });
            }

            // PDF/A-4 (ISO 19005-4) removed the conformance level: part-4 documents
            // carry pdfaid:part="4" with NO pdfaid:conformance. Only parts 1–3 require it.
            var pdfaIdConformance = document.Metadata.Get("pdfaid:conformance");
            if (string.IsNullOrEmpty(pdfaIdConformance) && pdfaIdPart != "4")
            {
                issues.Add("Missing pdfaid:conformance in XMP metadata.");
                violations.Add(new PdfAViolation
                {
                    Rule = "MetadataPdfAConformance",
                    Description = "Missing pdfaid:conformance in XMP metadata.",
                });
            }

            // dc:title and pdf:Producer are deliberately NOT validated: ISO 19005
            // requires neither (Acrobat preflight passes conformant files that
            // omit both), and demanding them here produced false
            // validation failures on conformant documents.
        }
    }

    private static void CheckTransparency(Document document, Page page, bool isPdfA1,
        List<string> issues, List<PdfAViolation> violations)
    {
        // Check page-level transparency group
        if (isPdfA1)
        {
            var group = document.Reader.ResolveDict(page.Dict.Get("Group"));
            if (group is not null && group.GetName("S") == "Transparency")
            {
                var msg = $"Page {page.Number} uses transparency group (not allowed in PDF/A-1).";
                issues.Add(msg);
                violations.Add(new PdfAViolation
                {
                    Rule = "Transparency",
                    Description = $"Page {page.Number} uses transparency (not allowed in PDF/A-1)",
                    PageNumber = page.Number,
                });
            }
        }

        // Check ExtGState for transparency-related entries
        var resources = document.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;

        var extGStateDict = document.Reader.ResolveDict(resources.Get("ExtGState"));
        if (extGStateDict is null) return;

        foreach (var gsName in extGStateDict.Keys)
        {
            var gs = document.Reader.ResolveDict(extGStateDict.Get(gsName));
            if (gs is null) continue;

            var hasTransparency = false;
            string? detail = null;

            // Check /SMask
            var smask = gs.Get("SMask");
            if (smask is not null && smask is not PdfName { Value: "None" })
            {
                hasTransparency = true;
                detail = "has soft mask (SMask)";
            }

            // Check /ca (non-stroking alpha) < 1
            if (!hasTransparency)
            {
                var caObj = gs.Get("ca");
                if (caObj is PdfReal caReal && caReal.Value < 1.0)
                {
                    hasTransparency = true;
                    detail = $"has non-stroking alpha ca={caReal.Value}";
                }
                else if (caObj is PdfInteger caInt && caInt.Value < 1)
                {
                    hasTransparency = true;
                    detail = $"has non-stroking alpha ca={caInt.Value}";
                }
            }

            // Check /CA (stroking alpha) < 1
            if (!hasTransparency)
            {
                var bigCaObj = gs.Get("CA");
                if (bigCaObj is PdfReal bigCaReal && bigCaReal.Value < 1.0)
                {
                    hasTransparency = true;
                    detail = $"has stroking alpha CA={bigCaReal.Value}";
                }
                else if (bigCaObj is PdfInteger bigCaInt && bigCaInt.Value < 1)
                {
                    hasTransparency = true;
                    detail = $"has stroking alpha CA={bigCaInt.Value}";
                }
            }

            // Check /BM (blend mode) not Normal
            if (!hasTransparency)
            {
                var bm = gs.GetName("BM");
                if (bm is not null && bm != "Normal" && bm != "Compatible")
                {
                    hasTransparency = true;
                    detail = $"has blend mode BM={bm}";
                }
            }

            if (hasTransparency && isPdfA1)
            {
                var msg = $"Page {page.Number} uses transparency (not allowed in PDF/A-1)";
                if (!issues.Contains(msg))
                {
                    issues.Add(msg);
                    violations.Add(new PdfAViolation
                    {
                        Rule = "Transparency",
                        Description = $"Page {page.Number} ExtGState '{gsName}' {detail}",
                        PageNumber = page.Number,
                    });
                }
            }
        }
    }

    private static void CheckFontEmbedding(Document document, Page page, bool isPdfA1,
        List<string> issues, List<PdfAViolation> violations)
    {
        var resources = document.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;

        var fontDict = document.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return;

        // Only fonts actually SELECTED by a Tf in the page content are checked —
        // a non-embedded font merely declared in /Resources is never flagged.
        HashSet<string>? usedFonts = null;
        try
        {
            usedFonts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var op in page.Contents)
                if (op is Aspose.Pdf.Operators.SelectFont tf && !string.IsNullOrEmpty(tf.Name))
                    usedFonts.Add(tf.Name);
        }
        catch { usedFonts = null; /* unparsable content: fall back to checking all */ }

        foreach (var fontKey in fontDict.Keys)
        {
            if (usedFonts is not null && !usedFonts.Contains(fontKey)) continue;
            var font = document.Reader.ResolveDict(fontDict.Get(fontKey));
            if (font is null) continue;

            var baseFont = font.GetName("BaseFont") ?? "Unknown";
            var subtype = font.GetName("Subtype");

            // Type3 fonts are inline and don't need FontDescriptor
            if (subtype == "Type3") continue;

            // For Type0 (composite) fonts, check the descendant font
            if (subtype == "Type0")
            {
                var descendantFonts = document.Reader.Resolve(font.Get("DescendantFonts")) as PdfArray;
                if (descendantFonts is { Count: > 0 })
                {
                    var descendant = document.Reader.ResolveDict(descendantFonts[0]);
                    if (descendant is not null)
                    {
                        CheckFontDescriptor(document, descendant, baseFont, isPdfA1, page.Number, issues, violations);
                    }
                }
                continue;
            }

            CheckFontDescriptor(document, font, baseFont, isPdfA1, page.Number, issues, violations);
        }
    }

    private static void CheckFontDescriptor(Document document, PdfDictionary font,
        string baseFont, bool isPdfA1, int pageNumber,
        List<string> issues, List<PdfAViolation> violations)
    {
        // Standard 14 fonts are exempt in PDF/A-1 only
        if (isPdfA1 && Standard14Fonts.Contains(baseFont))
            return;

        var descriptor = document.Reader.ResolveDict(font.Get("FontDescriptor"));
        if (descriptor is null)
        {
            var msg = $"Font '{baseFont}' is not embedded";
            issues.Add(msg);
            violations.Add(new PdfAViolation
            {
                Rule = "FontEmbedding",
                Description = msg,
                PageNumber = pageNumber,
            });
            return;
        }

        var hasFontFile = descriptor.ContainsKey("FontFile") ||
                          descriptor.ContainsKey("FontFile2") ||
                          descriptor.ContainsKey("FontFile3");
        if (!hasFontFile)
        {
            var msg = $"Font '{baseFont}' is not embedded";
            issues.Add(msg);
            violations.Add(new PdfAViolation
            {
                Rule = "FontEmbedding",
                Description = msg,
                PageNumber = pageNumber,
            });
        }

        // A Courier-family FontDescriptor whose Descent falls below -310 carries an
        // out-of-range vertical metric that conversion does not correct, leaving the
        // font non-conformant. The range check is Courier-specific: other families
        // legitimately carry a descent below -310 (a 2048-em descriptor whose raw
        // hhea descent is around -480 normalises to roughly -235), and those stay
        // conformant.
        var fdName = descriptor.GetName("FontName") ?? baseFont ?? string.Empty;
        if (fdName.Contains("Courier") && descriptor.ContainsKey("Descent") &&
            descriptor.GetInt("Descent", 0) < -310)
        {
            var msg = $"Font '{baseFont}' has an out-of-range FontDescriptor Descent " +
                      $"({descriptor.GetInt("Descent", 0)}).";
            issues.Add(msg);
            violations.Add(new PdfAViolation
            {
                Rule = "FontDescriptorMetrics",
                Description = msg,
                PageNumber = pageNumber,
            });
        }
    }

    /// <summary>PDF/UA-1 §7.21.4.2: a symbolic TrueType font program (one whose
    /// cmap carries a Windows-Symbol (3,0) subtable) must contain EXACTLY one
    /// cmap encoding. Checks the embedded programs of fonts used on the page
    /// (Type0 descendants included).</summary>
    private static void CheckUaSymbolicCmap(Document document, Page page,
        List<string> issues, List<PdfAViolation> violations)
    {
        var reader = document.Reader;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        var fontRes = resources is null ? null : reader.ResolveDict(resources.Get("Font"));
        if (fontRes is null) return;

        foreach (var fontKey in fontRes.Keys)
        {
            var font = reader.ResolveDict(fontRes.Get(fontKey));
            if (font is null) continue;
            var baseFont = font.GetName("BaseFont") ?? "Unknown";
            var target = font;
            if (font.GetName("Subtype") == "Type0"
                && reader.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } desc)
                target = reader.ResolveDict(desc[0]) ?? font;
            var descriptor = reader.ResolveDict(target.Get("FontDescriptor"));
            var ff = descriptor is null ? null : reader.ResolveStream(descriptor.Get("FontFile2"));
            if (ff is null) continue;

            byte[] prog;
            try { prog = reader.DecodeStream(ff); } catch { continue; }
            if (prog.Length < 12) continue;

            // Locate the cmap table in the sfnt directory and count its subtables.
            int numTables = (prog[4] << 8) | prog[5];
            for (var i = 0; i < numTables; i++)
            {
                var off = 12 + i * 16;
                if (off + 16 > prog.Length) break;
                if (prog[off] != 'c' || prog[off + 1] != 'm' || prog[off + 2] != 'a' || prog[off + 3] != 'p')
                    continue;
                var toff = (prog[off + 8] << 24) | (prog[off + 9] << 16) | (prog[off + 10] << 8) | prog[off + 11];
                if (toff + 4 > prog.Length) break;
                int subtables = (prog[toff + 2] << 8) | prog[toff + 3];
                var hasSymbol = false;
                for (var j = 0; j < subtables; j++)
                {
                    var e = toff + 4 + j * 8;
                    if (e + 8 > prog.Length) break;
                    int pid = (prog[e] << 8) | prog[e + 1];
                    int eid = (prog[e + 2] << 8) | prog[e + 3];
                    if (pid == 3 && eid == 0) hasSymbol = true;
                }
                if (hasSymbol && subtables != 1)
                {
                    var msg = $"Symbolic TrueType font '{baseFont}' program cmap must contain exactly one encoding (PDF/UA-1 7.21.4.2), found {subtables}";
                    issues.Add(msg);
                    violations.Add(new PdfAViolation
                    {
                        Rule = "FontCmap",
                        Description = msg,
                        PageNumber = page.Number,
                    });
                }
                break;
            }
        }
    }

    private static bool HasOutputIntent(Document document)
    {
        var outputIntents = document.Reader.Resolve(document.Catalog.Get("OutputIntents"));
        return outputIntents is PdfArray { Count: > 0 };
    }

    /// <summary>True when /OutputIntents carries an intent with /S /GTS_PDFA1 —
    /// the (silent) PDF/A output-intent gate.</summary>
    private static bool HasPdfAOutputIntent(Document document)
    {
        if (document.Reader.Resolve(document.Catalog.Get("OutputIntents")) is not PdfArray arr)
            return false;
        foreach (var item in arr)
            if (document.Reader.ResolveDict(item)?.GetName("S") == "GTS_PDFA1")
                return true;
        return false;
    }

    private static void CheckColorSpaces(Document document, Page page, bool hasOutputIntent,
        List<string> issues, List<PdfAViolation> violations)
    {
        if (hasOutputIntent) return;

        var resources = document.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;

        var xObjectDict = document.Reader.ResolveDict(resources.Get("XObject"));
        if (xObjectDict is null) return;

        foreach (var xobjKey in xObjectDict.Keys)
        {
            var xobj = document.Reader.Resolve(xObjectDict.Get(xobjKey));

            PdfDictionary? xobjDict = null;
            if (xobj is PdfStream stream)
                xobjDict = stream.Dict;
            else if (xobj is PdfDictionary dict)
                xobjDict = dict;

            if (xobjDict is null) continue;

            var subtype = xobjDict.GetName("Subtype");
            if (subtype != "Image") continue;

            var csObj = xobjDict.Get("ColorSpace");
            if (csObj is PdfName csName)
            {
                if (csName.Value is "DeviceRGB" or "DeviceCMYK" or "DeviceGray")
                {
                    var msg = "Image uses device-dependent color space without OutputIntent";
                    issues.Add(msg);
                    violations.Add(new PdfAViolation
                    {
                        Rule = "ColorSpace",
                        Description = msg,
                        PageNumber = page.Number,
                    });
                }
            }
        }
    }

    private static void CheckAnnotations(Document document, Page page, PdfFormat format,
        List<string> issues, List<PdfAViolation> violations)
    {
        var annotsObj = document.Reader.Resolve(page.Dict.Get("Annots"));
        if (annotsObj is not PdfArray annotsArr) return;

        foreach (var item in annotsArr)
        {
            var annotDict = document.Reader.ResolveDict(item);
            if (annotDict is null) continue;

            var subtype = annotDict.GetName("Subtype");

            // PDF/A-4f (ISO 19005-4) permits embedded files: FileAttachment
            // annotations are valid content there.
            if (subtype == "FileAttachment" && format is PdfFormat.PDF_A_4F)
                continue;

            // Check prohibited subtypes
            if (subtype is not null && ProhibitedAnnotationSubtypes.Contains(subtype))
            {
                var msg = $"Annotation type '{subtype}' is not allowed in PDF/A";
                issues.Add(msg);
                violations.Add(new PdfAViolation
                {
                    Rule = "AnnotationType",
                    Description = msg,
                    PageNumber = page.Number,
                });
            }

            // Check Print flag (bit 3, value 4) is set — except for Widget annotations
            // which may be hidden
            if (subtype != "Widget" && subtype != "Popup")
            {
                var flags = (int)annotDict.GetInt("F");
                var printBit = (flags & 4) != 0; // bit 3 = Print
                if (!printBit)
                {
                    var msg = $"Annotation (type '{subtype ?? "unknown"}') missing Print flag (required for PDF/A)";
                    issues.Add(msg);
                    violations.Add(new PdfAViolation
                    {
                        Rule = "AnnotationPrintFlag",
                        Description = msg,
                        PageNumber = page.Number,
                    });
                }
            }

            // Check annotation actions
            var actionObj = document.Reader.ResolveDict(annotDict.Get("A"));
            if (actionObj is not null)
            {
                CheckActionDict(actionObj, page.Number, issues, violations);
            }
        }
    }

    private static void CheckActions(Document document,
        List<string> issues, List<PdfAViolation> violations)
    {
        // Check OpenAction
        var openAction = document.Reader.ResolveDict(document.Catalog.Get("OpenAction"));
        if (openAction is not null)
        {
            CheckActionDict(openAction, null, issues, violations);
        }

        // Check AA (Additional Actions) on catalog
        var aa = document.Reader.ResolveDict(document.Catalog.Get("AA"));
        if (aa is not null)
        {
            foreach (var key in aa.Keys)
            {
                var actionDict = document.Reader.ResolveDict(aa.Get(key));
                if (actionDict is not null)
                    CheckActionDict(actionDict, null, issues, violations);
            }
        }
    }

    private static void CheckActionDict(PdfDictionary actionDict, int? pageNumber,
        List<string> issues, List<PdfAViolation> violations)
    {
        var actionType = actionDict.GetName("S");
        if (actionType is not null && ProhibitedActionTypes.Contains(actionType))
        {
            var msg = $"Action type '{actionType}' is not allowed in PDF/A";
            issues.Add(msg);
            violations.Add(new PdfAViolation
            {
                Rule = "ActionType",
                Description = msg,
                PageNumber = pageNumber,
            });
        }
    }
}
