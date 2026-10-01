using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

public sealed partial class PdfSigner
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SignFlowState
{
    public int contentsSize;
    // The opened document and the incremental-update stream are owned (using) by the
    // signing entry; the stages reach them through these references.
    public Document doc = null!;
    public MemoryStream ms = null!;
    public IO.PdfReader reader = null!;
    public Aspose.Pdf.Core.PdfDictionary trailer = null!;
    public IO.XRefTable xref = null!;
    // Resolve the target field name. An explicit name is honoured; otherwise
    // pick the first SignatureN that is free or blank — signing again must not
    // overwrite an already-signed field (each signature is its own revision).
    public string fieldName = null!;
    // Determine next available object numbers
    public int maxObj;
    public int nextObj;
    // An inline field is adopted: it keeps its /Rect and /T but takes a fresh object
    // number, and its /Fields slot is rewritten to point at it.
    public Aspose.Pdf.Core.PdfIndirectRef? existingFieldRef;
    public Aspose.Pdf.Core.PdfDictionary? existingFieldDict;
    public bool adoptDirectField;
    // Allocate object numbers for: sig value dict, sig field, updated AcroForm
    public int sigValObjNum;
    public int sigFieldObjNum;
    public int acroFormObjNum;
    public int dssCertObjNum;
    // The signed widget carries a TEXT BANNER, not the blank field's placeholder:
    // one is written for every signature, an invisible one applied to a
    // pre-existing field included (see SignatureBanner). Built before the field dict
    // so its /AP can point at the appearance.
    public (int ApObjNum, List<(int Num, PdfDictionary Dict, bool IsStream)> Objects, int NextObj)? banner;
    // Build signature value dictionary with placeholder
    public Aspose.Pdf.Core.PdfDictionary sigValDict = null!;
    // Build signature field widget — reuse the existing field dict (keeping
    // its /Rect, /P, /AP, /MK …) when signing a pre-existing blank field.
    public Aspose.Pdf.Core.PdfDictionary sigFieldDict = null!;
    // Build or update AcroForm. When reusing an existing field, it is
    // already listed in /Fields — don't append a duplicate reference.
    public Aspose.Pdf.Core.PdfDictionary acroFormDict = null!;
    public long contentsOffset;
    public long contentsLength;
    // Write the new objects
    public Dictionary<int, long> offsets = null!;
    public byte[] sigValBytes = null!;
    // Write updated catalog (add/update AcroForm reference)
    public Aspose.Pdf.Core.PdfIndirectRef? catalogRef;
    public int catalogObjNum;
    public Aspose.Pdf.Core.PdfDictionary catalogDict = null!;
    // Write xref + trailer
    public long originalStartXref;
    public byte[] fileBytes = null!;
    // Step 3: Compute ByteRange — the two ranges that exclude /Contents value
    // ByteRange = [0, contentsOffset, contentsOffset + contentsLength, fileLength - (contentsOffset + contentsLength)]
    public long[] byteRange = null!;
    // Step 5: Compute the hash over the two byte ranges. Which digest applies
    // depends on the caller's request, the /SubFilter default (SHA-1 for the
    // adbe.pkcs7.sha1 / adbe.x509.rsa_sha1 handlers) and the signing key.
    public DigestHashAlgorithm digest;
    public byte[] hashInput = null!;
    public byte[] hash = null!;
    // Step 6: Create PKCS#7/CMS detached signature
    public byte[] signatureBytes = null!;
    // Step 7: Write the signature into the /Contents placeholder
    public string hexSignature = null!;
    // Write hex string into the placeholder (skip the leading '<' at contentsOffset)
    public byte[] hexBytes = null!;
    public byte[] pdfData = default!;
    public PdfCertificate certificate = default!;
    public SignatureOptions options = null!;
}
}
