namespace Aspose.Pdf.Security;

/// <summary>
/// Minimal managed reader for PKCS#7 / CMS SignedData (RFC 5652). Surfaces the
/// inspection subset the PDF signature facades need — the first signer's
/// algorithm OIDs and the first embedded certificate — without
/// System.Security.Cryptography.Pkcs.SignedCms. The write side lives in
/// <see cref="CmsBuilder"/>.
/// </summary>
internal static class CmsParser
{
    private const string OidSignedData = "1.2.840.113549.1.7.2";

    /// <summary>
    /// Parse a CMS ContentInfo and return the first SignerInfo's digest- and
    /// signature-algorithm OIDs (dotted). Returns false on any parse failure or
    /// when the structure carries no signers.
    /// </summary>
    public static bool TryGetSignerAlgorithms(byte[] cms, out string digestOid, out string signatureOid)
    {
        digestOid = string.Empty;
        signatureOid = string.Empty;
        try
        {
            var signerInfos = ReadSignerInfos(cms);
            if (signerInfos is null || !signerInfos.HasData) return false;

            var signer = signerInfos.ReadSequence();
            signer.ReadInteger();                            // version
            signer.Skip();                                   // sid (issuerAndSerial | [0] subjectKeyId)
            digestOid = signer.ReadSequence().ReadOid();     // digestAlgorithm
            signer.TryReadContextConstructed(0);             // signedAttrs [0] IMPLICIT (optional)
            signatureOid = signer.ReadSequence().ReadOid();  // signatureAlgorithm
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extract the DER bytes of the first certificate in the CMS certificate
    /// set (suitable for <c>new X509Certificate2(...)</c> or writing as a .cer),
    /// or null when there is no certificate / on parse failure.
    /// </summary>
    public static byte[]? GetFirstCertificateDer(byte[] cms)
    {
        try
        {
            var sd = ReadSignedData(cms);
            if (sd is null) return null;
            sd.ReadInteger();   // version
            sd.ReadSet();       // digestAlgorithms
            sd.ReadSequence();  // encapContentInfo
            var certs = sd.TryReadContextConstructed(0); // certificates [0] IMPLICIT (optional)
            if (certs is null || !certs.HasData) return null;
            // First CertificateChoices entry; a plain X.509 certificate is a SEQUENCE.
            return certs.ReadRawTlv();
        }
        catch
        {
            return null;
        }
    }

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
