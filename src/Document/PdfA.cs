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
            RemoveDeprecatedSignatureHandlers();
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
            if (string.IsNullOrEmpty(uaMeta.Get("pdf:Producer"))) uaMeta.SetStamped("pdf:Producer", BuildVersionInfo.ProducerString);
            if (string.IsNullOrEmpty(uaMeta.Get("xmp:CreateDate")) && Info.CreationDate != DateTime.MinValue)
                uaMeta.Set("xmp:CreateDate", FormatXmpDate(Info.CreationDate, Info.CreationTimeZone));
            if (string.IsNullOrEmpty(uaMeta.Get("xmp:ModifyDate")) && Info.ModDate != DateTime.MinValue)
                uaMeta.Set("xmp:ModifyDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));
            if (_reader.Trailer.Get("ID") is null)
            {
                _forceWriteId = true;
                var feId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
                var feIdArr = new PdfArray();
                feIdArr.Add(new PdfString(feId, isHex: true));
                feIdArr.Add(new PdfString(feId, isHex: true));
                _reader.Trailer.Set("ID", feIdArr);
            }
            EmbedNonEmbeddedFonts(options, includeStandard14: true);
            if (options.AutoTaggingSettings is { EnableAutoTagging: true })
                Tagged.AutoTagger.Apply(this, options.AutoTaggingSettings);
            RetagStructRootAsDocument();
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

        var isPdfX = format is PdfFormat.PDF_X_1A or PdfFormat.PDF_X_3 or PdfFormat.PDF_X_4;

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
            PdfFormat.PDF_X_4 => ("X-4", ""),
            _ => (null, (string?)null),
        };

        if (part is null)
            return true; // Not a PDF/A or PDF/X format, nothing to do

        // Clause 6.2.11.8 (ISO 19005-2/-3): a font program's cmap shall not map a
        // used character to the .notdef glyph. For an Identity-encoded Type0 font
        // the code IS the CID, so a shown 2-byte code 0000 references .notdef
        // directly — reported one problem per font, on the first page seen
        // (probed: three CID fonts, ObjectID = the Type0 dict's object number,
        // name = BaseFont with the subset prefix stripped). Conversion proceeds —
        // the problem is informational (Convertable stays true).
        if (part is "2" or "3")
            ReportNotdefGlyphReferences(options);

        // ISO 19005-4 (clause 6.1.2): PDF/A-4 is defined over PDF 2.0 — a 1.x
        // document must be brought to 2.0 FIRST (Convert(v_2_0)), or the A-4
        // conversion refuses with the clause-6.1.2 error. The version the save
        // would stamp decides (a prior SetVersion("2.0") counts).
        if (part == "4"
            && string.Compare(_versionOverride ?? PdfVersion ?? "1.4", "2.0", StringComparison.Ordinal) < 0)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "PdfVersionForPart4",
                Clause = "6.1.2",
                Description = "PDF/A-4 requires a PDF 2.0 document; convert the document to PDF 2.0 first.",
                Convertable = false,
            });
            return false;
        }

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
                var v = meta.ContainsKey(key) ? meta.Get(key) : null;
                if (v is not null && SanitizeXmlText(v) is { } sv && sv != v) meta.Set(key, sv);
            }
            // Info.Title is "" (not null) when the source has no title, so "??" would
            // store an empty dc:title that the validator still flags as missing — fall
            // back to "Untitled" for null OR empty.
            if (needsTitle) meta.Set("dc:title", string.IsNullOrEmpty(Info.Title) ? "Untitled" : Info.Title);
            if (needsProducer) meta.SetStamped("pdf:Producer", BuildVersionInfo.ProducerString);

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
                var fileId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
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
            else if (format == PdfFormat.PDF_X_4)
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
            // The tagging the level-A conversion is about to synthesise REPAIRS real
            // violations of the source — the log must still carry them (the measured
            // vocabulary; the clause NUMBERS are part-dependent: ISO 19005-1
            // files these under 6.8.x, parts 2/3 under 6.7.x).
            var structRoot = _reader.ResolveDict(_reader.Catalog.Get("StructTreeRoot"));
            if (structRoot is null)
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "StructureTree",
                    Clause = part == "1" ? "6.8.3.3" : "6.7.3.3",
                    Description = "Catalog shall have struct tree root entry",
                });
            if (_reader.ResolveDict(_reader.Catalog.Get("MarkInfo")) is null)
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "MarkInfo",
                    Clause = part == "1" ? "6.8.2.2" : "6.7.2.2",
                    Description = "Catalog shall have MarkInfo entry",
                });
            // ISO 19005-1 §6.8.3.4: every non-standard structure type must be
            // role-mapped to a functionally equivalent standard type. One problem
            // per distinct unmapped type (probed: "Non-standard structure type
            // 'Article' not mapped to functionally equivalent standard type").
            if (part == "1" && structRoot is not null)
                ReportUnmappedStructureTypes(structRoot, options);
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
        {
            ConvertEmbeddedPdfAttachmentsToPdfA2B(options, strip);
            // ISO 19005-2 §6.9: an optional-content configuration dictionary
            // (the /OCProperties /D default and every /Configs entry) shall not
            // contain the /AS key. The log carries the clause-6.9 error with the
            // reference validator's exact vocabulary; Delete strips the key so
            // the output validates.
            ReportProhibitedOptionalContentAS(options, strip);
        }

        // 12c. Flat-colour DCT images (part 2+): a JPEG whose decoded samples use at
        // most 256 distinct colours gets an /Indexed /DeviceRGB palette colorspace.
        // The conversion emits a fixed 256-slot palette (hival 255) for
        // such an image regardless of the actual count - measured: a
        // 63-colour 900x253 flatten raster comes out [/Indexed/DeviceRGB 255 ...]
        // while its continuous-tone siblings (2205..5786 colours) stay DeviceRGB.
        // The samples are re-encoded as real 8-bit palette indices (Flate).
        if (fix && part is "2" or "3" or "4")
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

}
