using Aspose.Pdf.Facades;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Signatures;

namespace Aspose.Pdf;

/// <summary>Checks the signatures of a document for the known structural attacks - a
/// universal signature forgery (an empty, hollow or fabricated /Contents or /ByteRange), a
/// signature wrapping attack (unsigned bytes hidden in the /Contents gap) - and measures
/// whether the sound signatures cover the whole file or content was appended after them
/// (an incremental saving attack).</summary>
public sealed class SignaturesCompromiseDetector
{
    private readonly Document? _document;
    private readonly PdfFileSignature? _facade;

    /// <summary>Check the signatures of <paramref name="document"/>.</summary>
    public SignaturesCompromiseDetector(Document document) => _document = document;

    internal SignaturesCompromiseDetector(PdfFileSignature signature) => _facade = signature;

    /// <summary>Run the check. True only when every signature is sound and together they
    /// cover the entire document; the details are in <paramref name="compromiseCheckResult"/>.</summary>
    public bool Check(out CompromiseCheckResult compromiseCheckResult)
    {
        var result = new CompromiseCheckResult { SignaturesCoverage = SignaturesCoverage.Undefined };
        compromiseCheckResult = result;
        using var opened = _document is null ? _facade?.OpenBound() : null;
        var document = _document ?? opened;
        if (document is null) return false;

        long fileLength = document.Reader?.RawData?.Length ?? 0;
        long coveredEnd = -1;
        var any = false;
        foreach (var signature in Signature.EnumerateSignatures(document))
        {
            any = true;
            var name = signature.FieldName ?? string.Empty;
            if (signature.DetectForgery(fileLength) is not null)
            {
                result.AddSignature(new SignatureName(name, name, hasSignature: true));
                continue;
            }
            var range = signature.ByteRangeRaw!;
            coveredEnd = System.Math.Max(coveredEnd, range[2] + range[3]);
        }

        // Nothing signed is nothing compromised: the check passes and there is no
        // coverage to measure. A forged signature fails it whatever the others cover.
        if (!any) return true;
        if (result.HasCompromisedSignatures) return false;
        result.SignaturesCoverage = coveredEnd >= fileLength
            ? SignaturesCoverage.EntirelySigned
            : SignaturesCoverage.PartiallySigned;
        return result.SignaturesCoverage == SignaturesCoverage.EntirelySigned;
    }
}
