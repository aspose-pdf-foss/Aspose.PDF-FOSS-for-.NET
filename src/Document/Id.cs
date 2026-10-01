namespace Aspose.Pdf;

/// <summary>
/// Pair of byte strings that make up the /ID array in the PDF trailer.
/// Original = first /ID entry (set at file creation); Modified = second
/// entry (rewritten on every save).
/// </summary>
public sealed class Id
{
    /// <summary>Creates a file identifier from its two parts; a <c>null</c> part becomes an empty string.</summary>
    public Id(string original, string modified)
    {
        Original = original ?? string.Empty;
        Modified = modified ?? string.Empty;
    }

    public string Original { get; }
    /// <summary>Gets the second /ID entry, which identifies this particular version of the file.</summary>
    public string Modified { get; }
}
