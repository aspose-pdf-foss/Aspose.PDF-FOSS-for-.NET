using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Dictionary-style facade over a PDF document's XMP metadata stream.
/// Implements <see cref="IDictionary{TKey,TValue}"/> for round-trippable
/// access to property values keyed by <c>"prefix:name"</c> strings or by
/// the well-known <see cref="DefaultMetadataProperties"/> entries.
/// </summary>
public sealed class PdfXmpMetadata : IDictionary<string, XmpValue>
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

    private readonly Dictionary<string, string> _namespaces = new(_defaultNamespaces, StringComparer.Ordinal);
    private XmpMetadata? _metadata;
    // The bound document is kept alive so metadata edits can be written back on Save.
    private Aspose.Pdf.Document? _doc;
    private bool _ownsDoc;
    private string? _inputPath;

    /// <summary>The document bound to this facade, exposed so it can be
    /// chained into another facade.</summary>
    public Aspose.Pdf.Document Document => _doc ?? throw new InvalidOperationException("No document bound.");

    /// <summary>Construct an unbound facade. Call one of the
    /// <c>BindPdf</c> overloads before using.</summary>
    public PdfXmpMetadata() { }

    /// <summary>Construct a facade already bound to <paramref name="document"/>.</summary>
    public PdfXmpMetadata(Aspose.Pdf.Document document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        _doc = document;
        _ownsDoc = false;
        _metadata = document.GetOrCreateMetadata();
    }

    /// <summary>Bind a PDF document by path for subsequent operations.</summary>
    public void BindPdf(string path)
    {
        CloseInternal();
        _doc = Aspose.Pdf.Document.Open(path);
        _ownsDoc = true;
        _inputPath = path;
        _metadata = _doc.GetOrCreateMetadata();
    }

    /// <summary>Bind a PDF stream for subsequent operations.</summary>
    public void BindPdf(Stream input)
    {
        CloseInternal();
        using var ms = new MemoryStream();
        if (input.CanSeek) input.Position = 0;
        input.CopyTo(ms);
        _doc = Aspose.Pdf.Document.Open(ms.ToArray());
        _ownsDoc = true;
        _metadata = _doc.GetOrCreateMetadata();
    }

    /// <summary>Bind a live document for subsequent operations.</summary>
    public void BindPdf(Aspose.Pdf.Document document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        CloseInternal();
        _doc = document;
        _ownsDoc = false;
        _metadata = document.GetOrCreateMetadata();
    }

    private string? _outputPath;

    /// <summary>Bind a PDF document by path, target the given output path
    /// when Save() is called later. Mirrors the BindPdf(in, out)
    /// pattern.</summary>
    public void BindPdf(string inputPath, string outputPath)
    {
        BindPdf(inputPath);
        _outputPath = outputPath;
    }

    /// <summary>Save the bound document with any XMP metadata edits to the
    /// output path supplied to <see cref="BindPdf(string, string)"/>, falling
    /// back to the input path. No-op when nothing is bound.</summary>
    public void Save()
    {
        if (_doc is null) return;
        var target = _outputPath ?? _inputPath;
        if (target is not null) _doc.Save(target);
    }

    /// <summary>Save the bound document with its XMP edits to a stream.</summary>
    public void Save(Stream outputStream) => _doc?.Save(outputStream);

    /// <summary>Save the bound document with its XMP edits to an output path.</summary>
    public void Save(string outputFile)
    {
        _outputPath = outputFile;
        _doc?.Save(outputFile);
    }

    /// <summary>Release the bound document / metadata (the
    /// BindPdf / Save / Close lifecycle).</summary>
    public void Close()
    {
        CloseInternal();
        _metadata = null;
    }

    private void CloseInternal()
    {
        if (_ownsDoc) _doc?.Dispose();
        _doc = null;
        _ownsDoc = false;
    }

    // ── IDictionary<string, XmpValue> ───────────────────────────────────────

    /// <summary>Number of metadata properties currently set.</summary>
    public int Count => _metadata?.Count ?? 0;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <summary>Always false: callers may add and remove keys.</summary>
    public bool IsFixedSize => false;

    /// <summary>True: this facade is single-threaded; callers serialise their own access.</summary>
    public bool IsSynchronized => false;

    /// <summary>Sentinel object returned for <see cref="ICollection.SyncRoot"/>-style locking.</summary>
    public object SyncRoot { get; } = new();

    /// <summary>All property keys present in the bound metadata.</summary>
    public ICollection<string> Keys => _metadata?.Keys.ToList() ?? new List<string>();

    /// <summary>All property values present in the bound metadata. Each value
    /// carries its property key, so <see cref="XmpValue.ToNamedValue"/> yields
    /// the <c>"prefix:name"</c> key alongside the value.</summary>
    public ICollection<XmpValue> Values =>
        _metadata?.Keys.Select(k =>
            new XmpValue(new KeyValuePair<string, XmpValue>(k, new XmpValue(_metadata[k] ?? string.Empty)))).ToList()
            ?? new List<XmpValue>();

    /// <summary>PDF/A extension fields, keyed by extension namespace prefix.
    /// In this build the dictionary is always empty; full PDF/A-3 extension
    /// emission is not implemented.</summary>
    public IDictionary<string, XmpPdfAExtensionSchema> ExtensionFields { get; }
        = new Dictionary<string, XmpPdfAExtensionSchema>(StringComparer.Ordinal);

    /// <summary>Get or set a property by raw <c>"prefix:name"</c> key.</summary>
    public XmpValue this[string key]
    {
        get
        {
            EnsureBound();
            // A missing property reads back as null,
            // not a KeyNotFoundException — even though IDictionary types it non-null.
            var raw = _metadata![key];
            return raw is not null ? new XmpValue(raw) : null!;
        }
        set
        {
            EnsureBound();
            _metadata![key] = value?.ToStringValue();
        }
    }

    /// <summary>Get or set a property by well-known
    /// <see cref="DefaultMetadataProperties"/> key.</summary>
    public XmpValue this[DefaultMetadataProperties key]
    {
        get => this[KeyOf(key)];
        set => this[KeyOf(key)] = value;
    }

    /// <summary>Add a property by raw key.</summary>
    public void Add(string key, XmpValue value)
    {
        EnsureBound();
        if (_metadata!.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' already exists.", nameof(key));
        _metadata.Add(key, value);
    }

    /// <summary>Add a property by raw key with an arbitrary <see cref="object"/> value.
    /// The value is converted through <see cref="object.ToString"/>.</summary>
    public void Add(string key, object value)
    {
        if (value is XmpValue xv) Add(key, xv);
        else Add(key, new XmpValue(value?.ToString() ?? string.Empty));
    }

    /// <summary>Add a property by well-known
    /// <see cref="DefaultMetadataProperties"/> key.</summary>
    public void Add(DefaultMetadataProperties key, XmpValue value) => Add(KeyOf(key), value);

    /// <summary>Add a key/value pair (IDictionary contract).</summary>
    public void Add(KeyValuePair<string, XmpValue> item) => Add(item.Key, item.Value);

    /// <summary>Register a PDF/A extension object under <paramref name="namespacePrefix"/>.
    /// Stored only in this build.</summary>
    public void Add(XmpPdfAExtensionObject xmpPdfAExtensionObject,
                    string namespacePrefix,
                    string namespaceUri,
                    string schemaDescription)
    {
        if (xmpPdfAExtensionObject is null) throw new ArgumentNullException(nameof(xmpPdfAExtensionObject));
        if (string.IsNullOrEmpty(namespacePrefix)) throw new ArgumentException("Prefix required.", nameof(namespacePrefix));

        RegisterNamespaceURI(namespacePrefix, namespaceUri);
        if (!ExtensionFields.TryGetValue(namespacePrefix, out var schema))
        {
            schema = new XmpPdfAExtensionSchema(new XmpPdfAExtensionSchemaDescription(namespacePrefix, namespaceUri ?? string.Empty, schemaDescription));
            ExtensionFields[namespacePrefix] = schema;
        }
        schema.Objects.Add(xmpPdfAExtensionObject);
    }

    /// <summary>Remove every property from the bound metadata.</summary>
    public void Clear()
    {
        if (_metadata is null) return;
        foreach (var key in _metadata.Keys.ToList())
            _metadata.Remove(key);
    }

    /// <summary>Whether the bound metadata contains the given raw key.</summary>
    public bool Contains(string key) => ContainsKey(key);

    /// <summary>Whether the bound metadata contains the given well-known property.</summary>
    public bool Contains(DefaultMetadataProperties property) => ContainsKey(KeyOf(property));

    /// <summary>Whether the bound metadata contains the exact key/value pair.</summary>
    public bool Contains(KeyValuePair<string, XmpValue> item)
    {
        if (_metadata is null) return false;
        var raw = _metadata[item.Key];
        return raw is not null
               && string.Equals(raw, item.Value?.ToStringValue(), StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public bool ContainsKey(string key) => _metadata is not null && _metadata.ContainsKey(key);

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
        if (_metadata is null) yield break;
        foreach (var key in _metadata.Keys)
            yield return new KeyValuePair<string, XmpValue>(key, new XmpValue(_metadata[key] ?? string.Empty));
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Resolve a registered prefix to its XML namespace URI, or null.</summary>
    public string? GetNamespaceURIByPrefix(string prefix)
    {
        if (prefix is null) return null;
        if (_namespaces.TryGetValue(prefix, out var uri)) return uri;
        // Fall back to namespaces recovered from the bound (e.g. reloaded) packet.
        return _metadata is not null && _metadata.CustomNamespaces.TryGetValue(prefix, out var u) ? u : null;
    }

    /// <summary>Reverse lookup: namespace URI to its registered prefix, or null.</summary>
    public string? GetPrefixByNamespaceURI(string namespaceURI)
    {
        if (namespaceURI is null) return null;
        foreach (var kv in _namespaces)
            if (string.Equals(kv.Value, namespaceURI, StringComparison.Ordinal))
                return kv.Key;
        if (_metadata is not null)
            foreach (var kv in _metadata.CustomNamespaces)
                if (string.Equals(kv.Value, namespaceURI, StringComparison.Ordinal))
                    return kv.Key;
        return null;
    }

    /// <summary>Serialise the entire bound XMP packet as a UTF-8 byte array.</summary>
    public byte[] GetXmpMetadata()
    {
        if (_metadata is null) return Array.Empty<byte>();
        return Encoding.UTF8.GetBytes(SerializeAsXml(filterKey: null));
    }

    /// <summary>Serialise just the property named <paramref name="name"/> as a UTF-8 byte array.</summary>
    /// <remarks>Matches the reference: the single-property overload returns the bare
    /// property element as the document root, with its prefix's namespace declared inline —
    /// e.g. <c>&lt;pdf:Name xmlns:pdf="http://ns.adobe.com/pdf/1.3/"&gt;value&lt;/pdf:Name&gt;</c>
    /// — not the full xpacket/rdf wrapper (which is what the no-argument overload returns).</remarks>
    public byte[] GetXmpMetadata(string name)
    {
        if (_metadata is null || !_metadata.ContainsKey(name)) return Array.Empty<byte>();
        var v = _metadata[name];
        var sb = new StringBuilder();
        sb.Append('<').Append(name);
        var colon = name.IndexOf(':');
        if (colon > 0)
        {
            var prefix = name.Substring(0, colon);
            var uri = GetNamespaceURIByPrefix(prefix);
            if (!string.IsNullOrEmpty(uri))
                sb.Append(" xmlns:").Append(prefix).Append("=\"")
                  .Append(System.Security.SecurityElement.Escape(uri)).Append('"');
        }
        sb.Append('>')
          .Append(System.Security.SecurityElement.Escape(v ?? string.Empty))
          .Append("</").Append(name).Append('>');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Register an XML namespace prefix → URI mapping. Existing
    /// mappings are overwritten.</summary>
    public void RegisterNamespaceURI(string prefix, string namespaceURI)
    {
        if (string.IsNullOrEmpty(prefix)) throw new ArgumentException("Prefix required.", nameof(prefix));
        _namespaces[prefix] = namespaceURI ?? string.Empty;
        // Propagate to the bound metadata so the saved XMP packet declares the
        // prefix with this URI (otherwise it falls back to a generic ns/custom URI).
        if (_metadata is not null && !string.IsNullOrEmpty(namespaceURI))
            _metadata.RegisterNamespaceUri(prefix, namespaceURI);
    }

    /// <summary>Remove the property at <paramref name="key"/>; returns true if it existed.</summary>
    public bool Remove(string key)
    {
        if (_metadata is null || !_metadata.ContainsKey(key)) return false;
        _metadata.Remove(key);
        return true;
    }

    /// <summary>Remove the property indexed by the well-known
    /// <see cref="DefaultMetadataProperties"/> key.</summary>
    public void Remove(DefaultMetadataProperties key) => Remove(KeyOf(key));

    /// <summary>Remove the exact key/value pair if it is currently present.</summary>
    public bool Remove(KeyValuePair<string, XmpValue> item)
        => Contains(item) && Remove(item.Key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out XmpValue value)
    {
        var raw = _metadata?[key];
        if (raw is null) { value = new XmpValue(string.Empty); return false; }
        value = new XmpValue(raw);
        return true;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private void EnsureBound()
    {
        if (_metadata is null)
            throw new InvalidOperationException("PdfXmpMetadata is not bound to a document. Call BindPdf or use the Document constructor.");
    }

    private static string KeyOf(DefaultMetadataProperties key) => key switch
    {
        DefaultMetadataProperties.Advisory     => "xmp:Advisory",
        DefaultMetadataProperties.BaseURL      => "xmp:BaseURL",
        DefaultMetadataProperties.CreateDate   => "xmp:CreateDate",
        DefaultMetadataProperties.CreatorTool  => "xmp:CreatorTool",
        DefaultMetadataProperties.Identifier   => "xmp:Identifier",
        DefaultMetadataProperties.MetadataDate => "xmp:MetadataDate",
        DefaultMetadataProperties.ModifyDate   => "xmp:ModifyDate",
        DefaultMetadataProperties.Nickname     => "xmp:Nickname",
        DefaultMetadataProperties.Thumbnails   => "xmp:Thumbnails",
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private string SerializeAsXml(string? filterKey)
    {
        var sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");
        sb.Append("<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        sb.Append("<rdf:Description rdf:about=\"\"");
        foreach (var kv in _namespaces)
            sb.Append(" xmlns:").Append(kv.Key).Append("=\"").Append(System.Security.SecurityElement.Escape(kv.Value)).Append('"');
        sb.Append('>');
        if (_metadata is not null)
        {
            foreach (var key in _metadata.Keys)
            {
                if (filterKey is not null && !string.Equals(filterKey, key, StringComparison.Ordinal)) continue;
                var v = _metadata[key];
                sb.Append('<').Append(key).Append('>')
                  .Append(System.Security.SecurityElement.Escape(v ?? string.Empty))
                  .Append("</").Append(key).Append('>');
            }
        }
        sb.Append("</rdf:Description></rdf:RDF></x:xmpmeta>");
        sb.Append("<?xpacket end=\"w\"?>");
        return sb.ToString();
    }
}
