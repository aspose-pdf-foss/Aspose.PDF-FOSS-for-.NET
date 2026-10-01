using System;
using System.IO;
using System.Numerics;
using Aspose.Pdf.Security.Asn1;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

/// <summary>The ASN.1 object model against encodings worked by hand from X.690.</summary>
public class Asn1NodeTests
{
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");
    private static byte[] Bytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = System.Convert.ToByte(hex.Substring(2 * i, 2), 16);
        return bytes;
    }

    [Theory]
    [InlineData(0, "020100")]
    [InlineData(127, "02017F")]
    [InlineData(128, "02020080")]
    [InlineData(256, "02020100")]
    [InlineData(-1, "0201FF")]
    [InlineData(-128, "020180")]
    [InlineData(-129, "0202FF7F")]
    public void Integers_are_minimal_twos_complement(int value, string der)
    {
        Assert.Equal(der, Hex(new Asn1Integer(value).Encode()));
        Assert.Equal(new BigInteger(value), ((Asn1Integer)Asn1Node.Parse(Bytes(der))).Value);
    }

    [Fact]
    public void Object_identifiers_join_the_first_two_arcs_and_write_base_128()
    {
        var oid = new Asn1ObjectIdentifier("1.2.840.113549.1.1.11");
        Assert.Equal("06092A864886F70D01010B", Hex(oid.Encode()));
        Assert.Equal("1.2.840.113549.1.1.11", ((Asn1ObjectIdentifier)Asn1Node.Parse(oid.Encode())).Id);
        Assert.Equal("2.999.3", Asn1ObjectIdentifier.FromContents(Bytes("883703")));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("3.1")]
    [InlineData("1.40")]
    [InlineData("1..2")]
    [InlineData("1.02")]
    public void Malformed_object_identifiers_are_refused(string id)
    {
        Assert.False(Asn1ObjectIdentifier.IsValid(id));
        Assert.Throws<FormatException>(() => new Asn1ObjectIdentifier(id));
    }

    [Fact]
    public void A_der_set_is_held_in_the_order_of_its_encodings()
    {
        var set = Asn1Set.Sorted([new Asn1OctetString([0xFF]), new Asn1Integer(300), new Asn1Integer(2), Asn1Null.Instance]);
        Assert.Equal("310C0201020202012C0401FF0500", Hex(set.Encode()));
    }

    [Fact]
    public void Tagging_explicitly_wraps_and_implicitly_replaces_the_tag()
    {
        var inner = new Asn1OctetString([1, 2]);
        Assert.Equal("A00404020102", Hex(Asn1Tagged.ExplicitOf(Asn1Class.Context, 0, inner).Encode()));
        var implicitTag = Asn1Tagged.ImplicitOf(Asn1Class.Context, 1, inner);
        Assert.Equal("81020102", Hex(implicitTag.Encode()));
        var read = (Asn1Tagged)Asn1Node.Parse(implicitTag.Encode());
        Assert.Equal("0102", Hex(((Asn1OctetString)read.Implicit(Asn1Node.OctetStringTag)).Octets));
        Assert.Null(read.Explicit());
    }

    [Fact]
    public void An_indefinite_length_is_read_and_written_again_as_read()
    {
        var node = (Asn1Sequence)Asn1Node.Parse(Bytes("30800201010000"));
        Assert.True(node.Indefinite);
        Assert.Single(node.Items);
        Assert.Equal("30800201010000", Hex(node.Encode()));
    }

    [Fact]
    public void Segmented_strings_are_joined()
    {
        var node = (Asn1OctetString)Asn1Node.Parse(Bytes("2480040201020401030000"));
        Assert.Equal("010203", Hex(node.Octets));
    }

    [Fact]
    public void Truncation_trailing_data_and_the_length_limit_are_reported()
    {
        var truncated = Assert.Throws<Asn1ParseException>(() => Asn1Node.Parse(Bytes("3005")));
        Assert.Equal((Asn1Problem.Truncated, 5L, 5L), (truncated.Problem, truncated.First, truncated.Second));
        Assert.Equal(Asn1Problem.TrailingData, Assert.Throws<Asn1ParseException>(() => Asn1Node.Parse(Bytes("0500FF"))).Problem);
        var bounded = Assert.Throws<Asn1ParseException>(() => Asn1Node.ReadFrom(new MemoryStream(Bytes("0205")), 2));
        Assert.Equal((Asn1Problem.LengthOutOfBounds, 5L, 2L), (bounded.Problem, bounded.First, bounded.Second));
    }

    [Fact]
    public void Times_write_der_forms_and_read_zones()
    {
        var instant = new DateTime(2024, 1, 2, 3, 4, 5, 120, DateTimeKind.Utc);
        Assert.Equal("20240102030405.12Z", Asn1Time.GeneralizedOf(instant).Text);
        Assert.Equal("240102030405Z", Asn1Time.UtcOf(instant).Text);
        Assert.Equal(new DateTime(2024, 1, 2, 1, 4, 5, DateTimeKind.Utc), new Asn1Time(true, "20240102030405+0200").ToDateTime());
        Assert.Equal(new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc), new Asn1Time(false, "991231235959Z").ToDateTime());
    }

    [Fact]
    public void Strings_are_written_in_their_own_character_sets()
    {
        Assert.Equal("1602683F", Hex(new Asn1String(Asn1Node.Ia5StringTag, "hé").Encode()));
        Assert.Equal("1E0400680065", Hex(new Asn1String(Asn1Node.BmpStringTag, "he").Encode()));
        Assert.Equal("0C0368C3A9", Hex(new Asn1String(Asn1Node.Utf8StringTag, "hé").Encode()));
        Assert.Equal("hé", ((Asn1String)Asn1Node.Parse(Bytes("0C0368C3A9"))).Value);
    }

    [Fact]
    public void Bit_strings_carry_their_unused_bits()
    {
        var bits = (Asn1BitString)Asn1Node.Parse(Bytes("030206C0"));
        Assert.Equal((6, "C0"), (bits.UnusedBits, Hex(bits.Octets)));
        Assert.Equal("030206C0", Hex(bits.Encode()));
    }

    [Fact]
    public void High_tag_numbers_use_the_long_identifier_form()
    {
        var tagged = Asn1Tagged.ExplicitOf(Asn1Class.Application, 40, Asn1Null.Instance);
        Assert.Equal("7F28020500", Hex(tagged.Encode()));
        Assert.Equal(40, Asn1Node.Parse(tagged.Encode()).TagNumber);
    }
}
