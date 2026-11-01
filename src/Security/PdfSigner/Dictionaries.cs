using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

public sealed partial class PdfSigner
{
    /// <summary>The PKCS#1 handler: /Contents is a bare RSA signature over SHA-1, /Cert the certificate.</summary>
    internal const string SubFilterRsaSha1 = "adbe.x509.rsa_sha1";

    /// <summary>The enveloping PKCS#7 handler: the CMS carries the SHA-1 digest of the signed bytes.</summary>
    internal const string SubFilterPkcs7Sha1 = "adbe.pkcs7.sha1";

    private static PdfDictionary BuildSignatureValueDict(PdfCertificate cert,
        SignatureOptions options, int contentsSize)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Sig"));
        dict.Set("Filter", new PdfName("Adobe.PPKLite"));
        dict.Set("SubFilter", new PdfName(options.SubFilter));

        // Name from the caller's signer name when supplied, else the certificate subject.
        var name = string.IsNullOrEmpty(options.SignerName) ? cert.SubjectName : options.SignerName;
        dict.Set("Name", EncodePdfText(name));

        if (options.Reason is not null)
            dict.Set("Reason", EncodePdfText(options.Reason));
        if (options.Location is not null)
            dict.Set("Location", EncodePdfText(options.Location));
        if (options.ContactInfo is not null)
            dict.Set("ContactInfo", EncodePdfText(options.ContactInfo));

        // Signing date — honour the caller's date when supplied, else now.
        // The date components are written verbatim (the reader ignores the
        // trailing UTC offset), so a caller-supplied local time round-trips.
        var when = options.SigningDate ?? DateTime.UtcNow;
        var dateStr = Compat.Format(System.Globalization.CultureInfo.InvariantCulture, $"D:{when:yyyyMMddHHmmss}+00'00'");
        dict.Set("M", new PdfString(Compat.Latin1.GetBytes(dateStr)));

        // ByteRange placeholder — will be patched later
        // Use a distinctive pattern for easy replacement
        var byteRangeArray = new PdfArray();
        byteRangeArray.Add(new PdfInteger(0));
        byteRangeArray.Add(new PdfInteger(9999999999)); // placeholder
        byteRangeArray.Add(new PdfInteger(9999999999)); // placeholder
        byteRangeArray.Add(new PdfInteger(9999999999)); // placeholder
        dict.Set("ByteRange", byteRangeArray);

        // Certifying (author) signature: a /DocMDP transform reference, written
        // BEFORE signing so it is inside the ByteRange the signature covers.
        if (options.DocMdpPermissions is int perms)
        {
            var transformParams = new PdfDictionary();
            transformParams.Set("Type", new PdfName("TransformParams"));
            transformParams.Set("P", new PdfInteger(perms));
            transformParams.Set("V", new PdfName("1.2"));

            var refDict = new PdfDictionary();
            refDict.Set("Type", new PdfName("SigRef"));
            refDict.Set("TransformMethod", new PdfName("DocMDP"));
            refDict.Set("TransformParams", transformParams);

            var refs = new PdfArray();
            refs.Add(refDict);
            dict.Set("Reference", refs);
        }

        // The raw PKCS#1 handler has no CMS to carry the certificate: /Cert does.
        if (options.SubFilter == SubFilterRsaSha1)
            dict.Set("Cert", new PdfString(cert.CertificateDer, isHex: true));

        // /Contents placeholder — hex string of zeros
        var contentsPlaceholder = new byte[contentsSize];
        dict.Set("Contents", new PdfString(contentsPlaceholder, isHex: true));

        return dict;
    }

    /// <summary>Build the value dictionary of an RFC 3161 document timestamp
    /// (PAdES DocTimeStamp): /Type /DocTimeStamp, /SubFilter /ETSI.RFC3161, plus
    /// ByteRange and /Contents placeholders. No /Name/Reason/date — the trusted
    /// time comes from the TSA token written into /Contents.</summary>
    private static PdfDictionary BuildTimestampValueDict(int contentsSize)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("DocTimeStamp"));
        dict.Set("Filter", new PdfName("Adobe.PPKLite"));
        dict.Set("SubFilter", new PdfName("ETSI.RFC3161"));

        var byteRangeArray = new PdfArray();
        byteRangeArray.Add(new PdfInteger(0));
        byteRangeArray.Add(new PdfInteger(9999999999)); // placeholder
        byteRangeArray.Add(new PdfInteger(9999999999)); // placeholder
        byteRangeArray.Add(new PdfInteger(9999999999)); // placeholder
        dict.Set("ByteRange", byteRangeArray);

        var contentsPlaceholder = new byte[contentsSize];
        dict.Set("Contents", new PdfString(contentsPlaceholder, isHex: true));

        return dict;
    }

    private static PdfDictionary BuildSignatureFieldDict(string fieldName,
        int sigValObjNum, PdfIndirectRef? pageRef)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Sig"));
        dict.Set("T", new PdfString(Compat.Latin1.GetBytes(fieldName)));
        dict.Set("V", new PdfIndirectRef(sigValObjNum, 0));
        // Invisible signature (zero rect)
        var rect = new PdfArray();
        rect.Add(new PdfInteger(0));
        rect.Add(new PdfInteger(0));
        rect.Add(new PdfInteger(0));
        rect.Add(new PdfInteger(0));
        dict.Set("Rect", rect);
        // Set F flag (bit 2 = Hidden is NOT set, bit 3 = Print)
        dict.Set("F", new PdfInteger(4)); // Print flag

        // The page the widget belongs to
        if (pageRef is not null) dict.Set("P", pageRef);

        return dict;
    }

    /// <summary>The signing revision's form dictionary: the existing one with every entry it has
    /// (/DR, /DA, /NeedAppearances, /CO, /Q, /XFA ...), only /Fields and /SigFlags updated. Its
    /// inline values are copies, so encrypting them for an encrypted document leaves the
    /// document's own objects alone.</summary>
    private static PdfDictionary BuildAcroFormDict(Document doc, int sigFieldObjNum,
        bool appendField = true, string? replaceFieldNamed = null, PdfReader? reader = null)
    {
        var existingForm = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("AcroForm"));
        var dict = existingForm is null ? new PdfDictionary() : (PdfDictionary)CopyDirect(existingForm);

        // Copy existing fields if AcroForm already exists
        var fields = new PdfArray();
        if (doc.Form is not null)
        {
            var existingAcroForm = existingForm;
            if (existingAcroForm is not null)
            {
                var existingFields = doc.Reader.Resolve(existingAcroForm.Get("Fields")) as PdfArray;
                if (existingFields is not null)
                {
                    foreach (var f in existingFields)
                    {
                        // The adopted inline field is replaced by a reference to the
                        // object the signer writes for it, so the form lists it once.
                        if (replaceFieldNamed is not null && f is PdfDictionary inline
                            && (reader?.Resolve(inline.Get("T")) as PdfString)?.ToText() == replaceFieldNamed)
                        {
                            fields.Add(new PdfIndirectRef(sigFieldObjNum, 0));
                            continue;
                        }
                        fields.Add(CopyDirect(f));
                    }
                }
            }
        }

        // Add the new signature field (unless we reused a field already listed)
        if (appendField)
            fields.Add(new PdfIndirectRef(sigFieldObjNum, 0));
        dict.Set("Fields", fields);

        // SigFlags: 1 = SignaturesExist, 2 = AppendOnly → 3
        dict.Set("SigFlags", new PdfInteger(3));

        return dict;
    }

    /// <summary>A copy of a direct value down to its indirect references, which stay shared.</summary>
    private static PdfObject CopyDirect(PdfObject value)
    {
        switch (value)
        {
            case PdfDictionary dict:
                var dictCopy = new PdfDictionary();
                foreach (var key in dict.Keys)
                    if (dict.Get(key) is { } item) dictCopy.Set(key, CopyDirect(item));
                return dictCopy;
            case PdfArray array:
                var arrayCopy = new PdfArray();
                foreach (var item in array) arrayCopy.Add(CopyDirect(item));
                return arrayCopy;
            default:
                return value;
        }
    }

    /// <summary>Build a stream object dictionary holding a DER certificate, for
    /// embedding in a /DSS /Certs array (LTV).</summary>
    private static PdfDictionary BuildCertStreamDict(byte[] certDer)
    {
        var d = new PdfDictionary();
        d.Set("Length", new PdfInteger(certDer.Length));
        d.Set("__StreamData", new PdfString(certDer));
        return d;
    }

    /// <summary>Build the catalog /DSS (Document Security Store) dictionary
    /// referencing the embedded signer certificate stream (ISO 32000-2 §12.8.4.3).</summary>
    private static PdfDictionary BuildDssDict(int certObjNum)
    {
        var certs = new PdfArray();
        certs.Add(new PdfIndirectRef(certObjNum, 0));
        var dss = new PdfDictionary();
        dss.Set("Type", new PdfName("DSS"));
        dss.Set("Certs", certs);
        return dss;
    }

    /// <summary>Resolve the signature field name to sign into. An explicit name is
    /// honoured verbatim; when none is given, pick the first <c>SignatureN</c> that is
    /// either absent or a blank (unsigned) field — so signing a document that already
    /// carries a signed <c>Signature1</c> adds <c>Signature2</c> rather than overwriting
    /// it (each signature is preserved as its own incremental revision).</summary>
    private static string ResolveSignatureFieldName(Document doc, string? explicitName)
    {
        if (explicitName is not null) return explicitName;
        for (var i = 1; i < 10000; i++)
        {
            var name = "Signature" + i;
            var fref = FindField(doc, name).Ref;
            if (fref is null) return name;                 // no such field — fresh name
            var fdict = doc.Reader.ResolveDict(fref);
            if (fdict is null || !fdict.ContainsKey("V"))  // blank field — reuse it
                return name;
            // field exists and is already signed — try the next index
        }
        return "Signature1";
    }

    /// <summary>Clone an existing signature field dict and point its /V at the
    /// new signature value object, preserving the field's other entries
    /// (/Rect, /P, /AP, /MK, /T …) so its visible appearance survives.</summary>
    private static PdfDictionary BuildUpdatedSignatureFieldDict(PdfDictionary existing,
        int sigValObjNum)
    {
        var dict = CloneDict(existing);
        dict.Set("FT", new PdfName("Sig"));
        dict.Set("V", new PdfIndirectRef(sigValObjNum, 0));
        return dict;
    }

    /// <summary>The /Contents value the signature's handler takes: for adbe.x509.rsa_sha1 the
    /// bare RSA signature of the hash (no CMS, so no timestamp); for adbe.pkcs7.sha1 a CMS that
    /// encapsulates the SHA-1 of the signed bytes and signs that (ISO 32000-1 12.8.3.3.1);
    /// else a detached CMS over the hash.</summary>
    private static byte[] CreateSignatureContents(byte[] signedBytes, byte[] hash, PdfCertificate certificate,
        DigestHashAlgorithm digest, SignatureOptions options)
    {
        if (options.SubFilter == SubFilterRsaSha1)
            return CmsBuilder.CreateRsaSha1Signature(hash, certificate, options.CustomSignHash);
        if (options.SubFilter != SubFilterPkcs7Sha1)
            return CmsBuilder.CreateDetachedSignature(hash, certificate, digest, options.CustomSignHash,
                SignatureTimestamp(options));
        var encapsulated = digest == DigestHashAlgorithm.Sha1 ? hash : HmacSha.Sha1Hash(signedBytes);
        return CmsBuilder.CreateDetachedSignature(HashData(encapsulated, digest), certificate, digest,
            options.CustomSignHash, SignatureTimestamp(options), encapsulated);
    }

    /// <summary>The RFC 3161 request for a signature timestamp, or null when no TSA is set:
    /// the token is taken over the signature value (RFC 3161 appendix A), so it can only be
    /// asked for once the signature exists. A TSA that fails throws.</summary>
    private static Func<byte[], byte[]>? SignatureTimestamp(SignatureOptions options)
    {
        if (string.IsNullOrEmpty(options.TimestampUrl)) return null;
        var digest = options.TimestampDigest == DigestHashAlgorithm.Auto
            ? DigestHashAlgorithm.Sha256
            : options.TimestampDigest;
        return signatureValue => Rfc3161.RequestTimestampToken(options.TimestampUrl!,
            options.TimestampBasicAuth, digest, HashData(signatureValue, digest));
    }

    /// <summary>The digest algorithm implied by the signature /SubFilter: adbe.pkcs7.sha1
    /// (and the raw adbe.x509.rsa_sha1 handler) use SHA-1; everything else uses SHA-256.</summary>
    private static DigestHashAlgorithm DigestForSubFilter(string? subFilter)
        => subFilter is SubFilterPkcs7Sha1 or SubFilterRsaSha1
            ? DigestHashAlgorithm.Sha1
            : DigestHashAlgorithm.Sha256;

    /// <summary>Hash the concatenated ByteRange input with the given digest.</summary>
    private static byte[] HashByteRange(byte[] input, DigestHashAlgorithm digest)
        => HashData(input, digest);

    /// <summary>Hash arbitrary data with the named digest — the document
    /// timestamp path supports SHA-1/256/384/512 message imprints.</summary>
    private static byte[] HashData(byte[] data, DigestHashAlgorithm digest) => digest switch
    {
        DigestHashAlgorithm.Sha1 => HmacSha.Sha1Hash(data),
        DigestHashAlgorithm.Sha384 => ShaDigest.Sha384(data),
        DigestHashAlgorithm.Sha512 => ShaDigest.Sha512(data),
        DigestHashAlgorithm.Sha3_256 => new Sha3_256().ComputeHash(data),
        DigestHashAlgorithm.Sha3_384 => new Sha3_384().ComputeHash(data),
        DigestHashAlgorithm.Sha3_512 => new Sha3_512().ComputeHash(data),
        _ => ShaDigest.Sha256(data),
    };
}
