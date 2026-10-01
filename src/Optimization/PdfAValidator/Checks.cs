using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

internal static partial class PdfAValidator
{
    /// <summary>Report untagged real content: a painting operator sitting outside
    /// every marked-content sequence that carries an /MCID — and not inside an
    /// /Artifact sequence (artifacts are exempt from tagging) — is real content a
    /// PDF/UA reader cannot reach. One violation per object class per document
    /// ("Path object not tagged", "Text object not tagged"), matching the
    /// reference validator's vocabulary.</summary>
    private static void CheckUaUntaggedContent(
        Page page, UaReport report, HashSet<string> reportedClasses)
    {
        System.Collections.Generic.IEnumerable<Operator> ops;
        try { ops = page.Contents; } catch { return; }

        var mcidDepth = 0;      // inside a BDC carrying an /MCID
        var artifactDepth = 0;  // inside an /Artifact sequence
        var stack = new Stack<(bool Mcid, bool Artifact)>();

        void Report(string objectClass)
        {
            if (!reportedClasses.Add(objectClass)) return;
            report.Add(UaProblems.ObjectNotTagged, "UaUntaggedContent",
                $"{objectClass} object not tagged", page.Number);
        }

        foreach (var op in ops)
        {
            switch (op)
            {
                case Aspose.Pdf.Operators.BDC bdc:
                    // An inline dict without /MCID is NOT tagging (an OC or artifact
                    // property list); a NAMED property-list reference parses with null
                    // Properties and cannot be inspected here — treat it as tagged so
                    // a loaded document using /Properties resources never reports a
                    // false positive.
                    var tagged = bdc.Properties is null || bdc.Properties.MCID is not null;
                    var artifact = bdc.Tag == "Artifact";
                    stack.Push((tagged, artifact));
                    if (tagged) mcidDepth++;
                    if (artifact) artifactDepth++;
                    continue;
                case Aspose.Pdf.Operators.BMC bmc:
                    var bmcArtifact = bmc.Tag == "Artifact";
                    stack.Push((false, bmcArtifact));
                    if (bmcArtifact) artifactDepth++;
                    continue;
                case Aspose.Pdf.Operators.EMC:
                    if (stack.Count > 0)
                    {
                        var (m, a) = stack.Pop();
                        if (m) mcidDepth--;
                        if (a) artifactDepth--;
                    }
                    continue;
            }

            if (mcidDepth > 0 || artifactDepth > 0) continue;

            switch (op)
            {
                // A show operator with no glyphs paints nothing, so there is nothing
                // for a reader to reach and nothing to tag - an empty () Tj outside
                // every marked-content sequence is not untagged content.
                case Aspose.Pdf.Operators.TextShowOperator { Text.Length: > 0 }:
                    Report("Text");
                    break;
                case Aspose.Pdf.Operators.Stroke or Aspose.Pdf.Operators.ClosePathStroke
                    or Aspose.Pdf.Operators.Fill or Aspose.Pdf.Operators.EOFill
                    or Aspose.Pdf.Operators.FillStroke or Aspose.Pdf.Operators.EOFillStroke
                    or Aspose.Pdf.Operators.ClosePathFillStroke or Aspose.Pdf.Operators.ClosePathEOFillStroke:
                    Report("Path");
                    break;
            }
        }
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

    private static void CheckTransparency(Document document, Page page, bool isPdfA1, List<string> issues, List<PdfAViolation> violations)
    {
        var tc = new TransparencyCheckState();
        tc.document = document;
        tc.page = page;
        tc.isPdfA1 = isPdfA1;
        tc.issues = issues;
        tc.violations = violations;
        // Check page-level transparency group
        if (tc.isPdfA1)
        {
            var group = tc.document.Reader.ResolveDict(tc.page.Dict.Get("Group"));
            if (group is not null && group.GetName("S") == "Transparency")
            {
                var msg = $"Page {tc.page.Number} uses transparency group (not allowed in PDF/A-1).";
                tc.issues.Add(msg);
                tc.violations.Add(new PdfAViolation
                {
                    Rule = "Transparency",
                    Description = $"Page {tc.page.Number} uses transparency (not allowed in PDF/A-1)",
                    PageNumber = tc.page.Number,
                });
            }
        }

        tc.resources = tc.document.Reader.ResolveDict(tc.page.Dict.Get("Resources"));
        if (tc.resources is null) return;

        tc.extGStateDict = tc.document.Reader.ResolveDict(tc.resources.Get("ExtGState"));
        if (tc.extGStateDict is null) return;

        foreach (var gsName in tc.extGStateDict.Keys)
        {
            CheckExtGStateTransparency(tc, gsName);
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
