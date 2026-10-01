using System;
using System.IO;
using System.Numerics;
using System.Text;

namespace Aspose.Pdf.Security.Asn1;

/// <summary>A primitive universal value held as its contents octets.</summary>
internal abstract class Asn1Primitive : Asn1Node
{
    public override Asn1Class Class => Asn1Class.Universal;
    public override bool Constructed => false;
}

/// <summary>BOOLEAN (X.690 8.2; DER writes true as FF).</summary>
internal sealed class Asn1Boolean(bool value) : Asn1Primitive
{
    public bool Value { get; } = value;
    public override int TagNumber => BooleanTag;
    public override byte[] Contents() => [Value ? (byte)0xFF : (byte)0];
}

/// <summary>NULL (X.690 8.8).</summary>
internal sealed class Asn1Null : Asn1Primitive
{
    public static readonly Asn1Null Instance = new();
    public override int TagNumber => NullTag;
    public override byte[] Contents() => [];
}

/// <summary>INTEGER or ENUMERATED: two's complement in the fewest octets (X.690 8.3, 8.4).</summary>
internal sealed class Asn1Integer(BigInteger value, bool enumerated = false) : Asn1Primitive
{
    public BigInteger Value { get; } = value;
    public bool Enumerated { get; } = enumerated;
    public override int TagNumber => Enumerated ? EnumeratedTag : IntegerTag;

    public override byte[] Contents()
    {
        var littleEndian = Value.ToByteArray();
        Array.Reverse(littleEndian);
        return littleEndian;
    }

    /// <summary>The value of two's complement contents, high octet first.</summary>
    public static BigInteger FromContents(byte[] contents)
    {
        if (contents.Length == 0) throw new Asn1ParseException(Asn1Problem.BadContents, IntegerTag, 0);
        var littleEndian = (byte[])contents.Clone();
        Array.Reverse(littleEndian);
        return new BigInteger(littleEndian);
    }
}

/// <summary>OCTET STRING (X.690 8.7).</summary>
internal sealed class Asn1OctetString(byte[] octets) : Asn1Primitive
{
    public byte[] Octets { get; } = octets;
    public override int TagNumber => OctetStringTag;
    public override byte[] Contents() => Octets;
}

/// <summary>BIT STRING: the count of unused bits in the last octet, then the octets (X.690 8.6).</summary>
internal sealed class Asn1BitString(byte[] octets, int unusedBits) : Asn1Primitive
{
    public byte[] Octets { get; } = octets;
    public int UnusedBits { get; } = unusedBits;
    public override int TagNumber => BitStringTag;

    public override byte[] Contents()
    {
        var contents = new byte[Octets.Length + 1];
        contents[0] = (byte)UnusedBits;
        Array.Copy(Octets, 0, contents, 1, Octets.Length);
        return contents;
    }
}

/// <summary>OBJECT IDENTIFIER in dotted form (X.690 8.19).</summary>
internal sealed class Asn1ObjectIdentifier : Asn1Primitive
{
    public Asn1ObjectIdentifier(string id)
    {
        if (!IsValid(id)) throw new FormatException("ASN.1: not an object identifier: " + id);
        Id = id;
    }

    public string Id { get; }
    public override int TagNumber => ObjectIdentifierTag;

    /// <summary>At least two arcs of digits, the first 0, 1 or 2, the second under 40 unless the
    /// first is 2, no empty arcs and no leading zeros.</summary>
    public static bool IsValid(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var arcs = id!.Split('.');
        if (arcs.Length < 2) return false;
        foreach (var arc in arcs)
        {
            if (arc.Length == 0 || (arc.Length > 1 && arc[0] == '0')) return false;
            foreach (var c in arc)
                if (c < '0' || c > '9') return false;
        }
        if (arcs[0].Length > 1 || arcs[0][0] > '2') return false;
        return arcs[0] == "2" || BigInteger.Parse(arcs[1]) < 40;
    }

    public override byte[] Contents()
    {
        var arcs = Id.Split('.');
        using var output = new MemoryStream();
        Asn1Encoding.WriteBase128(output, BigInteger.Parse(arcs[0]) * 40 + BigInteger.Parse(arcs[1]));
        for (var i = 2; i < arcs.Length; i++) Asn1Encoding.WriteBase128(output, BigInteger.Parse(arcs[i]));
        return output.ToArray();
    }

    /// <summary>The dotted form of the contents octets.</summary>
    public static string FromContents(byte[] contents)
    {
        var id = new StringBuilder();
        var value = BigInteger.Zero;
        var first = true;
        foreach (var b in contents)
        {
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) != 0) continue;
            if (first)
            {
                var head = value < 80 ? value / 40 : 2;
                id.Append(head).Append('.').Append(value - head * 40);
                first = false;
            }
            else id.Append('.').Append(value);
            value = BigInteger.Zero;
        }
        if (first || (contents[contents.Length - 1] & 0x80) != 0)
            throw new Asn1ParseException(Asn1Problem.BadContents, ObjectIdentifierTag, 0);
        return id.ToString();
    }
}

/// <summary>A character string of one of the universal string types, written in its own character
/// set: UTF-8, UTF-16BE (BMP), UTF-32BE (universal), Latin-1 (teletex), and ASCII for the rest with a
/// character outside it written as '?'.</summary>
internal sealed class Asn1String(int tagNumber, string value) : Asn1Primitive
{
    public string Value { get; } = value;
    public override int TagNumber { get; } = tagNumber;

    public static bool IsStringTag(int tag) => tag is Utf8StringTag or NumericStringTag or PrintableStringTag
        or T61StringTag or VideotexStringTag or Ia5StringTag or GraphicStringTag or VisibleStringTag
        or GeneralStringTag or UniversalStringTag or BmpStringTag;

    public override byte[] Contents() => TagNumber switch
    {
        Utf8StringTag => new UTF8Encoding(false).GetBytes(Value),
        BmpStringTag => new UnicodeEncoding(true, false).GetBytes(Value),
        UniversalStringTag => new UTF32Encoding(true, false).GetBytes(Value),
        T61StringTag or VideotexStringTag => Latin1(Value),
        _ => Ascii(Value),
    };

    public static string FromContents(int tagNumber, byte[] contents) => tagNumber switch
    {
        Utf8StringTag => new UTF8Encoding(false).GetString(contents),
        BmpStringTag => new UnicodeEncoding(true, false).GetString(contents),
        UniversalStringTag => new UTF32Encoding(true, false).GetString(contents),
        _ => FromLatin1(contents),
    };

    private static byte[] Ascii(string value)
    {
        var bytes = new byte[value.Length];
        for (var i = 0; i < value.Length; i++) bytes[i] = value[i] < 0x80 ? (byte)value[i] : (byte)'?';
        return bytes;
    }

    private static byte[] Latin1(string value)
    {
        var bytes = new byte[value.Length];
        for (var i = 0; i < value.Length; i++) bytes[i] = value[i] < 0x100 ? (byte)value[i] : (byte)'?';
        return bytes;
    }

    private static string FromLatin1(byte[] contents)
    {
        var chars = new char[contents.Length];
        for (var i = 0; i < contents.Length; i++) chars[i] = (char)contents[i];
        return new string(chars);
    }
}

/// <summary>A value whose universal tag this model does not interpret, kept as its octets.</summary>
internal sealed class Asn1Raw(Asn1Class cls, int tagNumber, bool constructed, byte[] contents) : Asn1Node
{
    private readonly byte[] _contents = contents;
    public override Asn1Class Class { get; } = cls;
    public override int TagNumber { get; } = tagNumber;
    public override bool Constructed { get; } = constructed;
    public override byte[] Contents() => _contents;
}
