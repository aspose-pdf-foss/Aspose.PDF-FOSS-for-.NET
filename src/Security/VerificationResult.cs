using System;

namespace Aspose.Pdf.Security;

/// <summary>Outcome of a signature verification.</summary>
public enum VerificationState
{
    /// <summary>The signature is cryptographically valid.</summary>
    Valid,

    /// <summary>The signature is present but failed cryptographic verification.</summary>
    Invalid,

    /// <summary>Verification could not reach a valid/invalid verdict — e.g. the
    /// signature structure is compromised by a forgery attack (USF/SWA).</summary>
    Undefined,
}

/// <summary>
/// Result of a non-throwing signature verification
/// (<see cref="Aspose.Pdf.Facades.PdfFileSignature.TryVerifySignature(Aspose.Pdf.Facades.SignatureName, out VerificationResult)"/>).
/// </summary>
public sealed class VerificationResult
{
    /// <summary>Parameterless constructor. Produces an <see cref="VerificationState.Undefined"/> result.</summary>
    public VerificationResult() { }

    private VerificationResult(VerificationState state, bool isCompromised, string message, Exception? exception)
    {
        State = state;
        IsCompromised = isCompromised;
        Message = message;
        VerificationException = exception;
    }

    /// <summary>The verification verdict.</summary>
    public VerificationState State { get; } = VerificationState.Undefined;

    /// <summary>True when the signature structure was recognised as a forgery attack
    /// (USF/SWA) rather than merely failing cryptographic checks.</summary>
    public bool IsCompromised { get; }

    /// <summary>Human-readable description of the verification outcome.</summary>
    public string Message { get; } = string.Empty;

    /// <summary>The exception captured during verification, if any.</summary>
    public Exception? VerificationException { get; }

    internal static VerificationResult Valid()
        => new(VerificationState.Valid, isCompromised: false, "Signature is valid.", null);

    internal static VerificationResult Invalid(string message)
        => new(VerificationState.Invalid, isCompromised: false, message, null);

    internal static VerificationResult Undefined(string message = "", Exception? exception = null)
        => new(VerificationState.Undefined, isCompromised: false, message, exception);

    /// <summary>A result for a signature whose structure is compromised by a
    /// forgery attack — state is <see cref="VerificationState.Undefined"/> and
    /// <see cref="IsCompromised"/> is true.</summary>
    internal static VerificationResult Compromised(string message, Exception? exception = null)
        => new(VerificationState.Undefined, isCompromised: true, message, exception);
}
