using System.Collections.Generic;

namespace Aspose.Pdf;

/// <summary>Relationship between an embedded file and the document
/// content that references it (/AFRelationship, PDF 2.0 §7.11.3).</summary>
public enum AFRelationship
{
    /// <summary>Relationship is unspecified.</summary>
    Unspecified,

    /// <summary>The file represents the source from which the document was derived.</summary>
    Source,

    /// <summary>The file is a data source for content rendered in the document.</summary>
    Data,

    /// <summary>The file is an alternative representation of the document.</summary>
    Alternative,

    /// <summary>The file supplements the document.</summary>
    Supplement,

    /// <summary>The file is an encrypted-payload (PDF 2.0 unencrypted-wrapper documents).</summary>
    EncryptedPayload,

    /// <summary>No specific relationship.</summary>
    None,
}

/// <summary>Compression / encoding used for the embedded file stream's data
/// (consumed by <see cref="FileSpecification.Encoding"/>).</summary>
public enum FileEncoding
{
    /// <summary>No compression — stored as-is.</summary>
    None,

    /// <summary>FlateDecode (zlib) compression — the default for new attachments.</summary>
    Zip,
}

/// <summary>Per-file metadata entries declared by a portfolio /Collection's
/// schema, exposed as a typed dictionary on <see cref="FileSpecification.CollectionItem"/>.</summary>
public class CollectionItem
{
    private readonly Dictionary<string, object?> _values = new(System.StringComparer.Ordinal);

    /// <summary>One typed value pulled out of a <see cref="CollectionItem"/>
    /// dict by <c>TryGet*Value</c>. Carries both the value and any prefix
    /// the collection schema decorates it with. Nested under
    /// <see cref="CollectionItem"/> per the Aspose.Pdf reflection signature.</summary>
    public sealed class Value<T>
    {
        /// <summary>The typed value.</summary>
        public T Data { get; }

        /// <summary>Prefix string declared by the collection schema, or empty.</summary>
        public string Prefix { get; }

        internal Value(T data, string prefix = "")
        {
            Data = data;
            Prefix = prefix;
        }
    }

    /// <summary>Whether the item carries no schema entries.</summary>
    public bool IsEmpty => _values.Count == 0;

    /// <summary>All schema-entry names declared on this item.</summary>
    public ICollection<string> AllNames => _values.Keys;

    /// <summary>True when <paramref name="name"/> is declared on the item.</summary>
    public bool HasName(string name) => name is not null && _values.ContainsKey(name);

    /// <summary>Read a string-typed schema entry. Returns false when the
    /// entry is missing or holds a non-string value.</summary>
    public bool TryGetTextValue(string name, out Value<string> value)
    {
        if (name is not null && _values.TryGetValue(name, out var raw) && raw is string s)
        {
            value = new Value<string>(s);
            return true;
        }
        value = new Value<string>(string.Empty);
        return false;
    }

    /// <summary>Read an int-typed schema entry.</summary>
    public bool TryGetIntValue(string name, out Value<int> value)
    {
        if (name is not null && _values.TryGetValue(name, out var raw) && raw is int i)
        {
            value = new Value<int>(i);
            return true;
        }
        value = new Value<int>(0);
        return false;
    }

    /// <summary>Read a double-typed schema entry.</summary>
    public bool TryGetDoubleValue(string name, out Value<double> value)
    {
        if (name is not null && _values.TryGetValue(name, out var raw) && raw is double d)
        {
            value = new Value<double>(d);
            return true;
        }
        value = new Value<double>(0);
        return false;
    }

    /// <summary>Read a DateTime-typed schema entry.</summary>
    public bool TryGetDateTimeValue(string name, out Value<System.DateTime> value)
    {
        if (name is not null && _values.TryGetValue(name, out var raw) && raw is System.DateTime dt)
        {
            value = new Value<System.DateTime>(dt);
            return true;
        }
        value = new Value<System.DateTime>(System.DateTime.MinValue);
        return false;
    }

    /// <summary>Internal: write an entry. Used by the parser.</summary>
    internal void Set(string name, object? value) { if (name is not null) _values[name] = value; }
}

/// <summary>Wraps the /EP entry on an embedded-file file-spec — the
/// encrypted-payload that signals a PDF 2.0 unencrypted-wrapper document.
/// Stored only in this build: FOSS reads/writes the /EP dict but does
/// not implement the actual payload-encryption pipeline.</summary>
public sealed class EncryptedPayload
{
    private readonly FileSpecification _spec;

    /// <summary>Construct an EncryptedPayload wrapper over an existing
    /// <see cref="FileSpecification"/>'s /EP dictionary.</summary>
    public EncryptedPayload(FileSpecification fileSpecification)
    {
        _spec = fileSpecification ?? throw new System.ArgumentNullException(nameof(fileSpecification));
    }

    /// <summary>The /Type entry of the encrypted-payload dictionary.</summary>
    public string Type => _spec.GetEncryptedPayloadDict()?.GetName("Type") ?? "EncryptedPayload";

    /// <summary>The /Subtype entry naming the encryption scheme
    /// (e.g. "MicrosoftIRMServices", "AdobePDF").</summary>
    public string Subtype => _spec.GetEncryptedPayloadDict()?.GetName("Subtype") ?? string.Empty;

    /// <summary>The /Version entry identifying the scheme version (e.g. "2").
    /// Stored as a name or number in the PDF; surfaced as a string.</summary>
    public string Version
    {
        get
        {
            var ep = _spec.GetEncryptedPayloadDict();
            if (ep is null) return string.Empty;
            var name = ep.GetName("Version");
            if (name is not null) return name;
            const long sentinel = long.MinValue;
            var n = ep.GetInt("Version", sentinel);
            if (n != sentinel) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ep.Get("Version")?.ToString() ?? string.Empty;
        }
    }
}
