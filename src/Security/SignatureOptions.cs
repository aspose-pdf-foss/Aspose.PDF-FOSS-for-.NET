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
    /// <see cref="ContentsSize"/> directly; a signature that does not fit
    /// the reservation raises <see cref="SignatureLengthMismatchException"/>
    /// instead of the generic overflow error.</summary>
    public bool AvoidEstimating { get; set; }

    /// <summary>The fixed /Contents reservation applied when estimation is
    /// skipped and the caller supplied no explicit length — the
    /// signer's default signature size (3000 bytes).</summary>
    internal const int DefaultSignatureSize = 3000;

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
