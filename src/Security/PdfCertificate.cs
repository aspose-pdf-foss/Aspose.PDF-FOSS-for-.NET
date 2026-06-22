namespace Aspose.Pdf.Security;

/// <summary>
/// Represents a certificate + private key for PDF signing.
/// Replaces System.Security.Cryptography.X509Certificates.X509Certificate2.
/// Can be loaded from PFX/PKCS#12 files.
/// </summary>
public sealed class PdfCertificate
{
    internal byte[] CertificateDer { get; }
    internal RsaKey PrivateKey { get; }

    /// <summary>The subject name extracted from the certificate (CN or O).</summary>
    public string SubjectName { get; }

    /// <summary>The issuer name extracted from the certificate.</summary>
    public string IssuerName { get; }

    /// <summary>The serial number of the certificate.</summary>
    public byte[] SerialNumber { get; }

    /// <summary>The raw DER-encoded issuer distinguished name.</summary>
    internal byte[] IssuerDer { get; }

    private PdfCertificate(byte[] certDer, RsaKey privateKey,
        string subjectName, string issuerName, byte[] serialNumber, byte[] issuerDer)
    {
        CertificateDer = certDer;
        PrivateKey = privateKey;
        SubjectName = subjectName;
        IssuerName = issuerName;
        SerialNumber = serialNumber;
        IssuerDer = issuerDer;
    }

    /// <summary>Load a certificate and private key from a PFX/PKCS#12 file.</summary>
    public static PdfCertificate FromPfx(byte[] pfxData, string password)
    {
        var (certDer, key) = Pkcs12Parser.Parse(pfxData, password);
        return FromDer(certDer, key);
    }

    /// <summary>Load from a PFX file path.</summary>
    public static PdfCertificate FromPfx(string path, string password)
        => FromPfx(File.ReadAllBytes(path), password);

    /// <summary>Load from raw DER certificate bytes and a PKCS#8 DER private key.</summary>
    public static PdfCertificate FromDerFiles(byte[] certificateDer, byte[] pkcs8PrivateKeyDer)
        => FromDer(certificateDer, RsaKey.FromPkcs8(pkcs8PrivateKeyDer));

    internal static PdfCertificate FromDer(byte[] certDer, RsaKey privateKey)
    {
        // Parse X.509 certificate to extract subject, issuer, serial
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

        return new PdfCertificate(certDer, privateKey, subjectName, issuerName, serial, issuerDer);
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
