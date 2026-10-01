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

    /// <summary>Returns true when the named signature is intact and valid; false when the name is null.</summary>
    public bool VerifySignature(SignatureName signName)
        => signName is not null && VerifySignature(signName.FullName);

    /// <summary>Returns the signing certificate of the named signature as a stream of DER-encoded (.cer) bytes, or null when it cannot be found or the name is null.</summary>
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
            certStream.ReadExactly(bytes);
            certificate = Compat.LoadCertificate(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns true when the named signature is intact and valid. Throws when the signature is detected as forged.</summary>
    public bool VerifySignature(string signName)
    {
        var forgery = DetectSignatureForgery(signName);
        if (forgery is not null)
            throw new Aspose.Pdf.Sanitization.SanitizationException(forgery);
        var input = RequireBound();
        return PdfSigner.Verify(input, signName, _password);
    }

    /// <summary>The forgery verdict for the named signature - see
    /// <see cref="Signature.DetectForgery(long)"/> - or null when it is sound or absent.</summary>
    private string? DetectSignatureForgery(string signName)
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        var sig = Signature.EnumerateSignatures(doc).FirstOrDefault(s => s.FieldName == signName);
        return sig?.DetectForgery(input.Length);
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

    /// <summary>Returns true when the bound document carries usage rights (a /UR or /UR3 entry in /Perms).</summary>
    public bool ContainsUsageRights()
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        var perms = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("Perms"));
        if (perms is null) return false;
        return perms.ContainsKey("UR") || perms.ContainsKey("UR3");
    }

    /// <summary>Removes the usage rights (/UR and /UR3) from the bound document.</summary>
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
        (var valid, validationResult) = Validate(basic, options, signName);
        return valid;
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
                certStream.ReadExactly(bytes);
                var signerCert = Compat.LoadCertificate(bytes);
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
        (var valid, validationResult) = Validate(basic, options, signName);
        return valid;
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
    /// <returns>Whether the signature passes under the options, and the result that says why.</returns>
    private (bool valid, Security.ValidationResult result) Validate(bool ok, Security.ValidationOptions? options, string signName)
    {
        if (!ok)
            return (false, new Security.ValidationResult(Security.ValidationStatus.Invalid,
                $"Signature {signName} failed cryptographic verification."));
        if (options is null || options.ValidationMode == Security.ValidationMode.None)
            return (true, options is null
                ? new Security.ValidationResult(Security.ValidationStatus.Valid, $"Signature {signName} verified.")
                : new Security.ValidationResult(Security.ValidationStatus.Undefined,
                    "No validation requested (ValidationMode.None)."));
        var strict = options.ValidationMode == Security.ValidationMode.Strict;
        if (options.CheckCertificateChain && UntrustedChainReason(signName) is { } why)
            return (!strict, new Security.ValidationResult(Security.ValidationStatus.Undefined,
                $"Signature {signName} passed cryptographic verification but its certificate chain " +
                $"could not be validated to a trusted root: {why}"));
        if (options.ValidationMethod != Security.ValidationMethod.Auto)
            return (!strict, new Security.ValidationResult(Security.ValidationStatus.Undefined,
                "Failed to obtain certificate revocation list."));
        return (true, new Security.ValidationResult(Security.ValidationStatus.Valid,
            $"Signature {signName} verified."));
    }

    /// <summary>Resolve the named signature's signer chain to a trusted root
    /// (see <see cref="Security.SignerChainTrust"/>): the certificates the
    /// signature carries — the CMS certificate set, or the /Cert array of a
    /// PKCS#1 signature — judged at the signing time (/M), or now when the
    /// signature carries no date.</summary>
    /// <returns>Why the chain is not trusted, or null when it resolves to a trusted root.</returns>
    private string? UntrustedChainReason(string signName)
    {
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
            return Security.SignerChainTrust.UntrustedReason(certs, when);
        }
        return "signature not found";
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
