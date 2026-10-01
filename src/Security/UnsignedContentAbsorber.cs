using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Facades;
using Aspose.Pdf.IO;
using Aspose.Pdf.Signatures;

namespace Aspose.Pdf.Security;

/// <summary>
/// Finds the content a signed document acquired AFTER its last signature. A signature's
/// /ByteRange fixes the bytes it covers; every later incremental update lands past that
/// range, and an object whose latest cross-reference offset lies there is unsigned. The
/// absorber sorts those objects into the pages, form widgets, other annotations and form
/// XObjects the document now shows.
/// </summary>
public sealed class UnsignedContentAbsorber
{
    /// <summary>The outcome of <see cref="TryGetContent"/>.</summary>
    public sealed class Result
    {
        /// <summary>True when the document could be measured against its signatures.</summary>
        public bool Success { get; internal set; }

        /// <summary>The unsigned content found; empty when the document is entirely signed.</summary>
        public UnsignedContent UnsignedContent { get; internal set; } = new();

        /// <summary>Why the measurement could not be made, when it could not.</summary>
        public string Message { get; internal set; } = string.Empty;

        /// <summary>How much of the document the signatures cover.</summary>
        public SignaturesCoverage Coverage { get; internal set; }

        internal Result() { }
    }

    /// <summary>The objects that lie past the last signature's covered range.</summary>
    public sealed class UnsignedContent
    {
        /// <summary>Pages added after signing, or whose content stream was rewritten.</summary>
        public List<Page> Pages { get; internal set; } = new();

        /// <summary>Form-field widgets added or changed after signing.</summary>
        public List<WidgetAnnotation> Forms { get; internal set; } = new();

        /// <summary>Form XObjects added or rewritten after signing, by object number.</summary>
        public Dictionary<int, XForm> XForms { get; internal set; } = new();

        /// <summary>Annotations other than widgets added or changed after signing, by object
        /// number. A change to the dictionary or to any appearance stream counts.</summary>
        public Dictionary<int, Annotation> Annotations { get; internal set; } = new();

        internal UnsignedContent() { }
    }

    private readonly PdfFileSignature _signature;

    /// <summary>An absorber over the document <paramref name="signature"/> is bound to.</summary>
    public UnsignedContentAbsorber(PdfFileSignature signature)
    {
        _signature = signature ?? throw new ArgumentNullException(nameof(signature));
    }

    /// <summary>
    /// Measures the bound document against its signatures. Without a signature the coverage
    /// is <see cref="SignaturesCoverage.Undefined"/> and nothing is reported; otherwise the
    /// content past the last covered byte is collected.
    /// </summary>
    public Result TryGetContent()
    {
        var result = new Result();
        byte[] bytes;
        try
        {
            bytes = _signature.BoundBytes;
        }
        catch (InvalidOperationException notBound)
        {
            result.Message = notBound.Message;
            return result;
        }

        using var document = _signature.OpenBound(bytes);
        var coveredEnd = SignedExtent(document);
        if (coveredEnd < 0)
        {
            // Nothing signed leaves nothing to measure against: the measurement itself
            // succeeds and reports that the coverage is undefined.
            result.Success = true;
            result.Message = "The document carries no signature with a byte range.";
            return result;
        }

        if (!IsRevisionBoundary(bytes, coveredEnd))
        {
            // The bytes a signature covers no longer end a revision: the document was
            // rewritten after signing, every offset moved, and lying inside the range says
            // nothing about what was signed.
            result.Message = "The document was saved non-incrementally after it was signed; " +
                             "the signed byte range no longer bounds a revision.";
            return result;
        }

        result.Success = true;
        if (coveredEnd >= bytes.LongLength)
        {
            result.Coverage = SignaturesCoverage.EntirelySigned;
            return result;
        }

        result.Coverage = SignaturesCoverage.PartiallySigned;
        using var signedRevision = _signature.OpenBound(bytes[..(int)coveredEnd]);
        var updates = new Updates(document.Reader.XRefTable, signedRevision.Reader.XRefTable, coveredEnd);
        Collect(document, updates, result.UnsignedContent);
        return result;
    }

    // A signature's range must end where a revision ends: at the %%EOF that closed the
    // file when it was signed. A full rewrite after signing leaves arbitrary bytes there.
    private static bool IsRevisionBoundary(byte[] bytes, long coveredEnd)
    {
        if (coveredEnd > bytes.LongLength) return false;
        var end = (int)coveredEnd;
        while (end > 0 && bytes[end - 1] is (byte)'\n' or (byte)'\r' or (byte)' ' or (byte)'\t') end--;
        const string marker = "%%EOF";
        if (end < marker.Length) return false;
        for (var i = 0; i < marker.Length; i++)
            if (bytes[end - marker.Length + i] != marker[i]) return false;
        return true;
    }

    // The last byte any signature covers: the end of the second /ByteRange segment.
    private static long SignedExtent(Document document)
    {
        long end = -1;
        foreach (var signature in Forms.Signature.EnumerateSignatures(document))
        {
            var range = signature.ByteRangeRaw;
            if (range is null || range.Length < 4) continue;
            end = Math.Max(end, range[2] + range[3]);
        }
        return end;
    }

    private static void Collect(Document document, Updates updates, UnsignedContent content)
    {
        foreach (var page in document.Pages)
        {
            var pageObject = document.FindObjectNumber(page.Dict);
            if (pageObject < 0 || !updates.ExistedWhenSigned(pageObject)
                || ContentStreamsOf(page, document.Reader).Any(updates.IsUnsigned))
                content.Pages.Add(page);

            foreach (var annotation in page.Annotations)
            {
                var number = annotation.ObjectNumber;
                if (number < 0) continue;
                // An annotation is unsigned when its dictionary or any of its appearance
                // streams was written after signing; those streams belong to the annotation,
                // not to the page's form resources.
                if (!updates.IsUnsigned(number)
                    && !AppearanceStreamsOf(annotation, document.Reader).Any(updates.IsUnsigned)) continue;
                if (annotation is WidgetAnnotation widget) content.Forms.Add(widget);
                else content.Annotations[number] = annotation;
            }

            foreach (var form in page.Resources.Forms)
            {
                var number = form.ObjectNumber;
                if (number > 0 && updates.IsUnsigned(number)) content.XForms[number] = form;
            }
        }
    }

    // The streams under /AP: each appearance is a stream or a state dictionary of streams.
    private static IEnumerable<int> AppearanceStreamsOf(Annotation annotation, PdfReader reader)
    {
        if (reader.ResolveDict(annotation.Dict.Get("AP")) is not { } appearances) yield break;
        foreach (var key in appearances.Keys)
        {
            var entry = appearances.Get(key);
            if (entry is PdfIndirectRef reference && reader.Resolve(reference) is PdfStream)
            {
                yield return reference.ObjectNumber;
                continue;
            }
            if (reader.ResolveDict(entry) is not { } states) continue;
            foreach (var state in states.Keys)
                if (states.Get(state) is PdfIndirectRef stateReference) yield return stateReference.ObjectNumber;
        }
    }

    // A page's content lives in one stream or an array of them; the array itself may be
    // an indirect object that a rewrite replaces, so it counts along with its members.
    private static IEnumerable<int> ContentStreamsOf(Page page, PdfReader reader)
    {
        var contents = page.Dict.Get("Contents");
        if (contents is PdfIndirectRef reference)
        {
            yield return reference.ObjectNumber;
            contents = reader.Resolve(reference);
        }
        if (contents is not PdfArray array) yield break;
        for (var i = 0; i < array.Count; i++)
            if (array[i] is PdfIndirectRef item) yield return item.ObjectNumber;
    }

    /// <summary>The two cross-reference views the classification needs: the current one,
    /// whose latest offsets say where each object was last written, and the signed
    /// revision's, which says whether an object existed at all when the signature was made.</summary>
    private sealed class Updates
    {
        private readonly XRefTable _current;
        private readonly XRefTable _signed;
        private readonly long _coveredEnd;

        internal Updates(XRefTable current, XRefTable signed, long coveredEnd)
        {
            _current = current;
            _signed = signed;
            _coveredEnd = coveredEnd;
        }

        internal bool ExistedWhenSigned(int objectNumber) =>
            _signed.GetEntry(objectNumber) is { InUse: true };

        // An object in an object stream was written wherever that stream was.
        internal bool IsUnsigned(int objectNumber)
        {
            if (_current.GetEntry(objectNumber) is not { InUse: true } entry) return false;
            var offset = entry.IsCompressed
                ? _current.GetEntry(entry.StreamObjectNumber)?.Offset ?? 0
                : entry.Offset;
            return offset >= _coveredEnd;
        }
    }
}
