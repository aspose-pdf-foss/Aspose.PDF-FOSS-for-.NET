namespace Aspose.Pdf;

/// <summary>
/// Predefined collection field subtypes (PDF spec §7.11.5 Table 75).
/// Each subtype binds a schema field to a specific source on the file
/// specification or its embedded file stream.
/// </summary>
public enum CollectionFieldSubtype
{
    /// <summary>Unknown or absent /Subtype.</summary>
    None = 0,

    /// <summary>Text — value comes from the file spec's /CI dict.</summary>
    S = 1,

    /// <summary>Date — value comes from the file spec's /CI dict.</summary>
    D = 2,

    /// <summary>Number — value comes from the file spec's /CI dict.</summary>
    N = 3,

    /// <summary>File name (from /UF or /F on the file spec).</summary>
    F = 4,

    /// <summary>File description (from /Desc on the file spec).</summary>
    Desc = 5,

    /// <summary>Embedded file modification date (from /Params/ModDate).</summary>
    ModDate = 6,

    /// <summary>Embedded file creation date (from /Params/CreationDate).</summary>
    CreationDate = 7,

    /// <summary>Uncompressed size in bytes (from /Params/Size).</summary>
    Size = 8,

    /// <summary>Compressed (encoded) size in bytes (from the EF stream's /Length).</summary>
    CompressedSize = 9,
}
