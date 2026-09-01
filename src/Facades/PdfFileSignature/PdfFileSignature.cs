using Aspose.Pdf.Forms;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for signing and reading digital signatures in PDF documents.
/// Supports PKCS#7/CMS detached signatures with X.509 certificates.
/// </summary>
public sealed partial class PdfFileSignature : IDisposable
{
    private byte[]? _boundPdf;
    private string? _outputFile;
    private Document? _document;
    // Password the bound document was opened with, when it is encrypted — the
    // bound bytes are still encrypted, so re-opening them to read fields or to
    // sign/verify must authenticate again with this password.
    private string? _password;

    /// <summary>The bound document, lazily opened from the bound PDF bytes,
    /// for reading its fields / signatures.</summary>
    public Document Document => _document ??= OpenDoc(RequireBound());

    /// <summary>True when the bound document reports version 2.0 (header or
    /// catalog /Version override). Unopenable bytes are treated as non-2.0 so
    /// the signing pipeline surfaces its own error for them.</summary>
    /// <summary>True when the bound document already carries a DocMDP certification
    /// signature. Unopenable bytes are treated as uncertified so the signing pipeline
    /// surfaces its own error for them.</summary>
    private bool BoundDocumentIsCertified()
    {
        try { return Document.HasCertificationSignature(); }
        catch { return false; }
    }

    private bool BoundDocumentIsPdf20()
    {
        try { return Document.Version == "2.0"; }
        catch { return false; }
    }

    /// <summary>Open bound PDF bytes, re-authenticating with the captured
    /// password when the document is encrypted.</summary>
    private Document OpenDoc(byte[] data)
        => _password is not null ? Document.Open(data, _password) : Document.Open(data);

    public void Dispose() { _document?.Dispose(); _document = null; _boundPdf = null; }
    public void Close() => Dispose();

    public PdfFileSignature() { }

    // The live document a Document-bound facade wraps (never owned, never
    // disposed here). Signature REMOVAL mirrors onto it so the caller's
    // document reflects the change — the facade semantics: remove through
    // the facade, then Save/Convert the same document as unsigned.
    private Document? _sourceDocument;

    public PdfFileSignature(Document document)
    {
        // A DOM-level Sign may already have produced signed revisions for this
        // document; signing continues from those bytes, not the original source.
        _boundPdf = document.PendingSignedBytes ?? document.Reader.RawData;
        _password = document.Reader.Password;
        _sourceDocument = document;
    }

    public PdfFileSignature(Document document, string outputFile)
        : this(document)
    {
        _outputFile = outputFile;
    }

    public PdfFileSignature(string inputFile)
    {
        _boundPdf = File.ReadAllBytes(inputFile);
    }

    public PdfFileSignature(string inputFile, string outputFile)
        : this(inputFile)
    {
        _outputFile = outputFile;
    }

    public void BindPdf(string inputFile)
    {
        _boundPdf = File.ReadAllBytes(inputFile);
    }

    public void BindPdf(byte[] input)
    {
        _boundPdf = input;
    }

    public void BindPdf(Stream inputStream)
    {
        if (inputStream is null) throw new ArgumentNullException(nameof(inputStream));
        if (inputStream.CanSeek) inputStream.Seek(0, SeekOrigin.Begin);
        using var ms = new MemoryStream();
        inputStream.CopyTo(ms);
        _boundPdf = ms.ToArray();
    }

    public void BindPdf(Document document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        // Prefer the original on-disk bytes: re-serializing (ToArray) shifts
        // object offsets, so any existing signature's /ByteRange no longer
        // matches the data and verification breaks. Fall back to ToArray only
        // for in-memory documents that were never read from a source. A DOM-level
        // Sign's accumulated revisions take precedence over both.
        _boundPdf = document.PendingSignedBytes ?? document.Reader?.RawData ?? document.ToArray();
        _password ??= document.Reader?.Password;
        _sourceDocument = document;
    }

    public IList<string> GetSignNames()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        return Signature.EnumerateSignatures(doc)
            .Select(s => s.FieldName ?? "")
            .ToList();
    }

    /// <summary>Active signatures only when <paramref name="onlyActive"/> is true;
    /// otherwise returns every signature field that carries a /V value (matches
    /// the parameterless overload).</summary>
    public IList<string> GetSignNames(bool onlyActive)
    {
        // Active = signature is intact (last covered byte matches input length).
        // When false, return every signed field regardless of whether subsequent
        // incremental updates have invalidated the signature.
        var input = RequireBound();
        using var doc = OpenDoc(input);
        var names = new List<string>();
        foreach (var sig in Signature.EnumerateSignatures(doc))
        {
            if (sig.FieldName is null) continue;
            if (onlyActive)
            {
                if (sig.ByteRangeRaw is null || sig.ByteRangeRaw.Length < 4) continue;
                var coveredEnd = sig.ByteRangeRaw[2] + sig.ByteRangeRaw[3];
                if (coveredEnd != input.Length) continue;
            }
            names.Add(sig.FieldName);
        }
        return names;
    }

    /// <summary>All signed signature fields wrapped as <see cref="SignatureName"/>
    /// entries (the partial+full+HasSignature shape callers expect).</summary>
    public IList<SignatureName> GetSignatureNames()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        return Signature.EnumerateSignatures(doc)
            .Select(s => BuildSignatureName(s.FieldName, hasSignature: true))
            .ToList();
    }

    /// <summary>Filter on signature activity — same predicate as
    /// <see cref="GetSignNames(bool)"/>.</summary>
    public IList<SignatureName> GetSignatureNames(bool onlyActive)
        => GetSignNames(onlyActive).Select(n => BuildSignatureName(n, hasSignature: true)).ToList();

    /// <summary>Names of signature fields that exist on the form but carry no
    /// signature value (/V entry absent). These are the placeholders ready
    /// to be signed via <see cref="Sign(int, string, string, string, bool, System.Drawing.Rectangle, Forms.Signature)"/>.</summary>
    public IList<string> GetBlankSignNames()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        return EnumerateBlankSignatureFields(doc)
            .Select(f => f.FullName)
            .ToList();
    }

    public IList<SignatureName> GetBlankSignatureNames()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        return EnumerateBlankSignatureFields(doc)
            .Select(f => BuildSignatureName(f.FullName, hasSignature: false))
            .ToList();
    }

    /// <summary>Revision index of the named signature (1-based) — counts
    /// signatures in the order they appear in the field tree.</summary>
    public int GetRevision(string signName)
    {
        if (string.IsNullOrEmpty(signName)) return 0;
        var input = RequireBound();
        using var doc = OpenDoc(input);
        var index = 0;
        foreach (var sig in Signature.EnumerateSignatures(doc))
        {
            index++;
            if (sig.FieldName == signName) return index;
        }
        return 0;
    }

    public int GetRevision(SignatureName signName)
        => signName is null ? 0 : GetRevision(signName.FullName);

    /// <summary>Total number of signatures present in the document (1-based
    /// max revision returned by <see cref="GetRevision(string)"/>).</summary>
    public int GetTotalRevision()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        return Signature.EnumerateSignatures(doc).Count();
    }

    /// <summary>Whether the named signature covers the entire bound document
    /// (i.e. no incremental updates were appended after signing).</summary>
    public bool CoversWholeDocument(string signName) => IsCoversWholeDocument(signName);

    public bool CoversWholeDocument(SignatureName signName)
        => signName is not null && IsCoversWholeDocument(signName.FullName);

    public void RemoveSignature(SignatureName signName)
    {
        if (signName is not null) RemoveSignature(signName.FullName);
    }

    public void RemoveSignature(SignatureName signName, bool removeField)
    {
        if (signName is not null) RemoveSignature(signName.FullName, removeField);
    }

    public string? GetSignerName(SignatureName signName)
        => signName is null ? null : GetSignerName(signName.FullName);

    public string? GetReason(SignatureName signName)
        => signName is null ? null : GetReason(signName.FullName);

    public string? GetLocation(SignatureName signName)
        => signName is null ? null : GetLocation(signName.FullName);

    public string? GetContactInfo(SignatureName signName)
        => signName is null ? null : GetContactInfo(signName.FullName);

    public DateTime GetDateTime(SignatureName signName)
        => signName is null ? DateTime.MinValue : GetDateTime(signName.FullName);

    public string? GetSignerName(string signName)
    {
        return FindSignature(signName)?.Authority;
    }

    public string? GetReason(string signName)
    {
        return FindSignature(signName)?.Reason;
    }

    public string? GetLocation(string signName)
    {
        return FindSignature(signName)?.Location;
    }

    public string? GetContactInfo(string signName)
    {
        return FindSignature(signName)?.ContactInfo;
    }

    public DateTime GetDateTime(string signName)
    {
        return FindSignature(signName)?.Date ?? DateTime.MinValue;
    }

    public bool IsCoversWholeDocument(string signName)
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        var sig = Signature.EnumerateSignatures(doc)
            .FirstOrDefault(s => s.FieldName == signName);
        if (sig?.ByteRangeRaw is null || sig.ByteRangeRaw.Length < 4)
            return false;

        // A signature covers the whole document only when its /ByteRange starts at the
        // very beginning of the file (offset 0) AND its second segment reaches the end,
        // excluding just the /Contents hex gap. Content appended after signing leaves
        // start2+len2 short of the file length; a tampered range crafted with a non-zero
        // start so that start2+len2 happens to equal the length (e.g. [106 … len 0]) is
        // likewise rejected here.
        var br = sig.ByteRangeRaw;
        var coveredEnd = br[2] + br[3];
        return br[0] == 0 && coveredEnd == input.Length;
    }

    public void RemoveSignature(string signName)
    {
        GuardCertificationRemoval(signName);
        var input = RequireBound();
        using var doc = OpenDoc(input);
        StripSignatureValue(doc, signName);
        _boundPdf = doc.ToArray();
        // A Document-bound facade removes the signature from the LIVE document
        // too: the caller then saves or converts that document as unsigned.
        if (_sourceDocument is not null) StripSignatureValue(_sourceDocument, signName);
    }

    /// <summary>Remove the signature value (<paramref name="removeField"/> false)
    /// or the entire signature field (true) by name.</summary>
    public void RemoveSignature(string signName, bool removeField)
    {
        GuardCertificationRemoval(signName);
        if (removeField)
        {
            var input = RequireBound();
            using var doc = OpenDoc(input);
            RemoveSignatureField(doc, signName);
            _boundPdf = doc.ToArray();
            if (_sourceDocument is not null) RemoveSignatureField(_sourceDocument, signName);
        }
        else
        {
            RemoveSignature(signName);
        }
    }

    public void RemoveSignatures()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        StripSignatureValue(doc, null);
        _boundPdf = doc.ToArray();
        if (_sourceDocument is not null) StripSignatureValue(_sourceDocument, null);
    }

    public bool IsContainSignature()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        return Signature.HasAny(doc);
    }

    /// <summary>
    /// True when the bound document is certified — i.e. carries a signature
    /// whose /V dict has a /Reference array containing a /TransformMethod
    /// /DocMDP entry (PDF spec §12.8.2.2).
    /// </summary>
    public bool IsCertified
    {
        get
        {
            var input = RequireBound();
            using var doc = OpenDoc(input);
            var form = doc.Form;
            if (form is null) return false;
            foreach (var field in form.Fields)
            {
                if (field.Type != Forms.FieldType.Signature) continue;
                var sigDict = doc.Reader.ResolveDict(field.Dict.Get("V"));
                if (sigDict is null) continue;
                var refs = doc.Reader.Resolve(sigDict.Get("Reference")) as Aspose.Pdf.Core.PdfArray;
                if (refs is null) continue;
                foreach (var refObj in refs)
                {
                    var refDict = doc.Reader.ResolveDict(refObj);
                    if (refDict is null) continue;
                    var transformMethod = refDict.GetName("TransformMethod");
                    if (transformMethod == "DocMDP") return true;
                }
            }
            return false;
        }
    }

    /// <summary>Refuse to remove a certification (DocMDP) signature — doing so
    /// would break the document's certification. This throws a
    /// <see cref="PdfException"/>; approval signatures remain removable.</summary>
    private void GuardCertificationRemoval(string signName)
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        var form = doc.Form;
        if (form is null) return;
        foreach (var field in form.Fields)
        {
            if (field.Type != Forms.FieldType.Signature) continue;
            if (field.FullName != signName && field.PartialName != signName) continue;
            var sigDict = doc.Reader.ResolveDict(field.Dict.Get("V"));
            var refs = doc.Reader.Resolve(sigDict?.Get("Reference")) as Aspose.Pdf.Core.PdfArray;
            if (refs is null) continue;
            foreach (var refObj in refs)
            {
                var refDict = doc.Reader.ResolveDict(refObj);
                if (refDict?.GetName("TransformMethod") == "DocMDP")
                    throw new PdfException(
                        $"Signature '{signName}' certifies the document (DocMDP) and cannot be removed; removal would break the certification.");
            }
        }
    }

    public bool ContainsSignature() => IsContainSignature();

    /// <summary>A signing error that <see cref="Sign(int, string, string, string, bool, System.Drawing.Rectangle, Forms.Signature)"/>
    /// records but defers to <see cref="Save(string)"/> — the
    /// incompatible-algorithm check surfaces when the document is written.</summary>
    private Exception? _deferredSignException;

    public void Save(string outputFile)
    {
        if (_deferredSignException is not null) throw _deferredSignException;
        var input = RequireBound();
        File.WriteAllBytes(outputFile, input);
    }

    public void Save(Stream outputStream)
    {
        if (_deferredSignException is not null) throw _deferredSignException;
        var input = RequireBound();
        outputStream.Write(input, 0, input.Length);
        // A seekable output is left rewound so callers can read
        // the signed bytes back without seeking.
        if (outputStream.CanSeek) outputStream.Position = 0;
    }

    // ── Sign / Certify / SetCertificate ──────────────────────────────

    private Security.PdfCertificate? _certificate;

    /// <summary>Path to an image used as the visible signature appearance
    /// graphic. When set, subsequent Sign calls embed the image into the
    /// /AP /N form XObject.</summary>
    public string SignatureAppearance
    {
        get => _signatureAppearanceFile ?? string.Empty;
        set => _signatureAppearanceFile = value;
    }
    private string? _signatureAppearanceFile;

    /// <summary>Stream of bytes for the visible signature appearance
    /// graphic. Takes precedence over <see cref="SignatureAppearance"/>
    /// when both are set.</summary>
    public Stream? SignatureAppearanceStream
    {
        get => _signatureAppearanceStream;
        set => _signatureAppearanceStream = value;
    }
    private Stream? _signatureAppearanceStream;

    /// <summary>True iff the document carries a /DSS (Document Security
    /// Store) entry in the catalog — the marker for LTV (long-term
    /// validation) enabled signatures.</summary>
    public bool IsLtvEnabled
    {
        get
        {
            var input = RequireBound();
            using var doc = OpenDoc(input);
            return doc.Reader.Catalog.ContainsKey("DSS");
        }
    }

    /// <summary>Save the bound (possibly signed) document to the
    /// <c>outputFile</c> argument from the ctor. Throws
    /// <see cref="InvalidOperationException"/> when constructed without one.</summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(_outputFile))
            throw new InvalidOperationException("No output file is set. Use Save(string) or Save(Stream), or construct the facade with an output file path.");
        Save(_outputFile);
    }

    private byte[] RequireBound()
    {
        return _boundPdf ?? throw new InvalidOperationException(
            "No PDF is bound. Call BindPdf() first.");
    }

    private Signature? FindSignature(string fieldName)
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        return Signature.EnumerateSignatures(doc)
            .FirstOrDefault(s => s.FieldName == fieldName);
    }

    private static SignatureName BuildSignatureName(string? fullName, bool hasSignature)
    {
        fullName ??= string.Empty;
        var dot = fullName.LastIndexOf('.');
        var name = dot >= 0 ? fullName.Substring(dot + 1) : fullName;
        return new SignatureName(fullName, name, hasSignature);
    }

    private static IEnumerable<(string FullName, Forms.Field Field)> EnumerateBlankSignatureFields(Document doc)
    {
        var form = doc.Form;
        if (form is null) yield break;
        foreach (var field in form.Fields)
        {
            if (field.Type != Forms.FieldType.Signature) continue;
            if (field.Dict.ContainsKey("V")) continue;
            yield return (field.FullName ?? string.Empty, field);
        }
    }

    private static void RemoveSignatureField(Document doc, string fullName)
    {
        var form = doc.Form;
        if (form is null) return;
        for (var i = 0; i < form.Count; i++)
        {
            var field = form.Fields[i];
            if (field.Type != Forms.FieldType.Signature) continue;
            if (field.FullName != fullName) continue;
            // Strip the value first; field removal from the catalog /Fields
            // array is not currently wired through Form, so settle for clearing
            // the field's dict (any subsequent reader sees an empty signature
            // field rather than a stale signed one).
            field.Dict.Remove("V");
            field.Dict.Remove("Kids");
            field.Dict.Remove("T");
            return;
        }
    }

    /// <summary>Dirty-mark a stripped dict so an incremental save persists the
    /// /V removal (a facade-internal full re-serialize doesn't need it, but the
    /// live source document the caller saves afterwards does).</summary>
    private static void MarkStripDirty(Document doc, Core.PdfDictionary dict)
    {
        var num = doc.FindObjectNumber(dict);
        if (num > 0) doc.MarkDirty(num, dict);
    }

    private static void StripSignatureValue(Document doc, string? fieldName)
    {
        var form = doc.Form;
        if (form is null) return;

        foreach (var field in form.Fields)
        {
            if (field.Type != Forms.FieldType.Signature) continue;
            if (fieldName is not null && field.FullName != fieldName) continue;

            field.Dict.Remove("V");
            MarkStripDirty(doc, field.Dict);
            var kids = doc.Reader.Resolve(field.Dict.Get("Kids")) as Core.PdfArray;
            if (kids is not null)
            {
                foreach (var kid in kids)
                {
                    var kidDict = doc.Reader.ResolveDict(kid);
                    if (kidDict is null) continue;
                    kidDict.Remove("V");
                    MarkStripDirty(doc, kidDict);
                }
            }
        }
    }
}
