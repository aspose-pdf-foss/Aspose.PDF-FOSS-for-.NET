namespace Aspose.Pdf;

/// <summary>
/// XMP field categories (matches the public reflection signature
/// exactly — the underlying integer values are not load-bearing because
/// callers compare against the named members).
/// </summary>
public enum XmpFieldType
{
    Array,
    Packet,
    Property,
    Struct,
    Unknown,
}

/// <summary>
/// One named XMP field — a (prefix, local-name) pair plus a typed value.
/// Used as the leaf of <see cref="XmpValue"/> structures and arrays.
/// </summary>
public sealed class XmpField
{
    private static readonly XmpField _empty = new();

    /// <summary>Default-construct an empty field. The result reports
    /// <see cref="IsEmpty"/> == true and <see cref="FieldType"/> ==
    /// <see cref="XmpFieldType.Unknown"/>.</summary>
    public XmpField() { }

    /// <summary>Construct a fully-populated field.</summary>
    public XmpField(string? prefix, string? localName, string? namespaceUri, XmpValue? value, XmpFieldType type = XmpFieldType.Property)
    {
        Prefix = prefix;
        LocalName = localName;
        NamespaceUri = namespaceUri;
        Value = value;
        FieldType = type;
    }

    /// <summary>The XML prefix (e.g. <c>"dc"</c>).</summary>
    public string? Prefix { get; set; }

    /// <summary>The XML local name (e.g. <c>"title"</c>).</summary>
    public string? LocalName { get; set; }

    /// <summary>The XML namespace URI bound to <see cref="Prefix"/>.</summary>
    public string? NamespaceUri { get; set; }

    /// <summary>Combined name in <c>"prefix:localName"</c> form (or just
    /// the local name when no prefix is set).</summary>
    public string Name => string.IsNullOrEmpty(Prefix) ? (LocalName ?? string.Empty) : $"{Prefix}:{LocalName}";

    /// <summary>Field category. Defaults to <see cref="XmpFieldType.Unknown"/>
    /// when the field carries no value.</summary>
    public XmpFieldType FieldType { get; private set; } = XmpFieldType.Unknown;

    /// <summary>The field value, or null when this is an empty placeholder.</summary>
    public XmpValue? Value { get; private set; }

    /// <summary>Singleton empty field. Equality uses
    /// <see cref="IsEmpty"/> rather than reference identity.</summary>
    public static XmpField Empty => _empty;

    /// <summary>True when the field carries no value.</summary>
    public bool IsEmpty => Value is null;

    /// <summary>The xml:lang qualifier attached to this field (or null when
    /// none). Returned as a nested <see cref="XmpField"/> per the public
    /// signature; the xml:lang field's value carries the language tag.</summary>
    public XmpField? Lang { get; private set; }

    /// <summary>Unwrap the field's value as an array, or empty when the
    /// value isn't array-typed.</summary>
    public XmpValue[] ToArray() => Value is null ? System.Array.Empty<XmpValue>() : Value.ToArray();

    /// <summary>Unwrap the field's value as a struct (named-field array),
    /// or empty when the value isn't struct-typed.</summary>
    public XmpField[] ToStructure() => Value is null ? System.Array.Empty<XmpField>() : Value.ToStructure();

    public override bool Equals(object? obj)
    {
        if (obj is not XmpField other) return false;
        return string.Equals(Prefix, other.Prefix, System.StringComparison.Ordinal)
            && string.Equals(LocalName, other.LocalName, System.StringComparison.Ordinal)
            && string.Equals(NamespaceUri, other.NamespaceUri, System.StringComparison.Ordinal);
    }

    public override int GetHashCode() => System.HashCode.Combine(Prefix, LocalName, NamespaceUri);
}
