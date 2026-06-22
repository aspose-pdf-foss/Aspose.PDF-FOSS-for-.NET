using System.Collections;
using System.Globalization;
using System.Text;

namespace Aspose.Pdf.Core;

internal abstract class PdfObject
{
}

internal sealed class PdfNull : PdfObject
{
    public static readonly PdfNull Instance = new();
    private PdfNull() { }
    public override string ToString() => "null";
}

internal sealed class PdfBoolean : PdfObject
{
    public static readonly PdfBoolean True = new(true);
    public static readonly PdfBoolean False = new(false);

    public bool Value { get; }
    private PdfBoolean(bool value) => Value = value;
    public override string ToString() => Value ? "true" : "false";
}

internal sealed class PdfInteger : PdfObject
{
    public long Value { get; }
    public PdfInteger(long value) => Value = value;
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

internal sealed class PdfReal : PdfObject
{
    public double Value { get; }
    public PdfReal(double value) => Value = value;

    // PDF real numbers (PDF 32000-1 §7.3.3) are written as a plain decimal —
    // exponential notation is NOT permitted. Default "G" formatting emits
    // exponent form for magnitudes below 1e-4 or very large (e.g. an ArtBox
    // coordinate 0.0000610352 became "6.10352E-05"), which downstream PDF
    // parsers — including this library's own — reject, derailing the whole
    // object and falling back to defaults (a blank, mis-sized page). Keep the
    // exact "G" text for every value it already renders without an exponent
    // (so existing byte-for-byte output is unchanged), and only expand the
    // exponent cases to an equivalent plain decimal.
    public override string ToString()
    {
        var g = Value.ToString("G", CultureInfo.InvariantCulture);
        if (g.IndexOf('E') < 0 && g.IndexOf('e') < 0)
            return g;
        // "0.################" never uses an exponent and trims trailing zeros;
        // 16 fractional digits cover the sub-1e-4 magnitudes that triggered the
        // exponent without introducing floating-point noise.
        var plain = Value.ToString("0.################", CultureInfo.InvariantCulture);
        return plain.Length == 0 || plain == "-0" ? "0" : plain;
    }
}

internal sealed class PdfString : PdfObject
{
    public byte[] Value { get; }
    public bool IsHex { get; }

    public PdfString(byte[] value, bool isHex = false)
    {
        Value = value;
        IsHex = isHex;
    }

    public string ToText()
    {
        // PDF text strings may be UTF-16BE (with BOM \xFE\xFF) or PDFDocEncoding (PDF 32000:2008 D.2)
        if (Value.Length >= 2 && Value[0] == 0xFE && Value[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(Value, 2, Value.Length - 2);
        // UTF-16LE (BOM \xFF\xFE) — non-standard for PDF but emitted by many
        // Windows/Word producers; decode it rather than returning raw bytes.
        if (Value.Length >= 2 && Value[0] == 0xFF && Value[1] == 0xFE)
            return Encoding.Unicode.GetString(Value, 2, Value.Length - 2);
        // UTF-8 BOM
        if (Value.Length >= 3 && Value[0] == 0xEF && Value[1] == 0xBB && Value[2] == 0xBF)
            return Encoding.UTF8.GetString(Value, 3, Value.Length - 3);
        return DecodePdfDocEncoding(Value);
    }

    // PDFDocEncoding → Unicode (PDF 32000:2008 Annex D.2). Bytes 0x00-0x17 and
    // 0x20-0x7E match ASCII; 0x18-0x1F and 0x80-0xA0 are PDFDocEncoding-specific;
    // 0xA1-0xFF match ISO Latin-1 (with 0xAD undefined, mapped to U+00AD for round-trip safety).
    private static readonly char[] PdfDocEncodingTable = BuildPdfDocEncoding();

    private static char[] BuildPdfDocEncoding()
    {
        var t = new char[256];
        for (int i = 0; i < 256; i++) t[i] = (char)i;
        t[0x18] = '˘'; t[0x19] = 'ˇ'; t[0x1A] = 'ˆ'; t[0x1B] = '˙';
        t[0x1C] = '˝'; t[0x1D] = '˛'; t[0x1E] = '˚'; t[0x1F] = '˜';
        t[0x80] = '•'; t[0x81] = '†'; t[0x82] = '‡'; t[0x83] = '…';
        t[0x84] = '—'; t[0x85] = '–'; t[0x86] = 'ƒ'; t[0x87] = '⁄';
        t[0x88] = '‹'; t[0x89] = '›'; t[0x8A] = '−'; t[0x8B] = '‰';
        t[0x8C] = '„'; t[0x8D] = '“'; t[0x8E] = '”'; t[0x8F] = '‘';
        t[0x90] = '’'; t[0x91] = '‚'; t[0x92] = '™'; t[0x93] = 'ﬁ';
        t[0x94] = 'ﬂ'; t[0x95] = 'Ł'; t[0x96] = 'Œ'; t[0x97] = 'Š';
        t[0x98] = 'Ÿ'; t[0x99] = 'Ž'; t[0x9A] = 'ı'; t[0x9B] = 'ł';
        t[0x9C] = 'œ'; t[0x9D] = 'š'; t[0x9E] = 'ž'; t[0xA0] = '€';
        // 0x9F and 0xAD are undefined in PDFDocEncoding; leave as raw byte for round-trip.
        return t;
    }

    private static string DecodePdfDocEncoding(byte[] bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) chars[i] = PdfDocEncodingTable[bytes[i]];
        return new string(chars);
    }

    public override string ToString()
    {
        if (IsHex)
            return $"<{Convert.ToHexString(Value)}>";
        return $"({Encoding.Latin1.GetString(Value)})";
    }
}

internal sealed class PdfName : PdfObject, IEquatable<PdfName>
{
    public string Value { get; }
    public PdfName(string value) => Value = value;

    public bool Equals(PdfName? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is PdfName other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => $"/{Value}";

    public static bool operator ==(PdfName? left, PdfName? right) =>
        left is null ? right is null : left.Equals(right);
    public static bool operator !=(PdfName? left, PdfName? right) => !(left == right);
}

internal sealed class PdfArray : PdfObject, IReadOnlyList<PdfObject>
{
    private readonly List<PdfObject> _items;

    public PdfArray() => _items = [];
    public PdfArray(List<PdfObject> items) => _items = items;

    public PdfObject this[int index] => _items[index];
    public int Count => _items.Count;

    public void Add(PdfObject item) => _items.Add(item);
    public void Insert(int index, PdfObject item) => _items.Insert(index, item);
    public void RemoveAt(int index) => _items.RemoveAt(index);
    public void ReplaceAt(int index, PdfObject item) => _items[index] = item;

    public IEnumerator<PdfObject> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class PdfDictionary : PdfObject
{
    private readonly Dictionary<string, PdfObject> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;
    public IEnumerable<string> Keys => _entries.Keys;

    public PdfObject? Get(string key) => _entries.GetValueOrDefault(key);
    public void Set(string key, PdfObject value) => _entries[key] = value;
    public bool Remove(string key) => _entries.Remove(key);
    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    public long GetInt(string key, long defaultValue = 0)
    {
        var obj = Get(key);
        return obj is PdfInteger i ? i.Value : defaultValue;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        var obj = Get(key);
        return obj switch
        {
            PdfBoolean b => b.Value,
            PdfInteger i => i.Value != 0,
            _ => defaultValue,
        };
    }

    public string? GetName(string key)
    {
        var obj = Get(key);
        return obj is PdfName n ? n.Value : null;
    }

    public override string ToString() => $"<< {Count} entries >>";
}

internal sealed class PdfStream : PdfObject
{
    public PdfDictionary Dict { get; }
    public byte[] RawData { get; private set; }

    /// <summary>Object number for decryption context.</summary>
    internal int ObjectNumber { get; set; }

    /// <summary>Generation number for decryption context.</summary>
    internal int Generation { get; set; }

    /// <summary>When true, the writer must emit the raw bytes verbatim with
    /// no /Filter — used for embedded files added with FileEncoding.None.</summary>
    internal bool DoNotCompress { get; set; }

    public PdfStream(PdfDictionary dict, byte[] rawData)
    {
        Dict = dict;
        RawData = rawData;
    }

    /// <summary>Replace the raw stream data (used by optimization).</summary>
    internal void ReplaceData(byte[] newData) => RawData = newData;
}

internal sealed class PdfIndirectRef : PdfObject
{
    public int ObjectNumber { get; }
    public int Generation { get; }

    public PdfIndirectRef(int objectNumber, int generation)
    {
        ObjectNumber = objectNumber;
        Generation = generation;
    }

    public override string ToString() => $"{ObjectNumber} {Generation} R";
}

internal readonly record struct IndirectObject(int ObjectNumber, int Generation, PdfObject Value);
