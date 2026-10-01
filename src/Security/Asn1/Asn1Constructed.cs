using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Aspose.Pdf.Security.Asn1;

/// <summary>SEQUENCE or SET of values in the order held; those read with the indefinite length
/// form are written with it again.</summary>
internal abstract class Asn1Collection(IReadOnlyList<Asn1Node> items, bool indefinite) : Asn1Node
{
    public IReadOnlyList<Asn1Node> Items { get; } = items;
    public override Asn1Class Class => Asn1Class.Universal;
    public override bool Constructed => true;
    public override bool Indefinite { get; } = indefinite;

    public override byte[] Contents()
    {
        using var output = new MemoryStream();
        foreach (var item in Items) item.WriteTo(output);
        return output.ToArray();
    }
}

/// <summary>SEQUENCE (X.690 8.9).</summary>
internal sealed class Asn1Sequence(IReadOnlyList<Asn1Node> items, bool indefinite = false) : Asn1Collection(items, indefinite)
{
    public override int TagNumber => SequenceTag;
}

/// <summary>SET (X.690 8.11).</summary>
internal sealed class Asn1Set(IReadOnlyList<Asn1Node> items, bool indefinite = false) : Asn1Collection(items, indefinite)
{
    public override int TagNumber => SetTag;

    /// <summary>A set whose values are held in the DER order of their encodings (X.690 11.6).</summary>
    public static Asn1Set Sorted(IEnumerable<Asn1Node> items)
    {
        var list = items.Select(item => (Item: item, Bytes: item.Encode())).ToList();
        list.Sort((a, b) => Asn1Encoding.CompareEncodings(a.Bytes, b.Bytes));
        return new Asn1Set(list.Select(entry => entry.Item).ToList());
    }
}

/// <summary>
/// A value under an application, context-specific or private tag (X.690 8.14). Tagged explicitly
/// it wraps a whole encoding; implicitly it carries another value's contents under its own tag. As
/// read, which of the two was meant is not known: <see cref="Explicit"/> and <see cref="Implicit"/>
/// read the contents either way.
/// </summary>
internal sealed class Asn1Tagged : Asn1Node
{
    private readonly byte[] _contents;

    private Asn1Tagged(Asn1Class cls, int tagNumber, bool constructed, byte[] contents, bool indefinite)
    {
        Class = cls;
        TagNumber = tagNumber;
        Constructed = constructed;
        _contents = contents;
        Indefinite = indefinite;
    }

    public override Asn1Class Class { get; }
    public override int TagNumber { get; }
    public override bool Constructed { get; }
    public override bool Indefinite { get; }
    public override byte[] Contents() => _contents;

    /// <summary>The value wrapped whole: always constructed.</summary>
    public static Asn1Tagged ExplicitOf(Asn1Class cls, int tagNumber, Asn1Node inner) =>
        new(cls, tagNumber, true, inner.Encode(), false);

    /// <summary>The value's contents under this tag, constructed when the value is.</summary>
    public static Asn1Tagged ImplicitOf(Asn1Class cls, int tagNumber, Asn1Node inner) =>
        new(cls, tagNumber, inner.Constructed, inner.Contents(), false);

    /// <summary>As read from an encoding.</summary>
    public static Asn1Tagged Read(Asn1Class cls, int tagNumber, bool constructed, byte[] contents, bool indefinite) =>
        new(cls, tagNumber, constructed, contents, indefinite);

    /// <summary>The contents read as the single wrapped value; null when they hold something else.</summary>
    public Asn1Node? Explicit()
    {
        if (!Constructed) return null;
        var items = Asn1Parser.ReadAll(_contents);
        return items.Count == 1 ? items[0] : null;
    }

    /// <summary>The values in the contents, one after another.</summary>
    public IReadOnlyList<Asn1Node> Elements() => Constructed ? Asn1Parser.ReadAll(_contents) : [];

    /// <summary>The contents read as a value with the universal tag given.</summary>
    public Asn1Node Implicit(int universalTag) =>
        Asn1Parser.Build(Asn1Class.Universal, universalTag, Constructed, _contents, Indefinite);
}

/// <summary>UTCTime or GeneralizedTime as written (X.680 46, 47; DER form X.690 11.7, 11.8).</summary>
internal sealed class Asn1Time(bool generalized, string text) : Asn1Node
{
    public bool Generalized { get; } = generalized;
    public string Text { get; } = text;
    public override Asn1Class Class => Asn1Class.Universal;
    public override int TagNumber => Generalized ? GeneralizedTimeTag : UtcTimeTag;
    public override bool Constructed => false;
    public override byte[] Contents() => Encoding.ASCII.GetBytes(Text);

    /// <summary>DER GeneralizedTime of an instant: UTC, seconds always, a fraction only when
    /// not zero and without trailing zeros, then Z. Local and unspecified times are taken as local.</summary>
    public static Asn1Time GeneralizedOf(DateTime instant)
    {
        var utc = instant.Kind == DateTimeKind.Utc ? instant : instant.ToUniversalTime();
        var text = utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var fraction = utc.ToString("fffffff", CultureInfo.InvariantCulture).TrimEnd('0');
        return new Asn1Time(true, text + (fraction.Length > 0 ? "." + fraction : "") + "Z");
    }

    /// <summary>DER UTCTime of an instant (years 1950 to 2049).</summary>
    public static Asn1Time UtcOf(DateTime instant)
    {
        var utc = instant.Kind == DateTimeKind.Utc ? instant : instant.ToUniversalTime();
        return new Asn1Time(false, utc.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture) + "Z");
    }

    /// <summary>The instant written, in UTC: seconds and a fraction optional, then Z, an offset, or
    /// nothing (local time); a two-digit year from 50 is 19xx.</summary>
    public DateTime ToDateTime()
    {
        var digits = Generalized ? 4 : 2;
        var i = 0;
        int Number(int count)
        {
            if (i + count > Text.Length) throw new FormatException("ASN.1: bad time " + Text);
            var value = int.Parse(Text.Substring(i, count), NumberStyles.None, CultureInfo.InvariantCulture);
            i += count;
            return value;
        }
        var year = Number(digits);
        if (!Generalized) year += year < 50 ? 2000 : 1900;
        int month = Number(2), day = Number(2), hour = Number(2), minute = Number(2);
        var second = i + 2 <= Text.Length && char.IsDigit(Text[i]) ? Number(2) : 0;
        var ticks = 0L;
        if (i < Text.Length && (Text[i] == '.' || Text[i] == ','))
        {
            var start = ++i;
            while (i < Text.Length && char.IsDigit(Text[i])) i++;
            var fraction = Text.Substring(start, i - start);
            ticks = (long)Math.Round(double.Parse("0." + fraction, CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond);
        }
        var local = new DateTime(year, month, day, hour, minute, second).AddTicks(ticks);
        return Zone(local, i);
    }

    private DateTime Zone(DateTime written, int i)
    {
        if (i >= Text.Length) return DateTime.SpecifyKind(written, DateTimeKind.Local).ToUniversalTime();
        if (Text[i] == 'Z') return DateTime.SpecifyKind(written, DateTimeKind.Utc);
        var sign = Text[i] == '-' ? -1 : 1;
        var hours = int.Parse(Text.Substring(i + 1, 2), CultureInfo.InvariantCulture);
        var minutes = Text.Length >= i + 5 ? int.Parse(Text.Substring(i + 3, 2), CultureInfo.InvariantCulture) : 0;
        return DateTime.SpecifyKind(written.AddMinutes(-sign * (hours * 60 + minutes)), DateTimeKind.Utc);
    }
}
