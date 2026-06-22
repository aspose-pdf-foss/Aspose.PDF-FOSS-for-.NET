using System.Security.Cryptography.X509Certificates;

namespace Aspose.Pdf.Security;

/// <summary>Bundles the public certificate used to encrypt a document
/// and the private-key store needed to decrypt it. Constructed by callers
/// and passed to <see cref="Document"/> ctors that open certificate-
/// encrypted PDFs.</summary>
public sealed class CertificateEncryptionOptions
{
    /// <summary>Public-key certificate that encrypted the document (the
    /// /Recipient entry of a PDF Public-Key security handler dictionary).</summary>
    public X509Certificate2 PublicCertificate { get; }

    /// <summary>Path to a PFX/PKCS#12 file holding the private key matching
    /// <see cref="PublicCertificate"/>, or null when the private key is in
    /// a Windows certificate store.</summary>
    public string? PfxPath { get; }

    /// <summary>Password for the PFX file at <see cref="PfxPath"/>, or
    /// null when the file isn't password-protected.</summary>
    public string? PfxPassword { get; }

    /// <summary>Windows certificate store containing the private key, when
    /// no <see cref="PfxPath"/> is supplied.</summary>
    public StoreName? StoreName { get; }

    /// <summary>Windows certificate store location.</summary>
    public StoreLocation? StoreLocation { get; }

    public CertificateEncryptionOptions(X509Certificate2 publicCertificate, string pfxPath, string pfxPassword)
    {
        PublicCertificate = publicCertificate ?? throw new System.ArgumentNullException(nameof(publicCertificate));
        PfxPath = pfxPath;
        PfxPassword = pfxPassword;
    }

    public CertificateEncryptionOptions(X509Certificate2 publicCertificate, StoreName storeName, StoreLocation storeLocation)
    {
        PublicCertificate = publicCertificate ?? throw new System.ArgumentNullException(nameof(publicCertificate));
        StoreName = storeName;
        StoreLocation = storeLocation;
    }

    public CertificateEncryptionOptions(string publicCertificatePath, string pfxPath, string pfxPassword)
    {
#pragma warning disable SYSLIB0057
        PublicCertificate = new X509Certificate2(publicCertificatePath);
#pragma warning restore SYSLIB0057
        PfxPath = pfxPath;
        PfxPassword = pfxPassword;
    }

    public CertificateEncryptionOptions(string publicCertificatePath, StoreName storeName, StoreLocation storeLocation)
    {
#pragma warning disable SYSLIB0057
        PublicCertificate = new X509Certificate2(publicCertificatePath);
#pragma warning restore SYSLIB0057
        StoreName = storeName;
        StoreLocation = storeLocation;
    }
}
