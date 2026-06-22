namespace Aspose.Pdf.GroupProcessor;

/// <summary>
/// Identifies a PDF indirect object by its object number and generation
/// number. Used as a key when grouping or matching objects across documents.
/// </summary>
public readonly struct ObjectKey : IEquatable<ObjectKey>
{
    /// <summary>The PDF object number.</summary>
    public long Number { get; }

    /// <summary>The PDF generation number.</summary>
    public long Generation { get; }

    /// <summary>Default key with both fields set to zero.</summary>
    public static ObjectKey Empty => default;

    public ObjectKey(long number, long generation)
    {
        Number = number;
        Generation = generation;
    }

    public bool Equals(ObjectKey other) =>
        Number == other.Number && Generation == other.Generation;

    public override bool Equals(object? obj) =>
        obj is ObjectKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Number, Generation);

    public static bool operator ==(ObjectKey left, ObjectKey right) => left.Equals(right);
    public static bool operator !=(ObjectKey left, ObjectKey right) => !left.Equals(right);

    public override string ToString() => $"{Number} {Generation} R";
}
