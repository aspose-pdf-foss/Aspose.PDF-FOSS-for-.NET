using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace Aspose.Pdf;

/// <summary>
/// XMP metadata accessor exposed by per-resource members such as
/// <see cref="XImage.Metadata"/>. Implements
/// <see cref="IDictionary{TKey,TValue}"/> over property keys formatted as
/// <c>"prefix:name"</c>; namespace prefix → URI mapping is exposed via the
/// <see cref="NamespaceManager"/> property.
/// </summary>
public sealed class Metadata : IDictionary<string, XmpValue>
{
    private static readonly Dictionary<string, string> _defaultNamespaces = new(StringComparer.Ordinal)
    {
        ["xmp"] = "http://ns.adobe.com/xap/1.0/",
        ["dc"] = "http://purl.org/dc/elements/1.1/",
        ["pdf"] = "http://ns.adobe.com/pdf/1.3/",
        ["xmpMM"] = "http://ns.adobe.com/xap/1.0/mm/",
        ["xmpRights"] = "http://ns.adobe.com/xap/1.0/rights/",
        ["pdfaid"] = "http://www.aiim.org/pdfa/ns/id/",
    };

    private readonly XmpMetadata _xmp;

    /// <summary>Internal constructor used by document/resource accessors.
    /// A null-or-empty backing store yields a live but empty Metadata.</summary>
    internal Metadata(XmpMetadata? xmp = null)
    {
        _xmp = xmp ?? new XmpMetadata();
        NamespaceManager = new XmlNamespaceManager(new NameTable());
        foreach (var kv in _defaultNamespaces)
            NamespaceManager.AddNamespace(kv.Key, kv.Value);
        // Surface any PDF/A extension-schema descriptions persisted on the backing
        // store (e.g. parsed from a reloaded document) through ExtensionFields.
        foreach (var (prefix, schema) in _xmp.ExtensionSchemas)
        {
            NamespaceManager.AddNamespace(prefix, schema.Uri);
            ExtensionFields[prefix] = new XmpPdfAExtensionSchema(
                new XmpPdfAExtensionSchemaDescription(prefix, schema.Uri, schema.Description));
        }
    }

    // ── IDictionary<string, XmpValue> ───────────────────────────────────────

    /// <summary>Number of metadata properties currently set.</summary>
    public int Count => _xmp.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <summary>Always false: callers may add and remove keys.</summary>
    public bool IsFixedSize => false;

    /// <summary>Always false: callers serialise their own access.</summary>
    public bool IsSynchronized => false;

    /// <summary>Sentinel object for ICollection.SyncRoot-style locking.</summary>
    public object SyncRoot { get; } = new();

    /// <summary>All property keys present in the bound metadata.</summary>
    public ICollection<string> Keys => _xmp.Keys.ToList();

    /// <summary>All property values present in the bound metadata.</summary>
    public ICollection<XmpValue> Values
        => _xmp.Keys.Select(k => new XmpValue(_xmp[k] ?? string.Empty)).ToList();

    /// <summary>Live <see cref="XmlNamespaceManager"/> driving prefix → URI
    /// resolution. Updated by <see cref="RegisterNamespaceUri(string,string)"/>.</summary>
    public XmlNamespaceManager NamespaceManager { get; }

    /// <summary>PDF/A extension fields, keyed by extension namespace prefix.
    /// Stored only in this build; full PDF/A-3 extension emission is left to
    /// Aspose.PDF for .NET.</summary>
    public IDictionary<string, XmpPdfAExtensionSchema> ExtensionFields { get; }
        = new Dictionary<string, XmpPdfAExtensionSchema>(StringComparer.Ordinal);

    /// <summary>Get or set a property by raw <c>"prefix:name"</c> key.</summary>
    public XmpValue this[string key]
    {
        get
        {
            var structured = _xmp.GetStructured(key);
            if (structured is not null) return structured;
            var raw = _xmp[key];
            if (raw is null) throw new KeyNotFoundException(key);
            return new XmpValue(raw);
        }
        set => _xmp[key] = value?.ToStringValue();
    }

    /// <summary>Add a property by raw key.</summary>
    public void Add(string key, XmpValue value)
    {
        if (_xmp.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' already exists.", nameof(key));
        _xmp.Add(key, value);
    }

    /// <summary>Add a property by raw key with an arbitrary <see cref="object"/> value.</summary>
    public void Add(string key, object value)
    {
        if (value is XmpValue xv) Add(key, xv);
        else Add(key, new XmpValue(value?.ToString() ?? string.Empty));
    }

    /// <summary>Add a key/value pair (IDictionary contract).</summary>
    public void Add(KeyValuePair<string, XmpValue> item) => Add(item.Key, item.Value);

    /// <summary>Add a PDF/A extension property under <paramref name="prefix"/>.
    /// Stored only.</summary>
    public void Add(string prefix, XmpPdfAExtensionObject value)
    {
        if (string.IsNullOrEmpty(prefix)) throw new ArgumentException("Prefix required.", nameof(prefix));
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (!ExtensionFields.TryGetValue(prefix, out var schema))
        {
            schema = new XmpPdfAExtensionSchema(new XmpPdfAExtensionSchemaDescription(prefix, NamespaceManager.LookupNamespace(prefix) ?? string.Empty, string.Empty));
            ExtensionFields[prefix] = schema;
        }
        schema.Objects.Add(value);
    }

    /// <summary>Remove every property from the bound metadata.</summary>
    public void Clear()
    {
        foreach (var key in _xmp.Keys.ToList())
            _xmp.Remove(key);
    }

    /// <summary>Whether the bound metadata contains the given raw key.</summary>
    public bool Contains(string key) => ContainsKey(key);

    /// <summary>Whether the bound metadata contains the exact key/value pair.</summary>
    public bool Contains(KeyValuePair<string, XmpValue> item)
    {
        var raw = _xmp[item.Key];
        return raw is not null
               && string.Equals(raw, item.Value?.ToStringValue(), StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public bool ContainsKey(string key) => _xmp.ContainsKey(key);

    /// <summary>Copy all key/value pairs into <paramref name="array"/> starting at <paramref name="index"/>.</summary>
    public void CopyTo(KeyValuePair<string, XmpValue>[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        foreach (var pair in this)
        {
            if (index >= array.Length)
                throw new ArgumentException("Destination array is not long enough.");
            array[index++] = pair;
        }
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, XmpValue>> GetEnumerator()
    {
        foreach (var key in _xmp.Keys)
        {
            var structured = _xmp.GetStructured(key);
            yield return new KeyValuePair<string, XmpValue>(
                key, structured ?? new XmpValue(_xmp[key] ?? string.Empty));
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Resolve a registered prefix to its XML namespace URI, or null.</summary>
    public string? GetNamespaceUriByPrefix(string prefix)
        => prefix is null ? null : NamespaceManager.LookupNamespace(prefix);

    /// <summary>Reverse lookup: namespace URI to its registered prefix, or null.</summary>
    public string? GetPrefixByNamespaceUri(string namespaceUri)
        => namespaceUri is null ? null : NamespaceManager.LookupPrefix(namespaceUri);

    /// <summary>Register an XML namespace prefix → URI mapping.</summary>
    public void RegisterNamespaceUri(string prefix, string namespaceUri)
        => RegisterNamespaceUri(prefix, namespaceUri, schemaDescription: null);

    /// <summary>Register an XML namespace prefix → URI mapping along with a
    /// schema description (carried for PDF/A extension wiring).</summary>
    public void RegisterNamespaceUri(string prefix, string namespaceUri, string? schemaDescription)
    {
        if (string.IsNullOrEmpty(prefix)) throw new ArgumentException("Prefix required.", nameof(prefix));
        NamespaceManager.AddNamespace(prefix, namespaceUri ?? string.Empty);
        if (!string.IsNullOrEmpty(schemaDescription))
        {
            if (!ExtensionFields.ContainsKey(prefix))
                ExtensionFields[prefix] = new XmpPdfAExtensionSchema(
                    new XmpPdfAExtensionSchemaDescription(prefix, namespaceUri ?? string.Empty, schemaDescription));
            // Persist on the backing store so the description is serialized to the
            // pdfaExtension block and survives save/reload.
            _xmp.SetExtensionSchema(prefix, namespaceUri ?? string.Empty, schemaDescription!);
        }
    }

    /// <summary>Remove the property at <paramref name="key"/>; returns true if it existed.</summary>
    public bool Remove(string key)
    {
        if (!_xmp.ContainsKey(key)) return false;
        _xmp.Remove(key);
        return true;
    }

    /// <summary>Remove the exact key/value pair if it is currently present.</summary>
    public bool Remove(KeyValuePair<string, XmpValue> item)
        => Contains(item) && Remove(item.Key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out XmpValue value)
    {
        var raw = _xmp[key];
        if (raw is null) { value = new XmpValue(string.Empty); return false; }
        value = new XmpValue(raw);
        return true;
    }

    // ── XmpMetadata-shape conveniences ──────────────────────────────────────

    /// <summary>Get a property's string value by raw key, or null if absent.
    /// Mirrors <see cref="XmpMetadata.Get(string)"/> for callers that
    /// don't want to go through the IDictionary indexer.</summary>
    public string? Get(string key) => _xmp[key];

    /// <summary>PDF/A part identifier from <c>pdfaid:part</c>, or null.</summary>
    public string? PdfAidPart => _xmp.PdfAidPart;

    /// <summary>PDF/A conformance level from <c>pdfaid:conformance</c>, or null.</summary>
    public string? PdfAidConformance => _xmp.PdfAidConformance;

    /// <summary>Set the PDF/A identifier triple (<c>part</c>, <c>conformance</c>,
    /// optional <c>amd</c> amendment string).</summary>
    public void SetPdfAidPart(string part, string? conformance = null, string? amd = null)
    {
        _xmp.PdfAidPart = part;
        if (conformance is not null) _xmp.PdfAidConformance = conformance;
        if (amd is not null) _xmp["pdfaid:amd"] = amd;
    }

    /// <summary>Underlying XmpMetadata accessor for FOSS-internal use.</summary>
    internal XmpMetadata Inner => _xmp;
}
