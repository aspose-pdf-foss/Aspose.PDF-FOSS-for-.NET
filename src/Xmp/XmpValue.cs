using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

namespace Aspose.Pdf;

/// <summary>
/// Represents a typed XMP metadata value. Holds one of: integer, double,
/// DateTime, string, array (<see cref="XmpValue"/>[]), struct
/// (dictionary or <see cref="XmpField"/>[]), named value
/// (<c>KeyValuePair&lt;string, XmpValue&gt;</c> or array of those), single
/// <see cref="XmpField"/>, or raw <see cref="XmlNode"/>.
/// </summary>
public sealed class XmpValue
{
    private readonly object _value;

    /// <summary>The text this value was READ from, when it came from a packet.
    /// An XMP simple property is text on the wire; typing it as a number is a
    /// reading convenience, so the original spelling is kept and returned by
    /// <see cref="ToStringValue"/> — otherwise a version like "1.0" round-trips
    /// back as "1" once it has been sniffed into a double.</summary>
    private readonly string? _rawText;

    /// <summary>Create an XmpValue from an integer.</summary>
    public XmpValue(int value) { _value = value; }

    /// <summary>Create a typed XmpValue that remembers the text it was parsed from.</summary>
    internal XmpValue(object value, string rawText) { _value = value; _rawText = rawText; }

    /// <summary>Create an XmpValue from a double.</summary>
    public XmpValue(double value) { _value = value; }

    /// <summary>Create an XmpValue from a DateTime.</summary>
    public XmpValue(DateTime value) { _value = value; }

    /// <summary>Create an XmpValue from a string.</summary>
    public XmpValue(string value) { _value = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Implicit promotion from string for natural assignment
    /// syntax (<c>doc.Metadata[key] = "literal"</c>).</summary>
    public static implicit operator XmpValue(string value) => new XmpValue(value);

    /// <summary>Implicit promotions from the other scalar value types, matching
    /// the XmpValue surface (<c>doc.Metadata[key] = DateTime.Now</c>).</summary>
    public static implicit operator XmpValue(DateTime value) => new XmpValue(value);
    public static implicit operator XmpValue(int value) => new XmpValue(value);
    public static implicit operator XmpValue(double value) => new XmpValue(value);

    /// <summary>Explicit conversion to string — yields the value's string form,
    /// supporting the <c>(string)metadata[key]</c> cast used to read an XMP entry
    /// back as text.</summary>
    public static explicit operator string(XmpValue value) => value?.ToString() ?? string.Empty;

    /// <summary>Create an XmpValue from an array of XmpValues.</summary>
    public XmpValue(XmpValue[] array) { _value = array ?? throw new ArgumentNullException(nameof(array)); }

    // ── FOSS-internal: composite-kind constructors used by XmpField, XMP
    //                  parser, and Metadata.this[].
    internal XmpValue(Dictionary<string, XmpValue> structure) { _value = structure ?? throw new ArgumentNullException(nameof(structure)); }
    internal XmpValue(XmpField field) { _value = field ?? throw new ArgumentNullException(nameof(field)); }
    internal XmpValue(KeyValuePair<string, XmpValue> namedValue) { _value = namedValue; }
    internal XmpValue(KeyValuePair<string, XmpValue>[] namedValues) { _value = namedValues ?? throw new ArgumentNullException(nameof(namedValues)); }
    internal XmpValue(XmlNode raw) { _value = raw ?? throw new ArgumentNullException(nameof(raw)); }

    // ── Type-check predicates ───────────────────────────────────────────────

    /// <summary>Whether the value is an integer.</summary>
    public bool IsInteger => _value is int;

    /// <summary>Whether the value is a double.</summary>
    public bool IsDouble => _value is double;

    /// <summary>Whether the value is a DateTime.</summary>
    public bool IsDateTime => _value is DateTime;

    /// <summary>Whether the value is a string (and not a number or date).</summary>
    public bool IsString => _value is string;

    /// <summary>Whether the value is an array of <see cref="XmpValue"/>s.</summary>
    public bool IsArray => _value is XmpValue[];

    /// <summary>Whether the value is a single named field (an
    /// <see cref="XmpField"/> instance).</summary>
    public bool IsField => _value is XmpField;

    /// <summary>Whether the value is a named-value pair
    /// (<c>KeyValuePair&lt;string, XmpValue&gt;</c>).</summary>
    public bool IsNamedValue => _value is KeyValuePair<string, XmpValue>;

    /// <summary>Whether the value is an array of named-value pairs.</summary>
    // A struct parsed from the packet is Dictionary-backed (the live-mutation
    // contract); it IS named values for readers — ToNamedValues() serves both shapes.
    public bool IsNamedValues => _value is KeyValuePair<string, XmpValue>[] or Dictionary<string, XmpValue>;

    /// <summary>Whether the value is a raw XML node.</summary>
    public bool IsRaw => _value is XmlNode;

    /// <summary>Whether the value is a struct (a
    /// <c>Dictionary&lt;string, XmpValue&gt;</c> or an <see cref="XmpField"/>[]).</summary>
    public bool IsStructure => _value is Dictionary<string, XmpValue> || _value is XmpField[];

    // ── Conversions ────────────────────────────────────────────────────────

    /// <summary>Convert to integer.</summary>
    public int ToInteger() => _value switch
    {
        int i => i,
        double d => (int)d,
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) => v,
        _ => throw new InvalidCastException($"Cannot convert XmpValue of type {_value.GetType().Name} to integer."),
    };

    /// <summary>Convert to double.</summary>
    public double ToDouble() => _value switch
    {
        double d => d,
        int i => i,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
        _ => throw new InvalidCastException($"Cannot convert XmpValue of type {_value.GetType().Name} to double."),
    };

    /// <summary>Convert to DateTime.</summary>
    public DateTime ToDateTime() => _value switch
    {
        DateTime dt => dt,
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v) => v,
        _ => throw new InvalidCastException($"Cannot convert XmpValue of type {_value.GetType().Name} to DateTime."),
    };

    /// <summary>Convert to <see cref="XmpValue"/>[]. Single-valued kinds
    /// are wrapped in a length-1 array.</summary>
    public XmpValue[] ToArray() => _value switch
    {
        XmpValue[] arr => arr,
        KeyValuePair<string, XmpValue>[] pairs => pairs.Select(p => p.Value).ToArray(),
        Dictionary<string, XmpValue> dict => dict.Values.ToArray(),
        _ => new[] { this },
    };

    /// <summary>Convert to <c>Dictionary&lt;string, XmpValue&gt;</c>. When
    /// the underlying value isn't already a struct, returns an empty
    /// dictionary.</summary>
    public Dictionary<string, XmpValue> ToDictionary() => _value switch
    {
        Dictionary<string, XmpValue> dict => dict,
        XmpField[] fields => fields.Where(f => f is not null && !string.IsNullOrEmpty(f.Name) && f.Value is not null)
                                   .ToDictionary(f => f.Name, f => f.Value!),
        KeyValuePair<string, XmpValue>[] pairs => pairs.ToDictionary(p => p.Key, p => p.Value),
        _ => new Dictionary<string, XmpValue>(StringComparer.Ordinal),
    };

    /// <summary>Convert to a single <see cref="XmpField"/>. When the value
    /// isn't field-typed, wraps the raw value in an unnamed field.</summary>
    public XmpField ToField() => _value switch
    {
        XmpField f => f,
        _ => new XmpField(prefix: null, localName: null, namespaceUri: null, value: this),
    };

    /// <summary>Convert to a single named-value pair.</summary>
    public KeyValuePair<string, XmpValue> ToNamedValue() => _value switch
    {
        KeyValuePair<string, XmpValue> nv => nv,
        XmpField f => new KeyValuePair<string, XmpValue>(f.Name, f.Value ?? this),
        _ => new KeyValuePair<string, XmpValue>(string.Empty, this),
    };

    /// <summary>Convert to an array of named-value pairs.</summary>
    public KeyValuePair<string, XmpValue>[] ToNamedValues() => _value switch
    {
        KeyValuePair<string, XmpValue>[] arr => arr,
        Dictionary<string, XmpValue> dict => dict.ToArray(),
        XmpField[] fields => fields.Select(f => new KeyValuePair<string, XmpValue>(f.Name, f.Value ?? new XmpValue(string.Empty))).ToArray(),
        _ => new[] { ToNamedValue() },
    };

    /// <summary>Get the underlying raw <see cref="XmlNode"/>, or null when
    /// the value isn't <see cref="IsRaw"/>.</summary>
    public XmlNode? ToRaw() => _value as XmlNode;

    /// <summary>Convert to an <see cref="XmpField"/>[] (a struct, in XMP
    /// terms). When the underlying value isn't already structured, returns
    /// an empty array.</summary>
    public XmpField[] ToStructure() => _value switch
    {
        XmpField[] arr => arr,
        Dictionary<string, XmpValue> dict => dict.Select(kv => new XmpField(prefix: null, localName: kv.Key, namespaceUri: null, value: kv.Value)).ToArray(),
        _ => System.Array.Empty<XmpField>(),
    };

    /// <summary>Convert to string representation suitable for XMP serialization.</summary>
    public string ToStringValue() => _rawText ?? _value switch
    {
        int i => i.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O"),
        string s => s,
        // A named-values / structured property stringifies to its first field's
        // value (e.g. a custom property whose first field is the name returns
        // that name) rather than the CLR array type name.
        KeyValuePair<string, XmpValue>[] nv => nv.Length > 0 ? nv[0].Value.ToStringValue() : string.Empty,
        KeyValuePair<string, XmpValue> kv => kv.Value.ToStringValue(),
        _ => _value.ToString()!,
    };

    /// <inheritdoc />
    public override string ToString() => ToStringValue();

    /// <summary>Extended string form: a structured / named-values property
    /// renders EVERY field as <c>key=value</c> lines (nested structs indent
    /// recursively), an array joins its items, and a scalar is its plain
    /// string — so a struct with an empty first field still stringifies to
    /// its full content instead of that field's empty value.</summary>
    internal string ToStringEx()
    {
        var sb = new System.Text.StringBuilder();
        AppendEx(sb, this, 0);
        return sb.ToString();

        static void AppendEx(System.Text.StringBuilder sb, XmpValue v, int depth)
        {
            var indent = new string(' ', depth * 2);
            switch (v._value)
            {
                case KeyValuePair<string, XmpValue>[] nv:
                    foreach (var kv in nv)
                    {
                        if (kv.Value._value is KeyValuePair<string, XmpValue>[] or Dictionary<string, XmpValue> or XmpValue[])
                        {
                            sb.Append(indent).Append(kv.Key).AppendLine("=");
                            AppendEx(sb, kv.Value, depth + 1);
                        }
                        else
                            sb.Append(indent).Append(kv.Key).Append('=').AppendLine(kv.Value.ToStringValue());
                    }
                    break;
                case Dictionary<string, XmpValue> dict:
                    AppendEx(sb, new XmpValue(dict.ToArray()), depth);
                    break;
                case XmpValue[] arr:
                    foreach (var item in arr) AppendEx(sb, item, depth);
                    break;
                default:
                    sb.Append(indent).AppendLine(v.ToStringValue());
                    break;
            }
        }
    }

    /// <summary>Format with a culture-specific provider. Numeric and
    /// DateTime values honour <paramref name="formatProvider"/>; all other
    /// kinds fall back to <see cref="ToStringValue"/>.</summary>
    public string ToString(IFormatProvider formatProvider) => _value switch
    {
        int i => i.ToString(formatProvider),
        double d => d.ToString(formatProvider),
        DateTime dt => dt.ToString(formatProvider),
        IFormattable f => f.ToString(null, formatProvider),
        _ => ToStringValue(),
    };

    /// <summary>The raw underlying value.</summary>
    internal object RawValue => _value;
}
