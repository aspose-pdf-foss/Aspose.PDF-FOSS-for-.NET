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
    private bool ConvertInternal(PdfFormatConversionOptions options)
    {
        var format = options.TargetFormat;
        // The standard PDF/A transformations (embed fonts, write the XMP pdfaid, add an
        // OutputIntent, normalise the version) are applied for every ErrorAction — that
        // applies structural fixes only (a None-conversion still embeds fonts and writes
        // metadata) and is the only way the output can validate structurally.
        var fix = true;
        // Removing prohibited CONTENT (catalog/AA actions, non-compliant annotations) is what
        // ConvertErrorAction governs: Delete strips it, None only logs the violation and leaves
        // the content in place. The structural fixes above are applied regardless.
        var strip = options.ErrorAction == ConvertErrorAction.Delete;

        if (format == PdfFormat.v_1_7)
        {
            SetVersion("1.7");
            return true;
        }

        if (format == PdfFormat.v_2_0)
        {
            SetVersion("2.0");
            return true;
        }

        // Plain PDF version targets (1.0 – 1.6): retarget the header/catalog
        // version. No PDF/A conformance work is needed for these.
        var plainVersion = format switch
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
        if (plainVersion is not null)
        {
            SetVersion(plainVersion);
            // Keep the catalog /Version in sync so a reloaded document reports
            // the downgraded version regardless of header/catalog precedence.
            _reader.Catalog.Set("Version", new PdfName(plainVersion));
            return true;
        }

        // PDF/UA-1 (ISO 14289-1) accessibility: tag the document, give it a title + natural
        // language, set /ViewerPreferences /DisplayDocTitle, the pdfuaid:part identifier, the
        // XMP dates and a file /ID. The tagged-metadata stamp on save finalises the rest.
        if (format == PdfFormat.PDF_UA_1)
        {
            if (!fix) return CheckFontEmbedding(options);
            if (string.IsNullOrEmpty(Info.Title)) Info.Title = "Untitled";
            if (string.IsNullOrEmpty(Language)) Language = "en-US";
            DisplayDocTitle = true;
            var uaMeta = GetOrCreateMetadata();
            if (string.IsNullOrEmpty(uaMeta.Get("pdfuaid:part"))) uaMeta.Set("pdfuaid:part", "1");
            if (string.IsNullOrEmpty(uaMeta.Get("dc:title"))) uaMeta.Set("dc:title", Info.Title);
            if (string.IsNullOrEmpty(uaMeta.Get("pdf:Producer"))) uaMeta.Set("pdf:Producer", "Aspose.PDF FOSS for .NET");
            if (string.IsNullOrEmpty(uaMeta.Get("xmp:CreateDate")) && Info.CreationDate != DateTime.MinValue)
                uaMeta.Set("xmp:CreateDate", FormatXmpDate(Info.CreationDate, Info.CreationTimeZone));
            if (string.IsNullOrEmpty(uaMeta.Get("xmp:ModifyDate")) && Info.ModDate != DateTime.MinValue)
                uaMeta.Set("xmp:ModifyDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));
            if (_reader.Trailer.Get("ID") is null)
            {
                _forceWriteId = true;
                var feId = Security.CryptoRandom.GetBytes(16);
                var feIdArr = new PdfArray();
                feIdArr.Add(new PdfString(feId, isHex: true));
                feIdArr.Add(new PdfString(feId, isHex: true));
                _reader.Trailer.Set("ID", feIdArr);
            }
            EmbedNonEmbeddedFonts(options, includeStandard14: true);
            if (options.AutoTaggingSettings is { EnableAutoTagging: true })
                Tagged.AutoTagger.Apply(this, options.AutoTaggingSettings);
            return CheckFontEmbedding(options);
        }

        // PDF/E-1 (ISO 24517-1, based on PDF 1.6): engineering documents. Structural
        // fixes (font embedding, the pdfe XMP identification, version normalisation)
        // always apply; ConvertErrorAction.Delete additionally strips the prohibited
        // interactive content — the document JavaScript name tree and
        // JavaScript/launch-style catalog actions.
        if (format == PdfFormat.PDF_E_1)
        {
            if (IsEncrypted)
            {
                options.ConversionLog.Add(new PdfAViolation
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

            RemoveProhibitedCatalogActions(options, strip);
            if (strip) RemoveDocumentJavaScript(options);

            EmbedNonEmbeddedFonts(options, includeStandard14: true);
            return CheckFontEmbedding(options);
        }

        var isPdfX = format is PdfFormat.PDF_X_1A or PdfFormat.PDF_X_3;

        // Determine PDF/A part and conformance from format
        var (part, conformance) = format switch
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
            _ => (null, (string?)null),
        };

        if (part is null)
            return true; // Not a PDF/A or PDF/X format, nothing to do

        // 1. Remove encryption
        if (IsEncrypted)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "Encryption",
                Description = "Document is encrypted (not allowed in PDF/A).",
            });
            if (fix)
            {
                _encryptor = null;
                _reader.Trailer.Remove("Encrypt");
            }
        }

        // 2. Fix the PDF version up to the floor its part requires. PDF/A-1 sits on
        // PDF 1.4; parts 2 and 3 only need 1.3 underneath them, and a 1.2 source
        // converted to either PDF/A-2 level comes out at 1.3 — NOT restamped at the
        // 1.7 its conformance level is written against.
        var floor = part == "1" ? "1.4" : "1.3";
        var version = PdfVersion;
        if (version is not null && string.Compare(version, floor, StringComparison.Ordinal) < 0)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "PdfVersion",
                Description = $"PDF version {version} is below {floor} (minimum for PDF/A-{part}).",
            });
            if (fix)
            {
                SetVersion(floor);
            }
        }
        // PDF/A-3 alone is stamped at ISO 32000-1's 1.7 — a PDF/A-3 conversion reports
        // 1.7 whatever it started from, while PDF/A-2 keeps the document's own version.
        // The catalog /Version takes precedence over the header when reading, so it has
        // to follow, or a stale entry would mask the upgraded header.
        if (fix && part == "3"
            && string.Compare(PdfVersion ?? "1.0", "1.7", StringComparison.Ordinal) < 0)
        {
            SetVersion("1.7");
            if (_reader.Catalog.Get("Version") is not null)
                _reader.Catalog.Set("Version", new PdfName("1.7"));
        }

        // 3. Add/fix XMP metadata
        var meta = GetOrCreateMetadata();
        var needsPdfAId = string.IsNullOrEmpty(meta.PdfAidPart);
        // PDF/A-4 (part "4") carries no conformance level, so never treat its absence
        // as a violation; parts 1–3 still require pdfaid:conformance.
        var needsConformance = part != "4" && string.IsNullOrEmpty(meta.PdfAidConformance);
        var needsTitle = string.IsNullOrEmpty(meta.Get("dc:title"));
        var needsProducer = string.IsNullOrEmpty(meta.Get("pdf:Producer"));

        if (needsPdfAId)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataPdfAId",
                Description = "Missing pdfaid:part in XMP metadata.",
            });
        }
        if (needsConformance)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataPdfAConformance",
                Description = "Missing pdfaid:conformance in XMP metadata.",
            });
        }
        if (needsTitle)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataDcTitle",
                Description = "Missing dc:title in XMP metadata.",
            });
        }
        if (needsProducer)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataPdfProducer",
                Description = "Missing pdf:Producer in XMP metadata.",
            });
        }

        if (fix)
        {
            if (!isPdfX)
            {
                meta.PdfAidPart = part;
                // PDF/A-4 has no conformance level (empty) — leave the entry absent
                // rather than writing an empty pdfaid:conformance.
                if (string.IsNullOrEmpty(conformance)) meta.PdfAidConformance = null;
                else meta.PdfAidConformance = conformance;
            }
            // Info.Title is "" (not null) when the source has no title, so "??" would
            // store an empty dc:title that the validator still flags as missing — fall
            // back to "Untitled" for null OR empty.
            if (needsTitle) meta.Set("dc:title", string.IsNullOrEmpty(Info.Title) ? "Untitled" : Info.Title);
            if (needsProducer) meta.Set("pdf:Producer", "Aspose.PDF FOSS for .NET");

            // PDF/A requires the XMP xmp:CreateDate / xmp:ModifyDate to mirror the
            // /Info CreationDate / ModDate (ISO 8601). Without them the XMP and
            // document-info dates disagree and round-tripping Metadata["xmp:CreateDate"]
            // throws KeyNotFoundException. Write them in a form that
            // round-trips through XmpValue.ToDateTime().
            if (string.IsNullOrEmpty(meta.Get("xmp:CreateDate")) && Info.CreationDate != DateTime.MinValue)
                meta.Set("xmp:CreateDate", FormatXmpDate(Info.CreationDate, Info.CreationTimeZone));
            if (string.IsNullOrEmpty(meta.Get("xmp:ModifyDate")) && Info.ModDate != DateTime.MinValue)
                meta.Set("xmp:ModifyDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));
            if (string.IsNullOrEmpty(meta.Get("xmp:MetadataDate")) && Info.ModDate != DateTime.MinValue)
                meta.Set("xmp:MetadataDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));

            // ISO 19005 6.6.3 analog of the date sync above for the remaining
            // /Info↔XMP pairs: the XMP packet the conversion writes must mirror
            // the document-information strings (Keywords → pdf:Keywords, etc.) —
            // reloading the output and reading Metadata["pdf:Keywords"] must see
            // the value the caller put in DocumentInfo. NOTE: guarded with the
            // packet-only ContainsKey — Get() consults the Info fallback, which
            // would report the value "present" without it ever being serialised.
            if (!meta.ContainsKey("pdf:Keywords") && !string.IsNullOrEmpty(Info.Keywords))
                meta.Set("pdf:Keywords", Info.Keywords);
            if (!meta.ContainsKey("dc:creator") && !string.IsNullOrEmpty(Info.Author))
                meta.Set("dc:creator", Info.Author);
            if (!meta.ContainsKey("dc:description") && !string.IsNullOrEmpty(Info.Subject))
                meta.Set("dc:description", Info.Subject);
            if (!meta.ContainsKey("xmp:CreatorTool") && !string.IsNullOrEmpty(Info.Creator))
                meta.Set("xmp:CreatorTool", Info.Creator);
        }

        // 4. Ensure file ID exists. Materialise /ID into the in-memory
        //    trailer immediately so PdfAValidator (which reads
        //    document.Reader.Trailer in-memory) sees the fix without
        //    requiring a Save+reopen round-trip. The save path also
        //    honours the flag for re-encrypted saves; we keep it set so the
        //    same ID gets written through to disk.
        if (_reader.Trailer.Get("ID") is null)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "FileId",
                Description = "Missing file ID in trailer (required for PDF/A).",
            });
            if (fix)
            {
                _forceWriteId = true;
                var fileId = Security.CryptoRandom.GetBytes(16);
                var idArray = new PdfArray();
                idArray.Add(new PdfString(fileId, isHex: true));
                idArray.Add(new PdfString(fileId, isHex: true));
                _reader.Trailer.Set("ID", idArray);
            }
        }

        // 5. Remove prohibited actions from catalog (only when ErrorAction strips)
        RemoveProhibitedCatalogActions(options, strip);

        // 6. Fix annotations (per page) — print-flag fixes always, removal only when stripping
        foreach (var page in Pages)
        {
            FixAnnotationsForPdfA(page, options, fix, strip);
        }

        // 6b. Remove page-level transparency groups — PDF/A-1 (ISO 19005-1)
        // prohibits transparency. The page /Group entry only declares the
        // blending colour space / isolation for compositing the page onto the
        // backdrop; dropping it leaves opaque content rendering identically
        // while clearing the violation. PDF/A-2 and later permit transparency,
        // so this is scoped to part 1.
        if (part == "1")
        {
            foreach (var page in Pages)
            {
                var group = _reader.ResolveDict(page.Dict.Get("Group"));
                if (group is null || group.GetName("S") != "Transparency") continue;
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "Transparency",
                    Description = $"Page {page.Number} uses transparency group (not allowed in PDF/A-1).",
                    PageNumber = page.Number,
                });
                if (fix) page.Dict.Remove("Group");
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
            if (fix && options.TransparencyAction is ConvertTransparencyAction.Default
                    or ConvertTransparencyAction.Mask)
                foreach (var page in Pages)
                    try
                    {
                        SimulateTransparencyRegions(page,
                            recolorConstantAlpha: options.TransparencyAction == ConvertTransparencyAction.Mask);
                    }
                    catch { /* unparsable content or render failure: neutralisation still applies */ }

            // ConvertTransparencyAction.Mask: preserve the visual appearance of images that
            // are painted under a constant fill-alpha (/ca < 1). PDF/A-1 forbids ExtGState
            // alpha, so the neutralisation below would zero it and make such an image render
            // opaquely. Before that, bake the alpha into a constant DeviceGray soft mask on
            // the image XObject itself (an image /SMask is NOT stripped by this conversion),
            // so the image keeps compositing at the requested opacity while the prohibited
            // ExtGState alpha is removed. The Default action leaves the neutralisation opaque.
            if (fix && options.TransparencyAction == ConvertTransparencyAction.Mask)
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
            if (fix)
                foreach (var page in Pages)
                    try { SuppressAlphaZeroPaint(page); }
                    catch { /* unparsable content: leave as-is; neutralisation still applies */ }

            // ExtGState soft masks, constant alpha (ca/CA < 1) and non-Normal blend modes
            // are equally prohibited by PDF/A-1. Neutralise them in every graphics-state
            // dictionary reachable from the pages (including nested Form XObjects) so the
            // content renders opaquely instead of failing validation.
            foreach (var page in Pages)
                NeutralizeExtGStateTransparency(page.Dict, options, page.Number, fix,
                    new HashSet<PdfDictionary>());

            // PDF/A-1 implementation limits (ISO 19005-1 / PDF 1.4 Annex C): real numbers
            // must stay within ±32767. Round out-of-range FRACTIONAL reals in the page
            // content to integers (integral magnitudes beyond the limit are tolerated by
            // the target validators, and rounding keeps far-off-page geometry harmless).
            if (fix)
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

        // 6c'. PDF/X-1a (ISO 15930-1) prohibits transparency the same way PDF/A-1
        // does. A page whose content actually USES transparency (an ExtGState with
        // non-opaque alpha, a soft mask, or a non-Normal blend mode) cannot keep its
        // vector content in an X-1a file — it is FLATTENED: the rendered page
        // replaces the content as a single DeviceCMYK image resource (X-1a is a
        // CMYK-only profile). A page that merely DECLARES a /Group (compositing onto
        // the backdrop; opaque content renders the same) only loses the declaration.
        if (format == PdfFormat.PDF_X_1A)
        {
            foreach (var page in Pages)
            {
                if (!page.Dict.ContainsKey("Group")) continue;
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "Transparency",
                    Description = $"Page {page.Number} uses transparency group (not allowed in PDF/X-1a).",
                    PageNumber = page.Number,
                });
                if (!fix) continue;
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
        if (fix)
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
        if (!isPdfX)
            foreach (var page in Pages)
                try { RemoveNotdefGlyphShows(page, options, strip); }
                catch { /* unparsable content: leave the page as-is */ }

        // 7. Add OutputIntent
        if (isPdfX && fix)
        {
            // PDF/X requires an OutputIntent with ICC profile
            AddPdfXOutputIntent(options);
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
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "ColorSpace",
                    Description = "Device-dependent color space without OutputIntent.",
                });
            }
            // A PDF/A output always carries a GTS_PDFA1 OutputIntent (validators
            // gate on its presence), not only when device-dependent
            // colours were detected — the violation above is logged for those only.
            if (fix)
            {
                AddSrgbOutputIntent();
            }
        }

        // 8. For PDF/X, set GTS_PDFXVersion in Info dict and XMP
        if (isPdfX && fix)
        {
            var xMeta = GetOrCreateMetadata();
            if (format == PdfFormat.PDF_X_1A)
            {
                xMeta.Set("pdfx:GTS_PDFXVersion", "PDF/X-1a:2003");
                xMeta.PdfAidPart = null;
                xMeta.PdfAidConformance = null;
            }
            else if (format == PdfFormat.PDF_X_3)
            {
                xMeta.Set("pdfx:GTS_PDFXVersion", "PDF/X-3:2003");
                xMeta.PdfAidPart = null;
                xMeta.PdfAidConformance = null;
            }
        }

        // 9. Interactive form fields SURVIVE the conversion: PDF/A permits an
        // AcroForm as long as appearance streams do the rendering and no forbidden
        // actions ride on the widgets. The conversion therefore keeps every field
        // (a concatenated form keeps its full field count and /DR fonts) and only
        // strips what the profile forbids — NeedAppearances and widget trigger
        // actions. Callers that want the values baked into page content flatten
        // explicitly after converting.
        if (fix && !isPdfX)
            MakeFormFieldsPdfACompliant();

        // 9b. Accessible conformance (part 2/3 level A): text shown through a SYMBOLIC
        // TrueType font whose codes live in the Private Use Area has no Unicode meaning,
        // so each such usage is re-encoded as a fresh Type0 (Identity-H) font — resource
        // key C0_0, C1_0, … — with the shown glyphs addressed by glyph id and the show
        // wrapped in a /Span ActualText marker.
        if (fix && conformance == "A" && part is "2" or "3")
            foreach (var page in Pages)
                try { ConvertPuaSymbolicFontUsagesToType0(page); }
                catch { /* unparsable content/font: leave the usage as-is */ }

        // 10. Embed glyph-bearing fonts that the source left unembedded (PDF/A requires
        // every font to be embedded — including the Standard-14 faces, which a viewer would
        // otherwise substitute): resolve the real face, fall back to Arial for an
        // unresolvable family, and report each replacement via FontSubstitution.
        if (fix && !isPdfX)
            EmbedNonEmbeddedFonts(options, includeStandard14: true);

        // 10b. Repair out-of-range vertical metrics that would leave the output
        // non-conformant: a Courier-family FontDescriptor whose Descent falls below
        // -310 (the validator's range gate) is clamped to -300. Only the descriptor
        // metric changes — glyph programs and widths are untouched.
        if (fix && !isPdfX)
            RepairFontDescriptors();

        // 11. Verify all non-embedded non-Standard14 fonts can be resolved.
        // PDF/A requires every glyph-bearing font to be embedded; if a font is
        // unembedded AND FontRepository can't find it, conversion fails.
        var fontsResolved = CheckFontEmbedding(options);

        // 11a. Implementation limits (probed contract; ISO 19005-1 §6.1.12 /
        // ISO 19005-2 §6.1.13): graphics-state nesting deeper than 28 across the
        // page→form call chain and show strings longer than the per-part character
        // limit are baked into the content and UNFIXABLE — they mark the conversion
        // failed. PDF/A-1 additionally flags object-level fractional reals beyond
        // ±32767 (convertable: the fraction truncates toward zero).
        if (part is "1" or "2")
            CheckImplementationLimits(options, part);

        // 11b. Auto-tagging: synthesise a logical-structure tree from the page content so the
        // output carries a /StructTreeRoot. This is mandatory for the accessible A-levels
        // (which require a tagged, titled document), and otherwise runs when the caller opts in
        // (AutoTaggingSettings.Default enables it) for tagged PDF/A / PDF/UA output.
        var autoTag = options.AutoTaggingSettings is { EnableAutoTagging: true } || conformance == "A";
        if (fix && autoTag)
        {
            // A-level PDF/A also requires a document title (ISO 19005 §6.7.3); mirror the XMP
            // dc:title onto /Info so the validator's title check is satisfied.
            if (conformance == "A" && string.IsNullOrEmpty(Info.Title))
                Info.Title = string.IsNullOrEmpty(meta.Get("dc:title")) ? "Untitled" : meta.Get("dc:title");
            Tagged.AutoTagger.Apply(this, options.AutoTaggingSettings ?? AutoTaggingSettings.Default);
        }

        // 12. ZUGFeRD (factur-x): mark the embedded invoice XML as an associated file —
        // /AFRelationship /Alternative + MIME type text/xml — and reference every embedded
        // file from the catalog /AF array, per the ZUGFeRD/PDF-A-3 associated-files profile.
        // The profile also requires the ZUGFeRD XMP extension schema naming the invoice.
        if (fix && format == PdfFormat.ZUGFeRD)
        {
            ApplyZugferdAssociatedFiles();
            ApplyZugferdXmp(meta);
        }

        // 12a. PDF/A-3 (ISO 19005-3 §6.9): every embedded file is an associated file —
        // the spec carries /AFRelationship (Unspecified when the relationship is
        // unknown), its embedded stream carries a MIME /Subtype (application/pdf when
        // nothing better is known), and the catalog /AF array references every spec.
        if (fix && part == "3" && format != PdfFormat.ZUGFeRD)
            ApplyPdfA3AssociatedFiles();

        // 12b. PDF/A-2 (ISO 19005-2 §6.9): an embedded file must itself be a PDF/A
        // document. Convert every embedded PDF attachment to PDF/A-2B in place —
        // so the output attachments then claim 2B
        // (so a Validate(PDF_A_2B) of the extracted attachment passes and a
        // Validate(PDF_A_3B) fails the claim gate).
        if (fix && part == "2")
            ConvertEmbeddedPdfAttachmentsToPdfA2B();

        // 13. Size optimization (OptimizeFileSize): subset every embedded TrueType program to
        // the glyphs the document actually uses. Font embedding (step 10) is the dominant cost
        // of PDF/A conversion — a source that referenced but did not embed several system
        // faces gains a full WinAnsi program for each. Subsetting those (and any already-
        // embedded faces) to the used glyphs is what keeps the converted file at or below the
        // source size. The just-embedded /FontFile2 programs are still pending objects, so the
        // subsetter is given a resolver that reaches them. Non-destructive (glyph outlines only).
        if (options.OptimizeFileSize)
            // stripStandard14: false — PDF/A requires every used font embedded, so the
            // "drop standard-14 programs" size optimization must never run here.
            Optimization.FontSubsetter.SubsetFonts(_reader, subsetEmbedded: true,
                resolveNewStream: ResolvePendingStream, stripStandard14: false);
        else if (fix)
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
        if (fix && part == "1")
        {
            foreach (var page in Pages)
                try { NormalizePdfA1PageContent(page); }
                catch { /* undecodable content (e.g. exotic LZW): leave the page as-is */ }
            // Rewriting /Contents leaves each page's original stream object(s)
            // orphaned — have the save reachability-prune them, or every edited
            // page's content bytes are carried over twice.
            _reader.MayHaveOrphansOnSave = true;
        }

        // An unconvertable violation (implementation limit baked into the content)
        // makes the conversion report failure even though every fixable repair above
        // was still applied.
        var hasUnconvertable = false;
        foreach (var v in options.ConversionLog)
            if (!v.Convertable) { hasUnconvertable = true; break; }

        return fontsResolved && !hasUnconvertable;
    }

    /// <summary>Scan the page and form-XObject content for the probed
    /// implementation limits and log an unconvertable problem per offending stream or
    /// string; PDF/A-1 also flags (and truncates) object-level out-of-range fractional
    /// reals in the form dictionaries.</summary>
    private void CheckImplementationLimits(PdfFormatConversionOptions options, string part)
    {
        // Probed thresholds: effective q/Q nesting caps at 28 (the page's depth at
        // the Do, plus one implicit save entering the form, plus the form's own
        // depth); show strings cap at 32767 characters for PDF/A-1 and 16383 for
        // PDF/A-2; object-level reals cap at ±32767 when they carry a fraction.
        const int maxNesting = 28;
        var maxString = part == "1" ? 32767 : 16383;
        var clause = part == "1" ? "6.1.12" : "6.1.13";

        void AddNesting() => options.ConversionLog.Add(new PdfAViolation
        {
            Rule = "ImplementationLimits",
            Clause = clause,
            Convertable = false,
            Description = "Maximum number of q/Q operators nesting levels exceeded",
        });
        void AddString() => options.ConversionLog.Add(new PdfAViolation
        {
            Rule = "ImplementationLimits",
            Clause = clause,
            Convertable = false,
            Description = "The PDF string object length is larger that allowed by implementation limits",
        });
        void CheckShow(Operator op)
        {
            if (op is Aspose.Pdf.Operators.ShowText st && st.Text is { } t && t.Length > maxString)
                AddString();
        }

        foreach (var page in Pages)
        {
            int depth = 0, maxDepth = 0, depthAtFirstDo = 0;
            var sawDo = false;
            try
            {
                foreach (var op in page.Contents)
                {
                    switch (op)
                    {
                        case Aspose.Pdf.Operators.GSave:
                            depth++;
                            if (depth > maxDepth) maxDepth = depth;
                            break;
                        case Aspose.Pdf.Operators.GRestore:
                            depth--;
                            break;
                        case Aspose.Pdf.Operators.Do when !sawDo:
                            sawDo = true;
                            depthAtFirstDo = depth;
                            break;
                    }
                    CheckShow(op);
                }
            }
            catch { continue; /* unparsable content: no limits verdict for this page */ }
            if (maxDepth > maxNesting) AddNesting();

            var forms = page.Resources.Forms;
            for (var i = 1; i <= forms.Count; i++)
            {
                var form = forms[i];
                int fDepth = 0, fMax = 0;
                try
                {
                    foreach (var op in form.Contents)
                    {
                        if (op is Aspose.Pdf.Operators.GSave) { fDepth++; if (fDepth > fMax) fMax = fDepth; }
                        else if (op is Aspose.Pdf.Operators.GRestore) fDepth--;
                        CheckShow(op);
                    }
                }
                catch { continue; }
                // Call-chain contribution: the page depth where the form is invoked
                // plus the implicit graphics-state save on entering the XObject.
                if (depthAtFirstDo + 1 + fMax > maxNesting) AddNesting();

                if (part == "1")
                    FlagAndFixFormOutOfRangeReals(form, clause, options);
            }
        }
    }

    /// <summary>PDF/A-1 object-level real check: a numeric in the form XObject's
    /// dictionary arrays (BBox / Matrix) whose magnitude exceeds 32767 while carrying
    /// a non-zero fraction is logged (convertable) and repaired by truncating the
    /// fraction toward zero.</summary>
    private void FlagAndFixFormOutOfRangeReals(XForm form, string clause, PdfFormatConversionOptions options)
    {
        const double maxReal = 32767;
        var flagged = false;
        foreach (var key in new[] { "BBox", "Matrix" })
        {
            if (_reader.Resolve(form.StreamDict.Get(key)) is not PdfArray arr) continue;
            for (var i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not PdfReal r) continue;
                if (Math.Abs(r.Value) <= maxReal || r.Value == Math.Truncate(r.Value)) continue;
                arr.ReplaceAt(i, new PdfReal(Math.Truncate(r.Value)));
                flagged = true;
            }
        }
        if (flagged)
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "ImplementationLimits",
                Clause = clause,
                Description = "Real value is outside the implementation limits",
            });
    }

    /// <summary>Bracket <paramref name="page"/>'s content in q…Q and round
    /// path coordinates beyond the PDF/A-1 ±32767 real-value limit to integers.
    /// A page with inline images is wrapped at the byte level and its
    /// coordinates left untouched: materialising such a stream through the
    /// typed operator list would drop the inline-image binary payload.</summary>
    private void NormalizePdfA1PageContent(Page page)
    {
        const double limit = short.MaxValue; // 32767
        static bool OutOfRange(double v) => Math.Abs(v) >= limit && v != Math.Truncate(v);
        static double Clamp(double v) => OutOfRange(v) ? Math.Round(v) : v;

        // Pre-scan: does any path coordinate exceed the PDF/A-1 real limit? Pages
        // with inline images must not be re-serialised through the typed operator
        // list at all (its BI token carries no binary payload).
        var needsCoordFix = false;
        var hasInline = false;
        var ops = page.Contents;
        foreach (var op in ops)
        {
            switch (op)
            {
                case Operators.BI:
                    hasInline = true;
                    break;
                case Operators.MoveTo m when OutOfRange(m.X) || OutOfRange(m.Y):
                case Operators.LineTo l when OutOfRange(l.X) || OutOfRange(l.Y):
                case Operators.CurveTo c when OutOfRange(c.X1) || OutOfRange(c.Y1)
                    || OutOfRange(c.X2) || OutOfRange(c.Y2) || OutOfRange(c.X3) || OutOfRange(c.Y3):
                    needsCoordFix = true;
                    break;
            }
            if (hasInline) break;
        }

        if (hasInline || !needsCoordFix)
        {
            // Byte-level wrap: keeps the original stream bytes verbatim (their
            // operator text usually compresses tighter than a re-serialisation,
            // and inline-image payloads survive untouched).
            var bytes = page.GetContentStreamBytes() ?? [];
            var head = Encoding.ASCII.GetBytes("q\n");
            var tail = Encoding.ASCII.GetBytes("\nQ");
            var merged = new byte[head.Length + bytes.Length + tail.Length];
            head.CopyTo(merged, 0);
            bytes.CopyTo(merged, head.Length);
            tail.CopyTo(merged, head.Length + bytes.Length);
            page.SetContentStream(merged);
            return;
        }

        ops.Insert(1, new Operators.GSave());
        ops.Add(new Operators.GRestore());
        // The collection is materialised by the insert above, so the enumerator
        // yields the live operator instances; coordinate edits persist through
        // the flush-on-save.
        foreach (var op in ops)
            switch (op)
            {
                case Operators.MoveTo m:
                    m.X = Clamp(m.X); m.Y = Clamp(m.Y);
                    break;
                case Operators.LineTo l:
                    l.X = Clamp(l.X); l.Y = Clamp(l.Y);
                    break;
                case Operators.CurveTo c:
                    c.X1 = Clamp(c.X1); c.Y1 = Clamp(c.Y1);
                    c.X2 = Clamp(c.X2); c.Y2 = Clamp(c.Y2);
                    c.X3 = Clamp(c.X3); c.Y3 = Clamp(c.Y3);
                    break;
            }
    }

    /// <summary>FontUtilities.SubsetFonts(SubsetAllFonts) support: embed every used
    /// non-embedded font (including the Standard-14 faces) so the subsetter has a
    /// program to trim; pending programs resolve via <see cref="ResolvePendingStreamInternal"/>.</summary>
    internal void EmbedAllFontsForSubsetting() => EmbedNonEmbeddedFonts(includeStandard14: true);

    /// <summary>Internal accessor for <see cref="ResolvePendingStream"/> (same-assembly
    /// helpers like FontUtilities need to reach conversion-pending font programs).</summary>
    internal PdfStream? ResolvePendingStreamInternal(int objNum) => ResolvePendingStream(objNum);

    /// <summary>Resolve a stream that a preceding conversion step allocated but has not yet
    /// serialised — these live in <see cref="_newObjects"/> and are not reachable through the
    /// reader's xref. Returns null when the object number is unknown or not a stream.</summary>
    private PdfStream? ResolvePendingStream(int objNum)
    {
        foreach (var (num, obj) in _newObjects)
            if (num == objNum)
                return obj as PdfStream;
        return null;
    }

    /// <summary>Move a FileAttachment annotation's embedded payload into the
    /// document's EmbeddedFiles name tree (used when PDF/A-4 conversion strips the
    /// annotation itself). With <paramref name="pdfOnly"/> (PDF/A-4) non-PDF payloads
    /// are ignored; PDF/A-4e permits any file type, so everything migrates.</summary>
    private void MigrateFileAttachmentToEmbeddedFiles(PdfDictionary annotDict, bool pdfOnly)
    {
        var fs = _reader.ResolveDict(annotDict.Get("FS"));
        var ef = fs is null ? null : _reader.ResolveDict(fs.Get("EF"));
        var stream = ef is null ? null : _reader.ResolveStream(ef.Get("F"));
        if (fs is null || stream is null) return;
        byte[] data;
        try { data = _reader.DecodeStream(stream, stream.ObjectNumber, stream.Generation); }
        catch { return; }
        if (pdfOnly && (data.Length < 5 || data[0] != (byte)'%' || data[1] != (byte)'P'
            || data[2] != (byte)'D' || data[3] != (byte)'F')) return;
        var name = (_reader.Resolve(fs.Get("UF")) as PdfString)?.ToText()
            ?? (_reader.Resolve(fs.Get("F")) as PdfString)?.ToText() ?? "attachment.pdf";
        var desc = (_reader.Resolve(annotDict.Get("Contents")) as PdfString)?.ToText();
        try
        {
            AddEmbeddedFile(name, data, desc);
            _embeddedFiles = null; // collection re-materialises from the tree
        }
        catch { /* best-effort */ }
    }

    /// <summary>Convert every embedded PDF attachment to PDF/A-2B in place (ISO 19005-2
    /// §6.9 allows only PDF/A attachments). Non-PDF attachments and attachments whose
    /// conversion fails are left untouched. The embedded-file stream keeps its object;
    /// only its bytes (and /Params /Size) are replaced.</summary>
    private void ConvertEmbeddedPdfAttachmentsToPdfA2B()
    {
        var names = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        var efTree = names is not null ? _reader.ResolveDict(names.Get("EmbeddedFiles")) : null;
        var arr = efTree is not null ? _reader.Resolve(efTree.Get("Names")) as PdfArray : null;
        if (arr is null) return;

        for (var i = arr.Count - 2; i >= 0; i -= 2)
        {
            var fsDict = _reader.ResolveDict(arr[i + 1]);
            var ef = fsDict is null ? null : _reader.ResolveDict(fsDict.Get("EF"));
            var stream = ef is null ? null : _reader.ResolveStream(ef.Get("F"));
            if (stream is null) continue;

            byte[] data;
            try { data = _reader.DecodeStream(stream, stream.ObjectNumber, stream.Generation); }
            catch { continue; }
            if (data.Length < 5 || data[0] != (byte)'%' || data[1] != (byte)'P'
                || data[2] != (byte)'D' || data[3] != (byte)'F')
            {
                // ISO 19005-2 §6.9 allows only PDF/A attachments; a non-PDF payload
                // (image, data file, …) can't be made compliant, so under
                // ConvertErrorAction.Delete it is removed
                // from the name tree (2 array slots: name string + filespec).
                arr.RemoveAt(i + 1);
                arr.RemoveAt(i);
                _embeddedFiles = null; // collection re-materialises from the tree
                continue;
            }

            try
            {
                using var src = new MemoryStream(data);
                using var child = new Document(src);
                var childOpts = new PdfFormatConversionOptions(
                    Stream.Null, PdfFormat.PDF_A_2B, ConvertErrorAction.Delete);
                if (!child.Convert(childOpts)) continue;
                using var outMs = new MemoryStream();
                child.Save(outMs);
                var newBytes = outMs.ToArray();

                stream.ReplaceData(newBytes);
                stream.Dict.Remove("Filter");
                stream.Dict.Remove("DecodeParms");
                stream.Dict.Set("Length", new PdfInteger(newBytes.Length));
                stream.DoNotCompress = true;
                var prms = _reader.ResolveDict(stream.Dict.Get("Params"));
                prms?.Set("Size", new PdfInteger(newBytes.Length));
                prms?.Remove("CheckSum");
            }
            catch { /* best-effort: leave the attachment as-is */ }
        }
    }

    /// <summary>Write the ZUGFeRD 1.0 XMP extension schema — the invoice-identification
    /// properties (DocumentType/DocumentFileName/Version/ConformanceLevel) in the
    /// <c>zf</c> namespace plus its pdfaExtension schema description. Validation of the
    /// ZUGFeRD profile requires this block; a PDF/A-3 file without it is not an invoice.</summary>
    private void ApplyZugferdXmp(XmpMetadata meta)
    {
        const string zfUri = "urn:ferd:pdfa:CrossIndustryDocument:invoice:1p0#";
        meta.SetExtensionSchema("zf", zfUri, "ZUGFeRD PDFA Extension Schema");

        // The associated invoice XML gives the DocumentFileName; the first embedded
        // .xml attachment is the invoice per the associated-files tagging above.
        string? invoiceName = null;
        var embedded = EmbeddedFiles;
        for (var i = 1; i <= embedded.Count && invoiceName is null; i++)
            if (embedded[i].Name is { } n && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                invoiceName = System.IO.Path.GetFileName(n);

        if (string.IsNullOrEmpty(meta["zf:DocumentType"]))
            meta["zf:DocumentType"] = "INVOICE";
        if (string.IsNullOrEmpty(meta["zf:Version"]))
            meta["zf:Version"] = "1.0";
        if (string.IsNullOrEmpty(meta["zf:ConformanceLevel"]))
            meta["zf:ConformanceLevel"] = "BASIC";
        if (string.IsNullOrEmpty(meta["zf:DocumentFileName"]) && invoiceName is not null)
            meta["zf:DocumentFileName"] = invoiceName;
    }

    /// <summary>Tag the document's embedded files as ZUGFeRD/factur-x associated files: the
    /// invoice XML gets <c>/AFRelationship /Alternative</c> and the <c>text/xml</c> MIME
    /// subtype, and every embedded-file spec is referenced from the catalog <c>/AF</c> array
    /// (PDF 2.0 §7.11.3 associated files).</summary>
    private void ApplyZugferdAssociatedFiles()
    {
        // The public EmbeddedFiles collection holds FileSpecification objects that are
        // decoupled from the on-disk spec dictionaries (Add() copies the bytes in), so tag
        // those instances too — callers read MIMEType/AFRelationship back through them.
        var embedded = EmbeddedFiles;
        for (var i = 1; i <= embedded.Count; i++)
        {
            var spec = embedded[i];
            if (spec.Name is { } n && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(spec.MIMEType)) spec.MIMEType = "text/xml";
                if (spec.AFRelationship == AFRelationship.None)
                    spec.AFRelationship = AFRelationship.Alternative;
            }
        }

        var names = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        var efTree = names is not null ? _reader.ResolveDict(names.Get("EmbeddedFiles")) : null;
        var arr = efTree is not null ? _reader.Resolve(efTree.Get("Names")) as PdfArray : null;
        if (arr is null) return;

        var afArray = _reader.Resolve(_reader.Catalog.Get("AF")) as PdfArray ?? new PdfArray();
        var present = new HashSet<int>();
        foreach (var item in afArray)
            if (item is PdfIndirectRef r) present.Add(r.ObjectNumber);

        for (var i = 0; i + 1 < arr.Count; i += 2)
        {
            var key = (_reader.Resolve(arr[i]) as PdfString)?.ToText() ?? string.Empty;
            var fsRef = arr[i + 1];
            var fsDict = _reader.ResolveDict(fsRef);
            if (fsDict is null) continue;

            // The invoice XML is the alternative representation of the document's content.
            if (key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                fsDict.Set("AFRelationship", new PdfName("Alternative"));
                var ef = _reader.ResolveDict(fsDict.Get("EF"));
                var stream = ef is not null ? _reader.ResolveStream(ef.Get("F")) : null;
                stream?.Dict.Set("Subtype", new PdfName("text/xml"));
            }

            if (fsRef is PdfIndirectRef fr && present.Add(fr.ObjectNumber))
                afArray.Add(fsRef);
        }

        if (afArray.Count > 0)
            _reader.Catalog.Set("AF", afArray);
    }

    /// <summary>Tag every embedded file as a PDF/A-3 associated file (ISO 19005-3 §6.9):
    /// specs get <c>/AFRelationship /Unspecified</c> when none is declared, embedded-file
    /// streams get a MIME <c>/Subtype</c> (<c>application/pdf</c> when nothing better is
    /// known), and the catalog <c>/AF</c> array references every spec.</summary>
    private void ApplyPdfA3AssociatedFiles()
    {
        const string defaultSubtype = "application/pdf";

        // The public EmbeddedFiles collection holds FileSpecification objects that are
        // decoupled from the on-disk spec dictionaries (Add() copies the bytes in), so
        // tag those instances too — callers read MIMEType/AFRelationship back through them.
        var embedded = EmbeddedFiles;
        for (var i = 1; i <= embedded.Count; i++)
        {
            var spec = embedded[i];
            if (string.IsNullOrEmpty(spec.MIMEType)) spec.MIMEType = defaultSubtype;
            if (spec.AFRelationship == AFRelationship.None)
                spec.AFRelationship = AFRelationship.Unspecified;
        }

        var names = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        var efTree = names is not null ? _reader.ResolveDict(names.Get("EmbeddedFiles")) : null;
        var arr = efTree is not null ? _reader.Resolve(efTree.Get("Names")) as PdfArray : null;
        if (arr is null) return;

        var afArray = _reader.Resolve(_reader.Catalog.Get("AF")) as PdfArray ?? new PdfArray();
        var present = new HashSet<int>();
        foreach (var item in afArray)
            if (item is PdfIndirectRef r) present.Add(r.ObjectNumber);

        for (var i = 0; i + 1 < arr.Count; i += 2)
        {
            var fsRef = arr[i + 1];
            var fsDict = _reader.ResolveDict(fsRef);
            if (fsDict is null) continue;

            if (fsDict.Get("AFRelationship") is null)
                fsDict.Set("AFRelationship", new PdfName("Unspecified"));
            var ef = _reader.ResolveDict(fsDict.Get("EF"));
            var stream = ef is not null ? _reader.ResolveStream(ef.Get("F")) : null;
            if (stream is not null && stream.Dict.GetName("Subtype") is null)
                stream.Dict.Set("Subtype", new PdfName(defaultSubtype));

            if (fsRef is PdfIndirectRef fr && present.Add(fr.ObjectNumber))
                afArray.Add(fsRef);
        }

        if (afArray.Count > 0)
            _reader.Catalog.Set("AF", afArray);
    }

    /// <summary>True when the page's content actually paints with transparency:
    /// an /ExtGState carrying a non-opaque alpha (/ca or /CA &lt; 1), a soft mask
    /// other than /None, or a blend mode other than Normal/Compatible. The bare
    /// page /Group declaration does not count — opaque content composites the
    /// same with or without it.</summary>
    private bool PageUsesTransparency(Page page)
    {
        var res = _reader.ResolveDict(page.Dict.Get("Resources"))
                  ?? _reader.ResolveDict(FindInheritedRaw(page.Dict, "Resources"));
        var extGStates = res is not null ? _reader.ResolveDict(res.Get("ExtGState")) : null;
        if (extGStates is null) return false;

        static double NumberOr(PdfObject? o, double fallback) => o switch
        {
            PdfReal r => r.Value,
            PdfInteger i => i.Value,
            _ => fallback,
        };

        foreach (var key in extGStates.Keys)
        {
            var gs = _reader.ResolveDict(extGStates.Get(key));
            if (gs is null) continue;
            if (NumberOr(_reader.Resolve(gs.Get("ca")), 1.0) < 1.0) return true;
            if (NumberOr(_reader.Resolve(gs.Get("CA")), 1.0) < 1.0) return true;
            if (gs.Get("SMask") is { } sm && (_reader.Resolve(sm) as PdfName)?.Value != "None") return true;
            var bm = gs.GetName("BM");
            if (bm is not (null or "Normal" or "Compatible")) return true;
        }
        return false;
    }

    /// <summary>Flatten a transparent page for PDF/X-1a: render the page and replace
    /// its content with a single DeviceCMYK image resource named <c>Im0</c> drawn over
    /// the full page box. Text, vectors and the transparency they used all bake into
    /// the raster; the page keeps no fonts and no /Group.</summary>
    private void FlattenPageToCmykImage(Page page)
    {
        // Raster density for flattened pages: matches the harness render DPI, and at
        // A4 sizes stays well under the PDF/A-1 implementation limits.
        const int flattenDpi = 150;
        var rgba = new Devices.SoftwarePageRenderer().RenderPage(page, flattenDpi);

        // RGB -> naive DeviceCMYK (K = 1-max, remaining channels scaled by 1-K):
        // the standard device conversion; X-1a only requires the DATA be CMYK.
        var cmyk = new byte[rgba.Width * rgba.Height * 4];
        for (int i = 0, o = 0; o < cmyk.Length; i += 4, o += 4)
        {
            double r = rgba.Data[i] / 255.0, g = rgba.Data[i + 1] / 255.0, b = rgba.Data[i + 2] / 255.0;
            double k = 1 - Math.Max(r, Math.Max(g, b));
            double denom = 1 - k;
            cmyk[o] = (byte)Math.Round(255 * (denom <= 0 ? 0 : (1 - r - k) / denom));
            cmyk[o + 1] = (byte)Math.Round(255 * (denom <= 0 ? 0 : (1 - g - k) / denom));
            cmyk[o + 2] = (byte)Math.Round(255 * (denom <= 0 ? 0 : (1 - b - k) / denom));
            cmyk[o + 3] = (byte)Math.Round(255 * k);
        }

        var imgDict = new PdfDictionary();
        imgDict.Set("Type", new PdfName("XObject"));
        imgDict.Set("Subtype", new PdfName("Image"));
        imgDict.Set("Width", new PdfInteger(rgba.Width));
        imgDict.Set("Height", new PdfInteger(rgba.Height));
        imgDict.Set("ColorSpace", new PdfName("DeviceCMYK"));
        imgDict.Set("BitsPerComponent", new PdfInteger(8));
        var imgNum = AllocateObjectNumber();
        AddNewObject(imgNum, new PdfStream(imgDict, cmyk));

        var xObjects = new PdfDictionary();
        xObjects.Set("Im0", new PdfIndirectRef(imgNum, 0));
        var resources = new PdfDictionary();
        resources.Set("XObject", xObjects);
        page.Dict.Set("Resources", resources);

        var box = page.Rect;
        var content = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "q {0:0.###} 0 0 {1:0.###} 0 0 cm /Im0 Do Q", box.Width, box.Height);
        var csNum = AllocateObjectNumber();
        AddNewObject(csNum, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content)));
        page.Dict.Set("Contents", new PdfIndirectRef(csNum, 0));
        page.Dict.Remove("Group");
    }

    /// <summary>Embed every non-embedded simple (Type1/TrueType) font referenced by the
    /// pages, substituting a system face. The real family is used when it resolves;
    /// otherwise the text is re-mapped to Arial. The existing font dictionary is rewritten
    /// in place so the page's resource reference is preserved.</summary>
    private void EmbedNonEmbeddedFonts(PdfFormatConversionOptions? options = null,
        bool includeStandard14 = false)
    {
        // Records (once per BaseFont) that the source left a glyph-bearing font
        // unembedded — a PDF/A violation that this pass then fixes by embedding.
        var reported = new HashSet<string>(StringComparer.Ordinal);
        // An empty FontRepository.Sources means the caller has removed all font sources
        // (including system fonts), so no replacement face is available to embed. Resolving
        // straight from the OS here would silently embed system fonts and let the PDF/A
        // conversion "succeed" even though the fonts are unavailable — the conversion must
        // instead fail so CheckFontEmbedding reports the missing fonts.
        if (Text.FontRepository.Sources.Count == 0) return;

        var done = new HashSet<PdfDictionary>();
        // Shared across every dictionary so identical font programs are embedded once.
        var fontFileCache = new Dictionary<string, (int objNum, string embedName)>();
        var visitedRes = new HashSet<PdfDictionary>();

        // Embed one simple, glyph-bearing, non-embedded font dict in place, substituting a
        // resolved system face (Helvetica→Arial, etc.) when the named font has none.
        void EmbedOne(PdfDictionary fontDict)
        {
            if (!done.Add(fontDict)) return;
            // Consume the transient "embed full, don't subset" marker (set by
            // Font.IsSubset = false). Removed here so it never reaches the output.
            var embedFull = fontDict.GetBool("AsposeEmbedFull");
            fontDict.Remove("AsposeEmbedFull");
            var subtype = fontDict.GetName("Subtype");
            if (subtype == "Type0")
            {
                EmbedNonEmbeddedCidFont(fontDict, options, reported, fontFileCache);
                return;
            }
            if (subtype is not ("Type1" or "TrueType")) return;   // simple fonts only
            if (IsSimpleFontEmbedded(fontDict)) return;
            var baseFont = fontDict.GetName("BaseFont") ?? "";
            // A subset tag does NOT imply an embedded program: setting IsSubset on a
            // non-embedded font prefixes the name without adding a FontFile, and the
            // embed check above is the authority. Strip the tag so the bare family
            // resolves ("WRDIWR+Times-Roman" → "Times-Roman").
            if (baseFont.Length > 7 && baseFont[6] == '+') baseFont = baseFont[7..];
            if (!includeStandard14 &&
                new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal).Contains(baseFont))
                return; // standard-14 stay as-is unless the caller opts in (Document.EmbedStandardFonts)

            // The source carries this glyph-bearing font without an embedded
            // program — log it (once per name) as a PDF/A violation before the
            // pass below embeds a resolved face.
            if (options is not null && reported.Add(baseFont))
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "FontEmbedding",
                    Description = $"Font '{baseFont}' is not embedded.",
                });

            var resolved = Text.SystemFontResolver.Resolve(baseFont);
            string newName;
            byte[]? ttf;
            if (resolved is not null) { ttf = resolved; newName = baseFont; }
            else { ttf = Text.SystemFontResolver.Resolve("Arial"); newName = "Arial"; }
            if (ttf is null || ttf.Length == 0) return;

            // A Standard-14 font carries no program of its own, so the resolver returns a
            // host substitute (Helvetica→Arial, Times→Times New Roman, …). Name the embedded
            // font after the face actually embedded — read from its name table — so the output
            // reflects what was embedded rather than the abstract standard name (matching
            // the public surface). Host-dependent by nature.
            if (new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal).Contains(baseFont))
            {
                try
                {
                    var ttp = new Text.TrueTypeParser(ttf);
                    ttp.Parse();
                    var fam = ttp.FamilyName;
                    if (!string.IsNullOrWhiteSpace(fam) && fam != "Unknown")
                        newName = fam.Replace(" ", "");
                }
                catch { /* keep the standard name if the face can't be parsed */ }
            }

            try
            {
                Text.FontEmbedder.EmbedIntoFontDict(this, ttf, fontDict, newName, fontFileCache, subset: !embedFull);
                // The event reports the substitute by its user-facing family+style
                // name ("Courier-Bold" → "Courier New Bold"); the dictionary keeps
                // the PDF-safe space-free name written above. SynthesizedFontName
                // carries the display name past FontName's space-stripping.
                var reportedName = Text.FontInfo.SubstitutedFaceDisplayName(baseFont, ttf) ?? newName;
                RaiseFontSubstitution(new Text.Font(baseFont, "Type1"),
                    new Text.Font(reportedName, "TrueType") { SynthesizedFontName = reportedName });
            }
            catch { /* best-effort: leave the font as-is if embedding fails */ }
        }

        foreach (var page in Pages)
        {
            PdfDictionary? resources;
            try { resources = Reader.ResolveDict(page.Dict.Get("Resources")); } catch { continue; }
            // Walk the page resources and any nested Form XObject resources — a font
            // used only inside a form/appearance stream (not the page's own /Font) must
            // be embedded too.
            if (resources is not null)
                foreach (var fontDict in CollectFontDictsRecursive(resources, visitedRes))
                    EmbedOne(fontDict);

            // Annotation appearance (/AP) streams are NOT reachable from the page
            // /Resources, so their fonts (e.g. a FreeText appearance regenerated with a
            // non-embedded standard /Helvetica) must be walked separately for PDF/A.
            foreach (var apRes in CollectAnnotationAppearanceResources(page))
                foreach (var fontDict in CollectFontDictsRecursive(apRes, visitedRes))
                    EmbedOne(fontDict);
        }
    }

    /// <summary>Embed a system face into a non-embedded composite (Type0/CID) font.
    /// Unlike the simple-font path there is NO Arial fallback: under an Identity
    /// encoding the content stream's CIDs are the ORIGINAL face's glyph ids, so only
    /// the same-named real face keeps them valid — an unresolvable family is left
    /// unembedded (the conversion log still records the violation). A CJK-mojibake
    /// /BaseFont (its legacy-codepage bytes read as Latin-1, e.g. "ËÎÌå" = 宋体) is
    /// decoded through the font's CMap codepage and mapped to the host family.</summary>
    private void EmbedNonEmbeddedCidFont(PdfDictionary type0Dict, PdfFormatConversionOptions? options,
        HashSet<string> reported, Dictionary<string, (int objNum, string embedName)> fontFileCache)
    {
        var descArr = _reader.Resolve(type0Dict.Get("DescendantFonts")) as PdfArray;
        var cidFont = descArr is { Count: > 0 } ? _reader.ResolveDict(descArr[0]) : null;
        if (cidFont is null) return;
        var descriptor = _reader.ResolveDict(cidFont.Get("FontDescriptor"));
        if (descriptor is not null &&
            (descriptor.Get("FontFile") ?? descriptor.Get("FontFile2") ?? descriptor.Get("FontFile3")) is not null)
            return; // already embedded
        var baseFont = type0Dict.GetName("BaseFont") ?? cidFont.GetName("BaseFont") ?? "";
        // A subset tag does NOT imply an embedded program (see the simple-font pass):
        // the descriptor check above is the authority; resolve by the bare family.
        if (baseFont.Length > 7 && baseFont[6] == '+') baseFont = baseFont[7..];

        if (options is not null && reported.Add(baseFont))
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "FontEmbedding",
                Description = $"Font '{baseFont}' is not embedded.",
            });

        var ttf = Text.SystemFontResolver.Resolve(baseFont);
        if (ttf is null or { Length: 0 })
        {
            var decoded = DecodeCjkBaseFontName(baseFont, type0Dict, cidFont);
            if (decoded != baseFont)
                ttf = Text.SystemFontResolver.Resolve(decoded);
        }
        if (ttf is null or { Length: 0 }) return;

        try
        {
            Text.FontEmbedder.EmbedIntoCidFontDict(this, ttf, type0Dict, cidFont, fontFileCache);
            RaiseFontSubstitution(new Text.Font(baseFont, "Type0"), new Text.Font(baseFont, "Type0"));
        }
        catch { /* best-effort: leave the font as-is if embedding fails */ }
    }

    /// <summary>Decode a legacy-codepage-mojibake /BaseFont ("ËÎÌå") to its script-native
    /// name (宋体) via the font's CMap codepage, then map the common CJK display names to
    /// their host font families (宋体 → SimSun). Returns the input unchanged when it has no
    /// high bytes or no codepage applies.</summary>
    private string DecodeCjkBaseFontName(string baseFont, PdfDictionary type0Dict, PdfDictionary cidFont)
    {
        var hasHigh = false;
        foreach (var c in baseFont)
            if (c > 0x7F) { hasHigh = true; break; }
        if (!hasHigh) return baseFont;

        var cp = Text.CidFontInfo.CodepageForCMapName(type0Dict.GetName("Encoding"));
        if (cp == 0)
        {
            var csi = _reader.ResolveDict(cidFont.Get("CIDSystemInfo"));
            var orderingObj = csi?.Get("Ordering");
            var ordering = orderingObj is PdfString os ? os.ToText()
                : (orderingObj is PdfName on ? on.Value : null);
            cp = ordering switch { "CNS1" => 950, "GB1" => 936, "Japan1" => 932, "Korea1" or "KR" => 949, _ => 0 };
        }
        if (cp == 0) return baseFont;

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < baseFont.Length; i++)
        {
            var c = baseFont[i];
            if (c <= 0x7F || i + 1 >= baseFont.Length) { sb.Append(c); continue; }
            var code = (c << 8) | (baseFont[i + 1] & 0xFF);
            if (Text.CidFontInfo.LegacyLookup(cp, code) is int u)
            {
                sb.Append(char.ConvertFromUtf32(u));
                i++;
            }
            else sb.Append(c);
        }
        var native = sb.ToString();
        return native switch
        {
            "宋体" => "SimSun",
            "新宋体" => "NSimSun",
            "黑体" => "SimHei",
            "楷体" or "楷体_GB2312" => "KaiTi",
            "仿宋" or "仿宋_GB2312" => "FangSong",
            "微软雅黑" => "Microsoft YaHei",
            "ＭＳ ゴシック" or "ＭＳゴシック" => "MS Gothic",
            "ＭＳ 明朝" or "ＭＳ明朝" => "MS Mincho",
            "標楷體" => "DFKai-SB",
            "細明體" => "MingLiU",
            "新細明體" => "PMingLiU",
            "굴림" => "Gulim",
            "바탕" => "Batang",
            _ => native,
        };
    }

    /// <summary>Clamp out-of-range Courier-family descriptor metrics: a Descent below
    /// -310 goes to -300 across every page's fonts (Type0 descendants included), so a
    /// converted document passes the validator's Courier Descent range gate. Only the
    /// metric entry changes — programs, widths and everything else stay untouched.</summary>
    private void RepairFontDescriptors()
    {
        var visitedRes = new HashSet<PdfDictionary>();
        var repaired = new HashSet<PdfDictionary>();
        void RepairOne(PdfDictionary fontDict)
        {
            var target = fontDict;
            if (fontDict.GetName("Subtype") == "Type0")
            {
                var descArr = _reader.Resolve(fontDict.Get("DescendantFonts")) as PdfArray;
                target = descArr is { Count: > 0 } ? _reader.ResolveDict(descArr[0]) : null;
                if (target is null) return;
            }
            PdfDictionary? descriptor;
            try { descriptor = _reader.ResolveDict(target.Get("FontDescriptor")); } catch { return; }
            if (descriptor is null || !repaired.Add(descriptor)) return;
            // Repair covers the Courier New faces only: their descriptors habitually
            // carry the raw hhea descent (-680/-710) that the range gate rejects.
            // Other Courier-family faces (Courier Prime and friends) keep their
            // authored metrics — for those the violation must survive conversion.
            var name = descriptor.GetName("FontName")
                       ?? target.GetName("BaseFont") ?? fontDict.GetName("BaseFont") ?? "";
            if (name.Contains("CourierNew") && descriptor.GetInt("Descent", 0) < -310)
                descriptor.Set("Descent", new PdfInteger(-300));
        }

        foreach (var page in Pages)
        {
            PdfDictionary? resources;
            try { resources = Reader.ResolveDict(page.Dict.Get("Resources")); } catch { continue; }
            if (resources is not null)
                foreach (var fontDict in CollectFontDictsRecursive(resources, visitedRes))
                    RepairOne(fontDict);
            foreach (var apRes in CollectAnnotationAppearanceResources(page))
                foreach (var fontDict in CollectFontDictsRecursive(apRes, visitedRes))
                    RepairOne(fontDict);
        }
    }

    /// <summary>Yield the /Resources dict of every appearance (/AP /N, /D, /R) stream of
    /// every annotation on <paramref name="page"/>, descending state-keyed appearance
    /// sub-dictionaries. Used so PDF/A font embedding reaches fonts that live only inside
    /// an annotation's appearance stream.</summary>
    private IEnumerable<PdfDictionary> CollectAnnotationAppearanceResources(Page page)
    {
        PdfArray? annots;
        try { annots = Reader.Resolve(page.Dict.Get("Annots")) as PdfArray; } catch { yield break; }
        if (annots is null) yield break;
        foreach (var annotObj in annots)
        {
            var annot = Reader.ResolveDict(annotObj);
            var ap = annot is null ? null : Reader.ResolveDict(annot.Get("AP"));
            if (ap is null) continue;
            foreach (var apKey in new[] { "N", "D", "R" })
            {
                var entry = Reader.Resolve(ap.Get(apKey));
                if (entry is PdfStream stream)
                {
                    var res = Reader.ResolveDict(stream.Dict.Get("Resources"));
                    if (res is not null) yield return res;
                }
                else if (entry is PdfDictionary stateDict) // state-keyed appearances
                {
                    foreach (var stateKey in new List<string>(stateDict.Keys))
                    {
                        var s = Reader.ResolveStream(stateDict.Get(stateKey));
                        var res = s is null ? null : Reader.ResolveDict(s.Dict.Get("Resources"));
                        if (res is not null) yield return res;
                    }
                }
            }
        }
    }

    /// <summary>Yield every <c>/Font</c> child dictionary reachable from a <c>/Resources</c>
    /// dict, recursing through Form XObject (<c>/Subtype /Form</c>) resources so a font used
    /// only inside a form/appearance stream is reached too. <paramref name="visitedRes"/>
    /// guards against resource-dict cycles.</summary>
    private IEnumerable<PdfDictionary> CollectFontDictsRecursive(PdfDictionary resources,
        HashSet<PdfDictionary> visitedRes)
    {
        if (!visitedRes.Add(resources)) yield break;

        var fontRes = Reader.ResolveDict(resources.Get("Font"));
        if (fontRes is not null)
            foreach (var key in new List<string>(fontRes.Keys))
            {
                var fontDict = Reader.ResolveDict(fontRes.Get(key));
                if (fontDict is not null) yield return fontDict;
            }

        var xobjs = Reader.ResolveDict(resources.Get("XObject"));
        if (xobjs is not null)
            foreach (var key in new List<string>(xobjs.Keys))
            {
                var xobj = Reader.Resolve(xobjs.Get(key));
                var xdict = xobj is PdfStream s ? s.Dict : xobj as PdfDictionary;
                if (xdict is null || xdict.GetName("Subtype") != "Form") continue;
                var subRes = Reader.ResolveDict(xdict.Get("Resources"));
                if (subRes is not null)
                    foreach (var fd in CollectFontDictsRecursive(subRes, visitedRes))
                        yield return fd;
            }
    }

    private bool IsSimpleFontEmbedded(PdfDictionary fontDict)
    {
        var fd = Reader.ResolveDict(fontDict.Get("FontDescriptor"));
        if (fd is null) return false;
        return fd.Get("FontFile") is not null || fd.Get("FontFile2") is not null || fd.Get("FontFile3") is not null;
    }

    private bool CheckFontEmbedding(PdfFormatConversionOptions options)
    {
        // Narrow scope: only block conversion when the caller has explicitly
        // emptied FontRepository.Sources (canonical behaviour: with no font
        // sources at all, unembedded non-Standard14 fonts can't be resolved).
        // When sources are populated, SystemFontSource still has lookup
        // gaps (matches by filename rather than TTF name table) — applying
        // the check there would block valid conversions of common fonts.
        if (Text.FontRepository.Sources.Count > 0) return true;
        bool allResolved = true;
        var standard14 = new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal);
        foreach (var page in Pages)
        {
            Text.FontCollection? pageFonts;
            try { pageFonts = page.Fonts; } catch { continue; }
            if (pageFonts is null) continue;
            foreach (var font in pageFonts)
            {
                if (font.IsEmbedded) continue;
                // PDF spec §9.6.4: a BaseFont of the form "XXXXXX+Name" is a
                // subset font, embedded by definition. IsEmbedded doesn't
                // recognise the prefix; treat as embedded here.
                if (font.BaseFont.Length > 7 && font.BaseFont[6] == '+') continue;
                if (standard14.Contains(font.BaseFont)) continue;
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "FontNotEmbedded",
                    Description = $"Font '{font.BaseFont}' is not embedded and FontRepository.Sources is empty.",
                });
                allResolved = false;
            }
        }
        return allResolved;
    }

    /// <summary>
    /// Removes non-signature widget annotations from all pages and the AcroForm /Fields array.
    /// Signature fields (/FT=Sig) are preserved because they are valid in PDF/A.
    /// Walks each page's /Annots, then prunes /AcroForm/Fields to match.
    /// </summary>
    /// <summary>Make an AcroForm PDF/A-conformant without destroying it: the
    /// fields all stay interactive; only /NeedAppearances goes (appearance
    /// streams must carry the rendering). Prohibited widget actions are already
    /// stripped by the per-page annotation pass.</summary>
    private void MakeFormFieldsPdfACompliant()
    {
        var acroForm = _reader.ResolveDict(_reader.Catalog.Get("AcroForm"));
        acroForm?.Remove("NeedAppearances");
    }

    /// <summary>
    /// Remove PDF/A compliance identification from XMP metadata.
    /// </summary>
    public void RemovePdfaCompliance()
    {
        // Clear the in-memory tracker so IsPdfaCompliant / PdfFormat reflect the removal.
        _lastConvertedFormat = null;
        if (Metadata is null) return;
        Metadata.Remove("pdfaid:part");
        Metadata.Remove("pdfaid:conformance");
    }

    private static readonly HashSet<string> ConvertProhibitedActionTypes = new(StringComparer.Ordinal)
    {
        "Launch", "Sound", "Movie", "ResetForm", "ImportData", "JavaScript",
    };

    private static readonly HashSet<string> ConvertProhibitedAnnotationSubtypes = new(StringComparer.Ordinal)
    {
        "FileAttachment", "Sound", "Movie", "3D",
    };

    // PDF/A-1 (ISO 19005-1) is based on PDF 1.4 and allows only the annotation
    // subtypes listed in §6.5.3. The PDF 1.5+ types below are therefore prohibited
    // when converting to PDF/A-1 specifically (they ARE permitted in PDF/A-2/3, so
    // this set is applied only for the 1A/1B targets).
    private static readonly HashSet<string> PdfA1ProhibitedAnnotationSubtypes = new(StringComparer.Ordinal)
    {
        "Polygon", "PolyLine", "Caret", "Screen", "Watermark", "Redact", "RichMedia", "Projection",
    };

    /// <summary>
    /// PDF/A level-A: re-encode every page usage of a symbolic TrueType font whose used
    /// character codes resolve through the program's (3,0) cmap into the Private Use Area.
    /// Each such font gets a companion Type0/Identity-H font under a <c>C{n}_0</c> resource
    /// key (descendant CIDFontType2 sharing the embedded program, CIDs = glyph ids); the
    /// content's Tf is redirected to it, show strings become 2-byte glyph-id hex strings,
    /// and each show is wrapped in a <c>/Span &lt;&lt;/ActualText (…)&gt;&gt; BDC … EMC</c>
    /// marker, so the output carries a Unicode meaning for the PUA glyphs.
    /// </summary>
    private void ConvertPuaSymbolicFontUsagesToType0(Page page)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        var fonts = resources is not null ? _reader.ResolveDict(resources.Get("Font")) : null;
        if (fonts is null) return;

        var contentBytes = page.GetContentStreamBytes();
        if (contentBytes is null || contentBytes.Length == 0) return;
        var content = System.Text.Encoding.Latin1.GetString(contentBytes);

        var converted = 0;
        foreach (var key in fonts.Keys.ToList())
        {
            var fontDict = _reader.ResolveDict(fonts.Get(key));
            if (fontDict is null || fontDict.GetName("Subtype") != "TrueType") continue;
            var descriptor = _reader.ResolveDict(fontDict.Get("FontDescriptor"));
            var ff2 = descriptor is not null ? _reader.ResolveStream(descriptor.Get("FontFile2")) : null;
            if (descriptor is null || ff2 is null) continue;
            var flags = (_reader.Resolve(descriptor.Get("Flags")) as PdfInteger)?.Value ?? 0;
            if ((flags & 4) == 0) continue; // symbolic flag

            // The program's cmap decides whether the used codes are PUA-routed.
            Dictionary<int, int> cmap;
            try { cmap = new Text.GlyphOutlineParser(_reader.DecodeStream(ff2)).CMap; }
            catch { continue; }

            int CodeToGid(int code) =>
                cmap.TryGetValue(0xF000 | code, out var g) ? g
                : cmap.TryGetValue(code, out var g2) ? g2 : 0;
            bool CodeIsPua(int code) => cmap.ContainsKey(0xF000 | code);

            // Find this font's Tf selections and rewrite the shows that follow them.
            var tfPattern = new System.Text.RegularExpressions.Regex(
                @"/" + System.Text.RegularExpressions.Regex.Escape(key) + @"\s+([\d.]+)\s+Tf");
            if (!tfPattern.IsMatch(content)) continue;

            var firstChar = (int)((_reader.Resolve(fontDict.Get("FirstChar")) as PdfInteger)?.Value ?? 0);
            var widths = _reader.Resolve(fontDict.Get("Widths")) as PdfArray;
            var usedGidWidths = new SortedDictionary<int, double>();
            var anyPua = false;

            var newKey = $"C{converted}_0";
            var rewritten = new StringBuilder();
            var pos = 0;
            foreach (System.Text.RegularExpressions.Match m in tfPattern.Matches(content))
            {
                if (m.Index < pos) continue;
                rewritten.Append(content, pos, m.Index - pos);
                rewritten.Append('/').Append(newKey).Append(' ').Append(m.Groups[1].Value).Append(" Tf");
                pos = m.Index + m.Length;

                // Until the NEXT Tf or ET, re-encode literal show strings.
                var segEnd = content.Length;
                var nextTf = content.IndexOf(" Tf", pos, StringComparison.Ordinal);
                var nextEt = content.IndexOf("ET", pos, StringComparison.Ordinal);
                if (nextTf >= 0)
                {
                    // back up to the start of the /Name that belongs to that Tf
                    var nameStart = content.LastIndexOf('/', nextTf);
                    if (nameStart > pos) segEnd = Math.Min(segEnd, nameStart);
                }
                if (nextEt >= 0) segEnd = Math.Min(segEnd, nextEt);

                var segment = content[pos..segEnd];
                segment = System.Text.RegularExpressions.Regex.Replace(segment,
                    @"\(((?:[^()\\]|\\.)*)\)\s*(Tj|'\s*)",
                    sm =>
                    {
                        var raw = UnescapePdfLiteral(sm.Groups[1].Value);
                        var hex = new StringBuilder();
                        var actual = new StringBuilder();
                        foreach (var ch in raw)
                        {
                            var code = (int)ch;
                            if (CodeIsPua(code)) anyPua = true;
                            var gid = CodeToGid(code);
                            hex.Append(gid.ToString("X4"));
                            double w = 0;
                            if (widths is not null && code - firstChar >= 0 && code - firstChar < widths.Count)
                                w = _reader.Resolve(widths[code - firstChar]) switch
                                {
                                    PdfInteger wi => wi.Value,
                                    PdfReal wr => wr.Value,
                                    _ => 0,
                                };
                            if (gid > 0) usedGidWidths[gid] = w;
                            actual.Append(' '); // PUA glyph: no Unicode meaning; marked as a space
                        }
                        var op = sm.Groups[2].Value.TrimEnd();
                        return $"/Span <</ActualText ({actual})>> BDC\n<{hex}> {op}\nEMC";
                    });
                rewritten.Append(segment);
                pos = segEnd;
            }
            rewritten.Append(content, pos, content.Length - pos);

            if (!anyPua) continue; // not a PUA usage — leave the font/content untouched

            content = rewritten.ToString();
            converted++;

            // Companion Type0 font: descendant shares the embedded program via the
            // SAME descriptor, CIDs are the program's glyph ids (CIDToGIDMap Identity).
            var cidSystemInfo = new PdfDictionary();
            cidSystemInfo.Set("Registry", new PdfString(System.Text.Encoding.ASCII.GetBytes("Adobe")));
            cidSystemInfo.Set("Ordering", new PdfString(System.Text.Encoding.ASCII.GetBytes("Identity")));
            cidSystemInfo.Set("Supplement", new PdfInteger(0));

            var wArr = new PdfArray();
            foreach (var (gid, w) in usedGidWidths)
            {
                wArr.Add(new PdfInteger(gid));
                var inner = new PdfArray();
                inner.Add(new PdfReal(w));
                wArr.Add(inner);
            }

            var baseFontName = fontDict.GetName("BaseFont") ?? "Unknown";
            var cidFont = new PdfDictionary();
            cidFont.Set("Type", new PdfName("Font"));
            cidFont.Set("Subtype", new PdfName("CIDFontType2"));
            cidFont.Set("BaseFont", new PdfName(baseFontName));
            cidFont.Set("CIDSystemInfo", cidSystemInfo);
            cidFont.Set("FontDescriptor", fontDict.Get("FontDescriptor")!);
            cidFont.Set("DW", new PdfInteger(1000));
            if (wArr.Count > 0) cidFont.Set("W", wArr);
            cidFont.Set("CIDToGIDMap", new PdfName("Identity"));

            var type0 = new PdfDictionary();
            type0.Set("Type", new PdfName("Font"));
            type0.Set("Subtype", new PdfName("Type0"));
            type0.Set("BaseFont", new PdfName(baseFontName));
            type0.Set("Encoding", new PdfName("Identity-H"));
            var descendants = new PdfArray();
            descendants.Add(cidFont);
            type0.Set("DescendantFonts", descendants);
            fonts.Set(newKey, type0);
        }

        if (converted > 0)
            page.SetContentStream(System.Text.Encoding.Latin1.GetBytes(content));
    }

    /// <summary>Resolve backslash escapes in a PDF literal-string body.</summary>
    private static string UnescapePdfLiteral(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            var n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case '(': case ')': case '\\': sb.Append(n); break;
                default:
                    if (n is >= '0' and <= '7')
                    {
                        var oct = n - '0';
                        for (var k = 0; k < 2 && i + 1 < s.Length && s[i + 1] is >= '0' and <= '7'; k++)
                            oct = oct * 8 + (s[++i] - '0');
                        sb.Append((char)oct);
                    }
                    else sb.Append(n);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Remove text show operators whose every shown code is a certain .notdef
    /// reference: a control-range byte (&lt; 0x20) that the font's /Encoding maps to
    /// no glyph name. PDF/A prohibits any reference to the .notdef glyph; the
    /// violation is logged always, the operator is deleted only when
    /// <paramref name="strip"/> (ConvertErrorAction.Delete). Composite (Type0)
    /// fonts use multi-byte codes and are skipped. Applies to the page content
    /// and, recursively, to every reachable Form XObject.
    /// </summary>
    private void RemoveNotdefGlyphShows(Page page, PdfFormatConversionOptions options, bool strip)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;

        var pageBytes = page.GetContentStreamBytes();
        if (pageBytes is { Length: > 0 }
            && RewriteNotdefShows(pageBytes, resources, options, page.Number, strip) is { } rewritten)
            page.SetContentStream(rewritten);

        RemoveNotdefGlyphShowsInForms(resources, options, page.Number, strip,
            new HashSet<PdfDictionary>());
    }

    private void RemoveNotdefGlyphShowsInForms(PdfDictionary resources,
        PdfFormatConversionOptions options, int pageNumber, bool strip, HashSet<PdfDictionary> visited)
    {
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var key in xobjects.Keys.ToList())
        {
            if (_reader.Resolve(xobjects.Get(key)) is not PdfStream form
                || form.Dict.GetName("Subtype") != "Form"
                || !visited.Add(form.Dict))
                continue;
            var formRes = _reader.ResolveDict(form.Dict.Get("Resources")) ?? resources;
            byte[] data;
            try { data = _reader.DecodeStream(form); } catch { continue; }
            if (RewriteNotdefShows(data, formRes, options, pageNumber, strip) is { } rewritten)
            {
                form.Dict.Remove("Filter");
                form.Dict.Remove("DecodeParms");
                form.Dict.Set("Length", new PdfInteger(rewritten.Length));
                form.ReplaceData(rewritten);
            }
            RemoveNotdefGlyphShowsInForms(formRes, options, pageNumber, strip, visited);
        }
    }

    /// <summary>Token-level scan of one content stream for Tj / TJ operators whose every
    /// shown byte is an unmapped control code (a certain .notdef reference). Logs one
    /// violation per offending operator; when <paramref name="strip"/>, splices the
    /// operator (operands included) out of the stream. Returns null when nothing changed
    /// (or the stream carries inline images, whose binary payload this tokenizer does
    /// not model).</summary>
    private byte[]? RewriteNotdefShows(byte[] contentBytes, PdfDictionary resources,
        PdfFormatConversionOptions options, int pageNumber, bool strip)
    {
        var fonts = _reader.ResolveDict(resources.Get("Font"));
        if (fonts is null) return null;

        // code→glyph-name table per font resource; null = skip the font. Only Type1
        // faces qualify: their glyph lookup is NAME-keyed through the encoding, so a
        // control code with no name is a certain .notdef reference. A TrueType font
        // (esp. a subset with no /Encoding) addresses glyphs through its internal
        // cmap where low codes can be REAL glyphs, and composite (Type0) fonts use
        // multi-byte codes — no verdict is possible from the font dict alone.
        var encodings = new Dictionary<string, string?[]?>(StringComparer.Ordinal);
        string?[]? EncodingFor(string fontName)
        {
            if (encodings.TryGetValue(fontName, out var cached)) return cached;
            string?[]? names = null;
            if (_reader.ResolveDict(fonts.Get(fontName)) is { } fontDict
                && fontDict.GetName("Subtype") is "Type1" or "MMType1")
                names = Devices.SoftwarePageRenderer.ResolveEncoding(fontDict, _reader);
            encodings[fontName] = names;
            return names;
        }

        var text = System.Text.Encoding.Latin1.GetString(contentBytes);
        var deletions = new List<(int start, int end)>();
        string? lastName = null;      // most recent /Name token (Tf operand)
        string? currentFont = null;   // font selected by the last Tf
        int operandStart = -1;        // offset of the first operand token since the last operator
        var strings = new List<byte[]>(); // string operands gathered since the last operator
        var pos = 0;

        void BeginOperand(int at) { if (operandStart < 0) operandStart = at; }
        void EndOperator() { operandStart = -1; strings.Clear(); }

        while (pos < text.Length)
        {
            var c = text[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c is '[' or ']' or '{' or '}') { BeginOperand(pos); pos++; continue; }
            if (c == '%') // comment to end-of-line
            {
                while (pos < text.Length && text[pos] != '\n' && text[pos] != '\r') pos++;
                continue;
            }
            if (c == '(') // literal string, with escapes and balanced parens
            {
                BeginOperand(pos);
                var end = pos + 1;
                var depth = 1;
                while (end < text.Length && depth > 0)
                {
                    var sc = text[end];
                    if (sc == '\\') end++;
                    else if (sc == '(') depth++;
                    else if (sc == ')') depth--;
                    end++;
                }
                strings.Add(DecodeLiteralStringBytes(text, pos + 1, end - 1));
                pos = end; continue;
            }
            if (c == '<')
            {
                if (pos + 1 < text.Length && text[pos + 1] == '<') // dict
                { BeginOperand(pos); pos += 2; continue; }
                BeginOperand(pos);
                var end = text.IndexOf('>', pos + 1);
                if (end < 0) end = text.Length - 1;
                strings.Add(DecodeHexStringBytes(text, pos + 1, end));
                pos = end + 1; continue;
            }
            if (c == '>' && pos + 1 < text.Length && text[pos + 1] == '>')
            { BeginOperand(pos); pos += 2; continue; }
            if (c == '/') // name token
            {
                BeginOperand(pos);
                var end = pos + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                    end++;
                lastName = text[(pos + 1)..end];
                pos = end; continue;
            }

            // Regular token (number or operator).
            {
                var end = pos;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                    end++;
                // A stray delimiter byte (an unbalanced ')' or a lone '>') yields an
                // empty token with end == pos — skip the byte or the scan never advances.
                if (end == pos) { pos++; continue; }
                var token = text[pos..end];
                var isNumber = char.IsAsciiDigit(token[0]) || token[0] is '+' or '-' or '.';
                if (isNumber) { BeginOperand(pos); pos = end; continue; }

                switch (token)
                {
                    case "BI":
                        return null; // inline image: bail out, keep original bytes
                    case "Tf":
                        currentFont = lastName;
                        break;
                    case "Tj" or "TJ" when strings.Count > 0 && currentFont is not null
                        && EncodingFor(currentFont) is { } names:
                    {
                        var sawCode = false;
                        var allNotdef = true;
                        foreach (var s in strings)
                            foreach (var b in s)
                            {
                                sawCode = true;
                                if (b >= 0x20 || names[b] is not (null or ".notdef"))
                                { allNotdef = false; break; }
                            }
                        if (sawCode && allNotdef)
                        {
                            options.ConversionLog.Add(new PdfAViolation
                            {
                                Rule = "NotdefGlyph",
                                Description = $"Page {pageNumber} text show operator references only the .notdef glyph"
                                    + (strip ? " — operator removed." : "."),
                                PageNumber = pageNumber,
                            });
                            if (strip && operandStart >= 0)
                                deletions.Add((operandStart, end));
                        }
                        break;
                    }
                }
                EndOperator();
                pos = end;
            }
        }

        if (deletions.Count == 0) return null;

        var output = new List<byte>(contentBytes.Length);
        var copyFrom = 0;
        foreach (var (start, end) in deletions)
        {
            for (var i = copyFrom; i < start; i++) output.Add(contentBytes[i]);
            output.Add((byte)' '); // keep neighbouring tokens separated
            copyFrom = end;
        }
        for (var i = copyFrom; i < contentBytes.Length; i++) output.Add(contentBytes[i]);
        return output.ToArray();
    }

    /// <summary>Decode the raw bytes of a literal PDF string body (between the outer
    /// parens, exclusive) — escapes and octal sequences per PDF 32000 §7.3.4.2.</summary>
    private static byte[] DecodeLiteralStringBytes(string text, int start, int end)
    {
        var bytes = new List<byte>(end - start);
        for (var i = start; i < end && i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != '\\') { bytes.Add((byte)ch); continue; }
            if (++i >= end) break;
            var e = text[i];
            switch (e)
            {
                case 'n': bytes.Add((byte)'\n'); break;
                case 'r': bytes.Add((byte)'\r'); break;
                case 't': bytes.Add((byte)'\t'); break;
                case 'b': bytes.Add((byte)'\b'); break;
                case 'f': bytes.Add((byte)'\f'); break;
                case '\r': if (i + 1 < end && text[i + 1] == '\n') i++; break; // line continuation
                case '\n': break;
                case >= '0' and <= '7':
                {
                    var oct = e - '0';
                    for (var k = 0; k < 2 && i + 1 < end && text[i + 1] is >= '0' and <= '7'; k++)
                        oct = oct * 8 + (text[++i] - '0');
                    bytes.Add((byte)oct);
                    break;
                }
                default: bytes.Add((byte)e); break;
            }
        }
        return bytes.ToArray();
    }

    /// <summary>Decode the raw bytes of a hex PDF string body (between &lt; and &gt;,
    /// exclusive); an odd trailing digit is padded with 0 per PDF 32000 §7.3.4.3.</summary>
    private static byte[] DecodeHexStringBytes(string text, int start, int end)
    {
        var bytes = new List<byte>((end - start) / 2 + 1);
        var hi = -1;
        for (var i = start; i < end && i < text.Length; i++)
        {
            var ch = text[i];
            var v = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'A' and <= 'F' => ch - 'A' + 10,
                >= 'a' and <= 'f' => ch - 'a' + 10,
                _ => -1,
            };
            if (v < 0) continue;
            if (hi < 0) hi = v;
            else { bytes.Add((byte)(hi * 16 + v)); hi = -1; }
        }
        if (hi >= 0) bytes.Add((byte)(hi * 16));
        return bytes.ToArray();
    }

    /// <summary>
    /// Rewrite paint operators executed under a FULLY transparent graphics state
    /// (ExtGState /ca 0 for fills, /CA 0 for strokes) into no-ops, so the PDF/A-1
    /// alpha neutralisation (ca/CA → 1) cannot turn invisible content into opaque
    /// paint. Applies to the page content and, recursively, to every reachable
    /// Form XObject (each against its own resources). Streams with inline images
    /// keep their bytes untouched.
    /// </summary>
    private void SuppressAlphaZeroPaint(Page page)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;
        var visited = new HashSet<PdfDictionary>();

        var pageBytes = page.GetContentStreamBytes();
        if (pageBytes is { Length: > 0 }
            && RewriteAlphaZeroPaint(pageBytes, resources) is { } rewritten)
            page.SetContentStream(rewritten);

        SuppressAlphaZeroPaintInForms(resources, visited);
    }

    private void SuppressAlphaZeroPaintInForms(PdfDictionary resources, HashSet<PdfDictionary> visited)
    {
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var key in xobjects.Keys.ToList())
        {
            if (_reader.Resolve(xobjects.Get(key)) is not PdfStream form
                || form.Dict.GetName("Subtype") != "Form"
                || !visited.Add(form.Dict))
                continue;
            var formRes = _reader.ResolveDict(form.Dict.Get("Resources")) ?? resources;
            byte[] data;
            try { data = _reader.DecodeStream(form); } catch { continue; }
            if (RewriteAlphaZeroPaint(data, formRes) is { } rewritten)
            {
                form.Dict.Remove("Filter");
                form.Dict.Remove("DecodeParms");
                form.Dict.Set("Length", new PdfInteger(rewritten.Length));
                form.ReplaceData(rewritten);
            }
            SuppressAlphaZeroPaintInForms(formRes, visited);
        }
    }

    /// <summary>Token-level rewrite of one content stream: paint operators active under
    /// an alpha-0 ExtGState become <c>n</c> (or drop just the dead half of a fill+stroke).
    /// Returns null when nothing needed changing (or the stream carries inline images,
    /// whose binary payload this tokenizer does not model).</summary>
    private byte[]? RewriteAlphaZeroPaint(byte[] contentBytes, PdfDictionary resources)
    {
        var extGStates = _reader.ResolveDict(resources.Get("ExtGState"));
        if (extGStates is null) return null;

        var fillZero = new HashSet<string>(StringComparer.Ordinal);
        var strokeZero = new HashSet<string>(StringComparer.Ordinal);
        var fillSet = new HashSet<string>(StringComparer.Ordinal);   // gs entries that SET ca (any value)
        var strokeSet = new HashSet<string>(StringComparer.Ordinal); // gs entries that SET CA
        foreach (var key in extGStates.Keys)
        {
            var gs = _reader.ResolveDict(extGStates.Get(key));
            if (gs is null) continue;
            if (gs.Get("ca") is not null)
            {
                fillSet.Add(key);
                if (AlphaValue(gs.Get("ca")) == 0.0) fillZero.Add(key);
            }
            if (gs.Get("CA") is not null)
            {
                strokeSet.Add(key);
                if (AlphaValue(gs.Get("CA")) == 0.0) strokeZero.Add(key);
            }
        }
        if (fillZero.Count == 0 && strokeZero.Count == 0) return null;

        var text = System.Text.Encoding.Latin1.GetString(contentBytes);
        var output = new StringBuilder(text.Length);
        var stack = new Stack<(bool fill0, bool stroke0)>();
        bool fill0 = false, stroke0 = false;
        string? lastName = null;
        var changed = false;
        var pos = 0;

        while (pos < text.Length)
        {
            var c = text[pos];
            // Delimiters and non-token content are copied verbatim.
            if (char.IsWhiteSpace(c) || c is '[' or ']' or '{' or '}')
            { output.Append(c); pos++; continue; }
            if (c == '%') // comment to end-of-line
            {
                var eol = pos;
                while (eol < text.Length && text[eol] != '\n' && text[eol] != '\r') eol++;
                output.Append(text, pos, eol - pos); pos = eol; continue;
            }
            if (c == '(') // literal string, with escapes and balanced parens
            {
                var end = pos + 1;
                var depth = 1;
                while (end < text.Length && depth > 0)
                {
                    var sc = text[end];
                    if (sc == '\\') end++;
                    else if (sc == '(') depth++;
                    else if (sc == ')') depth--;
                    end++;
                }
                output.Append(text, pos, end - pos); pos = end; continue;
            }
            if (c == '<')
            {
                if (pos + 1 < text.Length && text[pos + 1] == '<') // dict
                { output.Append("<<"); pos += 2; continue; }
                var end = text.IndexOf('>', pos + 1);
                if (end < 0) end = text.Length - 1;
                output.Append(text, pos, end - pos + 1); pos = end + 1; continue;
            }
            if (c == '>' && pos + 1 < text.Length && text[pos + 1] == '>')
            { output.Append(">>"); pos += 2; continue; }
            if (c == '/') // name token
            {
                var end = pos + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                    end++;
                lastName = text[(pos + 1)..end];
                output.Append(text, pos, end - pos); pos = end; continue;
            }

            // Regular token (number or operator).
            {
                var end = pos;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                    end++;
                var token = text[pos..end];
                string? replacement = null;
                switch (token)
                {
                    case "BI":
                        return null; // inline image: bail out, keep original bytes
                    case "q":
                        stack.Push((fill0, stroke0));
                        break;
                    case "Q":
                        if (stack.Count > 0) (fill0, stroke0) = stack.Pop();
                        break;
                    case "gs" when lastName is not null:
                        if (fillZero.Contains(lastName)) fill0 = true;
                        else if (fillSet.Contains(lastName)) fill0 = false;
                        if (strokeZero.Contains(lastName)) stroke0 = true;
                        else if (strokeSet.Contains(lastName)) stroke0 = false;
                        break;
                    case "f" or "F" or "f*" when fill0:
                    case "S" or "s" when stroke0:
                    case "B" or "B*" or "b" or "b*" when fill0 && stroke0:
                        replacement = "n";
                        break;
                    case "B" or "B*" when fill0: replacement = "S"; break;
                    case "b" or "b*" when fill0: replacement = "s"; break;
                    case "B" or "B*" or "b" or "b*" when stroke0: replacement = "f"; break;
                }
                if (replacement is not null) { output.Append(replacement); changed = true; }
                else output.Append(token);
                pos = end;
            }
        }

        return changed ? System.Text.Encoding.Latin1.GetBytes(output.ToString()) : null;
    }

    /// <summary>
    /// Neutralise transparency declared in graphics-state (ExtGState) dictionaries reachable
    /// from <paramref name="container"/> (a page or Form XObject): soft masks, constant alpha
    /// below 1, and non-Normal blend modes are all prohibited by PDF/A-1. Soft masks are set
    /// to /None, alpha to 1 and blend mode to /Normal so the content renders opaquely instead
    /// of failing validation. Recurses into nested Form XObjects; the visited set guards
    /// against shared dictionaries and reference cycles.
    /// </summary>
    private void NeutralizeExtGStateTransparency(PdfDictionary container,
        PdfFormatConversionOptions options, int pageNumber, bool fix, HashSet<PdfDictionary> visited)
    {
        var resources = _reader.ResolveDict(container.Get("Resources"));
        if (resources is null) return;

        var extGStates = _reader.ResolveDict(resources.Get("ExtGState"));
        if (extGStates is not null)
        {
            foreach (var key in extGStates.Keys.ToList())
            {
                var gs = _reader.ResolveDict(extGStates.Get(key));
                if (gs is null || !visited.Add(gs)) continue;

                var changed = false;
                var smask = gs.Get("SMask");
                if (smask is not null && smask is not PdfName { Value: "None" })
                {
                    changed = true;
                    if (fix) gs.Set("SMask", new PdfName("None"));
                }
                if (IsAlphaBelowOne(gs.Get("ca")))
                {
                    changed = true;
                    if (fix) gs.Set("ca", new PdfReal(1));
                }
                if (IsAlphaBelowOne(gs.Get("CA")))
                {
                    changed = true;
                    if (fix) gs.Set("CA", new PdfReal(1));
                }
                var bm = gs.GetName("BM");
                if (bm is not null && bm != "Normal" && bm != "Compatible")
                {
                    changed = true;
                    if (fix) gs.Set("BM", new PdfName("Normal"));
                }

                if (changed)
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "Transparency",
                        Description = $"Page {pageNumber} ExtGState '{key}' transparency neutralized for PDF/A-1.",
                        PageNumber = pageNumber,
                    });
            }
        }

        // Recurse into Form XObjects, whose own resources may carry transparency.
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var key in xobjects.Keys.ToList())
        {
            if (_reader.Resolve(xobjects.Get(key)) is PdfStream { } form
                && form.Dict.GetName("Subtype") == "Form"
                && visited.Add(form.Dict))
            {
                NeutralizeExtGStateTransparency(form.Dict, options, pageNumber, fix, visited);
            }
        }
    }

    private static bool IsAlphaBelowOne(PdfObject? value) => value switch
    {
        PdfReal r => r.Value < 1.0,
        PdfInteger i => i.Value < 1,
        _ => false,
    };

    private static double AlphaValue(PdfObject? value) => value switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => 1.0,
    };

    /// <summary>Walk a content stream tracking the current fill alpha (set by <c>/GS gs</c>
    /// against the resources' ExtGState /ca, saved/restored by q/Q) and, for every image
    /// XObject drawn while that alpha is below 1, bake the alpha into a constant DeviceGray
    /// soft mask on the image (unless it already carries a mask). This preserves the image's
    /// composited appearance once the prohibited ExtGState alpha is neutralised for PDF/A-1.
    /// Recurses into invoked Form XObjects, carrying the alpha active at their draw.</summary>
    private void MaskConstantAlphaImages(byte[] content, PdfDictionary resources,
        double initialAlpha, HashSet<PdfDictionary> visitedForms)
    {
        var extg = _reader.ResolveDict(resources.Get("ExtGState"));
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));

        var lexer = new IO.PdfLexer(content);
        var stack = new Stack<double>();
        var curAlpha = initialAlpha;
        string? lastName = null;
        // Form name -> the alpha active where it was invoked (last wins; a form drawn only
        // opaquely stays opaque). Recursed after the scan so lexer state is untouched.
        var formAlpha = new Dictionary<string, double>(StringComparer.Ordinal);

        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == IO.TokenKind.Eof) break;
            if (t.Kind == IO.TokenKind.Keyword && t.StringValue == "BI")
            {
                SkipInlineImage(lexer, new HashSet<string>());
                lastName = null;
                continue;
            }
            if (t.Kind == IO.TokenKind.Name) { lastName = t.StringValue; continue; }
            if (t.Kind != IO.TokenKind.Keyword) continue;

            switch (t.StringValue)
            {
                case "q":
                    stack.Push(curAlpha);
                    break;
                case "Q":
                    if (stack.Count > 0) curAlpha = stack.Pop();
                    break;
                case "gs":
                    if (lastName is not null && extg is not null &&
                        _reader.ResolveDict(extg.Get(lastName)) is { } gs)
                        curAlpha = AlphaValue(gs.Get("ca"));
                    break;
                case "Do":
                    if (lastName is not null && xobjects is not null &&
                        _reader.ResolveStream(xobjects.Get(lastName)) is { } xs)
                    {
                        var sub = xs.Dict.GetName("Subtype");
                        if (sub == "Image")
                        {
                            if (curAlpha < 1.0 - 1e-6) AttachConstantSoftMask(xs.Dict, curAlpha);
                        }
                        else if (sub == "Form" && curAlpha < 1.0 - 1e-6)
                        {
                            formAlpha[lastName] = curAlpha;
                        }
                    }
                    break;
            }
            lastName = null;
        }

        if (xobjects is null) return;
        foreach (var (name, alpha) in formAlpha)
        {
            var xs = _reader.ResolveStream(xobjects.Get(name));
            if (xs is null || xs.Dict.GetName("Subtype") != "Form") continue;
            if (!visitedForms.Add(xs.Dict)) continue;
            var formContent = _reader.DecodeStream(xs);
            if (formContent.Length == 0) continue;
            var formRes = _reader.ResolveDict(xs.Dict.Get("Resources")) ?? resources;
            MaskConstantAlphaImages(formContent, formRes, alpha, visitedForms);
        }
    }

    /// <summary>Attach a 1×1 constant DeviceGray <c>/SMask</c> of value <paramref name="alpha"/>
    /// to an image XObject so it composites at that opacity. No-op if the image already carries
    /// a soft mask or stencil mask (its existing transparency is preserved as-is).</summary>
    private void AttachConstantSoftMask(PdfDictionary imgDict, double alpha)
    {
        if (imgDict.Get("SMask") is not null || imgDict.Get("Mask") is not null) return;

        var smDict = new PdfDictionary();
        smDict.Set("Type", new PdfName("XObject"));
        smDict.Set("Subtype", new PdfName("Image"));
        smDict.Set("Width", new PdfInteger(1));
        smDict.Set("Height", new PdfInteger(1));
        smDict.Set("ColorSpace", new PdfName("DeviceGray"));
        smDict.Set("BitsPerComponent", new PdfInteger(8));
        var data = new byte[] { (byte)Math.Round(Math.Clamp(alpha, 0.0, 1.0) * 255.0) };
        smDict.Set("Length", new PdfInteger(data.Length));

        var objNum = AllocateObjectNumber();
        AddNewObject(objNum, new PdfStream(smDict, data));
        imgDict.Set("SMask", new PdfIndirectRef(objNum, 0));
    }

    /// <summary>Strip the document-level JavaScript name tree (<c>/Names /JavaScript</c>),
    /// logging it as removed prohibited content. The emptied /Names dictionary is dropped.</summary>
    private void RemoveDocumentJavaScript(PdfFormatConversionOptions options)
    {
        var names = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        if (names?.Get("JavaScript") is null) return;
        options.ConversionLog.Add(new PdfAViolation
        {
            Rule = "JavaScript",
            Description = "Document-level JavaScript is not allowed in the target format",
        });
        names.Remove("JavaScript");
        if (!names.Keys.Any())
            _reader.Catalog.Remove("Names");
    }

    private void RemoveProhibitedCatalogActions(PdfFormatConversionOptions options, bool strip)
    {
        // Check OpenAction
        var openActionObj = _reader.Catalog.Get("OpenAction");
        if (openActionObj is not null)
        {
            var openAction = _reader.ResolveDict(openActionObj);
            if (openAction is not null)
            {
                var actionType = openAction.GetName("S");
                if (actionType is not null && ConvertProhibitedActionTypes.Contains(actionType))
                {
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "ActionType",
                        Description = $"Action type '{actionType}' is not allowed in PDF/A",
                    });
                    if (strip)
                    {
                        _reader.Catalog.Remove("OpenAction");
                    }
                }
            }
        }

        // Check AA (Additional Actions) on catalog
        var aa = _reader.ResolveDict(_reader.Catalog.Get("AA"));
        if (aa is not null)
        {
            var keysToRemove = new List<string>();
            foreach (var key in aa.Keys)
            {
                var actionDict = _reader.ResolveDict(aa.Get(key));
                if (actionDict is null) continue;
                var actionType = actionDict.GetName("S");
                if (actionType is not null && ConvertProhibitedActionTypes.Contains(actionType))
                {
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "ActionType",
                        Description = $"Action type '{actionType}' is not allowed in PDF/A",
                    });
                    keysToRemove.Add(key);
                }
            }
            if (strip)
            {
                foreach (var key in keysToRemove)
                    aa.Remove(key);
                if (!aa.Keys.Any())
                    _reader.Catalog.Remove("AA");
            }
        }
    }

    private void FixAnnotationsForPdfA(Page page, PdfFormatConversionOptions options, bool fix, bool strip)
    {
        var annotsObj = _reader.Resolve(page.Dict.Get("Annots"));
        if (annotsObj is not PdfArray annotsArr) return;

        // PDF/A-1 prohibits the PDF 1.5+ annotation subtypes that later parts allow.
        var isPdfA1 = options.Format is PdfFormat.PDF_A_1A or PdfFormat.PDF_A_1B;

        var indicesToRemove = new List<int>();

        for (var i = 0; i < annotsArr.Count; i++)
        {
            var annotDict = _reader.ResolveDict(annotsArr[i]);
            if (annotDict is null) continue;

            var subtype = annotDict.GetName("Subtype");

            // PDF/A-4f (ISO 19005-4) permits embedded files, so FileAttachment
            // annotations stay as authored — no violation, no stripping.
            var allowedByPart = subtype == "FileAttachment" && options.Format is PdfFormat.PDF_A_4F;

            // Check prohibited subtypes
            if (subtype is not null && !allowedByPart &&
                (ConvertProhibitedAnnotationSubtypes.Contains(subtype) ||
                 (isPdfA1 && PdfA1ProhibitedAnnotationSubtypes.Contains(subtype))))
            {
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "AnnotationType",
                    Description = $"Annotation type '{subtype}' is not allowed in PDF/A",
                    PageNumber = page.Number,
                });
                if (strip)
                {
                    // PDF/A-4: a stripped FileAttachment's payload survives as a
                    // document embedded file (the conversion migrates the attachments
                    // there). Plain part 4 restricts attachments to PDF documents, so
                    // non-PDF payloads drop with the annotation; 4e takes any file type.
                    if (subtype == "FileAttachment"
                        && options.Format is PdfFormat.PDF_A_4 or PdfFormat.PDF_A_4E)
                        MigrateFileAttachmentToEmbeddedFiles(annotDict,
                            pdfOnly: options.Format is PdfFormat.PDF_A_4);
                    indicesToRemove.Add(i);
                }
                continue;
            }

            // Fix Print flag (bit 3, value 4) — except for Widget/Popup
            if (subtype != "Widget" && subtype != "Popup")
            {
                var flags = (int)annotDict.GetInt("F");
                if ((flags & 4) == 0)
                {
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "AnnotationPrintFlag",
                        Description = $"Annotation (type '{subtype ?? "unknown"}') missing Print flag",
                        PageNumber = page.Number,
                    });
                    if (fix)
                    {
                        annotDict.Set("F", new PdfInteger(flags | 4));
                    }
                }
            }

            // Check/remove prohibited actions on annotations
            var actionObj = _reader.ResolveDict(annotDict.Get("A"));
            if (actionObj is not null)
            {
                var actionType = actionObj.GetName("S");
                if (actionType is not null && ConvertProhibitedActionTypes.Contains(actionType))
                {
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "ActionType",
                        Description = $"Action type '{actionType}' is not allowed in PDF/A",
                        PageNumber = page.Number,
                    });
                    if (strip)
                    {
                        annotDict.Remove("A");
                    }
                }
            }

            // Check/remove AA on annotations
            var annotAa = _reader.ResolveDict(annotDict.Get("AA"));
            if (annotAa is not null)
            {
                var hasProhibited = false;
                foreach (var key in annotAa.Keys)
                {
                    var ad = _reader.ResolveDict(annotAa.Get(key));
                    if (ad is null) continue;
                    var at = ad.GetName("S");
                    if (at is not null && ConvertProhibitedActionTypes.Contains(at))
                    {
                        hasProhibited = true;
                        options.ConversionLog.Add(new PdfAViolation
                        {
                            Rule = "ActionType",
                            Description = $"Action type '{at}' is not allowed in PDF/A",
                            PageNumber = page.Number,
                        });
                    }
                }
                if (strip && hasProhibited)
                {
                    annotDict.Remove("AA");
                }
            }
        }

        // Remove prohibited annotations (reverse order to preserve indices)
        if (strip && indicesToRemove.Count > 0)
        {
            for (var i = indicesToRemove.Count - 1; i >= 0; i--)
            {
                annotsArr.RemoveAt(indicesToRemove[i]);
            }
            if (annotsArr.Count == 0)
                page.Dict.Remove("Annots");
        }
    }

    private bool HasOutputIntent()
    {
        var outputIntents = _reader.Resolve(_reader.Catalog.Get("OutputIntents"));
        return outputIntents is PdfArray { Count: > 0 };
    }

    /// <summary>True when /OutputIntents already carries a /GTS_PDFA1 intent.
    /// The PDF/A conversion must add one otherwise — a source with only a PDF/X
    /// (GTS_PDFX) intent still fails the validator's PDF/A output-intent gate.</summary>
    private bool HasPdfAOutputIntentInCatalog()
    {
        if (_reader.Resolve(_reader.Catalog.Get("OutputIntents")) is not PdfArray arr)
            return false;
        foreach (var item in arr)
            if (_reader.ResolveDict(item)?.GetName("S") == "GTS_PDFA1")
                return true;
        return false;
    }

    private static bool PageHasDeviceDependentParagraphs(Page page)
    {
        // DOM paragraphs flushed by Save() that emit a DeviceRGB/CMYK/Gray image XObject.
        // Walks user-added paragraphs (page.Paragraphs) — Image, ImageStamp.
        foreach (var p in page.Paragraphs)
        {
            switch (p)
            {
                case Image:
                case ImageStamp:
                    return true;
            }
        }
        return false;
    }

    private bool PageHasDeviceDependentColors(Page page)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return false;

        var xObjectDict = _reader.ResolveDict(resources.Get("XObject"));
        if (xObjectDict is null) return false;

        foreach (var xobjKey in xObjectDict.Keys)
        {
            var xobj = _reader.Resolve(xObjectDict.Get(xobjKey));

            PdfDictionary? xobjDict = null;
            if (xobj is PdfStream stream)
                xobjDict = stream.Dict;
            else if (xobj is PdfDictionary dict)
                xobjDict = dict;

            if (xobjDict is null) continue;

            var subtype = xobjDict.GetName("Subtype");
            if (subtype != "Image") continue;

            var csObj = xobjDict.Get("ColorSpace");
            if (csObj is PdfName csName && csName.Value is "DeviceRGB" or "DeviceCMYK" or "DeviceGray")
            {
                return true;
            }
        }

        return false;
    }

    private void AddPdfXOutputIntent(PdfFormatConversionOptions options)
    {
        var outputIntentDict = new PdfDictionary();
        outputIntentDict.Set("Type", new PdfName("OutputIntent"));
        outputIntentDict.Set("S", new PdfName("GTS_PDFX"));

        var oci = options.OutputIntent?.OutputConditionIdentifier ?? "Custom";
        outputIntentDict.Set("OutputConditionIdentifier",
            new PdfString(Encoding.Latin1.GetBytes(oci)));
        outputIntentDict.Set("RegistryName",
            new PdfString(Encoding.Latin1.GetBytes("http://www.color.org")));

        // Embed ICC profile if provided
        if (options.IccProfileFileName is not null && File.Exists(options.IccProfileFileName))
        {
            var iccData = File.ReadAllBytes(options.IccProfileFileName);
            var iccDict = new PdfDictionary();
            iccDict.Set("N", new PdfInteger(4)); // CMYK = 4 components
            iccDict.Set("Length", new PdfInteger(iccData.Length));
            var iccStream = new PdfStream(iccDict, iccData);
            var iccObjNum = AllocateObjectNumber();
            AddNewObject(iccObjNum, iccStream);
            outputIntentDict.Set("DestOutputProfile", new PdfIndirectRef(iccObjNum, 0));
        }

        var outputIntents = _reader.Resolve(_reader.Catalog.Get("OutputIntents")) as PdfArray;
        if (outputIntents is null)
        {
            outputIntents = new PdfArray();
            _reader.Catalog.Set("OutputIntents", outputIntents);
        }
        // Held DIRECT in the array (spec-valid), like the PDF/A intent: a
        // validation right after Convert must see the /S /GTS_PDFX entry, and a
        // pending indirect object isn't reachable through the reader.
        outputIntents.Add(outputIntentDict);
    }

    private void AddSrgbOutputIntent()
    {
        var outputIntentDict = new PdfDictionary();
        outputIntentDict.Set("Type", new PdfName("OutputIntent"));
        outputIntentDict.Set("S", new PdfName("GTS_PDFA1"));
        outputIntentDict.Set("OutputConditionIdentifier",
            new PdfString(Encoding.Latin1.GetBytes("sRGB IEC61966-2.1")));
        outputIntentDict.Set("RegistryName",
            new PdfString(Encoding.Latin1.GetBytes("http://www.color.org")));

        var outputIntents = _reader.Resolve(_reader.Catalog.Get("OutputIntents")) as PdfArray;
        if (outputIntents is null)
        {
            outputIntents = new PdfArray();
            _reader.Catalog.Set("OutputIntents", outputIntents);
        }
        // Held DIRECT in the array (spec-valid) so an in-memory validation right
        // after Convert can see the /S /GTS_PDFA1 intent — a pending indirect
        // object isn't reachable through the reader until the document is saved.
        outputIntents.Add(outputIntentDict);
    }
}
