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
        var pa = new PdfAConvertState();
        pa.options = options;
        pa.format = pa.options.TargetFormat;
        pa.fix = true;
        pa.strip = pa.options.ErrorAction == ConvertErrorAction.Delete;

        if (ConvertToPlainVersion(pa) is { } convertToPlainVersionResult) return convertToPlainVersionResult;
        // PDF/UA-1 (ISO 14289-1) accessibility: tag the document, give it a title + natural
        // language, set /ViewerPreferences /DisplayDocTitle, the pdfuaid:part identifier, the
        // XMP dates and a file /ID. The tagged-metadata stamp on save finalises the rest.
        if (ConvertToUaOrEngineering(pa) is { } convertToUaOrEngineeringResult) return convertToUaOrEngineeringResult;
        pa.isPdfX = pa.format is PdfFormat.PDF_X_1A or PdfFormat.PDF_X_3 or PdfFormat.PDF_X_4;

        if (ResolvePdfAPart(pa) is { } resolvePdfAPartResult) return resolvePdfAPartResult;
        // 1. Remove encryption
        RemoveEncryptionAndFloorVersion(pa);

        WritePdfAIdentification(pa);

        // 4. Ensure file ID exists. Materialise /ID into the in-memory
        //    trailer immediately so PdfAValidator (which reads
        //    document.Reader.Trailer in-memory) sees the fix without
        //    requiring a Save+reopen round-trip. The save path also
        //    honours the flag for re-encrypted saves; we keep it set so the
        //    same ID gets written through to disk.
        EnsureTrailerId(pa);

        // 5. Remove prohibited actions from catalog (only when ErrorAction strips)
        RemoveProhibitedCatalogActions(pa.options, pa.strip);

        // 6. Fix annotations (per page) — print-flag fixes always, removal only when stripping
        foreach (var page in Pages)
        {
            FixAnnotationsForPdfA(page, pa.options, pa.fix, pa.strip);
        }

        // 6b. Remove page-level transparency groups — PDF/A-1 (ISO 19005-1)
        // prohibits transparency. The page /Group entry only declares the
        // blending colour space / isolation for compositing the page onto the
        // backdrop; dropping it leaves opaque content rendering identically
        // while clearing the violation. PDF/A-2 and later permit transparency,
        // so this is scoped to part 1.
        NormalisePartOneContent(pa);

        // 6c'. PDF/X-1a (ISO 15930-1) prohibits transparency the same way PDF/A-1
        // does. A page whose content actually USES transparency (an ExtGState with
        // non-opaque alpha, a soft mask, or a non-Normal blend mode) cannot keep its
        // vector content in an X-1a file — it is FLATTENED: the rendered page
        // replaces the content as a single DeviceCMYK image resource (X-1a is a
        // CMYK-only profile). A page that merely DECLARES a /Group (compositing onto
        // the backdrop; opaque content renders the same) only loses the declaration.
        WriteOutputIntents(pa);

        // 8. For PDF/X, set GTS_PDFXVersion in Info dict and XMP
        ApplyPdfXAndLevelAFixes(pa);

        // 10b. Repair out-of-range vertical metrics that would leave the output
        // non-conformant: a Courier-family FontDescriptor whose Descent falls below
        // -310 (the validator's range gate) is clamped to -300. Only the descriptor
        // metric changes — glyph programs and widths are untouched.
        if (pa.fix && !pa.isPdfX)
            RepairFontDescriptors();

        // 10c. The faces embedded above carry their own advances, and the /Widths the
        // source declared were written for the faces it did not embed - clause 6.3.6.
        if (pa.fix && !pa.isPdfX)
            ReportInconsistentGlyphWidths(pa.options);

        ResolveFontsAndAutoTag(pa);

        ApplyAssociatedFileFixes(pa);

        pa.hasUnconvertable = false;
        foreach (var v in pa.options.ConversionLog)
            if (!v.Convertable) { pa.hasUnconvertable = true; break; }

        return pa.fontsResolved && !pa.hasUnconvertable;
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
