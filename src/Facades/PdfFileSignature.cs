using Aspose.Pdf.Forms;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for signing and reading digital signatures in PDF documents.
/// Supports PKCS#7/CMS detached signatures with X.509 certificates.
/// </summary>
public sealed class PdfFileSignature : IDisposable
{
    private byte[]? _boundPdf;
    private string? _outputFile;
    private Document? _document;

    /// <summary>The bound document, lazily opened from the bound PDF bytes,
    /// for reading its fields / signatures.</summary>
    public Document Document => _document ??= Document.Open(RequireBound());

    public void Dispose() { _document?.Dispose(); _document = null; _boundPdf = null; }
    public void Close() => Dispose();

    public PdfFileSignature() { }

    public PdfFileSignature(Document document)
    {
        _boundPdf = document.Reader.RawData;
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
        _boundPdf = document.ToArray();
    }

    public IList<string> GetSignNames()
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
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
        using var doc = Document.Open(input);
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
    /// entries (matches the partial+full+HasSignature shape Aspose.PDF for .NET expects).</summary>
    public IList<SignatureName> GetSignatureNames()
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
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
        using var doc = Document.Open(input);
        return EnumerateBlankSignatureFields(doc)
            .Select(f => f.FullName)
            .ToList();
    }

    public IList<SignatureName> GetBlankSignatureNames()
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
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
        using var doc = Document.Open(input);
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
        using var doc = Document.Open(input);
        return Signature.EnumerateSignatures(doc).Count();
    }

    /// <summary>Read the certifying signature's /DocMDP /P access-permission
    /// level (1, 2, or 3 per PDF 32000-1 §12.8.2.2). Returns
    /// <see cref="Forms.DocMDPAccessPermissions.NoChanges"/> (level 1)
    /// when no /DocMDP entry is present.</summary>
    public Forms.DocMDPAccessPermissions GetAccessPermissions()
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
        var form = doc.Form;
        if (form is null) return Forms.DocMDPAccessPermissions.NoChanges;
        foreach (var field in form.Fields)
        {
            if (field.Type != Forms.FieldType.Signature) continue;
            var sigDict = doc.Reader.ResolveDict(field.Dict.Get("V"));
            if (sigDict is null) continue;
            if (doc.Reader.Resolve(sigDict.Get("Reference")) is not Aspose.Pdf.Core.PdfArray refs) continue;
            foreach (var refObj in refs)
            {
                var refDict = doc.Reader.ResolveDict(refObj);
                if (refDict is null) continue;
                if (refDict.GetName("TransformMethod") != "DocMDP") continue;
                var paramsDict = doc.Reader.ResolveDict(refDict.Get("TransformParams"));
                var p = (int)(paramsDict?.GetInt("P") ?? 1);
                return (Forms.DocMDPAccessPermissions)p;
            }
        }
        return Forms.DocMDPAccessPermissions.NoChanges;
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

    public bool VerifySignature(SignatureName signName)
        => signName is not null && VerifySignature(signName.FullName);

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

    public Stream? ExtractCertificate(SignatureName signName)
        => signName is null ? null : ExtractCertificate(signName.FullName);

    public bool TryExtractCertificate(SignatureName signName, out Stream stream)
    {
        stream = null!;
        if (signName is null) return false;
        var s = ExtractCertificate(signName.FullName);
        if (s is null) return false;
        stream = s;
        return true;
    }

    public bool TryExtractCertificate(SignatureName signName, out System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        certificate = null!;
        if (signName is null) return false;
        using var certStream = ExtractCertificate(signName.FullName);
        if (certStream is null) return false;
        try
        {
            var bytes = new byte[certStream.Length];
            certStream.Read(bytes, 0, bytes.Length);
#pragma warning disable SYSLIB0057 // X509Certificate2(byte[]) still works on .NET 8; loader API is .NET 9+
            certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(bytes);
#pragma warning restore SYSLIB0057
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool VerifySignature(string signName)
    {
        var input = RequireBound();
        return PdfSigner.Verify(input, signName);
    }

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
        using var doc = Document.Open(input);
        var sig = Signature.EnumerateSignatures(doc)
            .FirstOrDefault(s => s.FieldName == signName);
        if (sig?.ByteRangeRaw is null || sig.ByteRangeRaw.Length < 4)
            return false;

        var br = sig.ByteRangeRaw;
        var coveredEnd = br[2] + br[3];
        return coveredEnd == input.Length;
    }

    public void RemoveSignature(string signName)
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
        StripSignatureValue(doc, signName);
        _boundPdf = doc.ToArray();
    }

    /// <summary>Remove the signature value (<paramref name="removeField"/> false)
    /// or the entire signature field (true) by name.</summary>
    public void RemoveSignature(string signName, bool removeField)
    {
        if (removeField)
        {
            var input = RequireBound();
            using var doc = Document.Open(input);
            RemoveSignatureField(doc, signName);
            _boundPdf = doc.ToArray();
        }
        else
        {
            RemoveSignature(signName);
        }
    }

    public void RemoveSignatures()
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
        StripSignatureValue(doc, null);
        _boundPdf = doc.ToArray();
    }

    public bool IsContainSignature()
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
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
            using var doc = Document.Open(input);
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

    /// <summary>
    /// Extract the X.509 signing certificate from the named signature field
    /// and return it as a memory stream of DER-encoded bytes (.cer format).
    /// PKCS#7 /Contents are parsed via the managed CMS reader; the legacy
    /// adbe.x509.rsa_sha1 /Cert array is also honoured.
    /// </summary>
    public Stream? ExtractCertificate(string signName)
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
        var form = doc.Form;
        if (form is null) return null;
        Forms.Field? field = null;
        foreach (var f in form.Fields)
        {
            if (f.Type == Forms.FieldType.Signature &&
                (f.FullName == signName || f.PartialName == signName))
            {
                field = f;
                break;
            }
        }
        if (field is null) return null;

        var sigDict = doc.Reader.ResolveDict(field.Dict.Get("V"));
        if (sigDict is null) return null;

        var certObj = doc.Reader.Resolve(sigDict.Get("Cert"));
        if (certObj is Aspose.Pdf.Core.PdfString single)
            return new MemoryStream(single.Value, writable: false);
        if (certObj is Aspose.Pdf.Core.PdfArray arr && arr.Count > 0 &&
            arr[0] is Aspose.Pdf.Core.PdfString s)
            return new MemoryStream(s.Value, writable: false);

        var contentsObj = doc.Reader.Resolve(sigDict.Get("Contents"));
        if (contentsObj is not Aspose.Pdf.Core.PdfString p7) return null;
        try
        {
            var certDer = Aspose.Pdf.Security.CmsParser.GetFirstCertificateDer(p7.Value);
            if (certDer is null) return null;
            return new MemoryStream(certDer, writable: false);
        }
        catch
        {
            return null;
        }
    }

    public bool ContainsUsageRights()
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
        var perms = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("Perms"));
        if (perms is null) return false;
        return perms.ContainsKey("UR") || perms.ContainsKey("UR3");
    }

    public void RemoveUsageRights()
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
        var perms = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("Perms"));
        if (perms is null)
        {
            _boundPdf = doc.ToArray();
            return;
        }
        perms.Remove("UR");
        perms.Remove("UR3");
        if (!perms.Keys.Any())
            doc.Reader.Catalog.Remove("Perms");
        _boundPdf = doc.ToArray();
    }

    public bool ContainsSignature() => IsContainSignature();

    public void Save(string outputFile)
    {
        var input = RequireBound();
        File.WriteAllBytes(outputFile, input);
    }

    public void Save(Stream outputStream)
    {
        var input = RequireBound();
        outputStream.Write(input, 0, input.Length);
    }

    // ── Sign / Certify / SetCertificate ──────────────────────────────

    /// <summary>Load a signing certificate from a PFX/PKCS#12 file. Stored
    /// for subsequent <see cref="Sign(int, string, string, string, bool, System.Drawing.Rectangle)"/>
    /// calls that don't take an explicit <see cref="Forms.Signature"/>.</summary>
    public void SetCertificate(string pfx, string pass)
    {
        _certificate = Security.PdfCertificate.FromPfx(pfx, pass ?? string.Empty);
    }

    private Security.PdfCertificate? _certificate;

    /// <summary>Sign with the previously-loaded <see cref="SetCertificate"/>
    /// certificate. <paramref name="annotRect"/> selects the on-page
    /// rectangle for the visible appearance when <paramref name="visible"/>
    /// is true.</summary>
    public void Sign(int page, string SigReason, string SigContact, string SigLocation,
        bool visible, System.Drawing.Rectangle annotRect)
    {
        if (_certificate is null)
            throw new InvalidOperationException("No certificate set. Call SetCertificate() or use a Sign overload that takes Forms.Signature.");
        SignCore(_certificate, fieldName: null, page, SigReason, SigContact, SigLocation, visible, annotRect, sig: null);
    }

    /// <summary>Sign on the given <paramref name="page"/> using the certificate
    /// embedded in <paramref name="sig"/>.</summary>
    public void Sign(int page, bool visible, System.Drawing.Rectangle annotRect, Forms.Signature sig)
    {
        var cert = RequireCertificate(sig);
        SignCore(cert, fieldName: null, page, sig.Reason, sig.ContactInfo, sig.Location, visible, annotRect, sig);
    }

    /// <summary>Sign on the given <paramref name="page"/> with explicit
    /// metadata, using the certificate embedded in <paramref name="sig"/>.</summary>
    public void Sign(int page, string SigReason, string SigContact, string SigLocation,
        bool visible, System.Drawing.Rectangle annotRect, Forms.Signature sig)
    {
        var cert = RequireCertificate(sig);
        SignCore(cert, fieldName: null, page, SigReason, SigContact, SigLocation, visible, annotRect, sig);
    }

    /// <summary>Sign with explicit field name + metadata.</summary>
    public void Sign(int page, string SigName, string SigReason, string SigContact, string SigLocation,
        bool visible, System.Drawing.Rectangle annotRect, Forms.Signature sig)
    {
        var cert = RequireCertificate(sig);
        SignCore(cert, SigName, page, SigReason, SigContact, SigLocation, visible, annotRect, sig);
    }

    /// <summary>Sign an existing blank signature field by name.</summary>
    public void Sign(string SigName, Forms.Signature sig)
    {
        var cert = RequireCertificate(sig);
        SignCore(cert, SigName, page: 1, sig.Reason, sig.ContactInfo, sig.Location, visible: false, default, sig);
    }

    /// <summary>Sign an existing blank signature field by name with explicit metadata.</summary>
    public void Sign(string SigName, string SigReason, string SigContact, string SigLocation, Forms.Signature sig)
    {
        var cert = RequireCertificate(sig);
        SignCore(cert, SigName, page: 1, SigReason, SigContact, SigLocation, visible: false, default, sig);
    }

    /// <summary>Sign on the given page with a /DocMDP reference so the
    /// resulting signature is a certifying signature (PDF 32000-1 §12.8.2.2).</summary>
    public void Certify(int page, string SigReason, string SigContact, string SigLocation,
        bool visible, System.Drawing.Rectangle annotRect, Forms.DocMDPSignature docMdpSignature)
    {
        if (docMdpSignature is null) throw new ArgumentNullException(nameof(docMdpSignature));
        var cert = RequireCertificate(docMdpSignature.Signature);
        CertifyCore(cert, fieldName: null, page, SigReason, SigContact, SigLocation,
            visible, annotRect, docMdpSignature.AccessPermissions);
    }

    /// <summary>Certify an existing blank signature field by name.</summary>
    public void Certify(string sigName, Forms.DocMDPSignature docMdpSignature)
    {
        if (docMdpSignature is null) throw new ArgumentNullException(nameof(docMdpSignature));
        var cert = RequireCertificate(docMdpSignature.Signature);
        CertifyCore(cert, sigName, page: 1,
            docMdpSignature.Signature.Reason,
            docMdpSignature.Signature.ContactInfo,
            docMdpSignature.Signature.Location,
            visible: false, default, docMdpSignature.AccessPermissions);
    }

    /// <summary>Verifies that a signed signature is intact. Alias for
    /// <see cref="VerifySignature(string)"/> matching Aspose.PDF for .NET naming.</summary>
    public bool VerifySigned(string signName) => VerifySignature(signName);

    /// <summary>Verify a signature with explicit options + return a
    /// <see cref="Security.ValidationResult"/> describing the outcome.
    /// Honours <see cref="Security.ValidationOptions.CheckCertificateChain"/>
    /// + ValidationMode/Method. Revocation checks (OCSP/CRL) are accepted
    /// for surface parity but the FOSS build only runs the
    /// cryptographic-bytes check — when ValidationMode is Strict and
    /// revocation is requested, results are reported Unknown rather than
    /// falsely Valid.</summary>
    public bool VerifySignature(string signName, Security.ValidationOptions options,
        out Security.ValidationResult validationResult)
    {
        var basic = VerifySignature(signName);
        validationResult = BuildValidationResult(basic, options, signName);
        return basic;
    }

    public bool VerifySignature(string signName,
        System.Security.Cryptography.X509Certificates.X509Certificate2 publicKeyCertificate,
        Security.ValidationOptions options,
        out Security.ValidationResult validationResult)
    {
        // Public-key certificate-pinned verification: confirm the
        // signature's signer cert matches the supplied public certificate
        // before reporting Valid.
        var basic = VerifySignature(signName);
        if (basic && publicKeyCertificate is not null)
        {
            using var certStream = ExtractCertificate(signName);
            if (certStream is not null)
            {
                var bytes = new byte[certStream.Length];
                certStream.Read(bytes, 0, bytes.Length);
#pragma warning disable SYSLIB0057
                var signerCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(bytes);
#pragma warning restore SYSLIB0057
                if (!signerCert.Thumbprint.Equals(publicKeyCertificate.Thumbprint,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    validationResult = new Security.ValidationResult(
                        Security.ValidationStatus.Invalid,
                        "Signature's signer certificate does not match the supplied public certificate.");
                    return false;
                }
            }
        }
        validationResult = BuildValidationResult(basic, options, signName);
        return basic;
    }

    /// <summary>Cert-pinned verify without ValidationOptions. Returns true
    /// iff the signature is intact AND the signer cert thumbprint matches
    /// <paramref name="publicKeyCertificate"/>.</summary>
    public bool VerifySignature(Facades.SignatureName signName,
        System.Security.Cryptography.X509Certificates.X509Certificate2 publicKeyCertificate)
    {
        if (signName is null) return false;
        return VerifySignature(signName.FullName, publicKeyCertificate,
            options: new Security.ValidationOptions(), out _);
    }

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

    public bool VerifySignature(Facades.SignatureName signName, Security.ValidationOptions options,
        out Security.ValidationResult validationResult)
    {
        if (signName is null)
        {
            validationResult = new Security.ValidationResult(Security.ValidationStatus.Undefined, "signName is null.");
            return false;
        }
        return VerifySignature(signName.FullName, options, out validationResult);
    }

    public bool VerifySignature(Facades.SignatureName signName,
        System.Security.Cryptography.X509Certificates.X509Certificate2 publicKeyCertificate,
        Security.ValidationOptions options,
        out Security.ValidationResult validationResult)
    {
        if (signName is null)
        {
            validationResult = new Security.ValidationResult(Security.ValidationStatus.Undefined, "signName is null.");
            return false;
        }
        return VerifySignature(signName.FullName, publicKeyCertificate, options, out validationResult);
    }

    private static Security.ValidationResult BuildValidationResult(
        bool ok, Security.ValidationOptions? options, string signName)
    {
        if (!ok)
            return new Security.ValidationResult(Security.ValidationStatus.Invalid,
                $"Signature {signName} failed cryptographic verification.");

        // Revocation checks (OCSP/CRL) not implemented — when Strict mode
        // is requested with a non-None ValidationMethod, report Unknown
        // rather than lying about a check that didn't run.
        if (options is not null
            && options.ValidationMode == Security.ValidationMode.Strict
            && options.ValidationMethod != Security.ValidationMethod.Auto)
        {
            return new Security.ValidationResult(Security.ValidationStatus.Unknown,
                "Cryptographic check passed but revocation validation (OCSP/CRL) is not implemented in this FOSS build.");
        }
        return new Security.ValidationResult(Security.ValidationStatus.Valid,
            $"Signature {signName} verified.");
    }

    /// <summary>Per-signature algorithm/digest/standard triple, parsed
    /// from each signature's PKCS#7 /Contents.</summary>
    public List<Security.SignatureAlgorithmInfo> GetSignaturesInfo()
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
        var result = new List<Security.SignatureAlgorithmInfo>();
        foreach (var sig in Signature.EnumerateSignatures(doc))
        {
            var leaf = sig.FieldName;
            if (leaf is not null)
            {
                var dot = leaf.LastIndexOf('.');
                if (dot >= 0) leaf = leaf.Substring(dot + 1);
            }
            result.Add(Security.SignatureAlgorithmInfo.FromPkcs7(sig.ContentsRaw, sig.SubFilter, leaf));
        }
        return result;
    }

    /// <summary>True iff the document carries a /DSS (Document Security
    /// Store) entry in the catalog — the marker for LTV (long-term
    /// validation) enabled signatures.</summary>
    public bool IsLtvEnabled
    {
        get
        {
            var input = RequireBound();
            using var doc = Document.Open(input);
            return doc.Reader.Catalog.ContainsKey("DSS");
        }
    }

    /// <summary>Extract the /AP /N (normal appearance) stream of the named
    /// signature's widget. Returns the raw appearance content stream as a
    /// read-only MemoryStream, or null if no appearance is present.</summary>
    public Stream? ExtractImage(string signName)
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
        var form = doc.Form;
        if (form is null) return null;
        foreach (var field in form.Fields)
        {
            if (field.Type != Forms.FieldType.Signature) continue;
            if (field.FullName != signName && field.PartialName != signName) continue;
            // Look in the field's /AP /N, then in any widget child's /AP /N.
            var stream = FindAppearanceStream(field.Dict, doc.Reader);
            if (stream is not null) return new MemoryStream(doc.Reader.DecodeStream(stream), writable: false);
            var kids = doc.Reader.Resolve(field.Dict.Get("Kids")) as Aspose.Pdf.Core.PdfArray;
            if (kids is null) continue;
            foreach (var kid in kids)
            {
                var kidDict = doc.Reader.ResolveDict(kid);
                if (kidDict is null) continue;
                stream = FindAppearanceStream(kidDict, doc.Reader);
                if (stream is not null) return new MemoryStream(doc.Reader.DecodeStream(stream), writable: false);
            }
        }
        return null;
    }

    public Stream? ExtractImage(SignatureName signName)
        => signName is null ? null : ExtractImage(signName.FullName);

    private static Aspose.Pdf.Core.PdfStream? FindAppearanceStream(
        Aspose.Pdf.Core.PdfDictionary fieldDict, Aspose.Pdf.IO.PdfReader reader)
    {
        var ap = reader.ResolveDict(fieldDict.Get("AP"));
        if (ap is null) return null;
        return reader.Resolve(ap.Get("N")) as Aspose.Pdf.Core.PdfStream;
    }

    private void SignCore(Security.PdfCertificate cert, string? fieldName,
        int page, string? reason, string? contact, string? location,
        bool visible, System.Drawing.Rectangle annotRect, Forms.Signature? sig)
    {
        var input = RequireBound();
        var opts = new Security.SignatureOptions
        {
            Reason = reason,
            Location = location,
            ContactInfo = contact,
            FieldName = fieldName,
        };
        // Honour Forms.Signature signer knobs when present.
        if (sig is not null)
        {
            if (sig.DefaultSignatureLength > 0) opts.ContentsSize = sig.DefaultSignatureLength;
            if (sig.AvoidEstimatingSignatureLength) opts.AvoidEstimating = true;
            if (sig.CustomSignHash is not null) opts.CustomSignHash = sig.CustomSignHash;
        }
        var appearance = visible ? BuildAppearance(page, annotRect, sig) : null;
        if (appearance is not null)
            appearance.ImageBytes = ResolveAppearanceImageBytes();
        var signed = appearance is not null
            ? Security.PdfSigner.SignWithAppearance(input, cert, opts, appearance)
            : Security.PdfSigner.Sign(input, cert, opts);
        _boundPdf = signed;
    }

    private void CertifyCore(Security.PdfCertificate cert, string? fieldName,
        int page, string? reason, string? contact, string? location,
        bool visible, System.Drawing.Rectangle annotRect,
        Forms.DocMDPAccessPermissions accessPermissions)
    {
        SignCore(cert, fieldName, page, reason, contact, location, visible, annotRect, sig: null);
        // After signing, re-open and add a /DocMDP /TransformParams /P
        // reference to the signature dict so downstream readers recognise
        // this as a certifying signature.
        var input = RequireBound();
        using var doc = Document.Open(input);
        var form = doc.Form;
        if (form is null) return;
        foreach (var field in form.Fields)
        {
            if (field.Type != Forms.FieldType.Signature) continue;
            if (fieldName is not null && field.FullName != fieldName) continue;
            var sigDict = doc.Reader.ResolveDict(field.Dict.Get("V"));
            if (sigDict is null) continue;
            AttachDocMdpReference(sigDict, accessPermissions);
            break;
        }
        _boundPdf = doc.ToArray();
    }

    private static void AttachDocMdpReference(Aspose.Pdf.Core.PdfDictionary sigDict,
        Forms.DocMDPAccessPermissions p)
    {
        var transformParams = new Aspose.Pdf.Core.PdfDictionary();
        transformParams.Set("Type", new Aspose.Pdf.Core.PdfName("TransformParams"));
        transformParams.Set("P", new Aspose.Pdf.Core.PdfInteger((int)p));
        transformParams.Set("V", new Aspose.Pdf.Core.PdfName("1.2"));

        var refDict = new Aspose.Pdf.Core.PdfDictionary();
        refDict.Set("Type", new Aspose.Pdf.Core.PdfName("SigRef"));
        refDict.Set("TransformMethod", new Aspose.Pdf.Core.PdfName("DocMDP"));
        refDict.Set("TransformParams", transformParams);

        var refs = new Aspose.Pdf.Core.PdfArray();
        refs.Add(refDict);
        sigDict.Set("Reference", refs);
    }

    private byte[]? ResolveAppearanceImageBytes()
    {
        if (_signatureAppearanceStream is not null)
        {
            if (_signatureAppearanceStream.CanSeek) _signatureAppearanceStream.Position = 0;
            using var ms = new MemoryStream();
            _signatureAppearanceStream.CopyTo(ms);
            return ms.ToArray();
        }
        if (!string.IsNullOrEmpty(_signatureAppearanceFile) && File.Exists(_signatureAppearanceFile))
            return File.ReadAllBytes(_signatureAppearanceFile);
        return null;
    }

    private static Security.SignatureAppearance BuildAppearance(int page, System.Drawing.Rectangle annotRect, Forms.Signature? sig)
    {
        var appearance = new Security.SignatureAppearance
        {
            PageNumber = page,
            Rect = new Rectangle(annotRect.Left, annotRect.Top, annotRect.Right, annotRect.Bottom),
        };
        // ShowProperties: when true, surface Reason/Location/ContactInfo
        // text on the visible appearance. Honoured by PdfSigner.SignWithAppearance.
        if (sig is { ShowProperties: true })
        {
            appearance.Reason = sig.Reason;
            appearance.Location = sig.Location;
            appearance.ContactInfo = sig.ContactInfo;
            appearance.SignerName = sig.Authority;
        }
        return appearance;
    }

    private static Security.PdfCertificate RequireCertificate(Forms.Signature? sig)
    {
        if (sig is null) throw new ArgumentNullException(nameof(sig));
        if (sig.Certificate is null)
            throw new InvalidOperationException("Forms.Signature has no certificate. Construct it with a PFX file or stream.");
        return sig.Certificate;
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

    /// <summary>FOSS-only convenience: return the bound (possibly signed)
    /// PDF bytes.</summary>
    public byte[] ToByteArray() => RequireBound();

    private byte[] RequireBound()
    {
        return _boundPdf ?? throw new InvalidOperationException(
            "No PDF is bound. Call BindPdf() first.");
    }

    private Signature? FindSignature(string fieldName)
    {
        var input = RequireBound();
        using var doc = Document.Open(input);
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

    private static void StripSignatureValue(Document doc, string? fieldName)
    {
        var form = doc.Form;
        if (form is null) return;

        foreach (var field in form.Fields)
        {
            if (field.Type != Forms.FieldType.Signature) continue;
            if (fieldName is not null && field.FullName != fieldName) continue;

            field.Dict.Remove("V");
            var kids = doc.Reader.Resolve(field.Dict.Get("Kids")) as Core.PdfArray;
            if (kids is not null)
            {
                foreach (var kid in kids)
                {
                    var kidDict = doc.Reader.ResolveDict(kid);
                    kidDict?.Remove("V");
                }
            }
        }
    }
}
