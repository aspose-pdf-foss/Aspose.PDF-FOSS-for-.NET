using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

public sealed partial class PdfSigner
{
    private static PdfDictionary BuildSignatureValueDict(PdfCertificate cert,
        SignatureOptions options, int contentsSize)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Sig"));
        dict.Set("Filter", new PdfName("Adobe.PPKLite"));
        dict.Set("SubFilter", new PdfName(options.SubFilter));

        // Name from the caller's signer name when supplied, else the certificate subject.
        var name = string.IsNullOrEmpty(options.SignerName) ? cert.SubjectName : options.SignerName!;
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
        var dateStr = $"D:{when:yyyyMMddHHmmss}+00'00'";
        dict.Set("M", new PdfString(Encoding.Latin1.GetBytes(dateStr)));

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
        int sigValObjNum, Document doc)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Sig"));
        dict.Set("T", new PdfString(Encoding.Latin1.GetBytes(fieldName)));
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

        // Reference to first page
        if (doc.PageCount > 0)
        {
            var firstPage = doc.Pages[1];
            // Find the page's object number from the reader
            var pagesDict = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("Pages"));
            if (pagesDict is not null)
            {
                var kids = doc.Reader.Resolve(pagesDict.Get("Kids")) as PdfArray;
                if (kids is { Count: > 0 } && kids[0] is PdfIndirectRef pageRef)
                {
                    dict.Set("P", pageRef);
                }
            }
        }

        return dict;
    }

    private static PdfDictionary BuildAcroFormDict(Document doc, int sigFieldObjNum,
        bool appendField = true, string? replaceFieldNamed = null, PdfReader? reader = null)
    {
        var dict = new PdfDictionary();

        // Copy existing fields if AcroForm already exists
        var fields = new PdfArray();
        if (doc.Form is not null)
        {
            var existingAcroForm = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("AcroForm"));
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
                        fields.Add(f);
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
            var fref = FindTopLevelFieldRef(doc, name);
            if (fref is null) return name;                 // no such field — fresh name
            var fdict = doc.Reader.ResolveDict(fref);
            if (fdict is null || !fdict.ContainsKey("V"))  // blank field — reuse it
                return name;
            // field exists and is already signed — try the next index
        }
        return "Signature1";
    }

    private static PdfIndirectRef? FindTopLevelFieldRef(Document doc, string fieldName)
    {
        var acroForm = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return null;
        var fields = doc.Reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fields is null) return null;

        foreach (var f in fields)
        {
            if (f is not PdfIndirectRef fieldRef) continue;
            var fieldDict = doc.Reader.ResolveDict(fieldRef);
            var name = doc.Reader.Resolve(fieldDict?.Get("T")) as PdfString;
            if (name is not null && name.ToText() == fieldName)
                return fieldRef;
        }
        return null;
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

    private static byte[] CreatePkcs7Signature(byte[] hash, PdfCertificate certificate,
        DigestHashAlgorithm digest = DigestHashAlgorithm.Sha256)
        => CmsBuilder.CreateDetachedSignature(hash, certificate, digest);

    /// <summary>The digest algorithm implied by the signature /SubFilter: adbe.pkcs7.sha1
    /// (and the raw adbe.x509.rsa_sha1 handler) use SHA-1; everything else uses SHA-256.</summary>
    private static DigestHashAlgorithm DigestForSubFilter(string? subFilter)
        => subFilter is "adbe.pkcs7.sha1" or "adbe.x509.rsa_sha1"
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
