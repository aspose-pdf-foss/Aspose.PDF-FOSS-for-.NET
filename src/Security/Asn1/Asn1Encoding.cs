using System;
using System.IO;

namespace Aspose.Pdf.Security.Asn1;

/// <summary>Identifier and length octets (X.690 8.1.2, 8.1.3) and the DER order of encodings.</summary>
internal static class Asn1Encoding
{
    private const int ConstructedBit = 0x20;
    private const int HighTagNumber = 0x1F;

    public static void WriteIdentifier(Stream output, Asn1Class cls, bool constructed, int tagNumber)
    {
        var first = ((int)cls << 6) | (constructed ? ConstructedBit : 0);
        if (tagNumber < HighTagNumber)
        {
            output.WriteByte((byte)(first | tagNumber));
            return;
        }
        output.WriteByte((byte)(first | HighTagNumber));
        WriteBase128(output, tagNumber);
    }

    /// <summary>A definite length in the fewest octets.</summary>
    public static void WriteLength(Stream output, int length)
    {
        if (length < 0x80)
        {
            output.WriteByte((byte)length);
            return;
        }
        var octets = 0;
        for (var rest = length; rest > 0; rest >>= 8) octets++;
        output.WriteByte((byte)(0x80 | octets));
        for (var i = octets - 1; i >= 0; i--) output.WriteByte((byte)(length >> (8 * i)));
    }

    /// <summary>An unsigned value in base 128, high groups first, all but the last with bit 8 set.</summary>
    public static void WriteBase128(Stream output, System.Numerics.BigInteger value)
    {
        var groups = new System.Collections.Generic.List<byte>();
        do
        {
            groups.Add((byte)(int)(value & 0x7F));
            value >>= 7;
        } while (value > 0);
        for (var i = groups.Count - 1; i >= 0; i--) output.WriteByte((byte)(groups[i] | (i > 0 ? 0x80 : 0)));
    }

    /// <summary>The DER order of two encodings (X.690 11.6): octet by octet, a shorter one that is
    /// a prefix of the other first.</summary>
    public static int CompareEncodings(byte[] a, byte[] b)
    {
        var common = Math.Min(a.Length, b.Length);
        for (var i = 0; i < common; i++)
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return a.Length.CompareTo(b.Length);
    }

    public static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
