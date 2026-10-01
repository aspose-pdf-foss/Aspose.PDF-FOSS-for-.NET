using System.IO;

namespace Aspose.Pdf.Security.Asn1;

/// <summary>The class bits of an ASN.1 identifier octet (X.690 8.1.2.2).</summary>
internal enum Asn1Class
{
    Universal = 0,
    Application = 1,
    Context = 2,
    Private = 3,
}

/// <summary>
/// One ASN.1 value as a tree node: what it is (class, tag number, primitive or constructed) and
/// how it is written. Values read with the indefinite length form (BER) keep that form when they
/// are written again; everything else is written in DER: definite, minimal lengths, sets of values
/// built with <see cref="Asn1Set.Sorted"/> in the order of their encodings.
/// </summary>
internal abstract class Asn1Node
{
    /// <summary>Universal tag numbers (X.680 8.4).</summary>
    public const int BooleanTag = 1, IntegerTag = 2, BitStringTag = 3, OctetStringTag = 4, NullTag = 5,
        ObjectIdentifierTag = 6, EnumeratedTag = 10, Utf8StringTag = 12, SequenceTag = 16, SetTag = 17,
        NumericStringTag = 18, PrintableStringTag = 19, T61StringTag = 20, VideotexStringTag = 21, Ia5StringTag = 22,
        UtcTimeTag = 23, GeneralizedTimeTag = 24, GraphicStringTag = 25, VisibleStringTag = 26,
        GeneralStringTag = 27, UniversalStringTag = 28, BmpStringTag = 30;

    public abstract Asn1Class Class { get; }
    public abstract int TagNumber { get; }
    public abstract bool Constructed { get; }

    /// <summary>Written with the indefinite length form (only values read that way).</summary>
    public virtual bool Indefinite => false;

    /// <summary>The contents octets.</summary>
    public abstract byte[] Contents();

    /// <summary>The whole encoding: identifier, length and contents.</summary>
    public byte[] Encode()
    {
        using var output = new MemoryStream();
        WriteTo(output);
        return output.ToArray();
    }

    /// <summary>Writes the whole encoding to a stream.</summary>
    public void WriteTo(Stream output)
    {
        Asn1Encoding.WriteIdentifier(output, Class, Constructed, TagNumber);
        if (Indefinite)
        {
            output.WriteByte(0x80);
            WriteIndefiniteContents(output);
            output.WriteByte(0);
            output.WriteByte(0);
            return;
        }
        var contents = Contents();
        Asn1Encoding.WriteLength(output, contents.Length);
        output.Write(contents, 0, contents.Length);
    }

    /// <summary>The contents of a value written with the indefinite length form.</summary>
    protected virtual void WriteIndefiniteContents(Stream output)
    {
        var contents = Contents();
        output.Write(contents, 0, contents.Length);
    }

    /// <summary>Equal when the encodings are equal.</summary>
    public override bool Equals(object? obj) =>
        obj is Asn1Node other && Asn1Encoding.SameBytes(Encode(), other.Encode());

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var b in Encode()) hash = hash * 31 + b;
        return hash;
    }

    /// <summary>Reads exactly one value from the bytes; anything after it is an error.</summary>
    public static Asn1Node Parse(byte[] data)
    {
        using var input = new MemoryStream(data, false);
        var node = new Asn1Parser(input, long.MaxValue).ReadNode() ?? throw new Asn1ParseException(Asn1Problem.Empty, 0, 0);
        if (input.Position < data.Length) throw new Asn1ParseException(Asn1Problem.TrailingData, 0, 0);
        return node;
    }

    /// <summary>Reads the next value from a stream; null at its end. A definite length at or past
    /// <paramref name="limit"/> is refused before its contents are read.</summary>
    public static Asn1Node? ReadFrom(Stream input, long limit) => new Asn1Parser(input, limit).ReadNode();
}

/// <summary>What was wrong with an encoding.</summary>
internal enum Asn1Problem
{
    Empty,
    TrailingData,
    Truncated,
    LengthOutOfBounds,
    UnknownTag,
    BadContents,
}

/// <summary>An encoding that cannot be read: the problem, and the declared length and the octets
/// missing (truncated), the declared length and the limit (out of bounds) or the tag number
/// (unknown tag).</summary>
internal sealed class Asn1ParseException : IOException
{
    public Asn1ParseException(Asn1Problem problem, long first, long second)
        : base("ASN.1: " + problem + " " + first + " " + second)
    {
        Problem = problem;
        First = first;
        Second = second;
    }

    public Asn1Problem Problem { get; }
    public long First { get; }
    public long Second { get; }
}
