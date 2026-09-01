using Aspose.Pdf.Forms;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileSignature
{
    /// <summary>Read the certifying signature's /DocMDP /P access-permission
    /// level (1, 2, or 3 per PDF 32000-1 §12.8.2.2). Returns
    /// <see cref="Forms.DocMDPAccessPermissions.NoChanges"/> (level 1)
    /// when no /DocMDP entry is present.</summary>
    public Forms.DocMDPAccessPermissions GetAccessPermissions()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
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

    public bool VerifySignature(SignatureName signName)
        => signName is not null && VerifySignature(signName.FullName);

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
        var forgery = DetectSignatureForgery(signName);
        if (forgery is not null)
            throw new Aspose.Pdf.Sanitization.SanitizationException(forgery);
        var input = RequireBound();
        return PdfSigner.Verify(input, signName, _password);
    }

    /// <summary>
    /// Detect a Universal Signature Forgery (USF) attack on the named signature.
    /// A signature whose CMS envelope (/Contents) is absent/empty, or whose
    /// /ByteRange is absent or malformed, is a forgery — verifiers that skip
    /// validation for such structures would wrongly report it as valid. Returns
    /// a description of the forgery, or <c>null</c> when the signature is sound.
    /// </summary>
    private string? DetectSignatureForgery(string signName)
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        var sig = Signature.EnumerateSignatures(doc).FirstOrDefault(s => s.FieldName == signName);
        if (sig is null) return null;

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
            || br[2] + br[3] > input.Length || br[0] + br[1] > br[2])
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

    /// <summary>
    /// Non-throwing signature verification. Returns whether the signature is valid
    /// and reports the outcome (including forgery detection) via
    /// <paramref name="verificationResult"/>.
    /// </summary>
    public bool TryVerifySignature(SignatureName signName, out VerificationResult verificationResult)
    {
        if (signName is null)
        {
            verificationResult = VerificationResult.Undefined("Signature name is null.");
            return false;
        }
        try
        {
            var ok = VerifySignature(signName.FullName);
            verificationResult = ok
                ? VerificationResult.Valid()
                : VerificationResult.Invalid($"Signature '{signName.FullName}' failed cryptographic verification.");
            return ok;
        }
        catch (Aspose.Pdf.Sanitization.SanitizationException e)
        {
            // A recognised forgery attack — undefined verdict, flagged compromised.
            verificationResult = VerificationResult.Compromised(e.Message, e);
            return false;
        }
        catch (Exception e)
        {
            verificationResult = VerificationResult.Undefined(e.Message, e);
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
        using var doc = OpenDoc(input);
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
        using var doc = OpenDoc(input);
        var perms = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("Perms"));
        if (perms is null) return false;
        return perms.ContainsKey("UR") || perms.ContainsKey("UR3");
    }

    public void RemoveUsageRights()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
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

    /// <summary>Verifies that a signed signature is intact. Alias for
    /// <see cref="VerifySignature(string)"/>.</summary>
    public bool VerifySigned(string signName) => VerifySignature(signName);

    /// <summary>Verify a signature with explicit options + return a
    /// <see cref="Security.ValidationResult"/> describing the outcome.
    /// Honours <see cref="Security.ValidationOptions.CheckCertificateChain"/>
    /// + ValidationMode/Method. Revocation checks (OCSP/CRL) are accepted
    /// for surface compatibility but the FOSS build only runs the
    /// cryptographic-bytes check — when ValidationMode is Strict and
    /// revocation is requested, results are reported Unknown rather than
    /// falsely Valid.</summary>
    public bool VerifySignature(string signName, Security.ValidationOptions options,
        out Security.ValidationResult validationResult)
    {
        var basic = VerifySignature(signName);
        return Validate(basic, options, signName, out validationResult);
    }

    public bool VerifySignature(string signName,
        System.Security.Cryptography.X509Certificates.X509Certificate2 publicKeyCertificate,
        Security.ValidationOptions options,
        out Security.ValidationResult validationResult)
    {
        // Public-key certificate-pinned verification: confirm the
        // signature's signer cert matches the supplied public certificate
        // before reporting Valid. The pin establishes IDENTITY only — the
        // reference still walks the chain when the options ask for it (a
        // pinned self-signed test certificate reports Undefined under
        // CheckCertificateChain), so the options apply unchanged afterwards.
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
        return Validate(basic, options, signName, out validationResult);
    }

    /// <summary>Cert-pinned verify without ValidationOptions. Returns true
    /// iff the signature is intact AND the signer cert thumbprint matches
    /// <paramref name="publicKeyCertificate"/>. No chain walk: this overload
    /// reports true for a self-signed test certificate that no chain
    /// check could accept, so this overload is identity + integrity only.</summary>
    public bool VerifySignature(Facades.SignatureName signName,
        System.Security.Cryptography.X509Certificates.X509Certificate2 publicKeyCertificate)
    {
        if (signName is null) return false;
        return VerifySignature(signName.FullName, publicKeyCertificate,
            options: new Security.ValidationOptions { CheckCertificateChain = false }, out _);
    }

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

    /// <summary>Apply <paramref name="options"/> on top of the cryptographic check
    /// and produce the (result, verified) pair the option-taking overloads return.
    /// The policy was fixed across the full
    /// mode × chain × method matrix:
    /// <list type="bullet">
    /// <item>ValidationMode.None performs no validation — status Undefined, verified
    /// = the bytes check.</item>
    /// <item>CheckCertificateChain resolves the signer's chain per
    /// <see cref="Security.SignerChainTrust"/>; a chain that does not reach a trusted
    /// root is Undefined. Under Strict that also fails the verify; under OnlyCheck
    /// the verify still reports the bytes result.</item>
    /// <item>Revocation (OCSP/CRL) is not implemented: a non-Auto method is Undefined
    /// with its own message, failing the verify only under Strict.</item>
    /// </list></summary>
    private bool Validate(bool ok, Security.ValidationOptions? options, string signName,
        out Security.ValidationResult validationResult)
    {
        if (!ok)
        {
            validationResult = new Security.ValidationResult(Security.ValidationStatus.Invalid,
                $"Signature {signName} failed cryptographic verification.");
            return false;
        }
        if (options is null || options.ValidationMode == Security.ValidationMode.None)
        {
            validationResult = options is null
                ? new Security.ValidationResult(Security.ValidationStatus.Valid, $"Signature {signName} verified.")
                : new Security.ValidationResult(Security.ValidationStatus.Undefined,
                    "No validation requested (ValidationMode.None).");
            return true;
        }
        var strict = options.ValidationMode == Security.ValidationMode.Strict;
        if (options.CheckCertificateChain && !SignerChainIsTrusted(signName, out var why))
        {
            validationResult = new Security.ValidationResult(Security.ValidationStatus.Undefined,
                $"Signature {signName} passed cryptographic verification but its certificate chain " +
                $"could not be validated to a trusted root: {why}");
            return !strict;
        }
        if (options.ValidationMethod != Security.ValidationMethod.Auto)
        {
            validationResult = new Security.ValidationResult(Security.ValidationStatus.Undefined,
                "Failed to obtain certificate revocation list.");
            return !strict;
        }
        validationResult = new Security.ValidationResult(Security.ValidationStatus.Valid,
            $"Signature {signName} verified.");
        return true;
    }

    /// <summary>Resolve the named signature's signer chain to a trusted root
    /// (see <see cref="Security.SignerChainTrust"/>): the certificates the
    /// signature carries — the CMS certificate set, or the /Cert array of a
    /// PKCS#1 signature — judged at the signing time (/M), or now when the
    /// signature carries no date.</summary>
    private bool SignerChainIsTrusted(string signName, out string reason)
    {
        reason = "signature not found";
        var input = RequireBound();
        using var doc = OpenDoc(input);
        foreach (var sig in Signature.EnumerateSignatures(doc))
        {
            if (sig.FieldName != signName && LeafName(sig.FieldName) != signName) continue;
            var certs = sig.ContentsRaw is not null
                ? Security.CmsParser.GetCertificatesDer(sig.ContentsRaw)
                : new List<byte[]>();
            if (certs.Count == 0 && sig.CertRaw is { Count: > 0 } pkcs1Certs) certs = pkcs1Certs;
            var when = sig.Date != default ? sig.Date : DateTime.UtcNow;
            return Security.SignerChainTrust.IsTrusted(certs, when, out reason);
        }
        return false;
    }

    private static string? LeafName(string? fullName)
    {
        if (fullName is null) return null;
        var dot = fullName.LastIndexOf('.');
        return dot >= 0 ? fullName.Substring(dot + 1) : fullName;
    }

    /// <summary>Per-signature algorithm/digest/standard triple, parsed
    /// from each signature's PKCS#7 /Contents.</summary>
    public List<Security.SignatureAlgorithmInfo> GetSignaturesInfo()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        var result = new List<Security.SignatureAlgorithmInfo>();
        foreach (var sig in Signature.EnumerateSignatures(doc))
        {
            var leaf = sig.FieldName;
            if (leaf is not null)
            {
                var dot = leaf.LastIndexOf('.');
                if (dot >= 0) leaf = leaf.Substring(dot + 1);
            }
            // An ETSI.RFC3161 document timestamp reports as a TimestampAlgorithmInfo
            // (its /Contents is a TSTInfo token, not an ordinary CMS signature).
            result.Add(sig.SubFilter == "ETSI.RFC3161"
                ? Security.SignatureAlgorithmInfo.FromTimestampToken(sig.ContentsRaw, leaf)
                : Security.SignatureAlgorithmInfo.FromPkcs7(sig.ContentsRaw, sig.SubFilter, leaf));
        }
        return result;
    }
}
