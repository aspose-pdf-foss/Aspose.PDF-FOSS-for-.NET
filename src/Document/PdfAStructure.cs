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

public sealed partial class Document
{
    // The standard structure types of PDF 1.4 (ISO 32000 / 19005-1 §6.8.3.4) —
    // a structure element whose /S is outside this set must role-map to one.
    private static readonly HashSet<string> StandardStructureTypes = new(StringComparer.Ordinal)
    {
        "Document", "Part", "Art", "Sect", "Div", "BlockQuote", "Caption", "TOC",
        "TOCI", "Index", "NonStruct", "Private", "P", "H", "H1", "H2", "H3", "H4",
        "H5", "H6", "L", "LI", "Lbl", "LBody", "Table", "TR", "TH", "TD", "Span",
        "Quote", "Note", "Reference", "BibEntry", "Code", "Link", "Figure",
        "Formula", "Form",
    };

    /// <summary>Report each distinct structure type used in the tree that is
    /// neither standard nor role-mapped (following /RoleMap chains) to a
    /// standard type — the clause-6.8.3.4 vocabulary.</summary>
    private void ReportUnmappedStructureTypes(PdfDictionary structRoot, PdfFormatConversionOptions options)
    {
        var roleMap = _reader.ResolveDict(structRoot.Get("RoleMap"));
        bool MapsToStandard(string type)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cur = type;
            while (seen.Add(cur))
            {
                if (StandardStructureTypes.Contains(cur)) return true;
                if (roleMap is null || roleMap.GetName(cur) is not { } mapped) return false;
                cur = mapped;
            }
            return false; // circular map never reaches a standard type
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        void Walk(PdfObject? node, int depth)
        {
            if (depth > 64 || _reader.ResolveDict(node) is not { } dict) return;
            if (dict.GetName("S") is { } s && !reported.Contains(s) && !MapsToStandard(s))
            {
                reported.Add(s);
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "StructureRoleMap",
                    Clause = "6.8.3.4",
                    Description = $"Non-standard structure type '{s}' not mapped to functionally equivalent standard type",
                });
            }
            var kids = _reader.Resolve(dict.Get("K"));
            if (kids is PdfArray arr)
                foreach (var k in arr) Walk(k, depth + 1);
            else if (kids is PdfDictionary or PdfIndirectRef)
                Walk(kids, depth + 1);
        }
        Walk(structRoot, 0);
    }

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
    /// <summary>Report (and under Delete remove) the /AS key of every
    /// optional-content configuration dictionary — prohibited by ISO 19005-2
    /// §6.9. One clause-6.9 problem per offending configuration.</summary>
    private void ReportProhibitedOptionalContentAS(PdfFormatConversionOptions options, bool strip)
    {
        var ocProps = _reader.ResolveDict(_reader.Catalog.Get("OCProperties"));
        if (ocProps is null) return;

        void CheckConfig(PdfObject? cfgObj)
        {
            if (_reader.ResolveDict(cfgObj) is not { } cfg || !cfg.ContainsKey("AS")) return;
            options.ConversionLog.Add(new Optimization.PdfAViolation
            {
                Rule = "OptionalContentAS",
                Clause = "6.9",
                Description = "The key 'AS' is prohibited for the optional content configuration dictionary",
                Convertable = strip,
            });
            if (strip) cfg.Remove("AS");
        }

        CheckConfig(ocProps.Get("D"));
        if (_reader.Resolve(ocProps.Get("Configs")) is PdfArray configs)
            foreach (var c in configs)
                CheckConfig(c);
    }

    private void ConvertEmbeddedPdfAttachmentsToPdfA2B(PdfFormatConversionOptions options, bool strip)
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
                // ISO 19005-2 §6.8/§6.9 allows only PDF/A attachments; a non-PDF
                // payload (image, data file, …) can't be made compliant. The log
                // carries the clause-6.8 error either way; ConvertErrorAction.Delete
                // removes the entry (2 array slots: name string + filespec) and the
                // conversion proceeds, while None keeps the file and the conversion
                // reports failure (the violation is unconvertable then).
                var entryName = (arr[i] as PdfString)?.ToText()
                    ?? (_reader.Resolve(arr[i]) as PdfString)?.ToText() ?? string.Empty;
                options.ConversionLog.Add(new Optimization.PdfAViolation
                {
                    Rule = "EmbeddedFileNotPdfA",
                    Clause = "6.8",
                    Description = $"Embedded file '{entryName}' can not be converted to PDF/A",
                    Convertable = strip,
                });
                if (strip)
                {
                    arr.RemoveAt(i + 1);
                    arr.RemoveAt(i);
                    _embeddedFiles = null; // collection re-materialises from the tree
                }
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

    /// <summary>PDF/UA-1: the logical structure hangs off a single Document
    /// root element. When a tagged document's structure tree has exactly one
    /// top-level element with a different role, retag it to Document. Trees
    /// with several top-level elements are left alone.</summary>
    /// <summary>PDF 2.0 deprecates the SHA-1 signature handlers (ISO 32000-2
    /// §12.8.3.2: adbe.pkcs7.sha1) and the raw PKCS#1 handler (adbe.x509.rsa_sha1):
    /// a v_2_0 conversion unlinks such a signature from its field — the field's /V
    /// is removed so the field reads as unsigned, while the orphaned signature
    /// dictionary itself is left behind (measured: the
    /// output still carries the /Type/Sig dict but the field's /V is
    /// gone and Signature reads null after reload). Removal is unconditional —
    /// these are stripped under the default ErrorAction too.</summary>
    private void RemoveDeprecatedSignatureHandlers()
    {
        var form = Form;
        if (form is null) return;
        foreach (var field in form.Fields)
        {
            if (field.Type != FieldType.Signature) continue;
            var sigDict = _reader.ResolveDict(field.Dict.Get("V"));
            if (sigDict is null) continue;
            var subFilter = sigDict.GetName("SubFilter");
            if (subFilter is "adbe.pkcs7.sha1" or "adbe.x509.rsa_sha1")
                field.Dict.Remove("V");
        }
    }

    private void RetagStructRootAsDocument()
    {
        var structRoot = _reader.ResolveDict(_reader.Catalog.Get("StructTreeRoot"));
        if (structRoot is null) return;
        var k = _reader.Resolve(structRoot.Get("K"));
        var rootElem = k switch
        {
            PdfDictionary d => d,
            PdfArray { Count: 1 } arr => _reader.Resolve(arr[0]) as PdfDictionary,
            _ => null,
        };
        if (rootElem is null) return;
        if (rootElem.GetName("Type") is not (null or "StructElem")) return;
        if (rootElem.GetName("S") != "Document")
            rootElem.Set("S", new PdfName("Document"));
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
                // ISO 19005-2 §6.8: for part 2 a file attachment is an EMBEDDED-FILES
                // violation (the log clause the corpus reads back is "6.8"), and one
                // the conversion can only repair by stripping — under
                // ConvertErrorAction.None the file stays and the conversion fails.
                var isA2FileAttachment = subtype == "FileAttachment"
                    && options.Format is PdfFormat.PDF_A_2A or PdfFormat.PDF_A_2B or PdfFormat.PDF_A_2U;
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "AnnotationType",
                    Description = $"Annotation type '{subtype}' is not allowed in PDF/A",
                    PageNumber = page.Number,
                    Clause = isA2FileAttachment ? "6.8" : null,
                    Convertable = !isA2FileAttachment || strip,
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

            // Check/remove prohibited actions on annotations. PDF/X (ISO 15930)
            // prohibits interactive behaviour outright — EVERY annotation /A is a
            // violation there, whatever its type; PDF/A prohibits only the
            // executable/media set.
            var isPdfXTarget = options.Format is PdfFormat.PDF_X_1A or PdfFormat.PDF_X_3 or PdfFormat.PDF_X_4;
            var actionObj = _reader.ResolveDict(annotDict.Get("A"));
            if (actionObj is not null)
            {
                var actionType = actionObj.GetName("S");
                if (actionType is not null && (isPdfXTarget || ConvertProhibitedActionTypes.Contains(actionType)))
                {
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "ActionType",
                        Description = isPdfXTarget
                            ? $"Annotation action '{actionType}' is not allowed in PDF/X"
                            : $"Action type '{actionType}' is not allowed in PDF/A",
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

        // X-4's default condition is CGATS TR001 (probed 2026-08-28: converting
        // with no explicit OutputIntent writes OCI "CGATS TR001"); the older X
        // profiles keep the generic placeholder.
        var oci = options.OutputIntent?.OutputConditionIdentifier
            ?? (options.TargetFormat == PdfFormat.PDF_X_4 ? "CGATS TR001" : "Custom");
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

    /// <summary>The page's /Resources dict, walking /Parent inheritance when the
    /// leaf carries none (PDF 32000 §7.7.3.4).</summary>
    private PdfDictionary? ResolveInheritedResources(PdfDictionary pageDict)
    {
        var node = pageDict;
        for (var depth = 0; node is not null && depth < 32; depth++)
        {
            if (_reader.ResolveDict(node.Get("Resources")) is { } res) return res;
            node = _reader.ResolveDict(node.Get("Parent"));
        }
        return null;
    }

    // The Info↔XMP descriptive mirror pairs whose text the conversion sanitizes.
    private static readonly string[] MirroredXmpTextKeys =
        ["dc:title", "dc:creator", "dc:description", "pdf:Keywords"];

    /// <summary>Replace characters the XML 1.0 grammar forbids (C0 controls other
    /// than tab/LF/CR, and the two non-characters U+FFFE/U+FFFF) with a space.
    /// Null stays null; a clean string is returned unchanged.</summary>
    private static string? SanitizeXmlText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        System.Text.StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var invalid = (c < 0x20 && c is not ('\t' or '\n' or '\r')) || c >= 0xFFFE;
            if (invalid && sb is null) sb = new System.Text.StringBuilder(text, 0, i, text.Length);
            if (sb is not null) sb.Append(invalid ? ' ' : c);
        }
        return sb?.ToString() ?? text;
    }
}
