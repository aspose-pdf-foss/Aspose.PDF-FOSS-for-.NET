namespace Aspose.Pdf.Security;

/// <summary>Signing-key algorithm family of a <see cref="PdfCertificate"/>.</summary>
internal enum SignatureKeyKind { Rsa, Dsa, Ecdsa }

/// <summary>
/// Represents a certificate + private key for PDF signing.
/// Replaces System.Security.Cryptography.X509Certificates.X509Certificate2.
/// Can be loaded from PFX/PKCS#12 files.
/// </summary>
public sealed class PdfCertificate
{
    internal byte[] CertificateDer { get; }

    /// <summary>Hand-rolled RSA private key — set only for the RSA path. Null when
    /// the key is DSA/ECDSA (those delegate to <see cref="DotNetCert"/>).</summary>
    internal RsaKey? PrivateKey { get; }

    /// <summary>The signing-key algorithm family.</summary>
    internal SignatureKeyKind KeyKind { get; }

    /// <summary>.NET certificate handle carrying the DSA/ECDSA private key. Set only
    /// when the hand-rolled RSA loader can't handle the key (non-RSA, or an RSA PFX
    /// whose PKCS#12 encoding the hand-rolled parser doesn't support). Null for the
    /// hand-rolled RSA path.</summary>
    internal System.Security.Cryptography.X509Certificates.X509Certificate2? DotNetCert { get; }

    /// <summary>The subject name extracted from the certificate (CN or O).</summary>
    public string SubjectName { get; }

    /// <summary>The full subject distinguished name, most-specific attribute
    /// first ("E=…, CN=…, C=…") — the form the visible-signature banner shows.</summary>
    internal string SubjectDn => _subjectDn ??= ParseSubjectDnFull(CertificateDer) ?? SubjectName;
    private string? _subjectDn;

    /// <summary>The issuer name extracted from the certificate.</summary>
    public string IssuerName { get; }

    /// <summary>The serial number of the certificate.</summary>
    public byte[] SerialNumber { get; }

    /// <summary>The raw DER-encoded issuer distinguished name.</summary>
    internal byte[] IssuerDer { get; }

    private PdfCertificate(byte[] certDer, RsaKey? privateKey,
        string subjectName, string issuerName, byte[] serialNumber, byte[] issuerDer,
        SignatureKeyKind keyKind = SignatureKeyKind.Rsa,
        System.Security.Cryptography.X509Certificates.X509Certificate2? dotNetCert = null)
    {
        CertificateDer = certDer;
        PrivateKey = privateKey;
        SubjectName = subjectName;
        IssuerName = issuerName;
        SerialNumber = serialNumber;
        IssuerDer = issuerDer;
        KeyKind = keyKind;
        DotNetCert = dotNetCert;
    }

    /// <summary>Load a certificate and private key from a PFX/PKCS#12 file.</summary>
    public static PdfCertificate FromPfx(byte[] pfxData, string password)
    {
        // Fast path: the hand-rolled RSA loader. It only understands RSA keys in the
        // PKCS#12 encodings it implements; anything else (DSA/ECDSA keys, or an RSA
        // PFX in an unsupported shrouding) throws — fall back to the platform loader,
        // which handles every key algorithm and PKCS#12 variant.
        try
        {
            var (certDer, key) = Pkcs12Parser.Parse(pfxData, password);
            return FromDer(certDer, key);
        }
        catch
        {
            return FromDotNet(pfxData, password);
        }
    }

    /// <summary>Fallback loader that uses the platform PKCS#12 reader. Supports
    /// DSA/ECDSA keys (and RSA PFX variants the hand-rolled parser rejects); the
    /// resulting <see cref="DotNetCert"/> carries the private key for signing.</summary>
    private static PdfCertificate FromDotNet(byte[] pfxData, string password)
    {
        var flags = System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable
                    | System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet;
        var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(pfxData, password, flags);
        var kind = cert.GetKeyAlgorithm() switch
        {
            "1.2.840.10040.4.1" => SignatureKeyKind.Dsa,   // id-dsa
            "1.2.840.10045.2.1" => SignatureKeyKind.Ecdsa, // id-ecPublicKey
            _ => SignatureKeyKind.Rsa,
        };
        var (subject, issuer, serial, issuerDer) = ParseCertMetadata(cert.RawData);
        return new PdfCertificate(cert.RawData, privateKey: null, subject, issuer, serial, issuerDer,
            kind, cert);
    }

    /// <summary>Wrap an already-loaded platform certificate. The private key
    /// (when present) stays inside the <see cref="DotNetCert"/> handle, which
    /// the signer uses for the actual crypto — this is the path for
    /// certificates from the OS store, a smartcard or an HSM.</summary>
    internal static PdfCertificate FromX509(
        System.Security.Cryptography.X509Certificates.X509Certificate2 cert)
    {
        var kind = cert.GetKeyAlgorithm() switch
        {
            "1.2.840.10040.4.1" => SignatureKeyKind.Dsa,   // id-dsa
            "1.2.840.10045.2.1" => SignatureKeyKind.Ecdsa, // id-ecPublicKey
            _ => SignatureKeyKind.Rsa,
        };
        var (subject, issuer, serial, issuerDer) = ParseCertMetadata(cert.RawData);
        return new PdfCertificate(cert.RawData, privateKey: null, subject, issuer, serial, issuerDer,
            kind, cert);
    }

    /// <summary>Load from a PFX file path.</summary>
    public static PdfCertificate FromPfx(string path, string password)
        => FromPfx(File.ReadAllBytes(path), password);

    /// <summary>Load from raw DER certificate bytes and a PKCS#8 DER private key.</summary>
    public static PdfCertificate FromDerFiles(byte[] certificateDer, byte[] pkcs8PrivateKeyDer)
        => FromDer(certificateDer, RsaKey.FromPkcs8(pkcs8PrivateKeyDer));

    internal static PdfCertificate FromDer(byte[] certDer, RsaKey privateKey)
    {
        var (subjectName, issuerName, serial, issuerDer) = ParseCertMetadata(certDer);
        return new PdfCertificate(certDer, privateKey, subjectName, issuerName, serial, issuerDer);
    }

    /// <summary>Parse subject/issuer/serial and the raw issuer DN from an X.509
    /// certificate's DER.</summary>
    private static (string subject, string issuer, byte[] serial, byte[] issuerDer)
        ParseCertMetadata(byte[] certDer)
    {
        var cert = new Asn1Reader(certDer).ReadSequence(); // Certificate
        var tbsCert = cert.ReadSequence(); // TBSCertificate

        // version [0] EXPLICIT
        tbsCert.TryReadContextConstructed(0); // skip version if present

        // serialNumber
        var serial = tbsCert.ReadIntegerBytes();

        // signature algorithm
        tbsCert.Skip(); // AlgorithmIdentifier

        // issuer (raw DER)
        var issuerDer = tbsCert.ReadRawTlv();
        var issuerName = ParseDistinguishedName(issuerDer);

        // validity
        tbsCert.Skip();

        // subject
        var subjectDer = tbsCert.ReadRawTlv();
        var subjectName = ParseDistinguishedName(subjectDer);

        return (subjectName, issuerName, serial, issuerDer);
    }

    /// <summary>Format the certificate's whole subject DN, RDNs reversed to
    /// most-specific-first, attributes as "{abbrev}={value}" joined with ", ".</summary>
    private static string? ParseSubjectDnFull(byte[] certDer)
    {
        try
        {
            var cert = new Asn1Reader(certDer).ReadSequence();
            var tbsCert = cert.ReadSequence();
            tbsCert.TryReadContextConstructed(0); // version
            tbsCert.ReadIntegerBytes();           // serialNumber
            tbsCert.Skip();                       // signature AlgorithmIdentifier
            tbsCert.ReadRawTlv();                 // issuer
            tbsCert.Skip();                       // validity
            var subjectDer = tbsCert.ReadRawTlv();

            var r = new Asn1Reader(subjectDer).ReadSequence();
            var parts = new List<string>();
            while (r.HasData)
            {
                var set = r.ReadSet();
                while (set.HasData)
                {
                    var atv = set.ReadSequence();
                    var oid = atv.ReadOid();
                    var value = atv.ReadAnyString();
                    var abbrev = oid switch
                    {
                        "2.5.4.3" => "CN",
                        "2.5.4.6" => "C",
                        "2.5.4.7" => "L",
                        "2.5.4.8" => "S",
                        "2.5.4.10" => "O",
                        "2.5.4.11" => "OU",
                        "1.2.840.113549.1.9.1" => "E",
                        _ => "OID." + oid,
                    };
                    parts.Add($"{abbrev}={value}");
                }
            }
            if (parts.Count == 0) return null;
            parts.Reverse();
            return string.Join(", ", parts);
        }
        catch
        {
            return null;
        }
    }

    private static string ParseDistinguishedName(byte[] der)
    {
        try
        {
            var r = new Asn1Reader(der).ReadSequence(); // SEQUENCE of SET of AttributeTypeAndValue
            string? cn = null, o = null, first = null;

            while (r.HasData)
            {
                var set = r.ReadSet();
                while (set.HasData)
                {
                    var atv = set.ReadSequence();
                    var oid = atv.ReadOid();
                    var value = atv.ReadAnyString();

                    first ??= value;
                    if (oid == "2.5.4.3") cn = value;       // commonName
                    else if (oid == "2.5.4.10") o = value;  // organizationName
                }
            }

            return cn ?? o ?? first ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
}
