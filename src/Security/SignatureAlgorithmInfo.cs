namespace Aspose.Pdf
{
    /// <summary>Digest hash algorithm used by a PDF signature
    /// (PKCS#7 <c>messageDigest</c> attribute).</summary>
    public enum DigestHashAlgorithm
    {
        Auto,
        Sha1,
        Sha256,
        Sha384,
        Sha512,
        Sha3_256,
        Sha3_384,
        Sha3_512,
    }
}

namespace Aspose.Pdf.Security
{
    /// <summary>Signing-algorithm family extracted from a signature's
    /// PKCS#7 <c>signatureAlgorithm</c> OID.</summary>
    public enum SignatureAlgorithmType
    {
        Unknown,
        Rsa,
        Dsa,
        Ecdsa,
        Timestamp,
    }

    /// <summary>Cryptographic envelope standard reported by a PDF signature
    /// /SubFilter entry.</summary>
    public enum CryptographicStandard
    {
        Pkcs1,
        Pkcs7,
        Rfc3161,
    }

    /// <summary>Algorithm + digest + envelope-standard triple extracted from
    /// a PDF signature value. Returned by
    /// <see cref="Facades.PdfFileSignature.GetSignaturesInfo"/> per signature
    /// and by <see cref="Forms.Signature.GetSignatureAlgorithmInfo"/> on a
    /// loaded signature value.</summary>
    public class SignatureAlgorithmInfo
    {
        private readonly string _signatureName;

        /// <summary>Partial name (leaf) of the signature field.</summary>
        public string SignatureName => _signatureName;

        public SignatureAlgorithmInfo(string signatureName = "")
        {
            _signatureName = signatureName ?? string.Empty;
        }

        public SignatureAlgorithmType AlgorithmType;
        public CryptographicStandard CryptographicStandard;
        public DigestHashAlgorithm DigestHashAlgorithm;

        public override string ToString() =>
            $"{SignatureName}: {AlgorithmType} / {DigestHashAlgorithm} ({CryptographicStandard})";

        /// <summary>Parse a signature's /Contents (PKCS#7 detached) into an
        /// algorithm-info triple. <paramref name="subFilter"/> is the
        /// signature's /SubFilter entry; <paramref name="signName"/> ends
        /// up on <see cref="SignatureName"/>.</summary>
        internal static SignatureAlgorithmInfo FromPkcs7(byte[]? contents, string? subFilter, string? signName)
        {
            var info = new SignatureAlgorithmInfo(signName ?? string.Empty)
            {
                CryptographicStandard = MapSubFilter(subFilter),
            };
            if (contents is null || contents.Length == 0) return info;
            try
            {
                if (CmsParser.TryGetSignerAlgorithms(contents, out var digestOid, out var sigOid))
                {
                    info.DigestHashAlgorithm = MapDigest(digestOid);
                    info.AlgorithmType = sigOid switch
                    {
                        // RSA OIDs
                        "1.2.840.113549.1.1.1" or "1.2.840.113549.1.1.11" or "1.2.840.113549.1.1.12"
                            or "1.2.840.113549.1.1.13" or "1.2.840.113549.1.1.5" => SignatureAlgorithmType.Rsa,
                        // DSA OID
                        "1.2.840.10040.4.1" or "1.2.840.10040.4.3" => SignatureAlgorithmType.Dsa,
                        // ECDSA OIDs
                        "1.2.840.10045.2.1" or "1.2.840.10045.4.1" or "1.2.840.10045.4.3.2"
                            or "1.2.840.10045.4.3.3" or "1.2.840.10045.4.3.4" => SignatureAlgorithmType.Ecdsa,
                        _ => SignatureAlgorithmType.Unknown,
                    };
                }
            }
            catch
            {
                info.AlgorithmType = SignatureAlgorithmType.Unknown;
            }
            return info;
        }

        private static CryptographicStandard MapSubFilter(string? subFilter) => subFilter switch
        {
            "adbe.pkcs7.detached" or "adbe.pkcs7.sha1" or "ETSI.CAdES.detached" => CryptographicStandard.Pkcs7,
            "adbe.x509.rsa_sha1" => CryptographicStandard.Pkcs1,
            "ETSI.RFC3161" => CryptographicStandard.Rfc3161,
            _ => CryptographicStandard.Pkcs7,
        };

        private static DigestHashAlgorithm MapDigest(string? oid) => oid switch
        {
            "1.3.14.3.2.26" => DigestHashAlgorithm.Sha1,
            "2.16.840.1.101.3.4.2.1" => DigestHashAlgorithm.Sha256,
            "2.16.840.1.101.3.4.2.2" => DigestHashAlgorithm.Sha384,
            "2.16.840.1.101.3.4.2.3" => DigestHashAlgorithm.Sha512,
            "2.16.840.1.101.3.4.2.8" => DigestHashAlgorithm.Sha3_256,
            "2.16.840.1.101.3.4.2.9" => DigestHashAlgorithm.Sha3_384,
            "2.16.840.1.101.3.4.2.10" => DigestHashAlgorithm.Sha3_512,
            _ => DigestHashAlgorithm.Auto,
        };
    }
}
