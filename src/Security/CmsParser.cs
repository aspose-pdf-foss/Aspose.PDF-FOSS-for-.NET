namespace Aspose.Pdf.Security;

/// <summary>
/// Minimal managed reader for PKCS#7 / CMS SignedData (RFC 5652). Surfaces the
/// inspection subset the PDF signature facades need — the first signer's
/// algorithm OIDs and its certificate — without
/// System.Security.Cryptography.Pkcs.SignedCms. The write side lives in
/// <see cref="CmsBuilder"/>.
/// </summary>
internal static class CmsParser
{
    private const string OidSignedData = "1.2.840.113549.1.7.2";

    /// <summary>
    /// Parse a CMS ContentInfo and return the first SignerInfo's digest- and
    /// signature-algorithm OIDs (dotted). Returns null on any parse failure or
    /// when the structure carries no signers.
    /// </summary>
    public static (string digestOid, string signatureOid)? TryGetSignerAlgorithms(byte[] cms)
    {
        try
        {
            var signerInfos = ReadSignerInfos(cms);
            if (signerInfos is null || !signerInfos.HasData) return null;

            var signer = signerInfos.ReadSequence();
            signer.ReadInteger();                                // version
            signer.Skip();                                       // sid (issuerAndSerial | [0] subjectKeyId)
            var digestOid = signer.ReadSequence().ReadOid();     // digestAlgorithm
            signer.TryReadContextConstructed(0);                 // signedAttrs [0] IMPLICIT (optional)
            var signatureOid = signer.ReadSequence().ReadOid();  // signatureAlgorithm
            return (digestOid, signatureOid);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract the DER bytes of the signer's certificate - the one the first SignerInfo's sid names - from
    /// the CMS certificate set (suitable for <c>new X509Certificate2(...)</c> or writing as a .cer); the
    /// first certificate when none matches, null when there is none or the set cannot be parsed.
    /// </summary>
    public static byte[]? GetSignerCertificateDer(byte[] cms)
    {
        // The set is unordered: a chain often lists its root or issuing CA first.
        var certificates = GetCertificatesDer(cms);
        if (certificates.Count == 0) return null;
        try
        {
            if (ReadFirstSignerId(cms) is { } sid)
                foreach (var certificate in certificates)
                    if (Identifies(sid, certificate)) return certificate;
        }
        catch
        {
            // an unreadable signer identifier names none of them
        }
        return certificates[0];
    }

    /// <summary>The first SignerInfo's sid: issuer and serial number, or a subject key identifier.</summary>
    private static (byte[]? Issuer, byte[]? Serial, byte[]? KeyId)? ReadFirstSignerId(byte[] cms)
    {
        var signerInfos = ReadSignerInfos(cms);
        if (signerInfos is null || !signerInfos.HasData) return null;
        var signer = signerInfos.ReadSequence();
        signer.ReadInteger(); // version
        if (signer.PeekTag() == SubjectKeyIdentifierTag) return (null, null, signer.ReadTlv().value);
        var issuerAndSerial = signer.ReadSequence();
        return (issuerAndSerial.ReadRawTlv(), issuerAndSerial.ReadIntegerBytes(), null);
    }

    /// <summary>Whether the certificate is the one the signer identifier names.</summary>
    private static bool Identifies((byte[]? Issuer, byte[]? Serial, byte[]? KeyId) sid, byte[] certificate)
    {
        var tbs = new Asn1Reader(certificate).ReadSequence().ReadSequence();
        tbs.TryReadContextConstructed(0); // version
        var serial = tbs.ReadIntegerBytes();
        tbs.Skip(); // signature algorithm
        var issuer = tbs.ReadRawTlv();
        if (sid.KeyId is null) return SameBytes(serial, sid.Serial!) && SameBytes(issuer, sid.Issuer!);

        tbs.Skip(); // validity
        tbs.Skip(); // subject
        tbs.Skip(); // subjectPublicKeyInfo
        var extensions = tbs.HasData ? FindExtensions(tbs) : null;
        while (extensions is not null && extensions.HasData)
        {
            var extension = extensions.ReadSequence();
            if (extension.ReadOid() != OidSubjectKeyIdentifier) continue;
            if (extension.PeekTag() == BooleanTag) extension.Skip(); // critical
            var keyId = new Asn1Reader(extension.ReadOctetString()).ReadOctetString();
            return SameBytes(keyId, sid.KeyId);
        }
        return false;
    }

    // The fields after the subject public key: [1] and [2] unique identifiers, then [3] extensions.
    private static Asn1Reader? FindExtensions(Asn1Reader tbs)
    {
        while (tbs.HasData)
        {
            if (tbs.TryReadContextConstructed(ExtensionsTagNumber) is { } extensions) return extensions.ReadSequence();
            tbs.Skip();
        }
        return null;
    }

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private const int SubjectKeyIdentifierTag = 0x80; // sid [0] IMPLICIT SubjectKeyIdentifier
    private const int BooleanTag = 0x01;
    private const int ExtensionsTagNumber = 3;
    private const string OidSubjectKeyIdentifier = "2.5.29.14";

    /// <summary>
    /// Extract the DER bytes of every certificate in the CMS certificate set
    /// (the signer's certificate plus any embedded intermediates), in stored
    /// order. Empty on parse failure or when the set is absent.
    /// </summary>
    public static List<byte[]> GetCertificatesDer(byte[] cms)
    {
        var result = new List<byte[]>();
        try
        {
            var sd = ReadSignedData(cms);
            if (sd is null) return result;
            sd.ReadInteger();   // version
            sd.ReadSet();       // digestAlgorithms
            sd.ReadSequence();  // encapContentInfo
            var certs = sd.TryReadContextConstructed(0); // certificates [0] IMPLICIT (optional)
            while (certs is not null && certs.HasData)
                result.Add(certs.ReadRawTlv());
        }
        catch
        {
            // best-effort: return whatever parsed before the failure
        }
        return result;
    }

    private static Asn1Reader? ReadSignedData(byte[] cms)
    {
        var contentInfo = new Asn1Reader(cms).ReadSequence();
        if (contentInfo.ReadOid() != OidSignedData) return null;
        var explicit0 = contentInfo.ReadContextConstructed(0); // [0] EXPLICIT
        return explicit0.ReadSequence();                       // SignedData
    }

    private static Asn1Reader? ReadSignerInfos(byte[] cms)
    {
        var sd = ReadSignedData(cms);
        if (sd is null) return null;
        sd.ReadInteger();                 // version
        sd.ReadSet();                     // digestAlgorithms
        sd.ReadSequence();                // encapContentInfo
        sd.TryReadContextConstructed(0);  // certificates (optional)
        sd.TryReadContextConstructed(1);  // crls (optional)
        return sd.ReadSet();              // signerInfos
    }
}
