namespace Aspose.Pdf.Security;

/// <summary>Revocation-check protocol selector used by
/// <see cref="ValidationOptions.ValidationMethod"/>.</summary>
public enum ValidationMethod
{
    /// <summary>Pick OCSP or CRL based on what the certificate exposes.</summary>
    Auto,
    /// <summary>OCSP responder protocol.</summary>
    Ocsp,
    /// <summary>Certificate Revocation List.</summary>
    Crl,
    /// <summary>Run both OCSP and CRL.</summary>
    All,
}

/// <summary>How aggressively <see cref="Facades.PdfFileSignature.VerifySignature(string, ValidationOptions, out ValidationResult)"/>
/// reports problems.</summary>
public enum ValidationMode
{
    /// <summary>No revocation check (signature-bytes integrity only).</summary>
    None,
    /// <summary>Run the configured <see cref="ValidationMethod"/> but tolerate
    /// network / responder failures (default).</summary>
    OnlyCheck,
    /// <summary>Strict — any check failure produces <see cref="ValidationStatus.Invalid"/>.</summary>
    Strict,
}

/// <summary>Outcome categories reported by <see cref="ValidationResult.Status"/>.</summary>
public enum ValidationStatus
{
    /// <summary>Validation hasn't run yet.</summary>
    Undefined,
    /// <summary>Signature passes the configured checks.</summary>
    Valid,
    /// <summary>Signature fails at least one check.</summary>
    Invalid,
    /// <summary>Unable to determine — non-fatal protocol or responder error
    /// in <see cref="ValidationMode.OnlyCheck"/>.</summary>
    Unknown,
}

/// <summary>Tunes the verification policy used by Verify(SignatureName,
/// ValidationOptions, out ValidationResult).</summary>
public sealed class ValidationOptions
{
    public ValidationOptions() { }

    /// <summary>Walk the certificate chain back to a trusted root in
    /// addition to verifying the signature bytes. Default true.</summary>
    public bool CheckCertificateChain { get; set; } = true;

    /// <summary>Timeout in milliseconds for OCSP / CRL fetches. Default 5000.</summary>
    public int RequestTimeout { get; set; } = 5000;

    /// <summary>Which revocation-check protocol to use.</summary>
    public ValidationMethod ValidationMethod { get; set; } = ValidationMethod.Auto;

    /// <summary>How aggressively to report problems.</summary>
    public ValidationMode ValidationMode { get; set; } = ValidationMode.OnlyCheck;
}

/// <summary>Verification outcome produced by Verify(SignatureName,
/// ValidationOptions, out ValidationResult). Carries the
/// <see cref="ValidationStatus"/> + a human-readable <see cref="Message"/>.</summary>
public sealed class ValidationResult
{
    internal ValidationResult(ValidationStatus status, string message)
    {
        Status = status;
        Message = message ?? string.Empty;
    }

    public ValidationStatus Status { get; }
    public string Message { get; }
}
