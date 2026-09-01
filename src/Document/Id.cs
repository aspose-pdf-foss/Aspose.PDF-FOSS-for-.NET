namespace Aspose.Pdf;

/// <summary>
/// Pair of byte strings that make up the /ID array in the PDF trailer.
/// Original = first /ID entry (set at file creation); Modified = second
/// entry (rewritten on every save).
/// </summary>
public sealed class Id
{
    public Id(string original, string modified)
    {
        Original = original ?? string.Empty;
        Modified = modified ?? string.Empty;
    }

    public string Original { get; }
    public string Modified { get; }
}
