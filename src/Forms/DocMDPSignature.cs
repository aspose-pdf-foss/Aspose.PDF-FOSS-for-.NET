namespace Aspose.Pdf.Forms;

/// <summary>Pairs a <see cref="Signature"/> (which carries the signing
/// certificate) with the <see cref="DocMDPAccessPermissions"/> level a
/// certifying signature will impose. Passed to
/// <see cref="Facades.PdfFileSignature.Certify(int, string, string, string, bool, System.Drawing.Rectangle, DocMDPSignature)"/>.</summary>
public class DocMDPSignature
{
    public DocMDPSignature(Signature signature, DocMDPAccessPermissions accessPermissions)
    {
        Signature = signature ?? throw new System.ArgumentNullException(nameof(signature));
        AccessPermissions = accessPermissions;
    }

    /// <summary>The signing-time signature (carries the PFX-loaded certificate
    /// and the standard Reason/Location/ContactInfo metadata).</summary>
    public Signature Signature { get; }

    /// <summary>Access-permission level enforced by the resulting /DocMDP
    /// transform.</summary>
    public DocMDPAccessPermissions AccessPermissions { get; }
}
