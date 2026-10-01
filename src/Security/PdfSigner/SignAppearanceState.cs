using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

public sealed partial class PdfSigner
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SignAppearanceState
{
    public int contentsSize;
    // The opened document and the incremental-update stream are owned (using) by the
    // signing entry; the stages reach them through these references.
    public Document doc = null!;
    public MemoryStream ms = null!;
    public long contentsOffset;
    public long contentsLength;
    public IO.PdfReader reader = null!;
    public Aspose.Pdf.Core.PdfDictionary trailer = null!;
    public IO.XRefTable xref = null!;
    public string fieldName = null!;
    public int maxObj;
    public int nextObj;
    public int sigValObjNum;
    public int sigFieldObjNum;
    public int acroFormObjNum;
    public int appearanceStreamObjNum;
    public int dssCertObjNum;
    public Aspose.Pdf.Core.PdfDictionary sigValDict = null!;
    // The visible banner rides the same nested forms and embedded face as the
    // invisible one; only the box comes from the caller's rectangle here.
    public Rectangle? visRect;
    public (int ApObjNum, List<(int Num, Aspose.Pdf.Core.PdfDictionary Dict, bool IsStream)> Objects, int NextObj)? visBanner;
    public Aspose.Pdf.Core.PdfDictionary sigFieldDict = null!;
    public Aspose.Pdf.Core.PdfDictionary acroFormDict = null!;
    public Aspose.Pdf.Core.PdfDictionary? appearanceStreamDict;
    // When the source document is encrypted, every appended string/stream must
    // be encrypted with the document's per-object key (only the signature
    // /Contents is exempt). Otherwise the signature reason/location text and the
    // visible appearance would leak as plaintext into an encrypted PDF.
    public Security.PdfDecryptor? appendDecryptor;
    // Write objects
    public Dictionary<int, long> offsets = null!;
    public byte[] sigValBytes = null!;
    // The widget must ride in its page's /Annots — viewers and rasterisers
    // paint annotations from the page tree, not from the AcroForm field
    // list — so the page object is rewritten with the widget appended.
    public Aspose.Pdf.Core.PdfIndirectRef? pageRef;
    public Aspose.Pdf.Core.PdfIndirectRef? catalogRef;
    public int catalogObjNum;
    public Aspose.Pdf.Core.PdfDictionary catalogDict = null!;
    public long originalStartXref;
    public byte[] fileBytes = null!;
    public long[] byteRange = null!;
    // Hash the two byte ranges (SHA-256, or SHA-1 for adbe.pkcs7.sha1 / adbe.x509.rsa_sha1).
    public DigestHashAlgorithm digest;
    public byte[] hashInput = null!;
    public byte[] hash = null!;
    public byte[] signatureBytes = null!;
    public string hexSignature = null!;
    public byte[] hexBytes = null!;
    public byte[] pdfData = default!;
    public PdfCertificate certificate = default!;
    public SignatureOptions options = null!;
    public SignatureAppearance appearance = default!;
}
}
