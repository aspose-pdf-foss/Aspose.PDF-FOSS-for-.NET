namespace Aspose.Pdf.Security.Saslprep;

/// <summary>
/// Unicode Normalization Form KC (NFKC) applied by the SASLprep (RFC 4013)
/// profile. Delegates to the framework normalizer, which implements the
/// canonical/compatibility decomposition + canonical composition that
/// <see cref="Stringprep"/> requires in its normalization step.
/// </summary>
internal static class Normalization
{
    public static string Normalize(string text)
        => string.IsNullOrEmpty(text)
            ? text ?? string.Empty
            : text.Normalize(System.Text.NormalizationForm.FormKC);
}
