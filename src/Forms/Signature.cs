using System;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// Represents a digital signature in a PDF document. Mirrors the
/// public-API surface used by tests — Authority (signer common name),
/// Reason, Location, ContactInfo and Date are populated from the
/// signature dictionary (/V) of a <see cref="SignatureField"/> when read
/// via <see cref="SignatureField.Signature"/>.
/// </summary>
public class Signature
{
    /// <summary>The loaded signing certificate when constructed from a PFX,
    /// or null when this Signature represents a parsed read-only signature.</summary>
    internal Security.PdfCertificate? Certificate { get; set; }

    /// <summary>Creates a signature configuration with no signing certificate.</summary>
    public Signature() { }

    /// <summary>Creates a signature configuration that signs with the certificate and private key loaded from a PFX
    /// file and its password. An empty path loads no certificate.</summary>
    public Signature(string pfx, string password)
    {
        if (!string.IsNullOrEmpty(pfx))
            Certificate = Security.PdfCertificate.FromPfx(pfx, password ?? string.Empty);
    }

    /// <summary>Creates a signature configuration that signs with the certificate and private key read from a PFX
    /// stream and its password. A seekable stream is read from its start.</summary>
    public Signature(Stream pfx, string password)
    {
        if (pfx is null) return;
        // Rewind a seekable stream so callers may reuse one open PFX stream for
        // several Signature instances (e.g. PKCS1/PKCS7Detached/PKCS7 in sequence)
        // without each constructor seeing it already drained to EOF.
        if (pfx.CanSeek) pfx.Seek(0, SeekOrigin.Begin);
        using var ms = new MemoryStream();
        pfx.CopyTo(ms);
        Certificate = Security.PdfCertificate.FromPfx(ms.ToArray(), password ?? string.Empty);
    }

    /// <summary>Gets or sets the signer name (the /Name entry). When set before signing it replaces the certificate
    /// common name; on a loaded signature it holds the stored name. Defaults to an empty string.</summary>
    public string Authority { get; set; } = string.Empty;
    /// <summary>Gets or sets the signer contact information written to, or read from, the signature dictionary.
    /// Defaults to an empty string.</summary>
    public string ContactInfo { get; set; } = string.Empty;
    /// <summary>Gets or sets the signing location written to, or read from, the signature dictionary. Defaults to an
    /// empty string.</summary>
    public string Location { get; set; } = string.Empty;
    /// <summary>Gets or sets the reason for signing written to, or read from, the signature dictionary. Defaults to
    /// an empty string.</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Gets or sets the signing time (the /M entry). When left unset, signing uses the current time; on a
    /// loaded signature it holds the stored time.</summary>
    public DateTime Date { get; set; }

    /// <summary>FOSS-only long[] backing of the underlying signature's
    /// /ByteRange entry — int[] is exposed publicly to match the public API.</summary>
    internal long[]? ByteRangeRaw { get; set; }

    /// <summary>The signature's /ByteRange entry. public-API shape int[].
    /// Returns the raw /ByteRange (e.g. four entries: offset, length,
    /// offset, length) for the bytes the signature covers.</summary>
    public int[] ByteRange
    {
        get
        {
            if (ByteRangeRaw is null) return System.Array.Empty<int>();
            var result = new int[ByteRangeRaw.Length];
            for (var i = 0; i < ByteRangeRaw.Length; i++)
                result[i] = (int)ByteRangeRaw[i];
            return result;
        }
    }

    /// <summary>RFC 3161 TSA settings; honoured by the signer when set.</summary>
    public Aspose.Pdf.TimestampSettings TimestampSettings { get; set; } = new();

    /// <summary>When true, this configuration produces a standalone RFC 3161
    /// document timestamp (PAdES DocTimeStamp, /SubFilter <c>ETSI.RFC3161</c>)
    /// rather than a certificate-based signature — the signer contacts the TSA
    /// in <see cref="TimestampSettings"/> and no signing certificate is used.
    /// Set by <see cref="PKCS7Detached(Aspose.Pdf.TimestampSettings)"/>.</summary>
    internal bool IsDocumentTimestamp { get; set; }

    /// <summary>OCSP / revocation-check settings; honoured by the signer
    /// when set.</summary>
    public Aspose.Pdf.OcspSettings OcspSettings { get; set; } = new();

    /// <summary>Visual-appearance knobs honoured by
    /// <see cref="Security.PdfSigner.SignWithAppearance"/> when the
    /// signature is signed via a visible-rect overload.</summary>
    public SignatureCustomAppearance? CustomAppearance { get; set; }

    /// <summary>Optional external-signer callback. When set, the signer
    /// hands the to-be-signed hash to the implementation and embeds the
    /// returned PKCS#7 envelope verbatim — letting an HSM / smartcard /
    /// remote-signing service produce the signature without exposing the
    /// private key.</summary>
    public SignHash? CustomSignHash { get; set; }

    /// <summary>Digest requested through a (certificate, digest) constructor.
    /// <see cref="DigestHashAlgorithm.Auto"/> — the default — lets the signer
    /// pick from the /SubFilter.</summary>
    internal DigestHashAlgorithm RequestedDigest { get; set; } = DigestHashAlgorithm.Auto;

    /// <summary>Gets or sets whether signing also embeds the signer certificate in a Document Security Store
    /// (/DSS) so the signature stays verifiable later. Defaults to false.</summary>
    public bool UseLtv { get; set; }

    /// <summary>When true, the signer skips the byte-range estimation pass and
    /// uses <see cref="DefaultSignatureLength"/> directly when reserving
    /// space for the PKCS#7 /Contents hex string. Real — consumed by
    /// <see cref="Facades.PdfFileSignature.Sign(int, string, string, string, bool, System.Drawing.Rectangle, Signature)"/>.</summary>
    public bool AvoidEstimatingSignatureLength { get; set; }

    /// <summary>Number of bytes reserved for the PKCS#7 /Contents hex
    /// string. Honoured by the signer; 0 falls back to PdfSigner's
    /// 8192-byte default.</summary>
    public int DefaultSignatureLength { get; set; }

    /// <summary>When true, the visible signature appearance includes the
    /// signer's Reason/Location/ContactInfo as text. Honoured by
    /// <see cref="Security.PdfSigner.SignWithAppearance"/>.</summary>
    public bool ShowProperties { get; set; }

    internal string? FieldName { get; set; }
    internal string? Filter { get; set; }
    internal string? SubFilter { get; set; }
    internal byte[]? ContentsRaw { get; set; }

    /// <summary>The signer certificate(s) from the signature's /Cert entry,
    /// present only for the raw adbe.x509.rsa_sha1 (PKCS#1) handler where the
    /// certificate lives in the signature dictionary rather than in a CMS.</summary>
    internal System.Collections.Generic.List<byte[]>? CertRaw { get; set; }

    /// <summary>Verify the loaded signature's PKCS#7 against the original
    /// PDF bytes — returns true when the signature decodes and validates.
    /// Only usable on Signature instances produced by reading an existing
    /// PDF (via <see cref="EnumerateSignatures"/>); freshly-constructed
    /// signing-side Signature instances have no signed bytes to verify.</summary>
    public bool Verify()
    {
        if (_sourceDocumentBytes is null || FieldName is null) return false;
        return Security.PdfSigner.Verify(_sourceDocumentBytes, FieldName);
    }

    /// <summary>Non-throwing verification: the verdict - valid, invalid, or undefined when
    /// the signature's structure is a recognised forgery - lands in
    /// <paramref name="verificationResult"/>; the return says whether a verdict was reached.</summary>
    public bool TryVerify(out Security.VerificationResult verificationResult)
    {
        if (_sourceDocumentBytes is null || FieldName is null)
        {
            verificationResult = Security.VerificationResult.Undefined("The signature was not read from a document.");
            return false;
        }
        var forgery = DetectForgery(_sourceDocumentBytes.Length);
        if (forgery is not null)
        {
            verificationResult = Security.VerificationResult.Compromised(forgery);
            return false;
        }
        try
        {
            verificationResult = Verify()
                ? Security.VerificationResult.Valid()
                : Security.VerificationResult.Invalid($"Signature '{FieldName}' failed cryptographic verification.");
            return true;
        }
        catch (Exception e)
        {
            verificationResult = Security.VerificationResult.Undefined(e.Message, e);
            return false;
        }
    }

    /// <summary>
    /// Detect a Universal Signature Forgery (USF) or Signature Wrapping Attack (SWA) on this signature.
    /// A signature whose CMS envelope (/Contents) is absent/empty, or whose
    /// /ByteRange is absent or malformed, is a forgery — verifiers that skip
    /// validation for such structures would wrongly report it as valid. Returns
    /// a description of the forgery, or <c>null</c> when the signature is sound.
    /// </summary>
    internal string? DetectForgery(long fileLength)
    {
        var sig = this;
        var signName = FieldName;

        const string usf = "Universal Signature Forgery";

        // Absent/empty CMS envelope or absent/malformed byte range.
        if (sig.ByteRangeRaw is null || sig.ByteRangeRaw.Length < 4
            || sig.ContentsRaw is null || sig.ContentsRaw.Length == 0)
        {
            return $"Signature '{signName}' is compromised by USF ({usf}): " +
                   "its /Contents or /ByteRange is missing, empty, or malformed.";
        }

        // A hollow CMS envelope: /Contents is present and well-formed hex, but
        // every byte is zero. Real envelopes zero-pad only the tail after the
        // DER structure; an all-zero envelope holds no signature at all, and a
        // verifier that trusts the padding would report it valid.
        var contents = sig.ContentsRaw;
        var allZero = true;
        for (var i = 0; i < contents.Length; i++)
        {
            if (contents[i] != 0) { allZero = false; break; }
        }
        if (allZero)
        {
            return $"Signature '{signName}' is compromised by USF ({usf}): " +
                   "its /Contents holds no signature data.";
        }

        // A fabricated /ByteRange whose covered region extends beyond the end of
        // the file (or starts before it): the signed range cannot be honoured, so
        // the signature validates nothing. A sound range never reaches past EOF.
        var br = sig.ByteRangeRaw;
        if (br[0] < 0 || br[1] < 0 || br[2] < 0 || br[3] < 0
            || br[2] + br[3] > fileLength || br[0] + br[1] > br[2])
        {
            return $"Signature '{signName}' is compromised by USF ({usf}): " +
                   "its /ByteRange does not correspond to the document.";
        }

        // SWA (Signature Wrapping Attack): the /ByteRange and /Contents are
        // structurally valid, but the unsigned gap between the two signed ranges
        // (which must hold only the /Contents hex string <…>) is larger than that
        // hex string. The surplus unsigned bytes hide injected objects the
        // signature does not cover. On-disk hex length = 2·bytes + 2 delimiters;
        // a small slack absorbs optional whitespace around the delimiters.
        var gap = br[2] - (br[0] + br[1]);
        var contentsHexLen = 2L * sig.ContentsRaw.Length + 2;
        if (gap - contentsHexLen > 128)
        {
            return $"Signature '{signName}' is compromised by SWA (Signature Wrapping Attack): " +
                   "the /ByteRange leaves unsigned content in the /Contents gap.";
        }
        return null;
    }

    /// <summary>Verify with explicit options + a result DTO. Real — runs
    /// the cryptographic-bytes check on the loaded source PDF; revocation
    /// (OCSP/CRL) not implemented so Strict + non-Auto method reports
    /// Unknown instead of lying about a check that didn't run.</summary>
    public bool Verify(Security.ValidationOptions options, out Security.ValidationResult validationResult)
    {
        var ok = Verify();
        validationResult = BuildValidationResult(ok, options);
        return ok;
    }

    /// <summary>Public-key-pinned verify. The signer certificate is
    /// extracted from /Contents and compared to <paramref name="publicKeyCertificate"/>
    /// via Thumbprint before reporting Valid.</summary>
    public bool Verify(System.Security.Cryptography.X509Certificates.X509Certificate2 publicKeyCertificate,
        Security.ValidationOptions options, out Security.ValidationResult validationResult)
    {
        var ok = Verify();
        if (ok && publicKeyCertificate is not null && _sourceDocumentBytes is not null && ContentsRaw is not null)
        {
            try
            {
                var certDer = Aspose.Pdf.Security.CmsParser.GetFirstCertificateDer(ContentsRaw);
                var signerCert = certDer is null
                    ? null
                    : Compat.LoadCertificate(certDer);
                if (signerCert is null
                    || !signerCert.Thumbprint.Equals(publicKeyCertificate.Thumbprint,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    validationResult = new Security.ValidationResult(
                        Security.ValidationStatus.Invalid,
                        "Signature's signer certificate does not match the supplied public certificate.");
                    return false;
                }
            }
            catch
            {
                validationResult = new Security.ValidationResult(
                    Security.ValidationStatus.Unknown,
                    "PKCS#7 decode failed — cannot compare signer certificate.");
                return false;
            }
        }
        validationResult = BuildValidationResult(ok, options);
        return ok;
    }

    private Security.ValidationResult BuildValidationResult(bool ok, Security.ValidationOptions? options)
    {
        if (!ok)
            return new Security.ValidationResult(Security.ValidationStatus.Invalid,
                $"Signature {FieldName} failed cryptographic verification.");
        if (options is not null
            && options.ValidationMode == Security.ValidationMode.Strict
            && options.ValidationMethod != Security.ValidationMethod.Auto)
        {
            return new Security.ValidationResult(Security.ValidationStatus.Unknown,
                "Cryptographic check passed but revocation validation (OCSP/CRL) is not implemented in this FOSS build.");
        }
        return new Security.ValidationResult(Security.ValidationStatus.Valid,
            $"Signature {FieldName} verified.");
    }

    /// <summary>Parse the PKCS#7 envelope (when loaded from an existing
    /// signature) to report the signing-algorithm / digest-algorithm /
    /// cryptographic-standard triple.</summary>
    public Security.SignatureAlgorithmInfo GetSignatureAlgorithmInfo()
    {
        var leaf = FieldName;
        if (leaf is not null)
        {
            var dot = leaf.LastIndexOf('.');
            if (dot >= 0) leaf = leaf.Substring(dot + 1);
        }
        return Security.SignatureAlgorithmInfo.FromPkcs7(ContentsRaw, SubFilter, leaf);
    }

    /// <summary>Source bytes the signature was loaded from — set by
    /// <see cref="EnumerateSignatures"/> so <c>Verify</c> has the
    /// original byte stream to hash against.</summary>
    internal byte[]? _sourceDocumentBytes;

    internal static IEnumerable<Signature> EnumerateSignatures(Document document)
    {
        if (document?.Reader is null) yield break;
        var sourceBytes = document.Reader.RawData;
        var form = document.Form;
        if (form is null) yield break;

        foreach (var field in form.Fields)
        {
            if (field.Type != FieldType.Signature) continue;
            // A field with no /V is an unsigned placeholder, not a signature.
            if (!field.Dict.ContainsKey("V")) continue;

            var sigDict = document.Reader.ResolveDict(field.Dict.Get("V"));
            if (sigDict is null)
            {
                // /V is present but does not resolve to a proper signature dictionary
                // (e.g. a null value). That is a malformed/forged signature — surface
                // it (with empty byte range/contents) so verification flags the forgery
                // rather than silently reporting "no signatures".
                yield return new Signature { FieldName = field.FullName, _sourceDocumentBytes = sourceBytes };
                continue;
            }

            var sig = FromDict(sigDict, document.Reader, field.FullName);
            sig._sourceDocumentBytes = sourceBytes;
            yield return sig;
        }
    }

    internal static bool HasAny(Document document)
    {
        var form = document.Form;
        if (form is null) return false;
        foreach (var field in form.Fields)
        {
            if (field.Type == FieldType.Signature && field.Dict.ContainsKey("V"))
                return true;
        }
        return false;
    }

    internal static Signature FromDict(PdfDictionary sigDict, PdfReader reader, string? fieldName)
    {
        // Reconstruct the concrete signature subtype from /SubFilter so that
        // callers can pattern-match `.Signature is PKCS7` etc. on a loaded
        // document (Table 252, ISO 32000-1 §12.8.3):
        //   adbe.x509.rsa_sha1  → PKCS#1 (raw RSA)     → PKCS1
        //   adbe.pkcs7.sha1     → PKCS#7 envelope       → PKCS7
        //   adbe.pkcs7.detached → detached PKCS#7 (CMS) → PKCS7Detached
        //   ETSI.CAdES.detached → CAdES detached (CMS)  → PKCS7Detached
        var subFilter = sigDict.GetName("SubFilter");
        Signature sig = subFilter switch
        {
            "adbe.x509.rsa_sha1" => new PKCS1(),
            "adbe.pkcs7.sha1" => new PKCS7(),
            "adbe.pkcs7.detached" => new PKCS7Detached(),
            "ETSI.CAdES.detached" => new PKCS7Detached(),
            _ => new Signature(),
        };
        sig.FieldName = fieldName;
        sig.Authority = GetString(sigDict, reader, "Name") ?? string.Empty;
        sig.Reason = GetString(sigDict, reader, "Reason") ?? string.Empty;
        sig.Location = GetString(sigDict, reader, "Location") ?? string.Empty;
        sig.ContactInfo = GetString(sigDict, reader, "ContactInfo") ?? string.Empty;
        sig.Filter = sigDict.GetName("Filter");
        sig.SubFilter = subFilter;
        sig.ByteRangeRaw = GetByteRange(sigDict, reader);
        sig.ContentsRaw = GetContents(sigDict, reader);
        sig.CertRaw = GetCerts(sigDict, reader);
        sig.Date = ParseDate(GetString(sigDict, reader, "M"));
        return sig;
    }

    private static string? GetString(PdfDictionary dict, PdfReader reader, string key)
    {
        var obj = reader.Resolve(dict.Get(key));
        return obj is PdfString s ? s.ToText() : null;
    }

    private static long[]? GetByteRange(PdfDictionary dict, PdfReader reader)
    {
        var arr = reader.Resolve(dict.Get("ByteRange")) as PdfArray;
        if (arr is null) return null;

        var result = new long[arr.Count];
        for (var i = 0; i < arr.Count; i++)
        {
            result[i] = arr[i] is PdfInteger n ? n.Value : 0;
        }
        return result;
    }

    private static byte[]? GetContents(PdfDictionary dict, PdfReader reader)
    {
        var obj = reader.Resolve(dict.Get("Contents"));
        return obj is PdfString s ? s.Value : null;
    }

    /// <summary>Read the /Cert entry — a single certificate string or an array
    /// of them (DER) — used by the raw adbe.x509.rsa_sha1 handler.</summary>
    private static System.Collections.Generic.List<byte[]>? GetCerts(PdfDictionary dict, PdfReader reader)
    {
        var obj = reader.Resolve(dict.Get("Cert"));
        if (obj is PdfString s) return new() { s.Value };
        if (obj is PdfArray arr)
        {
            var list = new System.Collections.Generic.List<byte[]>();
            foreach (var e in arr)
                if (reader.Resolve(e) is PdfString es) list.Add(es.Value);
            return list.Count > 0 ? list : null;
        }
        return null;
    }

    private static DateTime ParseDate(string? dateStr)
    {
        return DocumentInfo.ParseDate(dateStr) ?? DateTime.MinValue;
    }
}

/// <summary>A PKCS#1 signature configuration. Signing with it requires an RSA key; a loaded
/// <c>adbe.x509.rsa_sha1</c> signature reads back as this type.</summary>
public class PKCS1 : Signature
{
    /// <summary>The signature appearance image bytes, when constructed from
    /// a raw image stream rather than a PFX. Used by the visible-signature
    /// writer to draw the image inside the signature widget.</summary>
    internal byte[]? AppearanceImage { get; private set; }

    /// <summary>Creates a PKCS#1 signature configuration with no signing certificate.</summary>
    public PKCS1() { }
    /// <summary>Creates a PKCS#1 signature configuration from a PFX file and its password.</summary>
    public PKCS1(string pfx, string password) : base(pfx, password) { }
    /// <summary>Creates a PKCS#1 signature configuration from a PFX stream and its password.</summary>
    public PKCS1(Stream pfx, string password) : base(pfx, password) { }

    /// <summary>Construct a PKCS#1 signature configuration whose visible
    /// signature renders <paramref name="image"/> as the appearance graphic.
    /// The image bytes are stored verbatim; the certificate must be supplied
    /// separately via SetCertificate or via a Sign(…, Signature) overload.</summary>
    public PKCS1(Stream image)
    {
        if (image is null) return;
        using var ms = new MemoryStream();
        image.CopyTo(ms);
        AppearanceImage = ms.ToArray();
    }
}

/// <summary>A PKCS#7 signature configuration. <c>PdfFileSignature</c> signs it with the <c>adbe.pkcs7.sha1</c>
/// handler, and a loaded signature using that handler reads back as this type.</summary>
public class PKCS7 : Signature
{
    /// <summary>Creates a PKCS#7 signature configuration with no signing certificate.</summary>
    public PKCS7() { }
    /// <summary>Creates a PKCS#7 signature configuration from a PFX file and its password.</summary>
    public PKCS7(string pfx, string password) : base(pfx, password) { }
    /// <summary>Creates a PKCS#7 signature configuration from a PFX stream and its password.</summary>
    public PKCS7(Stream pfx, string password) : base(pfx, password) { }
}

/// <summary>A detached PKCS#7 (CMS) signature configuration. The signer
/// emits an <c>adbe.pkcs7.detached</c> /SubFilter signature for every
/// <see cref="Signature"/> subtype, so this differs from <see cref="PKCS7"/>
/// only in name — it documents caller intent to produce a detached envelope.</summary>
public class PKCS7Detached : Signature
{
    /// <summary>Creates a detached PKCS#7 signature configuration with no signing certificate.</summary>
    public PKCS7Detached() { }
    /// <summary>Creates a detached PKCS#7 signature configuration from a PFX file and its password.</summary>
    public PKCS7Detached(string pfx, string password) : base(pfx, password) { }
    /// <summary>Creates a detached PKCS#7 signature configuration from a PFX stream and its password.</summary>
    public PKCS7Detached(Stream pfx, string password) : base(pfx, password) { }

    /// <summary>Sign with an explicitly chosen message digest rather than the
    /// /SubFilter default. <see cref="DigestHashAlgorithm.Auto"/> keeps the
    /// default (SHA-256).</summary>
    public PKCS7Detached(string pfx, string password, DigestHashAlgorithm digestHashAlgorithm)
        : base(pfx, password)
    {
        RequestedDigest = digestHashAlgorithm;
    }

    /// <summary>Creates a detached PKCS#7 signature configuration from a PFX stream and its password, signing with
    /// the given message digest. <c>DigestHashAlgorithm.Auto</c> keeps the default (SHA-256).</summary>
    public PKCS7Detached(Stream pfx, string password, DigestHashAlgorithm digestHashAlgorithm)
        : base(pfx, password)
    {
        RequestedDigest = digestHashAlgorithm;
    }

    /// <summary>Construct a standalone RFC 3161 document-timestamp
    /// configuration. When signed, the signer requests a timestamp token from
    /// the TSA in <paramref name="timestampSettings"/> and writes an
    /// <c>ETSI.RFC3161</c> DocTimeStamp signature — no signing certificate is
    /// required.</summary>
    public PKCS7Detached(Aspose.Pdf.TimestampSettings timestampSettings)
    {
        TimestampSettings = timestampSettings ?? new Aspose.Pdf.TimestampSettings();
        IsDocumentTimestamp = true;
    }
}

/// <summary>A detached PKCS#7 signature configuration built around an
/// already-loaded platform certificate — typically from the OS certificate
/// store, a smartcard or an HSM — rather than a PFX file. When the
/// certificate carries no private key, supply the crypto via
/// <see cref="Signature.CustomSignHash"/>.</summary>
public class ExternalSignature : Signature
{
    /// <summary>The certificate this instance was constructed with. Shadows
    /// the internal PFX-loaded certificate of the base class, which is wired
    /// from this one so the existing signing pipeline applies.</summary>
    public new System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate { get; }

    public ExternalSignature(
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
        : this(certificate, detached: true)
    {
    }

    /// <summary><paramref name="detached"/> selects the handler, exactly as the
    /// <see cref="PKCS7"/> / <see cref="PKCS7Detached"/> pair does: true emits an
    /// <c>adbe.pkcs7.detached</c> envelope over a SHA-256 digest, false the
    /// <c>adbe.pkcs7.sha1</c> handler over a SHA-1 digest.</summary>
    public ExternalSignature(
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, bool detached)
    {
        Detached = detached;
        Certificate = certificate ?? throw new System.ArgumentNullException(nameof(certificate));
        base.Certificate = Security.PdfCertificate.FromX509(certificate);
    }

    /// <summary>Which PKCS#7 handler this configuration signs with; see the
    /// (certificate, detached) constructor.</summary>
    internal bool Detached { get; } = true;

    public ExternalSignature(
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        DigestHashAlgorithm digestHashAlgorithm)
        : this(certificate, detached: true)
    {
        RequestedDigest = digestHashAlgorithm;
    }

    /// <summary>Construct from a base64-encoded public certificate (no
    /// private key) for deferred/external signing via
    /// <see cref="Signature.CustomSignHash"/>.</summary>
    public ExternalSignature(string base64Certificate, bool detached)
    {
        Detached = detached;
        if (string.IsNullOrEmpty(base64Certificate))
            throw new System.ArgumentNullException(nameof(base64Certificate));
        Certificate = Compat.LoadCertificate(System.Convert.FromBase64String(base64Certificate));
        base.Certificate = Security.PdfCertificate.FromX509(Certificate);
    }
}
