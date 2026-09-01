namespace Aspose.Pdf.GroupProcessor;

/// <summary>
/// A pair of characters treated as a single key - the two halves of a surrogate
/// pair or of a two-byte CID code - packed into one 32-bit value so pairs can be
/// hashed and compared without allocating.
/// </summary>
internal class TwoChars
{
    /// <summary>The high half of the pair.</summary>
    public char FirstChar;

    /// <summary>The low half of the pair.</summary>
    public char SecondChar;

    /// <summary>The pair packed into one value: the first character in the high
    /// 16 bits, the second in the low 16.</summary>
    public uint Value => ((uint)FirstChar << 16) + SecondChar;

    public override int GetHashCode() => (int)Value;

    public override bool Equals(object? obj) =>
        obj is TwoChars other && other.FirstChar == FirstChar && other.SecondChar == SecondChar;
}
