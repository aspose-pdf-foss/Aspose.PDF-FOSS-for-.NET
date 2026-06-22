namespace Aspose.Pdf.IO;

internal readonly record struct XRefEntry
{
    public int ObjectNumber { get; init; }
    public int Generation { get; init; }
    public long Offset { get; init; }
    public bool InUse { get; init; }

    // For compressed objects (xref stream type 2)
    public bool IsCompressed { get; init; }
    public int StreamObjectNumber { get; init; }  // object stream containing this object
    public int IndexInStream { get; init; }        // index within the object stream
}
