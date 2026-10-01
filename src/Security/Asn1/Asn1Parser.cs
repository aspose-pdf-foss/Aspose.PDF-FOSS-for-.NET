using System.Collections.Generic;
using System.IO;

namespace Aspose.Pdf.Security.Asn1;

/// <summary>
/// Reads BER (and so DER) encodings into <see cref="Asn1Node"/> trees (X.690 8.1): identifier
/// octets with high tag numbers, definite and indefinite lengths, segmented (constructed) strings
/// joined into one.
/// </summary>
internal sealed class Asn1Parser(Stream input, long limit)
{
    private const int ConstructedBit = 0x20;
    private const int HighTagNumber = 0x1F;
    private const int IndefiniteLength = 0x80;

    /// <summary>A marker for the end-of-contents octets closing an indefinite length.</summary>
    private static readonly Asn1Node EndOfContents = new Asn1Raw(Asn1Class.Universal, 0, false, []);

    /// <summary>The next value; null at the end of the input.</summary>
    public Asn1Node? ReadNode()
    {
        var node = ReadNodeOrEnd();
        if (ReferenceEquals(node, EndOfContents)) throw new Asn1ParseException(Asn1Problem.BadContents, 0, 0);
        return node;
    }

    /// <summary>The values encoded one after another in the bytes.</summary>
    public static IReadOnlyList<Asn1Node> ReadAll(byte[] data)
    {
        using var stream = new MemoryStream(data, false);
        var parser = new Asn1Parser(stream, long.MaxValue);
        var items = new List<Asn1Node>();
        while (parser.ReadNode() is { } node) items.Add(node);
        return items;
    }

    private Asn1Node? ReadNodeOrEnd()
    {
        var first = input.ReadByte();
        if (first < 0) return null;
        var cls = (Asn1Class)(first >> 6);
        var constructed = (first & ConstructedBit) != 0;
        var tagNumber = first & HighTagNumber;
        if (tagNumber == HighTagNumber) tagNumber = (int)ReadBase128();
        var lengthOctet = NextByte();
        if (first == 0 && lengthOctet == 0) return EndOfContents;
        if (lengthOctet == IndefiniteLength)
        {
            if (!constructed) throw new Asn1ParseException(Asn1Problem.BadContents, tagNumber, 0);
            var children = new List<Asn1Node>();
            while (true)
            {
                var child = ReadNodeOrEnd() ?? throw new Asn1ParseException(Asn1Problem.Truncated, 0, 0);
                if (ReferenceEquals(child, EndOfContents)) break;
                children.Add(child);
            }
            return Build(cls, tagNumber, true, Join(children), true, children);
        }
        var length = ReadLength(lengthOctet);
        if (length >= limit) throw new Asn1ParseException(Asn1Problem.LengthOutOfBounds, length, limit);
        return Build(cls, tagNumber, constructed, ReadContents(length), false);
    }

    /// <summary>A value of the class and tag from its contents (and, read with an indefinite
    /// length, its values already read).</summary>
    public static Asn1Node Build(Asn1Class cls, int tag, bool constructed, byte[] contents, bool indefinite,
        IReadOnlyList<Asn1Node>? children = null)
    {
        if (cls != Asn1Class.Universal) return Asn1Tagged.Read(cls, tag, constructed, contents, indefinite);
        if (constructed)
        {
            var items = children ?? ReadAll(contents);
            if (tag == Asn1Node.SequenceTag) return new Asn1Sequence(items, indefinite);
            if (tag == Asn1Node.SetTag) return new Asn1Set(items, indefinite);
            if (tag is Asn1Node.OctetStringTag or Asn1Node.BitStringTag || Asn1String.IsStringTag(tag))
                return Primitive(tag, JoinSegments(tag, items));
            return new Asn1Raw(cls, tag, true, contents);
        }
        return Primitive(tag, contents);
    }

    private static Asn1Node Primitive(int tag, byte[] contents) => tag switch
    {
        Asn1Node.BooleanTag => new Asn1Boolean(contents.Length == 1 ? contents[0] != 0 : throw Bad(tag)),
        Asn1Node.IntegerTag => new Asn1Integer(Asn1Integer.FromContents(contents)),
        Asn1Node.EnumeratedTag => new Asn1Integer(Asn1Integer.FromContents(contents), true),
        Asn1Node.NullTag => contents.Length == 0 ? Asn1Null.Instance : throw Bad(tag),
        Asn1Node.OctetStringTag => new Asn1OctetString(contents),
        Asn1Node.BitStringTag => contents.Length > 0 && contents[0] < 8
            ? new Asn1BitString(contents[1..], contents[0]) : throw Bad(tag),
        Asn1Node.ObjectIdentifierTag => new Asn1ObjectIdentifier(Asn1ObjectIdentifier.FromContents(contents)),
        Asn1Node.UtcTimeTag => new Asn1Time(false, System.Text.Encoding.ASCII.GetString(contents)),
        Asn1Node.GeneralizedTimeTag => new Asn1Time(true, System.Text.Encoding.ASCII.GetString(contents)),
        _ when Asn1String.IsStringTag(tag) => new Asn1String(tag, Asn1String.FromContents(tag, contents)),
        _ => new Asn1Raw(Asn1Class.Universal, tag, false, contents),
    };

    private static Asn1ParseException Bad(int tag) => new(Asn1Problem.BadContents, tag, 0);

    /// <summary>The contents of a segmented string: its segments' octets end to end (a bit string's
    /// unused-bit count taken from the last segment).</summary>
    private static byte[] JoinSegments(int tag, IReadOnlyList<Asn1Node> segments)
    {
        using var output = new MemoryStream();
        var unused = 0;
        if (tag == Asn1Node.BitStringTag) output.WriteByte(0);
        foreach (var segment in segments)
        {
            var octets = segment.Contents();
            if (tag == Asn1Node.BitStringTag && octets.Length > 0)
            {
                unused = octets[0];
                output.Write(octets, 1, octets.Length - 1);
            }
            else output.Write(octets, 0, octets.Length);
        }
        var joined = output.ToArray();
        if (tag == Asn1Node.BitStringTag) joined[0] = (byte)unused;
        return joined;
    }

    private static byte[] Join(IReadOnlyList<Asn1Node> children)
    {
        using var output = new MemoryStream();
        foreach (var child in children) child.WriteTo(output);
        return output.ToArray();
    }

    private long ReadBase128()
    {
        long value = 0;
        int b;
        do
        {
            b = NextByte();
            value = (value << 7) | (long)(b & 0x7F);
        } while ((b & 0x80) != 0);
        return value;
    }

    private long ReadLength(int first)
    {
        if (first < 0x80) return first;
        long length = 0;
        for (var i = 0; i < (first & 0x7F); i++) length = (length << 8) | (long)NextByte();
        return length;
    }

    private byte[] ReadContents(long length)
    {
        var contents = new byte[length];
        var got = 0;
        while (got < length)
        {
            var n = input.Read(contents, got, (int)(length - got));
            if (n <= 0) throw new Asn1ParseException(Asn1Problem.Truncated, length, length - got);
            got += n;
        }
        return contents;
    }

    private int NextByte()
    {
        var b = input.ReadByte();
        return b >= 0 ? b : throw new Asn1ParseException(Asn1Problem.Truncated, 0, 1);
    }
}
