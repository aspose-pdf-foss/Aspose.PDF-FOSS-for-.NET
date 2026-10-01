using System.Collections.Generic;
using Aspose.Pdf.Facades;

namespace Aspose.Pdf.Signatures;

/// <summary>What a <see cref="SignaturesCompromiseDetector"/> found: the signatures whose
/// structure is a recognised forgery, and how much of the document the sound signatures
/// cover.</summary>
public sealed class CompromiseCheckResult
{
    /// <summary>The signatures recognised as forged (a universal signature forgery or a
    /// signature wrapping attack), in field order.</summary>
    public readonly IList<SignatureName> CompromisedSignatures = new List<SignatureName>();

    /// <summary>True when at least one signature is forged.</summary>
    public bool HasCompromisedSignatures => CompromisedSignatures.Count > 0;

    /// <summary>How much of the document the signatures cover:
    /// <see cref="SignaturesCoverage.Undefined"/> when there is nothing sound to measure,
    /// <see cref="SignaturesCoverage.EntirelySigned"/> when the last signature reaches the
    /// end of the file, <see cref="SignaturesCoverage.PartiallySigned"/> when content was
    /// added after it (an incremental saving attack leaves exactly that).</summary>
    public SignaturesCoverage SignaturesCoverage { get; internal set; }

    internal CompromiseCheckResult() { }

    internal void AddSignature(SignatureName signatureName) => CompromisedSignatures.Add(signatureName);
}
