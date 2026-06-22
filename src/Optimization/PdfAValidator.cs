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
    public static PdfAValidationResult Validate(Document document, PdfFormat format = PdfFormat.PDF_A_1B)
    {
        // PDF/X validation
        if (format is PdfFormat.PDF_X_1A or PdfFormat.PDF_X_3)
            return ValidatePdfX(document, format);

        // PDF/UA-1 (ISO 14289-1) — accessibility, not a PDF/A conformance
        // level, so it has its own requirement set (no pdfaid).
        if (format is PdfFormat.PDF_UA_1)
            return ValidatePdfUa(document);

        // Version-only formats — always return false (document doesn't "conform" to a version)
        if (format is PdfFormat.v_1_7 or PdfFormat.v_2_0 or PdfFormat.Pdf)
            return new PdfAValidationResult { IsValid = false, Format = format, Issues = ["Not a conformance format"] };

        var issues = new List<string>();
        var violations = new List<PdfAViolation>();

        var isPdfA1 = format is PdfFormat.PDF_A_1A or PdfFormat.PDF_A_1B;

        // 1. Must have XMP metadata
        CheckMetadata(document, format, issues, violations);

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

        // 3. Level A requirements
        if (format is PdfFormat.PDF_A_1A or PdfFormat.PDF_A_2A or PdfFormat.PDF_A_3A)
        {
            if (string.IsNullOrEmpty(document.Info.Title))
            {
                issues.Add("Missing document title (required for PDF/A Level A).");
                violations.Add(new PdfAViolation
                {
                    Rule = "DocumentTitle",
                    Description = "Missing document title (required for PDF/A Level A).",
                });
            }

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
            if (string.Compare(version, "1.4", StringComparison.Ordinal) < 0)
            {
                issues.Add($"PDF version {version} is below 1.4 (minimum for PDF/A).");
                violations.Add(new PdfAViolation
                {
                    Rule = "PdfVersion",
                    Description = $"PDF version {version} is below 1.4 (minimum for PDF/A).",
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
            CheckAnnotations(document, page, issues, violations);
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

        return new PdfAValidationResult
        {
            IsValid = issues.Count == 0,
            Format = PdfFormat.PDF_UA_1,
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

            var dcTitle = document.Metadata.Get("dc:title");
            if (string.IsNullOrEmpty(dcTitle))
            {
                issues.Add("Missing dc:title in XMP metadata.");
                violations.Add(new PdfAViolation
                {
                    Rule = "MetadataDcTitle",
                    Description = "Missing dc:title in XMP metadata.",
                });
            }

            var pdfProducer = document.Metadata.Get("pdf:Producer");
            if (string.IsNullOrEmpty(pdfProducer))
            {
                issues.Add("Missing pdf:Producer in XMP metadata.");
                violations.Add(new PdfAViolation
                {
                    Rule = "MetadataPdfProducer",
                    Description = "Missing pdf:Producer in XMP metadata.",
                });
            }
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

        foreach (var fontKey in fontDict.Keys)
        {
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
    }

    private static bool HasOutputIntent(Document document)
    {
        var outputIntents = document.Reader.Resolve(document.Catalog.Get("OutputIntents"));
        return outputIntents is PdfArray { Count: > 0 };
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

    private static void CheckAnnotations(Document document, Page page,
        List<string> issues, List<PdfAViolation> violations)
    {
        var annotsObj = document.Reader.Resolve(page.Dict.Get("Annots"));
        if (annotsObj is not PdfArray annotsArr) return;

        foreach (var item in annotsArr)
        {
            var annotDict = document.Reader.ResolveDict(item);
            if (annotDict is null) continue;

            var subtype = annotDict.GetName("Subtype");

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
