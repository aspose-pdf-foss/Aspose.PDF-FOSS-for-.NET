using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

/// <summary>
/// Options for signing a PDF document.
/// </summary>
public sealed class SignatureOptions
{
    /// <summary>The reason for signing.</summary>
    public string? Reason { get; set; }

    /// <summary>The location of signing.</summary>
    public string? Location { get; set; }

    /// <summary>Contact information of the signer.</summary>
    public string? ContactInfo { get; set; }

    /// <summary>The signature field name. If null, a default name is generated.</summary>
    public string? FieldName { get; set; }

    /// <summary>The signer's name (maps to signature dict /Name). If null or
    /// empty, the certificate subject common name is used.</summary>
    public string? SignerName { get; set; }

    /// <summary>Password for an encrypted input document, so the signer can
    /// re-open the raw (still-encrypted) bytes to read its structure and
    /// append the incremental signature update. Null for unencrypted input.</summary>
    public string? Password { get; set; }

    /// <summary>The /SubFilter (signature-handler) name written to the
    /// signature dictionary. Defaults to <c>adbe.pkcs7.detached</c>; callers
    /// signing with a concrete PKCS7 (envelope) subtype pass
    /// <c>adbe.pkcs7.sha1</c> so the reloaded signature round-trips to the
    /// same concrete type.</summary>
    public string SubFilter { get; set; } = "adbe.pkcs7.detached";

    /// <summary>The signing date (maps to signature dict /M). If null, the
    /// current time is used.</summary>
    public DateTime? SigningDate { get; set; }

    /// <summary>
    /// Size in bytes reserved for the /Contents hex string.
    /// Defaults to 8192 which is sufficient for most certificates.
    /// Increase if using large certificate chains or timestamps.
    /// </summary>
    public int ContentsSize { get; set; } = 8192;

    /// <summary>When true, the signer skips an estimation pass and uses
    /// <see cref="ContentsSize"/> directly. The estimation pass is not
    /// currently performed in the FOSS signer (ContentsSize is always
    /// honoured), so this flag is accepted for surface parity and stored
    /// internally.</summary>
    public bool AvoidEstimating { get; set; }

    /// <summary>External-signer callback. When set, the signer skips
    /// in-process PKCS#7 construction and hands the to-be-signed hash
    /// to the implementation; the returned bytes are written into
    /// /Contents verbatim and must already be a complete CMS envelope.</summary>
    public Forms.SignHash? CustomSignHash { get; set; }

    /// <summary>When true, enable long-term validation: the signer writes a
    /// /DSS (Document Security Store, ISO 32000-2 §12.8.4.3) entry into the
    /// catalog carrying the signer certificate chain, so the signature stays
    /// verifiable after the signing certificate expires.</summary>
    public bool UseLtv { get; set; }

    /// <summary>When set, produce a certifying (author) signature: a /DocMDP
    /// SigRef with this /P access-permission level (1/2/3, ISO 32000-1
    /// §12.8.2.2) is written into the signature dictionary before signing and a
    /// catalog /Perms /DocMDP entry points at it. Null = an ordinary approval
    /// signature.</summary>
    public int? DocMdpPermissions { get; set; }

    /// <summary>RFC 3161 Time-Stamp Authority URL. Consumed by
    /// <see cref="PdfSigner.SignDocumentTimestamp"/> to produce a document
    /// timestamp (/SubFilter <c>ETSI.RFC3161</c>).</summary>
    public string? TimestampUrl { get; set; }

    /// <summary>Optional Base64 BasicAuth value sent to the TSA.</summary>
    public string? TimestampBasicAuth { get; set; }

    /// <summary>Digest algorithm for the document-timestamp message imprint.</summary>
    public DigestHashAlgorithm TimestampDigest { get; set; } = DigestHashAlgorithm.Sha256;

    /// <summary>Message digest for the signature itself. <see cref="DigestHashAlgorithm.Auto"/>
    /// defers to the /SubFilter default (SHA-1 for the legacy handlers, SHA-256 otherwise).</summary>
    public DigestHashAlgorithm Digest { get; set; } = DigestHashAlgorithm.Auto;
}

/// <summary>
/// Signs PDF documents with X.509 certificates using PKCS#7/CMS detached signatures.
/// Spec: PDF32000_2008 §12.8 (Digital Signatures)
/// </summary>
public sealed class PdfSigner
{
    /// <summary>
    /// Sign a PDF document and return the signed bytes.
    /// Uses incremental update to preserve original content.
    /// </summary>
    public static byte[] Sign(byte[] pdfData, PdfCertificate certificate,
        SignatureOptions? options = null)
    {
        options ??= new SignatureOptions();
        var contentsSize = options.ContentsSize;

        // Step 1: Parse the original document to find AcroForm and allocate objects
        using var doc = OpenDoc(pdfData, options.Password);
        var reader = doc.Reader;
        var trailer = reader.Trailer;
        var xref = reader.XRefTable;

        // Resolve the target field name. An explicit name is honoured; otherwise
        // pick the first SignatureN that is free or blank — signing again must not
        // overwrite an already-signed field (each signature is its own revision).
        var fieldName = ResolveSignatureFieldName(doc, options.FieldName);

        // Determine next available object numbers
        var maxObj = 0;
        foreach (var entry in xref.Entries.Values)
            if (entry.ObjectNumber > maxObj) maxObj = entry.ObjectNumber;
        var nextObj = maxObj + 1;

        // If the document already contains a (blank) signature field with this
        // name, reuse it — update its /V in place rather than appending a
        // duplicate field. Otherwise allocate a fresh field object.
        var existingFieldRef = FindTopLevelFieldRef(doc, fieldName);
        var existingFieldDict = existingFieldRef is not null
            ? doc.Reader.ResolveDict(existingFieldRef)
            : null;

        // Allocate object numbers for: sig value dict, sig field, updated AcroForm
        var sigValObjNum = nextObj++;
        var sigFieldObjNum = existingFieldDict is not null
            ? existingFieldRef!.ObjectNumber
            : nextObj++;
        var acroFormObjNum = nextObj++;
        var dssCertObjNum = options.UseLtv ? nextObj++ : 0;

        // Step 2: Build the incremental update with placeholder
        using var ms = new MemoryStream();

        // Copy original PDF
        ms.Write(pdfData);

        // Build signature value dictionary with placeholder
        var sigValDict = BuildSignatureValueDict(certificate, options, contentsSize);

        // Build signature field widget — reuse the existing field dict (keeping
        // its /Rect, /P, /AP, /MK …) when signing a pre-existing blank field.
        var sigFieldDict = existingFieldDict is not null
            ? BuildUpdatedSignatureFieldDict(existingFieldDict, sigValObjNum)
            : BuildSignatureFieldDict(fieldName, sigValObjNum, doc);

        // Build or update AcroForm. When reusing an existing field, it is
        // already listed in /Fields — don't append a duplicate reference.
        var acroFormDict = BuildAcroFormDict(doc, sigFieldObjNum,
            appendField: existingFieldDict is null);

        // Write the new objects
        var offsets = new Dictionary<int, long>();

        // Write sig value dict — we need to track the /Contents placeholder position
        offsets[sigValObjNum] = ms.Position;
        var sigValBytes = SerializeObject(sigValObjNum, sigValDict, contentsSize, out var contentsOffset, out var contentsLength);
        ms.Write(sigValBytes);

        // Adjust contentsOffset to be relative to the full file
        contentsOffset += offsets[sigValObjNum];

        // Write sig field
        offsets[sigFieldObjNum] = ms.Position;
        WriteIndirectObject(ms, sigFieldObjNum, sigFieldDict);

        // Write AcroForm
        offsets[acroFormObjNum] = ms.Position;
        WriteIndirectObject(ms, acroFormObjNum, acroFormDict);

        // Write updated catalog (add/update AcroForm reference)
        var catalogRef = trailer.Get("Root") as PdfIndirectRef;
        var catalogObjNum = catalogRef?.ObjectNumber ?? 1;
        var catalogDict = CloneDict(reader.Catalog);
        catalogDict.Set("AcroForm", new PdfIndirectRef(acroFormObjNum, 0));

        // Certifying signature: catalog /Perms /DocMDP points at the sig value.
        if (options.DocMdpPermissions is not null)
        {
            var perms = new PdfDictionary();
            perms.Set("DocMDP", new PdfIndirectRef(sigValObjNum, 0));
            catalogDict.Set("Perms", perms);
        }

        // LTV: embed the signer certificate in a /DSS (Document Security Store)
        // so the signature stays verifiable after the certificate expires.
        if (options.UseLtv)
        {
            offsets[dssCertObjNum] = ms.Position;
            WriteStreamObject(ms, dssCertObjNum, BuildCertStreamDict(certificate.CertificateDer));
            catalogDict.Set("DSS", BuildDssDict(dssCertObjNum));
        }

        offsets[catalogObjNum] = ms.Position;
        WriteIndirectObject(ms, catalogObjNum, catalogDict);

        // Write xref + trailer
        var originalStartXref = XRefTable.FindStartXref(pdfData);
        WriteXRefAndTrailer(ms, offsets, trailer, nextObj, originalStartXref);

        var fileBytes = ms.ToArray();

        // Step 3: Compute ByteRange — the two ranges that exclude /Contents value
        // ByteRange = [0, contentsOffset, contentsOffset + contentsLength, fileLength - (contentsOffset + contentsLength)]
        var byteRange = new long[]
        {
            0,
            contentsOffset,
            contentsOffset + contentsLength,
            fileBytes.Length - (contentsOffset + contentsLength)
        };

        // Step 4: Patch the ByteRange in the signature value dict
        // Find and replace the placeholder ByteRange
        PatchByteRange(fileBytes, byteRange);

        // Step 5: Compute the hash over the two byte ranges. Which digest applies
        // depends on the caller's request, the /SubFilter default (SHA-1 for the
        // adbe.pkcs7.sha1 / adbe.x509.rsa_sha1 handlers) and the signing key.
        var digest = CmsBuilder.ResolveDigest(
            certificate.KeyKind, options.Digest, DigestForSubFilter(options.SubFilter));
        var hashInput = new byte[(int)byteRange[1] + (int)byteRange[3]];
        Array.Copy(fileBytes, 0, hashInput, 0, (int)byteRange[1]);
        Array.Copy(fileBytes, (int)byteRange[2], hashInput, (int)byteRange[1], (int)byteRange[3]);
        var hash = HashByteRange(hashInput, digest);

        // Step 6: Create PKCS#7/CMS detached signature
        var signatureBytes = options.CustomSignHash is not null
            ? options.CustomSignHash(hash, digest)
            : CreatePkcs7Signature(hash, certificate, digest);

        if (signatureBytes.Length > contentsSize)
            throw new InvalidOperationException(
                $"Signature ({signatureBytes.Length} bytes) exceeds reserved space ({contentsSize} bytes). " +
                "Increase SignatureOptions.ContentsSize.");

        // Step 7: Write the signature into the /Contents placeholder
        var hexSignature = Convert.ToHexString(signatureBytes);
        // Pad with zeros to fill the reserved space
        hexSignature = hexSignature.PadRight(contentsSize * 2, '0');

        // Write hex string into the placeholder (skip the leading '<' at contentsOffset)
        var hexBytes = Encoding.ASCII.GetBytes(hexSignature);
        Array.Copy(hexBytes, 0, fileBytes, (int)contentsOffset + 1, hexBytes.Length);

        return fileBytes;
    }

    /// <summary>
    /// Add an RFC 3161 document timestamp (PAdES DocTimeStamp, /SubFilter
    /// <c>ETSI.RFC3161</c>) via an incremental update. Hashes the ByteRange,
    /// requests a timestamp token from the TSA in
    /// <see cref="SignatureOptions.TimestampUrl"/>, and writes the token into
    /// the signature /Contents. No signing certificate is involved — the token
    /// is produced by the TSA.
    /// </summary>
    public static byte[] SignDocumentTimestamp(byte[] pdfData, SignatureOptions options)
    {
        // A TSA token embeds the TSA certificate chain, so reserve generously.
        const int contentsSize = 16384;

        using var doc = OpenDoc(pdfData, options.Password);
        var reader = doc.Reader;
        var trailer = reader.Trailer;
        var xref = reader.XRefTable;

        var fieldName = ResolveSignatureFieldName(doc, options.FieldName);

        var maxObj = 0;
        foreach (var entry in xref.Entries.Values)
            if (entry.ObjectNumber > maxObj) maxObj = entry.ObjectNumber;
        var nextObj = maxObj + 1;

        var existingFieldRef = FindTopLevelFieldRef(doc, fieldName);
        var existingFieldDict = existingFieldRef is not null
            ? doc.Reader.ResolveDict(existingFieldRef)
            : null;

        var sigValObjNum = nextObj++;
        var sigFieldObjNum = existingFieldDict is not null
            ? existingFieldRef!.ObjectNumber
            : nextObj++;
        var acroFormObjNum = nextObj++;

        using var ms = new MemoryStream();
        ms.Write(pdfData);

        var sigValDict = BuildTimestampValueDict(contentsSize);
        var sigFieldDict = existingFieldDict is not null
            ? BuildUpdatedSignatureFieldDict(existingFieldDict, sigValObjNum)
            : BuildSignatureFieldDict(fieldName, sigValObjNum, doc);
        var acroFormDict = BuildAcroFormDict(doc, sigFieldObjNum,
            appendField: existingFieldDict is null);

        var offsets = new Dictionary<int, long>();

        offsets[sigValObjNum] = ms.Position;
        var sigValBytes = SerializeObject(sigValObjNum, sigValDict, contentsSize,
            out var contentsOffset, out var contentsLength);
        ms.Write(sigValBytes);
        contentsOffset += offsets[sigValObjNum];

        offsets[sigFieldObjNum] = ms.Position;
        WriteIndirectObject(ms, sigFieldObjNum, sigFieldDict);

        offsets[acroFormObjNum] = ms.Position;
        WriteIndirectObject(ms, acroFormObjNum, acroFormDict);

        var catalogRef = trailer.Get("Root") as PdfIndirectRef;
        var catalogObjNum = catalogRef?.ObjectNumber ?? 1;
        var catalogDict = CloneDict(reader.Catalog);
        catalogDict.Set("AcroForm", new PdfIndirectRef(acroFormObjNum, 0));

        offsets[catalogObjNum] = ms.Position;
        WriteIndirectObject(ms, catalogObjNum, catalogDict);

        var originalStartXref = XRefTable.FindStartXref(pdfData);
        WriteXRefAndTrailer(ms, offsets, trailer, nextObj, originalStartXref);

        var fileBytes = ms.ToArray();

        var byteRange = new long[]
        {
            0,
            contentsOffset,
            contentsOffset + contentsLength,
            fileBytes.Length - (contentsOffset + contentsLength)
        };
        PatchByteRange(fileBytes, byteRange);

        var digest = options.TimestampDigest == DigestHashAlgorithm.Auto
            ? DigestHashAlgorithm.Sha256
            : options.TimestampDigest;
        var hashInput = new byte[(int)byteRange[1] + (int)byteRange[3]];
        Array.Copy(fileBytes, 0, hashInput, 0, (int)byteRange[1]);
        Array.Copy(fileBytes, (int)byteRange[2], hashInput, (int)byteRange[1], (int)byteRange[3]);
        var imprint = HashData(hashInput, digest);

        var token = Rfc3161.RequestTimestampToken(
            options.TimestampUrl ?? string.Empty, options.TimestampBasicAuth, digest, imprint);

        if (token.Length > contentsSize)
            throw new InvalidOperationException(
                $"Timestamp token ({token.Length} bytes) exceeds reserved space ({contentsSize} bytes).");

        var hexToken = Convert.ToHexString(token).PadRight(contentsSize * 2, '0');
        var hexBytes = Encoding.ASCII.GetBytes(hexToken);
        Array.Copy(hexBytes, 0, fileBytes, (int)contentsOffset + 1, hexBytes.Length);

        return fileBytes;
    }

    /// <summary>
    /// Verify a signature in a PDF document. Returns true if the signature is cryptographically valid.
    /// </summary>
    public static bool Verify(byte[] pdfData, string? fieldName = null, string? password = null)
    {
        using var doc = OpenDoc(pdfData, password);
        var sigs = Forms.Signature.EnumerateSignatures(doc);

        var sig = fieldName is not null
            ? sigs.FirstOrDefault(s => s.FieldName == fieldName)
            : sigs.FirstOrDefault();

        if (sig is null || sig.ByteRangeRaw is null)
            return false;

        var br = sig.ByteRangeRaw;
        if (br.Length != 4) return false;

        // Read the PKCS#7 /Contents directly from the raw ByteRange gap
        // (between the two covered ranges). The signature /Contents is exempt
        // from document encryption (ISO 32000-1 §7.6.1), so the reader-decrypted
        // sig.ContentsRaw is garbled for encrypted documents — the on-disk hex
        // bytes in the gap are always the true, unencrypted envelope.
        var contents = ExtractContentsFromGap(pdfData, (int)br[1], (int)br[2])
                       ?? sig.ContentsRaw;
        if (contents is null) return false;

        // Collect the signed byte ranges (the raw data the signature covers).
        // Guard against a /ByteRange that does not fit the supplied bytes (e.g.
        // a re-serialized or truncated document): a signature we cannot slice
        // out is simply unverifiable, so return false rather than throwing.
        long off1 = br[0], len1 = br[1], off2 = br[2], len2 = br[3];
        if (off1 < 0 || len1 < 0 || off2 < 0 || len2 < 0 ||
            off1 + len1 > pdfData.Length || off2 + len2 > pdfData.Length)
            return false;

        // The /ByteRange gap is exempt from the digest, so the signature can
        // only vouch for the whole file if the gap holds exactly the /Contents
        // hex string (optionally wrapped in whitespace). Any other byte in the
        // gap — e.g. text overwriting the hex padding — is a post-signing
        // modification the digest cannot see: treat it as tampering.
        if (!GapIsSingleHexString(pdfData, (int)len1, (int)off2))
            return false;

        var signedData = new byte[len1 + len2];
        Array.Copy(pdfData, off1, signedData, 0, len1);
        Array.Copy(pdfData, off2, signedData, len1, len2);

        // ETSI.RFC3161 document timestamp: /Contents is an RFC 3161 TimeStampToken
        // (an enveloping CMS whose eContent is a TSTInfo), not a detached signature.
        // Verify the TSA's signature and that the token's message imprint matches
        // the digest of the covered ByteRange bytes.
        if (sig.SubFilter == "ETSI.RFC3161")
            return Rfc3161.VerifyToken(contents, signedData);

        // Raw adbe.x509.rsa_sha1 (PKCS#1): /Contents is a bare RSA signature over
        // SHA-1 of the ByteRange, and the certificate lives in the signature dict's
        // /Cert entry (not a CMS envelope).
        if (sig.SubFilter == "adbe.x509.rsa_sha1" && sig.CertRaw is { Count: > 0 })
        {
            var sha1 = HmacSha.Sha1Hash(signedData);
            var unwrapped = TryUnwrapOctetString(contents);
            foreach (var certDer in sig.CertRaw)
            {
                if (CmsBuilder.VerifyRsaPkcs1(certDer, sha1, contents)) return true;
                if (unwrapped is not null && CmsBuilder.VerifyRsaPkcs1(certDer, sha1, unwrapped)) return true;
            }
            return false;
        }

        // Try verification with raw signed bytes first (standard CMS detached),
        // then fall back to hash-as-content: adbe.pkcs7.sha1 embeds the SHA-1
        // digest of the ByteRange as the CMS content, and this library's own
        // Sign signs over the digest directly (SHA-256, or SHA-1 for pkcs7.sha1).
        if (TryVerifyCms(signedData, contents))
            return true;
        if (TryVerifyCms(ShaDigest.Sha256(signedData), contents))
            return true;
        if (TryVerifyCms(HmacSha.Sha1Hash(signedData), contents))
            return true;
        return false;
    }

    /// <summary>True when the bytes between <paramref name="gapStart"/> and
    /// <paramref name="gapEnd"/> are exactly one PDF hex string —
    /// <c>&lt;hex-digits&gt;</c> with PDF whitespace allowed inside and around it.</summary>
    private static bool GapIsSingleHexString(byte[] data, int gapStart, int gapEnd)
    {
        if (gapStart < 0 || gapEnd > data.Length || gapStart >= gapEnd) return false;
        static bool IsWs(byte c) => c is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;
        static bool IsHex(byte c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

        var i = gapStart;
        while (i < gapEnd && IsWs(data[i])) i++;
        if (i >= gapEnd || data[i] != (byte)'<') return false;
        i++;
        while (i < gapEnd && data[i] != (byte)'>')
        {
            if (!IsHex(data[i]) && !IsWs(data[i])) return false;
            i++;
        }
        if (i >= gapEnd) return false; // no closing '>'
        i++;
        while (i < gapEnd && IsWs(data[i])) i++;
        return i >= gapEnd;
    }

    /// <summary>If <paramref name="data"/> is a single DER OCTET STRING, return its
    /// contents; otherwise null. Some producers wrap the adbe.x509.rsa_sha1 signature
    /// value in an OCTET STRING.</summary>
    private static byte[]? TryUnwrapOctetString(byte[]? data)
    {
        if (data is null || data.Length < 2 || data[0] != 0x04) return null;
        try { return new Asn1Reader(data).ReadOctetString(); }
        catch { return null; }
    }

    /// <summary>Extract the raw PKCS#7 bytes from a signature's /Contents hex
    /// string, read straight from the on-disk ByteRange gap
    /// (<c>&lt;hex…&gt;</c> between <paramref name="gapStart"/> and
    /// <paramref name="gapEnd"/>). Returns null when the gap holds no hex string.</summary>
    private static byte[]? ExtractContentsFromGap(byte[] pdfData, int gapStart, int gapEnd)
    {
        if (gapStart < 0 || gapEnd > pdfData.Length || gapStart >= gapEnd) return null;

        // Locate the '<' … '>' delimiting the hex /Contents within the gap.
        var open = -1;
        for (var i = gapStart; i < gapEnd; i++)
        {
            if (pdfData[i] == (byte)'<') { open = i + 1; break; }
        }
        if (open < 0) return null;
        var close = -1;
        for (var i = open; i < gapEnd; i++)
        {
            if (pdfData[i] == (byte)'>') { close = i; break; }
        }
        if (close < 0) return null;

        // Decode hex nibbles, ignoring whitespace; a signer pads the reserved
        // space with trailing '0's, which decode to trailing zero bytes that
        // the CMS parser tolerates (they follow the DER envelope's own length).
        var nibbles = new System.Collections.Generic.List<byte>(close - open);
        for (var i = open; i < close; i++)
        {
            var c = pdfData[i];
            int v;
            if (c >= '0' && c <= '9') v = c - '0';
            else if (c >= 'A' && c <= 'F') v = c - 'A' + 10;
            else if (c >= 'a' && c <= 'f') v = c - 'a' + 10;
            else continue; // skip whitespace / EOL
            nibbles.Add((byte)v);
        }
        if (nibbles.Count < 2) return null;

        var bytes = new byte[nibbles.Count / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((nibbles[2 * i] << 4) | nibbles[2 * i + 1]);
        return bytes;
    }

    private static bool TryVerifyCms(byte[] content, byte[] signature)
        => CmsBuilder.VerifyDetached(content, signature);

    /// <summary>Open PDF bytes, authenticating with <paramref name="password"/>
    /// when the document is encrypted.</summary>
    private static Document OpenDoc(byte[] pdfData, string? password)
        => password is not null ? Document.Open(pdfData, password) : Document.Open(pdfData);

    /// <summary>
    /// Sign a PDF document with an X.509 certificate and a visible signature appearance.
    /// Returns the signed PDF bytes.
    /// </summary>
    public static byte[] SignWithAppearance(byte[] pdfData, PdfCertificate certificate,
        SignatureOptions? options, SignatureAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        options ??= new SignatureOptions();
        var contentsSize = options.ContentsSize;

        using var doc = OpenDoc(pdfData, options.Password);
        var reader = doc.Reader;
        var trailer = reader.Trailer;
        var xref = reader.XRefTable;

        var fieldName = ResolveSignatureFieldName(doc, options.FieldName);

        var maxObj = 0;
        foreach (var entry in xref.Entries.Values)
            if (entry.ObjectNumber > maxObj) maxObj = entry.ObjectNumber;
        var nextObj = maxObj + 1;

        var sigValObjNum = nextObj++;
        var sigFieldObjNum = nextObj++;
        var acroFormObjNum = nextObj++;
        var appearanceStreamObjNum = nextObj++;
        var dssCertObjNum = options.UseLtv ? nextObj++ : 0;

        using var ms = new MemoryStream();
        ms.Write(pdfData);

        var sigValDict = BuildSignatureValueDict(certificate, options, contentsSize);
        var sigFieldDict = BuildSignatureFieldWithAppearance(
            fieldName, sigValObjNum, appearanceStreamObjNum, doc, appearance);
        var acroFormDict = BuildAcroFormDict(doc, sigFieldObjNum);
        var appearanceStreamDict = BuildSignatureAppearanceStream(appearance);

        // When the source document is encrypted, every appended string/stream must
        // be encrypted with the document's per-object key (only the signature
        // /Contents is exempt). Otherwise the signature reason/location text and the
        // visible appearance would leak as plaintext into an encrypted PDF.
        var appendDecryptor = reader.Decryptor;
        if (appendDecryptor is not null)
        {
            // Encrypt the signer-supplied text (/Reason /Location /ContactInfo /M …)
            // and the visible appearance content. The field dict's /T is left as-is
            // because signature-name lookup reads it structurally during verify.
            EncryptAppendedDict(appendDecryptor, sigValObjNum, sigValDict);
            EncryptAppendedStream(appendDecryptor, appearanceStreamObjNum, appearanceStreamDict);
        }

        // Write objects
        var offsets = new Dictionary<int, long>();

        offsets[sigValObjNum] = ms.Position;
        var sigValBytes = SerializeObject(sigValObjNum, sigValDict, contentsSize,
            out var contentsOffset, out var contentsLength);
        ms.Write(sigValBytes);
        contentsOffset += offsets[sigValObjNum];

        offsets[sigFieldObjNum] = ms.Position;
        WriteIndirectObject(ms, sigFieldObjNum, sigFieldDict);

        offsets[acroFormObjNum] = ms.Position;
        WriteIndirectObject(ms, acroFormObjNum, acroFormDict);

        // Write appearance stream object (stream with dict)
        offsets[appearanceStreamObjNum] = ms.Position;
        WriteStreamObject(ms, appearanceStreamObjNum, appearanceStreamDict);

        var catalogRef = trailer.Get("Root") as PdfIndirectRef;
        var catalogObjNum = catalogRef?.ObjectNumber ?? 1;
        var catalogDict = CloneDict(reader.Catalog);
        catalogDict.Set("AcroForm", new PdfIndirectRef(acroFormObjNum, 0));

        if (options.DocMdpPermissions is not null)
        {
            var perms = new PdfDictionary();
            perms.Set("DocMDP", new PdfIndirectRef(sigValObjNum, 0));
            catalogDict.Set("Perms", perms);
        }

        if (options.UseLtv)
        {
            offsets[dssCertObjNum] = ms.Position;
            WriteStreamObject(ms, dssCertObjNum, BuildCertStreamDict(certificate.CertificateDer));
            catalogDict.Set("DSS", BuildDssDict(dssCertObjNum));
        }

        offsets[catalogObjNum] = ms.Position;
        WriteIndirectObject(ms, catalogObjNum, catalogDict);

        var originalStartXref = XRefTable.FindStartXref(pdfData);
        WriteXRefAndTrailer(ms, offsets, trailer, nextObj, originalStartXref);

        var fileBytes = ms.ToArray();

        var byteRange = new long[]
        {
            0,
            contentsOffset,
            contentsOffset + contentsLength,
            fileBytes.Length - (contentsOffset + contentsLength)
        };

        PatchByteRange(fileBytes, byteRange);

        // Hash the two byte ranges (SHA-256, or SHA-1 for adbe.pkcs7.sha1 / adbe.x509.rsa_sha1).
        var digest = CmsBuilder.ResolveDigest(
            certificate.KeyKind, options.Digest, DigestForSubFilter(options.SubFilter));
        var hashInput = new byte[(int)byteRange[1] + (int)byteRange[3]];
        Array.Copy(fileBytes, 0, hashInput, 0, (int)byteRange[1]);
        Array.Copy(fileBytes, (int)byteRange[2], hashInput, (int)byteRange[1], (int)byteRange[3]);
        var hash = HashByteRange(hashInput, digest);

        var signatureBytes = options.CustomSignHash is not null
            ? options.CustomSignHash(hash, digest)
            : CreatePkcs7Signature(hash, certificate, digest);

        if (signatureBytes.Length > contentsSize)
            throw new InvalidOperationException(
                $"Signature ({signatureBytes.Length} bytes) exceeds reserved space ({contentsSize} bytes).");

        var hexSignature = Convert.ToHexString(signatureBytes);
        hexSignature = hexSignature.PadRight(contentsSize * 2, '0');
        var hexBytes = Encoding.ASCII.GetBytes(hexSignature);
        Array.Copy(hexBytes, 0, fileBytes, (int)contentsOffset + 1, hexBytes.Length);

        return fileBytes;
    }

    #region Private helpers

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
        bool appendField = true)
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
                        fields.Add(f);
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

    /// <summary>Find the indirect reference of a top-level AcroForm field whose
    /// /T equals <paramref name="fieldName"/>, or null when no such field
    /// exists. Used to reuse a pre-existing blank signature field rather than
    /// appending a duplicate one when signing.</summary>
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

    /// <summary>
    /// Serialize an indirect object, tracking the position and length of the /Contents hex string.
    /// </summary>
    private static byte[] SerializeObject(int objNum, PdfDictionary dict, int contentsSize,
        out long contentsOffset, out long contentsLength)
    {
        contentsOffset = 0;
        contentsLength = 0;

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write($"{objNum} 0 obj\n");
        Write("<< ");
        foreach (var key in dict.Keys)
        {
            Write($"/{key} ");
            var val = dict.Get(key)!;

            if (key == "Contents")
            {
                // Track the hex string position
                contentsOffset = ms.Position;
                var hexStr = new string('0', contentsSize * 2);
                Write($"<{hexStr}>");
                contentsLength = ms.Position - contentsOffset;
            }
            else
            {
                SerializeValue(ms, val);
            }
            Write(" ");
        }
        Write(">>\nendobj\n");

        return ms.ToArray();
    }

    private static void SerializeValue(MemoryStream ms, PdfObject val)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        switch (val)
        {
            case PdfNull:
                Write("null");
                break;
            case PdfBoolean b:
                Write(b.Value ? "true" : "false");
                break;
            case PdfInteger i:
                Write(i.Value.ToString());
                break;
            case PdfReal r:
                Write(r.Value.ToString("G"));
                break;
            case PdfString s when s.IsHex:
                Write($"<{Convert.ToHexString(s.Value)}>");
                break;
            case PdfString s:
                Write("(");
                foreach (var c in s.Value)
                {
                    if (c is (byte)'(' or (byte)')' or (byte)'\\')
                        ms.WriteByte((byte)'\\');
                    ms.WriteByte(c);
                }
                Write(")");
                break;
            case PdfName n:
                Write($"/{n.Value}");
                break;
            case PdfArray arr:
                Write("[");
                for (var i = 0; i < arr.Count; i++)
                {
                    if (i > 0) Write(" ");
                    SerializeValue(ms, arr[i]);
                }
                Write("]");
                break;
            case PdfDictionary d:
                Write("<< ");
                foreach (var key in d.Keys)
                {
                    Write($"/{key} ");
                    SerializeValue(ms, d.Get(key)!);
                    Write(" ");
                }
                Write(">>");
                break;
            case PdfIndirectRef iref:
                Write($"{iref.ObjectNumber} {iref.Generation} R");
                break;
        }
    }

    private static void WriteIndirectObject(MemoryStream ms, int objNum, PdfObject obj)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        Write($"{objNum} 0 obj\n");
        SerializeValue(ms, obj);
        Write("\nendobj\n");
    }

    private static void WriteXRefAndTrailer(MemoryStream ms, Dictionary<int, long> offsets,
        PdfDictionary originalTrailer, int nextObjNum, long originalStartXref)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        var xrefOffset = ms.Position;

        // Group consecutive object numbers into subsections
        var sortedNums = offsets.Keys.OrderBy(k => k).ToList();
        Write("xref\n");

        var i = 0;
        while (i < sortedNums.Count)
        {
            var start = sortedNums[i];
            var count = 1;
            while (i + count < sortedNums.Count && sortedNums[i + count] == start + count)
                count++;

            Write($"{start} {count}\n");
            for (var j = 0; j < count; j++)
            {
                Write($"{offsets[sortedNums[i + j]]:D10} 00000 n \n");
            }
            i += count;
        }

        // Trailer
        var newTrailer = new PdfDictionary();
        foreach (var key in new[] { "Root", "Info", "Encrypt", "ID" })
        {
            var val = originalTrailer.Get(key);
            if (val is not null) newTrailer.Set(key, val);
        }
        newTrailer.Set("Size", new PdfInteger(nextObjNum));
        newTrailer.Set("Prev", new PdfInteger(originalStartXref));

        Write("trailer\n");
        SerializeValue(ms, newTrailer);
        Write($"\nstartxref\n{xrefOffset}\n%%EOF\n");
    }

    private static void PatchByteRange(byte[] fileBytes, long[] byteRange)
    {
        // Find the ByteRange placeholder pattern: [0 9999999999 9999999999 9999999999]
        var placeholder = Encoding.ASCII.GetBytes("[0 9999999999 9999999999 9999999999]");
        var replacement = $"[{byteRange[0]} {byteRange[1]} {byteRange[2]} {byteRange[3]}]";
        // Pad replacement to same length as placeholder
        replacement = replacement.PadRight(placeholder.Length);
        var replacementBytes = Encoding.ASCII.GetBytes(replacement);

        var idx = FindBytes(fileBytes, placeholder);
        if (idx < 0)
            throw new InvalidOperationException("Could not find ByteRange placeholder in signed PDF.");

        Array.Copy(replacementBytes, 0, fileBytes, idx, replacementBytes.Length);
    }

    private static int FindBytes(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    private static PdfDictionary CloneDict(PdfDictionary source)
    {
        var clone = new PdfDictionary();
        foreach (var key in source.Keys)
        {
            var val = source.Get(key);
            if (val is not null) clone.Set(key, val);
        }
        return clone;
    }

    private static PdfDictionary BuildSignatureFieldWithAppearance(
        string fieldName, int sigValObjNum, int appearanceStreamObjNum,
        Document doc, SignatureAppearance appearance)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Sig"));
        dict.Set("T", new PdfString(Encoding.Latin1.GetBytes(fieldName)));
        dict.Set("V", new PdfIndirectRef(sigValObjNum, 0));

        // Set rect from appearance
        var rect = new PdfArray();
        if (appearance.Rect is not null)
        {
            rect.Add(new PdfReal(appearance.Rect.LLX));
            rect.Add(new PdfReal(appearance.Rect.LLY));
            rect.Add(new PdfReal(appearance.Rect.URX));
            rect.Add(new PdfReal(appearance.Rect.URY));
        }
        else
        {
            rect.Add(new PdfInteger(0));
            rect.Add(new PdfInteger(0));
            rect.Add(new PdfInteger(0));
            rect.Add(new PdfInteger(0));
        }
        dict.Set("Rect", rect);
        dict.Set("F", new PdfInteger(4)); // Print flag

        // AP — appearance dictionary with /N (normal) pointing to form XObject
        var apDict = new PdfDictionary();
        apDict.Set("N", new PdfIndirectRef(appearanceStreamObjNum, 0));
        dict.Set("AP", apDict);

        // Page reference (0-based index from 1-based PageNumber)
        var pageIndex = Math.Max(0, appearance.PageNumber - 1);
        if (doc.PageCount > pageIndex)
        {
            var pagesDict = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("Pages"));
            if (pagesDict is not null)
            {
                var kids = doc.Reader.Resolve(pagesDict.Get("Kids")) as PdfArray;
                if (kids is not null && pageIndex < kids.Count && kids[pageIndex] is PdfIndirectRef pageRef)
                {
                    dict.Set("P", pageRef);
                }
            }
        }

        return dict;
    }

    /// <summary>
    /// Build a Form XObject appearance stream showing signature details.
    /// Returns a dictionary with stream content stored under the "__StreamData" key
    /// (used by WriteStreamObject).
    /// </summary>
    internal static PdfDictionary BuildSignatureAppearanceStream(SignatureAppearance appearance)
    {
        var r = appearance.Rect ?? new Rectangle(0, 0, 200, 100);
        var width = r.Width;
        var height = r.Height;
        var fontSize = appearance.FontSize;
        var lineHeight = fontSize * 1.4;

        // Build content stream text
        var sb = new StringBuilder();

        // Border rectangle
        sb.Append("q\n");
        sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
            "0 0 {0:F2} {1:F2} re S\n", width, height);

        // Text
        sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
            "BT\n/F1 {0:F1} Tf\n", fontSize);

        var y = height - lineHeight;
        var x = 4.0;

        if (appearance.SignerName is not null)
        {
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2} {1:F2} Td\n", x, y);
            sb.AppendFormat("({0}) Tj\n", EscapePdfString($"Digitally signed by: {appearance.SignerName}"));
            y -= lineHeight;
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2} {1:F2} Td\n", 0.0, -lineHeight);
        }

        if (appearance.Reason is not null)
        {
            if (appearance.SignerName is null)
            {
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F2} {1:F2} Td\n", x, y);
                y -= lineHeight;
            }
            sb.AppendFormat("({0}) Tj\n", EscapePdfString($"Reason: {appearance.Reason}"));
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2} {1:F2} Td\n", 0.0, -lineHeight);
        }

        if (appearance.Location is not null)
        {
            if (appearance.SignerName is null && appearance.Reason is null)
            {
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F2} {1:F2} Td\n", x, y);
                y -= lineHeight;
            }
            sb.AppendFormat("({0}) Tj\n", EscapePdfString($"Location: {appearance.Location}"));
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2} {1:F2} Td\n", 0.0, -lineHeight);
        }

        var signDate = appearance.SignDate ?? DateTime.UtcNow;
        var dateStr = signDate.ToString("yyyy-MM-dd HH:mm:ss");
        if (appearance.SignerName is null && appearance.Reason is null && appearance.Location is null)
        {
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2} {1:F2} Td\n", x, y);
        }
        sb.AppendFormat("({0}) Tj\n", EscapePdfString($"Date: {dateStr}"));

        sb.Append("ET\n");
        sb.Append("Q\n");

        var streamContent = Encoding.Latin1.GetBytes(sb.ToString());

        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Form"));
        dict.Set("BBox", CreateBBoxArray(0, 0, width, height));
        dict.Set("Length", new PdfInteger(streamContent.Length));

        // Resources with the appearance font (a custom appearance may request a
        // specific family, e.g. "Times New Roman" → "TimesNewRoman"). The default is
        // Arial — the signer names the concrete host face rather than the
        // abstract Helvetica (a signed PDF/A must embed a concrete font, e.g. Arial).
        var baseFont = string.IsNullOrEmpty(appearance.FontFamily)
            ? "Arial"
            : appearance.FontFamily.Replace(" ", "");
        var fontDict = new PdfDictionary();
        var f1Dict = new PdfDictionary();
        f1Dict.Set("Type", new PdfName("Font"));
        f1Dict.Set("Subtype", new PdfName("Type1"));
        f1Dict.Set("BaseFont", new PdfName(baseFont));
        fontDict.Set("F1", f1Dict);
        var resources = new PdfDictionary();
        resources.Set("Font", fontDict);
        dict.Set("Resources", resources);

        // Store stream data for WriteStreamObject
        dict.Set("__StreamData", new PdfString(streamContent));

        return dict;
    }

    private static PdfArray CreateBBoxArray(double x1, double y1, double x2, double y2)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(x1));
        arr.Add(new PdfReal(y1));
        arr.Add(new PdfReal(x2));
        arr.Add(new PdfReal(y2));
        return arr;
    }

    private static string EscapePdfString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    /// <summary>
    /// Encode a text string as a PdfString, using UTF-16BE with BOM if it contains
    /// non-Latin-1 characters, otherwise Latin-1.
    /// </summary>
    private static PdfString EncodePdfText(string text)
    {
        // Check if all characters fit in Latin-1 (0x00-0xFF)
        var needsUnicode = false;
        foreach (var ch in text)
        {
            if (ch > 0xFF) { needsUnicode = true; break; }
        }

        if (!needsUnicode)
            return new PdfString(Encoding.Latin1.GetBytes(text));

        // UTF-16BE with BOM (0xFE 0xFF)
        var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        var withBom = new byte[2 + utf16.Length];
        withBom[0] = 0xFE;
        withBom[1] = 0xFF;
        Array.Copy(utf16, 0, withBom, 2, utf16.Length);
        return new PdfString(withBom);
    }

    /// <summary>Encrypt the string values (recursively) of an object being
    /// appended to an already-encrypted document, using the document's per-object
    /// key. The signature CMS envelope (/Contents) is exempt (ISO 32000-1
    /// §7.6.1); the "__StreamData" marker is handled by
    /// <see cref="EncryptAppendedStream"/>.</summary>
    private static void EncryptAppendedDict(PdfDecryptor dec, int objNum, PdfDictionary dict)
    {
        foreach (var key in new List<string>(dict.Keys))
        {
            if (key is "Contents" or "__StreamData") continue;
            switch (dict.Get(key))
            {
                case PdfString s:
                    dict.Set(key, new PdfString(dec.EncryptString(s.Value, objNum, 0), isHex: true));
                    break;
                case PdfDictionary sub: // inline sub-dictionary: same object
                    EncryptAppendedDict(dec, objNum, sub);
                    break;
                case PdfArray arr:
                    EncryptAppendedArray(dec, objNum, arr);
                    break;
            }
        }
    }

    private static void EncryptAppendedArray(PdfDecryptor dec, int objNum, PdfArray arr)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            switch (arr[i])
            {
                case PdfString s:
                    arr.ReplaceAt(i, new PdfString(dec.EncryptString(s.Value, objNum, 0), isHex: true));
                    break;
                case PdfDictionary sub: EncryptAppendedDict(dec, objNum, sub); break;
                case PdfArray sa: EncryptAppendedArray(dec, objNum, sa); break;
            }
        }
    }

    /// <summary>Encrypt an appended stream object: its raw stream data (stored
    /// under "__StreamData") and its dictionary strings.</summary>
    private static void EncryptAppendedStream(PdfDecryptor dec, int objNum, PdfDictionary streamDict)
    {
        if (streamDict.Get("__StreamData") is PdfString sd)
            streamDict.Set("__StreamData", new PdfString(dec.EncryptStream(sd.Value, objNum, 0)));
        EncryptAppendedDict(dec, objNum, streamDict);
    }

    private static void WriteStreamObject(MemoryStream ms, int objNum, PdfDictionary dict)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        // Extract stream data
        var streamDataObj = dict.Get("__StreamData") as PdfString;
        var streamData = streamDataObj?.Value ?? [];

        Write($"{objNum} 0 obj\n<< ");
        foreach (var key in dict.Keys)
        {
            if (key == "__StreamData") continue;
            Write($"/{key} ");
            SerializeValue(ms, dict.Get(key)!);
            Write(" ");
        }
        Write(">>\nstream\n");
        ms.Write(streamData);
        Write("\nendstream\nendobj\n");
    }

    #endregion
}
