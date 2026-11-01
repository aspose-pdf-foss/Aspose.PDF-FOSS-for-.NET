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
    /// <summary>The stages of the PDF/A annotation fix: one annotation.</summary>
    private bool FixAnnotationForPdfA(PdfAAnnotationFixState fa, PdfArray annotsArr, int i)
    {
        fa.annotDict = _reader.ResolveDict(annotsArr[i]);
        if (fa.annotDict is null) return true;

        fa.subtype = fa.annotDict.GetName("Subtype");

        // Parts 3 and 4f admit embedded files of any kind: a file attachment stays, and under
        // part 3 its file becomes an associated file of the document (ISO 19005-3 6.8).
        fa.allowedByPart = fa.subtype == "FileAttachment" && fa.options.Format is PdfFormat.PDF_A_4F
            or PdfFormat.PDF_A_3A or PdfFormat.PDF_A_3B or PdfFormat.PDF_A_3U;
        if (fa.allowedByPart && fa.fix && fa.options.Format is not PdfFormat.PDF_A_4F
            && fa.annotDict.Get("FS") is { } attachedFile)
            (_attachmentFileSpecs ??= new List<PdfObject>()).Add(attachedFile);

        // Check prohibited subtypes
        if (fa.subtype is not null && !fa.allowedByPart &&
            (ConvertProhibitedAnnotationSubtypes.Contains(fa.subtype) ||
             (fa.isPdfA1 && PdfA1ProhibitedAnnotationSubtypes.Contains(fa.subtype))))
        {
            // ISO 19005-2 §6.8: for part 2 a file attachment is an EMBEDDED-FILES
            // violation (the log clause the corpus reads back is "6.8"), and one
            // the conversion can only repair by stripping — under
            // ConvertErrorAction.None the file stays and the conversion fails.
            var isA2FileAttachment = fa.subtype == "FileAttachment"
                && fa.options.Format is PdfFormat.PDF_A_2A or PdfFormat.PDF_A_2B or PdfFormat.PDF_A_2U;
            fa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "AnnotationType",
                Description = $"Annotation type '{fa.subtype}' is not allowed in PDF/A",
                PageNumber = fa.page.Number,
                Clause = isA2FileAttachment ? "6.8" : null,
                Convertable = !isA2FileAttachment || fa.strip,
            });
            if (fa.strip)
            {
                // PDF/A-4: a stripped FileAttachment's payload survives as a
                // document embedded file (the conversion migrates the attachments
                // there). Plain part 4 restricts attachments to PDF documents, so
                // non-PDF payloads drop with the annotation; 4e takes any file type.
                // Part 2 embeds PDF/A files only, so it keeps the PDF payloads the same way;
                // the embedded-file pass that follows converts them with the others.
                if (fa.subtype == "FileAttachment"
                    && (isA2FileAttachment || fa.options.Format is PdfFormat.PDF_A_4 or PdfFormat.PDF_A_4E))
                    MigrateFileAttachmentToEmbeddedFiles(fa.annotDict,
                        pdfOnly: fa.options.Format is not PdfFormat.PDF_A_4E);
                fa.indicesToRemove.Add(i);
            }
            return true;
        }

        // Fix Print flag (bit 3, value 4) — except for Widget/Popup
        if (fa.subtype != "Widget" && fa.subtype != "Popup")
        {
            FixAnnotationFlagsAndAppearance(fa);
        }

        fa.isPdfXTarget = fa.options.Format is PdfFormat.PDF_X_1A or PdfFormat.PDF_X_3 or PdfFormat.PDF_X_4;
        if (fa.fix && fa.subtype == "Widget" && !fa.isPdfXTarget)
            GenerateMissingWidgetAppearance(fa.annotDict);
        fa.actionObj = _reader.ResolveDict(fa.annotDict.Get("A"));
        if (fa.actionObj is not null)
        {
            FixAnnotationAction(fa);
        }

        fa.annotAa = _reader.ResolveDict(fa.annotDict.Get("AA"));
        if (fa.annotAa is not null)
        {
            FixAnnotationAdditionalActions(fa);
        }
        return true;
    }

    /// <summary>ISO 19005-1 clause 6.5.3 and 19005-2 clause 6.3.3: a widget is drawn by its
    /// appearance stream alone, and the conversion takes away the NeedAppearances flag that
    /// asked a viewer to build one. A widget that has none gets the appearance of its field's
    /// value; the fonts it names are embedded with the others later in the conversion.</summary>
    private void GenerateMissingWidgetAppearance(PdfDictionary widget)
    {
        if (widget.ContainsKey("AP")) return;
        try { Field.Create(widget, _reader).GenerateAppearance(); }
        catch { /* a field the generator cannot draw is left as it was */ }
        if (_reader.ResolveDict(widget.Get("AP")) is not { } appearance) return;
        // The same clause admits the normal appearance alone; a button is generated with a
        // pressed one beside it.
        foreach (var key in appearance.Keys.Where(key => key != "N").ToList()) appearance.Remove(key);
        ShareGeneratedAppearanceFonts(appearance.Get("N"));
    }

    // The fonts the generated appearances name, each written once: the generator puts a
    // dictionary of its own into every appearance, and a form has hundreds of widgets.
    private Dictionary<string, PdfIndirectRef>? _generatedAppearanceFonts;

    private void ShareGeneratedAppearanceFonts(PdfObject? appearance)
    {
        if (_reader.Resolve(appearance) is PdfDictionary states)
        {
            foreach (var key in states.Keys) ShareGeneratedAppearanceFonts(states.Get(key));
            return;
        }
        var resources = _reader.ResolveDict(_reader.ResolveStream(appearance)?.Dict.Get("Resources"));
        if (_reader.ResolveDict(resources?.Get("Font")) is not { } fonts) return;
        foreach (var key in fonts.Keys.ToList())
        {
            // What the generator writes for a face it names: a dictionary of the appearance's
            // own, its descriptor inside it. A font of the file is an object with a
            // descriptor object, and is left where it is.
            if (fonts.Get(key) is not PdfDictionary font || font.Get("FontDescriptor") is PdfIndirectRef
                || font.Get("Encoding") is not (null or PdfName)) continue;
            var face = $"{font.GetName("Subtype")} {font.GetName("BaseFont")} {font.GetName("Encoding")}";
            _generatedAppearanceFonts ??= new Dictionary<string, PdfIndirectRef>(StringComparer.Ordinal);
            if (!_generatedAppearanceFonts.TryGetValue(face, out var shared))
            {
                var number = AllocateObjectNumber();
                AddNewObject(number, font, registerOverlay: true);
                _generatedAppearanceFonts[face] = shared = new PdfIndirectRef(number, 0);
            }
            fonts.Set(key, shared);
        }
    }

    /// <summary></summary>
    private void FixAnnotationAdditionalActions(PdfAAnnotationFixState fa)
    {
        var hasProhibited = false;
        foreach (var key in fa.annotAa!.Keys)
        {
            var ad = _reader.ResolveDict(fa.annotAa.Get(key));
            if (ad is null) continue;
            var at = ad.GetName("S");
            if (at is not null && ConvertProhibitedActionTypes.Contains(at))
            {
                hasProhibited = true;
                fa.options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "ActionType",
                    Description = $"Action type '{at}' is not allowed in PDF/A",
                    PageNumber = fa.page.Number,
                });
            }
        }
        if (fa.strip && hasProhibited)
        {
            fa.annotDict!.Remove("AA");
        }
    }

    /// <summary></summary>
    private void FixAnnotationAction(PdfAAnnotationFixState fa)
    {
        var actionType = fa.actionObj!.GetName("S");
        if (actionType is not null && (fa.isPdfXTarget || ConvertProhibitedActionTypes.Contains(actionType)))
        {
            fa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "ActionType",
                Description = fa.isPdfXTarget
                    ? $"Annotation action '{actionType}' is not allowed in PDF/X"
                    : $"Action type '{actionType}' is not allowed in PDF/A",
                PageNumber = fa.page.Number,
            });
            if (fa.strip)
            {
                fa.annotDict!.Remove("A");
            }
        }
    }

    /// <summary></summary>
    private void FixAnnotationFlagsAndAppearance(PdfAAnnotationFixState fa)
    {
        var flags = (int)fa.annotDict!.GetInt("F");
        if ((flags & 4) == 0)
        {
            fa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "AnnotationPrintFlag",
                Description = $"Annotation (type '{fa.subtype ?? "unknown"}') missing Print flag",
                PageNumber = fa.page.Number,
            });
            if (fa.fix)
            {
                fa.annotDict.Set("F", new PdfInteger(flags | 4));
            }
        }
    }
}
