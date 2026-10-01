using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document : IDisposable
{
    /// <summary>The part-specific fix-ups: part 2 embedded-file rules, the parts 2-4 content constraints, and the part 1 colour-space and annotation rules.</summary>
    private void ApplyPartSpecificFixes(PdfAConvertState pa)
    {
        if (pa.fix && pa.part == "2")
        {
            ConvertEmbeddedPdfAttachmentsToPdfA2B(pa.options, pa.strip);
            // ISO 19005-2 §6.9: an optional-content configuration dictionary
            // (the /OCProperties /D default and every /Configs entry) shall not
            // contain the /AS key. The log carries the clause-6.9 error with the
            // reference validator's exact vocabulary; Delete strips the key so
            // the output validates.
            ReportProhibitedOptionalContentAS(pa.options, pa.strip);
        }

        // 12c. Flat-colour DCT images (part 2+): a JPEG whose decoded samples use at
        // most 256 distinct colours gets an /Indexed /DeviceRGB palette colorspace.
        // The conversion emits a fixed 256-slot palette (hival 255) for
        // such an image regardless of the actual count - measured: a
        // 63-colour 900x253 flatten raster comes out [/Indexed/DeviceRGB 255 ...]
        // while its continuous-tone siblings (2205..5786 colours) stay DeviceRGB.
        // The samples are re-encoded as real 8-bit palette indices (Flate).
        if (pa.fix && pa.part is "2" or "3" or "4")
            foreach (var page in Pages)
                try { PalettizeFlatDctImages(page); }
                catch { /* undecodable image: keep it as-is */ }

        // 13. Size optimization (OptimizeFileSize): subset every embedded TrueType program to
        // the glyphs the document actually uses. Font embedding (step 10) is the dominant cost
        // of PDF/A conversion — a source that referenced but did not embed several system
        // faces gains a full WinAnsi program for each. Subsetting those (and any already-
        // embedded faces) to the used glyphs is what keeps the converted file at or below the
        // source size. The just-embedded /FontFile2 programs are still pending objects, so the
        // subsetter is given a resolver that reaches them. Non-destructive (glyph outlines only).
        if (pa.options.OptimizeFileSize)
        {
            // stripStandard14: false — PDF/A requires every used font embedded, so the
            // "drop standard-14 programs" size optimization must never run here.
            Optimization.FontSubsetter.SubsetFonts(_reader, subsetEmbedded: true,
                resolveNewStream: ResolvePendingStream, stripStandard14: false);
            // A tagged result is hundreds of small objects (structure elements); packed into
            // compressed object streams they cost a fraction. PDF/A-1 forbids object streams.
            _packObjectsOnSave = pa.part is not "1";
        }
        else if (pa.fix)
            // Output growth is kept bounded (capped near +10%), so the programs
            // THIS conversion just embedded are
            // subset to the used glyphs. The source's own embedded fonts are left
            // alone — re-subsetting a foreign subset (Word symbol cmaps etc.) has
            // stripped used glyphs into tofu.
            Optimization.FontSubsetter.SubsetEmbeddedFonts(_reader, ResolvePendingStream,
                newlyEmbeddedOnly: true);

        // 14. PDF/A-1 content-stream normalisation:
        //  - every page's content is bracketed in a q…Q pair so graphics state left
        //    open by the original stream can't leak into content the conversion
        //    appends (observable as exactly +2 operators per page);
        //  - ISO 19005-1 §6.1.13 implementation limits: a real value must fit ±32767,
        //    so an out-of-range path coordinate is rounded to an integer (sub-unit
        //    precision that far off the page is meaningless).
        if (pa.fix && pa.part == "1")
        {
            foreach (var page in Pages)
                try { NormalizePdfA1PageContent(page); }
                catch { /* undecodable content (e.g. exotic LZW): leave the page as-is */ }
            // Rewriting /Contents leaves each page's original stream object(s)
            // orphaned — have the save reachability-prune them, or every edited
            // page's content bytes are carried over twice.
            _reader.MayHaveOrphansOnSave = true;
        }
    }

    /// <summary>Fonts are embedded and checked, and a level-A or auto-tagging conversion builds the structure tree.</summary>
    private void ResolveFontsAndAutoTag(PdfAConvertState pa)
    {
        pa.fontsResolved = CheckFontEmbedding(pa.options);

        // 11a. Implementation limits (probed contract; ISO 19005-1 §6.1.12 /
        // ISO 19005-2 §6.1.13): graphics-state nesting deeper than 28 across the
        // page→form call chain and show strings longer than the per-part character
        // limit are baked into the content and UNFIXABLE — they mark the conversion
        // failed. PDF/A-1 additionally flags object-level fractional reals beyond
        // ±32767 (convertable: the fraction truncates toward zero).
        if (pa.part is "1" or "2")
            CheckImplementationLimits(pa.options, pa.part);

        pa.autoTag = pa.options.AutoTaggingSettings is { EnableAutoTagging: true } || pa.conformance == "A";
        if (pa.fix && pa.autoTag)
        {
            // The tagging the level-A conversion is about to synthesise REPAIRS real
            // violations of the source — the log must still carry them (the measured
            // vocabulary; the clause NUMBERS are part-dependent: ISO 19005-1
            // files these under 6.8.x, parts 2/3 under 6.7.x).
            var structRoot = _reader.ResolveDict(_reader.Catalog.Get("StructTreeRoot"));
            if (structRoot is null)
                pa.options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "StructureTree",
                    Clause = pa.part == "1" ? "6.8.3.3" : "6.7.3.3",
                    Description = "Catalog shall have struct tree root entry",
                });
            if (_reader.ResolveDict(_reader.Catalog.Get("MarkInfo")) is null)
                pa.options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "MarkInfo",
                    Clause = pa.part == "1" ? "6.8.2.2" : "6.7.2.2",
                    Description = "Catalog shall have MarkInfo entry",
                });
            // ISO 19005-1 §6.8.3.4 (parts 2/3: §6.7.3.4): every non-standard structure
            // type must be role-mapped to a functionally equivalent standard type. One
            // problem per distinct unmapped type (probed: "Non-standard structure type
            // 'Article' not mapped to functionally equivalent standard type").
            if (structRoot is not null)
                ReportUnmappedStructureTypes(structRoot, pa.options, pa.part!);
            // A-level PDF/A also requires a document title (ISO 19005 §6.7.3); mirror the XMP
            // dc:title onto /Info so the validator's title check is satisfied.
            if (pa.conformance == "A" && string.IsNullOrEmpty(Info.Title))
                Info.Title = string.IsNullOrEmpty(pa.meta.Get("dc:title")) ? "Untitled" : pa.meta.Get("dc:title");
            // Auto-tagging asked for replaces any tree. A level-A target alone keeps a complete
            // tree in a document this library produced (its /Info producer names it); a tree
            // from another producer, a partial one, or none is tagged afresh.
            var asked = pa.options.AutoTaggingSettings is { EnableAutoTagging: true };
            var keep = !asked && IsProducedByThisLibrary() && Tagged.AutoTagger.HasCompleteTagging(this);
            if (!keep)
                Tagged.AutoTagger.Apply(this, pa.options.AutoTaggingSettings ?? AutoTaggingSettings.Default);
        }
    }

    /// <summary>PDF/X trapping and boxes, and the level-A structure requirements of parts 2 and 3.</summary>
    private void ApplyPdfXAndLevelAFixes(PdfAConvertState pa)
    {
        if (pa.isPdfX && pa.fix)
        {
            var xMeta = GetOrCreateMetadata();
            if (pa.format == PdfFormat.PDF_X_1A)
            {
                xMeta.Set("pdfx:GTS_PDFXVersion", "PDF/X-1a:2003");
                xMeta.PdfAidPart = null;
                xMeta.PdfAidConformance = null;
            }
            else if (pa.format == PdfFormat.PDF_X_3)
            {
                xMeta.Set("pdfx:GTS_PDFXVersion", "PDF/X-3:2003");
                xMeta.PdfAidPart = null;
                xMeta.PdfAidConformance = null;
            }
            else if (pa.format == PdfFormat.PDF_X_4)
            {
                // X-4 identifies itself through the pdfxid namespace, not pdfx,
                // and stamps the Info dict too (measured 2026-08-28: the
                // output carries pdfxid:GTS_PDFXVersion = "PDF/X-4",
                // pdf:Trapped = False, Info /GTS_PDFXVersion and /Trapped).
                xMeta.Set("pdfxid:GTS_PDFXVersion", "PDF/X-4");
                xMeta.Set("pdf:Trapped", "False");
                xMeta.PdfAidPart = null;
                xMeta.PdfAidConformance = null;
                Info.SetCustom("GTS_PDFXVersion", "PDF/X-4");
                if (Info.Trapped is null) Info.Trapped = "False";
            }
        }

        // 9. Interactive form fields SURVIVE the conversion: PDF/A permits an
        // AcroForm as long as appearance streams do the rendering and no forbidden
        // actions ride on the widgets. The conversion therefore keeps every field
        // (a concatenated form keeps its full field count and /DR fonts) and only
        // strips what the profile forbids — NeedAppearances and widget trigger
        // actions. Callers that want the values baked into page content flatten
        // explicitly after converting.
        if (pa.fix && !pa.isPdfX)
            MakeFormFieldsPdfACompliant();

        // 9b. Accessible conformance (part 2/3 level A): text shown through a SYMBOLIC
        // TrueType font whose codes live in the Private Use Area has no Unicode meaning,
        // so each such usage is re-encoded as a fresh Type0 (Identity-H) font — resource
        // key C0_0, C1_0, … — with the shown glyphs addressed by glyph id and the show
        // wrapped in a /Span ActualText marker.
        if (pa.fix && pa.conformance == "A" && pa.part is "2" or "3")
            foreach (var page in Pages)
                try { ConvertPuaSymbolicFontUsagesToType0(page); }
                catch { /* unparsable content/font: leave the usage as-is */ }

        // 9c. Report what the SOURCE states that the standard does not admit, before the
        // repairs below rewrite it: a font program shape PDF/A-1 has no reader contract for
        // (clause 6.3.2), and an image rendering intent outside the four standard names
        // (clause 6.2.9). Both are repaired by the conversion; the log still names them.
        if (!pa.isPdfX)
        {
            if (pa.part == "1") ReportInvalidFontProgramSubtypes(pa.options);
            ReportInvalidRenderingIntents(pa.options);
            // Parts 2 and 3 state the colour rule as clause 6.2.4.3; the OutputIntent the
            // conversion just added is the one the check reads.
            if (pa.part is "2" or "3") ReportUnmatchedDeviceColorSpaces(pa.options);
        }

        // 10. Embed glyph-bearing fonts that the source left unembedded (PDF/A requires
        // every font to be embedded — including the Standard-14 faces, which a viewer would
        // otherwise substitute): resolve the real face, fall back to Arial for an
        // unresolvable family, and report each replacement via FontSubstitution.
        if (pa.fix && !pa.isPdfX)
            EmbedNonEmbeddedFonts(pa.options, includeStandard14: true);

        // 11. PDF/A-2 and -3: a font name whose bytes are not UTF-8 (a legacy-codepage
        // CJK face name, "ＭＳ 明朝" in Shift-JIS) is replaced by the embedded program's
        // English family, subset tag kept — the name the Latin name record carries for
        // the same face (measured: MS Mincho). PDF/A-1 keeps the authored name.
        if (pa.fix && !pa.isPdfX && pa.part is "2" or "3")
            RenameNonUtf8FontNames();
    }

    /// <summary>The output intent: PDF/X-1a takes a CMYK intent, PDF/A documents without one get the sRGB intent.</summary>
    private void WriteOutputIntents(PdfAConvertState pa)
    {
        if (pa.format == PdfFormat.PDF_X_1A)
        {
            foreach (var page in Pages)
            {
                if (!page.Dict.ContainsKey("Group")) continue;
                pa.options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "Transparency",
                    Description = $"Page {page.Number} uses transparency group (not allowed in PDF/X-1a).",
                    PageNumber = page.Number,
                });
                if (!pa.fix) continue;
                if (PageUsesTransparency(page))
                    FlattenPageToCmykImage(page);
                else
                    page.Dict.Remove("Group");
            }
        }

        // 6d. Materialise /Resources directly onto each page dict. The
        // conversion normalises resource inheritance away: a converted page always
        // carries its own /Resources entry (inherited resources are referenced from
        // the page; a page with none anywhere gets an empty dict).
        if (pa.fix)
        {
            foreach (var page in Pages)
            {
                if (page.Dict.ContainsKey("Resources")) continue;
                page.Dict.Set("Resources", FindInheritedRaw(page.Dict, "Resources") ?? new PdfDictionary());
            }
        }

        // 6c. Text show operators that reference the .notdef glyph are prohibited in
        // PDF/A (ISO 19005-1 §6.3.7; 19005-2/-3 §6.2.11.8). OCR producers emit
        // invisible control-code shows (e.g. a literal TAB) that no encoding maps to
        // a glyph; such show operators are deleted under
        // ConvertErrorAction.Delete. Only control-range codes (< 0x20) that resolve
        // to no glyph name count as certain .notdef references, and an operator is
        // removed only when EVERY code it shows is one (mixed operators keep their
        // visible text).
        if (!pa.isPdfX)
            foreach (var page in Pages)
                try { RemoveNotdefGlyphShows(page, pa.options, pa.strip); }
                catch { /* unparsable content: leave the page as-is */ }

        // 7. Add OutputIntent
        if (pa.isPdfX && pa.fix)
        {
            // PDF/X requires an OutputIntent with ICC profile
            AddPdfXOutputIntent(pa.options);
        }
        else if (!HasPdfAOutputIntentInCatalog())
        {
            // Detect device-dependent colours either already emitted as
            // page XObjects OR queued as DOM paragraphs (Image, ImageStamp)
            // that Save() will flush after Convert returns.
            var hasDeviceColors = false;
            foreach (var page in Pages)
            {
                if (PageHasDeviceDependentColors(page) || PageHasDeviceDependentParagraphs(page))
                {
                    hasDeviceColors = true;
                    break;
                }
            }
            if (hasDeviceColors)
            {
                pa.options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "ColorSpace",
                    Description = "Device-dependent color space without OutputIntent.",
                });
            }
            // A PDF/A output always carries a GTS_PDFA1 OutputIntent (validators
            // gate on its presence), not only when device-dependent
            // colours were detected — the violation above is logged for those only.
            if (pa.fix)
            {
                AddSrgbOutputIntent();
            }
        }
    }

    /// <summary>PDF/A-1 forbids what later parts allow: transparency, optional content, embedded files and the like are stripped or reported.</summary>
    private void NormalisePartOneContent(PdfAConvertState pa)
    {
        if (pa.part == "1")
        {
            foreach (var page in Pages)
            {
                var group = _reader.ResolveDict(page.Dict.Get("Group"));
                if (group is null || group.GetName("S") != "Transparency") continue;
                pa.options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "Transparency",
                    Description = $"Page {page.Number} uses transparency group (not allowed in PDF/A-1).",
                    PageNumber = page.Number,
                });
                if (pa.fix) page.Dict.Remove("Group");
            }

            // Default transparency action: preserve the visual appearance of content
            // painted with partial alpha or a non-Normal blend mode (and of Highlight
            // annotations, whose appearance streams blend with Multiply) by rasterising
            // each transparency region from the original page and painting the opaque
            // composite on top, before the neutralisation below strips the transparency.
            // The Mask action needs the same appearance preservation for VECTOR paint:
            // its dedicated image handling below bakes image alpha into /SMask, but a
            // 30–50%-alpha stroked map would still flip to opaque black under
            // plain neutralisation. The sim rewrites only path/text paints, so the two
            // passes compose without overlap.
            // Mask recolours constant-alpha VECTOR paint toward the white backdrop
            // (crisp light-grey lines, the way a viewer shows them) instead of
            // rasterising; blends still go through the raster composites either way.
            if (pa.fix && pa.options.TransparencyAction is ConvertTransparencyAction.Default
                    or ConvertTransparencyAction.Mask)
                foreach (var page in Pages)
                    try
                    {
                        SimulateTransparencyRegions(page,
                            recolorConstantAlpha: pa.options.TransparencyAction == ConvertTransparencyAction.Mask);
                    }
                    catch { /* unparsable content or render failure: neutralisation still applies */ }

            // ConvertTransparencyAction.Mask: preserve the visual appearance of images that
            // are painted under a constant fill-alpha (/ca < 1). PDF/A-1 forbids ExtGState
            // alpha, so the neutralisation below would zero it and make such an image render
            // opaquely. Before that, bake the alpha into a constant DeviceGray soft mask on
            // the image XObject itself (an image /SMask is NOT stripped by this conversion),
            // so the image keeps compositing at the requested opacity while the prohibited
            // ExtGState alpha is removed. The Default action leaves the neutralisation opaque.
            if (pa.fix && pa.options.TransparencyAction == ConvertTransparencyAction.Mask)
                foreach (var page in Pages)
                {
                    var res = _reader.ResolveDict(page.Dict.Get("Resources"));
                    var content = page.GetContentStreamBytes();
                    if (res is not null && content is { Length: > 0 })
                        MaskConstantAlphaImages(content, res, 1.0,
                            new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance));
                }

            // Content painted with FULLY transparent alpha (/ca 0, /CA 0) is invisible in
            // the source; the alpha neutralisation below would set ca/CA to 1 and make it
            // pop in as opaque paint (e.g. black boxes over the form captions).
            // Rewrite such paint operators to no-ops FIRST, while the alpha values are
            // still readable.
            if (pa.fix)
                foreach (var page in Pages)
                    try { SuppressAlphaZeroPaint(page); }
                    catch { /* unparsable content: leave as-is; neutralisation still applies */ }

            // ExtGState soft masks, constant alpha (ca/CA < 1) and non-Normal blend modes
            // are equally prohibited by PDF/A-1. Neutralise them in every graphics-state
            // dictionary reachable from the pages (including nested Form XObjects) so the
            // content renders opaquely instead of failing validation.
            foreach (var page in Pages)
                NeutralizeExtGStateTransparency(page.Dict, pa.options, page.Number, pa.fix,
                    new HashSet<PdfDictionary>());

            // PDF/A-1 implementation limits (ISO 19005-1 / PDF 1.4 Annex C): real numbers
            // must stay within ±32767. Round out-of-range FRACTIONAL reals in the page
            // content to integers (integral magnitudes beyond the limit are tolerated by
            // the target validators, and rounding keeps far-off-page geometry harmless).
            if (pa.fix)
                foreach (var page in Pages)
                {
                    // Defensive: an undecodable content stream must not abort the
                    // whole conversion — skip the range fix for that page.
                    try
                    {
                        var content = page.GetContentStreamBytes();
                        if (content is not { Length: > 0 }) continue;
                        var rounded = RoundOutOfRangeReals(content);
                        if (rounded is not null)
                            page.SetContentStream(rounded);
                    }
                    catch
                    {
                        // leave the page content untouched
                    }
                }
        }
    }

    /// <summary>A trailer without an ID gets one.</summary>
    private void EnsureTrailerId(PdfAConvertState pa)
    {
        if (_reader.Trailer.Get("ID") is null)
        {
            pa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "FileId",
                Description = "Missing file ID in trailer (required for PDF/A).",
            });
            if (pa.fix)
            {
                _forceWriteId = true;
                var fileId = Compat.RandomBytes(16);
                var idArray = new PdfArray();
                idArray.Add(new PdfString(fileId, isHex: true));
                idArray.Add(new PdfString(fileId, isHex: true));
                _reader.Trailer.Set("ID", idArray);
            }
        }
    }

    /// <summary>The XMP packet gets its pdfaid part and conformance, a title and a producer where they are missing, and the Info dictionary is reconciled with it.</summary>
    private void WritePdfAIdentification(PdfAConvertState pa)
    {
        pa.meta = GetOrCreateMetadata();
        pa.needsPdfAId = string.IsNullOrEmpty(pa.meta.PdfAidPart);
        pa.needsConformance = pa.part != "4" && string.IsNullOrEmpty(pa.meta.PdfAidConformance);
        pa.needsTitle = string.IsNullOrEmpty(pa.meta.Get("dc:title"));
        pa.needsProducer = string.IsNullOrEmpty(pa.meta.Get("pdf:Producer"));

        if (pa.needsPdfAId)
        {
            pa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataPdfAId",
                Description = "Missing pdfaid:part in XMP metadata.",
            });
        }
        if (pa.needsConformance)
        {
            pa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataPdfAConformance",
                Description = "Missing pdfaid:conformance in XMP metadata.",
            });
        }
        if (pa.needsTitle)
        {
            pa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataDcTitle",
                Description = "Missing dc:title in XMP metadata.",
            });
        }
        if (pa.needsProducer)
        {
            pa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataPdfProducer",
                Description = "Missing pdf:Producer in XMP metadata.",
            });
        }

        if (pa.fix)
        {
            WritePdfAIdEntries(pa);
        }
    }

    /// <summary>Encryption comes off, and the document version is raised to the part's floor (1.4 for part 1, 1.3 otherwise, 1.7 for part 3).</summary>
    private void RemoveEncryptionAndFloorVersion(PdfAConvertState pa)
    {
        if (IsEncrypted)
        {
            pa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "Encryption",
                Description = "Document is encrypted (not allowed in PDF/A).",
            });
            if (pa.fix)
            {
                _encryptor = null;
                _reader.Trailer.Remove("Encrypt");
            }
        }

        pa.floor = pa.part == "1" ? "1.4" : "1.3";
        pa.version = PdfVersion;
        if (pa.version is not null && string.Compare(pa.version, pa.floor, StringComparison.Ordinal) < 0)
        {
            pa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "PdfVersion",
                Description = $"PDF version {pa.version} is below {pa.floor} (minimum for PDF/A-{pa.part}).",
            });
            if (pa.fix)
            {
                SetVersion(pa.floor);
            }
        }
        // PDF/A-3 alone is stamped at ISO 32000-1's 1.7 — a PDF/A-3 conversion reports
        // 1.7 whatever it started from, while PDF/A-2 keeps the document's own version.
        // The catalog /Version takes precedence over the header when reading, so it has
        // to follow, or a stale entry would mask the upgraded header.
        if (pa.fix && pa.part == "3"
            && string.Compare(PdfVersion ?? "1.0", "1.7", StringComparison.Ordinal) < 0)
        {
            SetVersion("1.7");
            if (_reader.Catalog.Get("Version") is not null)
                _reader.Catalog.Set("Version", new PdfName("1.7"));
        }
    }

    /// <summary>The PDF/A or PDF/X part and conformance the target names; true when the target needs no conversion, false when a part-4 target sits on a pre-2.0 document, otherwise null to continue.</summary>
    private bool? ResolvePdfAPart(PdfAConvertState pa)
    {
        pa.partConformance = pa.format switch
        {
            PdfFormat.PDF_A_1A => ("1", "A"),
            PdfFormat.PDF_A_1B => ("1", "B"),
            PdfFormat.PDF_A_2A => ("2", "A"),
            PdfFormat.PDF_A_2B => ("2", "B"),
            PdfFormat.PDF_A_2U => ("2", "U"),
            PdfFormat.PDF_A_3A => ("3", "A"),
            PdfFormat.PDF_A_3B => ("3", "B"),
            PdfFormat.PDF_A_3U => ("3", "U"),
            // ZUGFeRD (factur-x) electronic invoices are PDF/A-3 documents that carry the
            // invoice XML as an associated file. Convert as PDF/A-3B, then attach the AF tagging.
            PdfFormat.ZUGFeRD => ("3", "B"),
            // PDF/A-4 and its E (engineering) / F (embedded-files) flavours are all
            // ISO 19005-4 part 4 with no A/B/U conformance level; the flavour only
            // widens what content is permitted, so they share the part-4 conversion.
            PdfFormat.PDF_A_4 or PdfFormat.PDF_A_4E or PdfFormat.PDF_A_4F => ("4", ""),
            PdfFormat.PDF_X_1A => ("X-1", "a"),
            PdfFormat.PDF_X_3 => ("X-3", ""),
            PdfFormat.PDF_X_4 => ("X-4", ""),
            _ => (null, (string?)null),
        };
        pa.part = pa.partConformance.Item1;
        pa.conformance = pa.partConformance.Item2;

        if (pa.part is null)
            return true; // Not a PDF/A or PDF/X format, nothing to do

        // Clause 6.2.11.8 (ISO 19005-2/-3): a font program's cmap shall not map a
        // used character to the .notdef glyph. For an Identity-encoded Type0 font
        // the code IS the CID, so a shown 2-byte code 0000 references .notdef
        // directly — reported one problem per font, on the first page seen
        // (probed: three CID fonts, ObjectID = the Type0 dict's object number,
        // name = BaseFont with the subset prefix stripped). Conversion proceeds —
        // the problem is informational (Convertable stays true).
        if (pa.part is "2" or "3")
            ReportNotdefGlyphReferences(pa.options);

        // ISO 19005-4 (clause 6.1.2): PDF/A-4 is defined over PDF 2.0 — a 1.x
        // document must be brought to 2.0 FIRST (Convert(v_2_0)), or the A-4
        // conversion refuses with the clause-6.1.2 error. The version the save
        // would stamp decides (a prior SetVersion("2.0") counts).
        if (pa.part == "4"
            && string.Compare(_versionOverride ?? PdfVersion ?? "1.4", "2.0", StringComparison.Ordinal) < 0)
        {
            pa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "PdfVersionForPart4",
                Clause = "6.1.2",
                Description = "PDF/A-4 requires a PDF 2.0 document; convert the document to PDF 2.0 first.",
                Convertable = false,
            });
            return false;
        }
        return null;
    }

    /// <summary>PDF/UA-1 and PDF/E-1 targets convert through their own structure and version rules; the result when the target was one, otherwise null.</summary>
    private bool? ConvertToUaOrEngineering(PdfAConvertState pa)
    {
        if (pa.format == PdfFormat.PDF_UA_1)
        {
            if (!pa.fix) return CheckFontEmbedding(pa.options);
            // A form that is dynamic XFA alone is refused before anything is changed.
            if (!AdmitXfaForUa(pa)) return false;
            // The log names what the SOURCE was missing, read before the repairs below
            // rewrite it.
            foreach (var problem in Optimization.PdfAValidator.ReportPdfUaConversion(this).Violations)
                pa.options.ConversionLog.Add(problem);
            // A title the source does not state is written EMPTY rather than invented: the
            // document has no title, and saying so is what a reader needs to know. A natural
            // language is not invented either - the conversion has no way to know what the
            // content is written in.
            Info.Title ??= string.Empty;
            DisplayDocTitle = true;
            // A document the conversion is about to make accessible declares itself tagged,
            // and nothing in it is a suspect region any more.
            MarkAsTagged();
            DescribeLinkAnnotations(pa.options);
            LabelFormFieldsForUa(pa.options);
            RepairOptionalContentConfigsForUa();
            CompleteEmbeddedFileSpecsForUa();
            var uaMeta = GetOrCreateMetadata();
            uaMeta.KeepOnePropertyPerName = true;
            if (string.IsNullOrEmpty(uaMeta.Get("pdfuaid:part"))) uaMeta.Set("pdfuaid:part", "1");
            if (!uaMeta.ContainsKey("dc:title")) uaMeta.Set("dc:title", Info.Title ?? string.Empty);
            if (string.IsNullOrEmpty(uaMeta.Get("pdf:Producer"))) uaMeta.SetStamped("pdf:Producer", BuildVersionInfo.ProducerString);
            if (string.IsNullOrEmpty(uaMeta.Get("xmp:CreateDate")) && Info.CreationDate != DateTime.MinValue)
                uaMeta.Set("xmp:CreateDate", FormatXmpDate(Info.CreationDate, Info.CreationTimeZone));
            if (string.IsNullOrEmpty(uaMeta.Get("xmp:ModifyDate")) && Info.ModDate != DateTime.MinValue)
                uaMeta.Set("xmp:ModifyDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));
            if (_reader.Trailer.Get("ID") is null)
            {
                _forceWriteId = true;
                var feId = Compat.RandomBytes(16);
                var feIdArr = new PdfArray();
                feIdArr.Add(new PdfString(feId, isHex: true));
                feIdArr.Add(new PdfString(feId, isHex: true));
                _reader.Trailer.Set("ID", feIdArr);
            }
            ReconcileEmbeddedWidths = true;
            try { EmbedNonEmbeddedFonts(pa.options, includeStandard14: true); }
            finally { ReconcileEmbeddedWidths = false; }
            RepairCidSets();
            DropType1CharSets();
            RepairSymbolicTrueTypeEncodings(pa.options);
            RepairTrueTypeEncodings();
            RepairEmbeddedTrueTypeWidths();
            FillUnicodeMappings();
            ReportNotdefReferencesForUa(pa.options);
            RepairCidToGidMaps();
            RepairCidSystemInfo(pa.options);
            if (pa.options.AutoTaggingSettings is { EnableAutoTagging: true })
                Tagged.AutoTagger.Apply(this, pa.options.AutoTaggingSettings);
            RetagStructRootAsDocument();
            // ⭐ A conforming file names the colour space its content was
            // prepared for. Measured against the reference: a PDF/UA document
            // carries the same sRGB GTS_PDFA1 output intent a PDF/A one does,
            // even though ISO 14289 does not demand one — a reader that has to
            // reproduce the colours has nothing else to go on.
            if (!HasPdfAOutputIntentInCatalog()) AddSrgbOutputIntent();
            return CheckFontEmbedding(pa.options);
        }

        // PDF/E-1 (ISO 24517-1, based on PDF 1.6): engineering documents. Structural
        // fixes (font embedding, the pdfe XMP identification, version normalisation)
        // always apply; ConvertErrorAction.Delete additionally strips the prohibited
        // interactive content — the document JavaScript name tree and
        // JavaScript/launch-style catalog actions.
        if (pa.format == PdfFormat.PDF_E_1)
        {
            if (IsEncrypted)
            {
                pa.options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "Encryption",
                    Description = "Document is encrypted (not allowed in PDF/E).",
                });
                _encryptor = null;
                _reader.Trailer.Remove("Encrypt");
            }

            if (PdfVersion != "1.6")
            {
                SetVersion("1.6");
                _reader.Catalog.Set("Version", new PdfName("1.6"));
            }

            var eMeta = GetOrCreateMetadata();
            if (string.IsNullOrEmpty(eMeta.Get("pdfe:ISO_PDFEVersion")))
                eMeta.Set("pdfe:ISO_PDFEVersion", "PDF/E-1");

            RemoveProhibitedCatalogActions(pa.options, pa.strip);
            if (pa.strip) RemoveDocumentJavaScript(pa.options);

            EmbedNonEmbeddedFonts(pa.options, includeStandard14: true);
            return CheckFontEmbedding(pa.options);
        }
        return null;
    }

    /// <summary>A plain PDF version target (1.7, 2.0 or a versioned format) only sets the version; the result when the target was one, otherwise null.</summary>
    private bool? ConvertToPlainVersion(PdfAConvertState pa)
    {
        if (pa.format == PdfFormat.v_1_7)
        {
            SetVersion("1.7");
            return true;
        }

        if (pa.format == PdfFormat.v_2_0)
        {
            RemoveDeprecatedSignatureHandlers();
            SetVersion("2.0");
            return true;
        }

        pa.plainVersion = pa.format switch
        {
            PdfFormat.v_1_0 => "1.0",
            PdfFormat.v_1_1 => "1.1",
            PdfFormat.v_1_2 => "1.2",
            PdfFormat.v_1_3 => "1.3",
            PdfFormat.v_1_4 => "1.4",
            PdfFormat.v_1_5 => "1.5",
            PdfFormat.v_1_6 => "1.6",
            _ => (string?)null,
        };
        if (pa.plainVersion is not null)
        {
            SetVersion(pa.plainVersion);
            // Keep the catalog /Version in sync so a reloaded document reports
            // the downgraded version regardless of header/catalog precedence.
            _reader.Catalog.Set("Version", new PdfName(pa.plainVersion));
            return true;
        }
        return null;
    }


    /// <summary>The pdfaid part and conformance, the title and the producer are written into the XMP packet where the document lacks them, and the Info dictionary is reconciled with it.</summary>
    private void WritePdfAIdEntries(PdfAConvertState pa)
    {
        if (!pa.isPdfX)
        {
            pa.meta.PdfAidPart = pa.part;
            // PDF/A-4 has no conformance level (empty) — leave the entry absent
            // rather than writing an empty pdfaid:conformance.
            if (string.IsNullOrEmpty(pa.conformance)) pa.meta.PdfAidConformance = null;
            else pa.meta.PdfAidConformance = pa.conformance;
        }

        // ISO 19005 metadata must be valid XML, so characters the XML 1.0
        // grammar forbids cannot ride into the XMP packet — the conversion
        // replaces each with a SPACE, in the /Info strings and in any XMP
        // mirror entry already present (probed: 0x01/0x04/0x0B/0x1F → ' ',
        // while tab — XML-valid — survives; both Info and dc:* read back
        // sanitized after the conversion is saved).
        if (SanitizeXmlText(Info.Title) is { } st && st != Info.Title) Info.Title = st;
        if (SanitizeXmlText(Info.Author) is { } sa && sa != Info.Author) Info.Author = sa;
        if (SanitizeXmlText(Info.Subject) is { } ss && ss != Info.Subject) Info.Subject = ss;
        if (SanitizeXmlText(Info.Keywords) is { } sk && sk != Info.Keywords) Info.Keywords = sk;
        foreach (var key in MirroredXmpTextKeys)
        {
            var v = pa.meta.ContainsKey(key) ? pa.meta.Get(key) : null;
            if (v is not null && SanitizeXmlText(v) is { } sv && sv != v) pa.meta.Set(key, sv);
        }
        // Info.Title is "" (not null) when the source has no title, so "??" would
        // store an empty dc:title that the validator still flags as missing — fall
        // back to "Untitled" for null OR empty.
        if (pa.needsTitle) pa.meta.Set("dc:title", string.IsNullOrEmpty(Info.Title) ? "Untitled" : Info.Title);
        if (pa.needsProducer) pa.meta.SetStamped("pdf:Producer", BuildVersionInfo.ProducerString);

        // PDF/A requires the XMP xmp:CreateDate / xmp:ModifyDate to mirror the
        // /Info CreationDate / ModDate (ISO 8601). Without them the XMP and
        // document-info dates disagree and round-tripping Metadata["xmp:CreateDate"]
        // throws KeyNotFoundException. Write them in a form that
        // round-trips through XmpValue.ToDateTime().
        if (string.IsNullOrEmpty(pa.meta.Get("xmp:CreateDate")) && Info.CreationDate != DateTime.MinValue)
            pa.meta.Set("xmp:CreateDate", FormatXmpDate(Info.CreationDate, Info.CreationTimeZone));
        if (string.IsNullOrEmpty(pa.meta.Get("xmp:ModifyDate")) && Info.ModDate != DateTime.MinValue)
            pa.meta.Set("xmp:ModifyDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));
        if (string.IsNullOrEmpty(pa.meta.Get("xmp:MetadataDate")) && Info.ModDate != DateTime.MinValue)
            pa.meta.Set("xmp:MetadataDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));

        // ISO 19005 6.6.3 analog of the date sync above for the remaining
        // /Info↔XMP pairs: the XMP packet the conversion writes must mirror
        // the document-information strings (Keywords → pdf:Keywords, etc.) —
        // reloading the output and reading Metadata["pdf:Keywords"] must see
        // the value the caller put in DocumentInfo. NOTE: guarded with the
        // packet-only ContainsKey — Get() consults the Info fallback, which
        // would report the value "present" without it ever being serialised.
        if (!pa.meta.ContainsKey("pdf:Keywords") && !string.IsNullOrEmpty(Info.Keywords))
            pa.meta.Set("pdf:Keywords", Info.Keywords);
        if (!pa.meta.ContainsKey("dc:creator") && !string.IsNullOrEmpty(Info.Author))
            pa.meta.Set("dc:creator", Info.Author);
        if (!pa.meta.ContainsKey("dc:description") && !string.IsNullOrEmpty(Info.Subject))
            pa.meta.Set("dc:description", Info.Subject);
        if (!pa.meta.ContainsKey("xmp:CreatorTool") && !string.IsNullOrEmpty(Info.Creator))
            pa.meta.Set("xmp:CreatorTool", Info.Creator);
    }
}
