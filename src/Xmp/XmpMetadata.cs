using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Provides access to XMP metadata embedded in the PDF.
/// Supports both reading and writing metadata properties.
/// </summary>
public sealed partial class XmpMetadata
{
    private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _customNamespaces = new(StringComparer.Ordinal);
    // PDF/A extension-schema descriptions, keyed by prefix → (namespaceURI, description).
    // Serialized as a pdfaExtension:schemas block and parsed back, so a registered
    // custom schema's description round-trips through save/reload.
    private readonly Dictionary<string, (string Uri, string Description)> _extensionSchemas = new(StringComparer.Ordinal);
    // Structured (named-value) properties: a property whose value is a nested
    // rdf:Description of child fields, e.g. custprops:Property1 -> {Name, Value}.
    // Kept separately from the flat string store so they round-trip as XmpValue
    // named-values.
    private readonly Dictionary<string, XmpValue> _structured = new(StringComparer.Ordinal);
    private bool _dirty;

    // When set, mutations are written straight back into the backing /Metadata
    // stream (used for per-resource XMP — e.g. an image XObject's /Metadata —
    // which the document save loop serialises verbatim from its object number).
    // The document-level catalog /Metadata uses a different save path (IsDirty →
    // ToXmpBytes into a fresh object) and leaves this null.
    private PdfStream? _backingStream;

    internal XmpMetadata(PdfStream metadataStream, PdfReader reader)
    {
        var data = reader.DecodeStream(metadataStream);
        var xml = Encoding.UTF8.GetString(data);
        ParseXmp(xml);
    }

    /// <summary>Bind this metadata to its source /Metadata stream so that
    /// subsequent edits are re-serialised back into that stream and persist on the
    /// next document save. Call once, right after constructing from a stream.</summary>
    internal void EnableWriteBackTo(PdfStream backingStream) => _backingStream = backingStream;

    /// <summary>Re-serialise into the bound backing stream (no-op when unbound).
    /// The XMP packet is plaintext, so the stream is written with no /Filter.</summary>
    private void PersistToBackingStream()
    {
        if (_backingStream is null) return;
        var bytes = ToXmpBytes();
        _backingStream.ReplaceData(bytes);
        _backingStream.Dict.Remove("Filter");
        _backingStream.Dict.Remove("DecodeParms");
        _backingStream.Dict.Set("Length", new PdfInteger(bytes.Length));
    }

    internal XmpMetadata()
    {
        _dirty = true;
    }

    /// <summary>All metadata property keys (flat string properties plus any
    /// structured named-value properties).</summary>
    public IEnumerable<string> Keys => _structured.Count == 0
        ? _properties.Keys
        : _properties.Keys.Concat(_structured.Keys.Where(k => !_properties.ContainsKey(k)));

    /// <summary>Number of properties.</summary>
    public int Count => Keys.Count();

    /// <summary>Get a metadata property by key (e.g., "dc:title", "xmp:CreatorTool").</summary>
    public string? Get(string key) => _properties.GetValueOrDefault(key);

    /// <summary>Whether a key exists.</summary>
    public bool ContainsKey(string key) => _properties.ContainsKey(key) || _structured.ContainsKey(key);

    /// <summary>Get a structured (named-value) property by key, or null when the
    /// key is absent or is a plain string property.</summary>
    internal XmpValue? GetStructured(string key) => _structured.TryGetValue(key, out var v) ? v : null;

    // When the document has no XMP packet (or a packet that omits a standard
    // property), the document-level Info dictionary is the source of truth for
    // keys like xmp:ModifyDate / xmp:CreatorTool. This delegate resolves those
    // on demand. It is consulted only by the value getters — Keys/Count/
    // ContainsKey stay packet-only so metadata enumeration is unaffected.
    private System.Func<string, string?>? _infoFallback;

    /// <summary>Wire a fallback that maps a standard XMP key to its
    /// document-Info-derived value (e.g. xmp:ModifyDate ← /Info /ModDate).</summary>
    internal void SetInfoFallback(System.Func<string, string?> fallback) => _infoFallback = fallback;

    /// <summary>Indexer access (read/write). Returns string value for backward compatibility.</summary>
    public string? this[string key]
    {
        get => _properties.TryGetValue(key, out var v) ? v : _infoFallback?.Invoke(key);
        set
        {
            if (value is null)
                Remove(key);
            else
                Set(key, value);
        }
    }

    /// <summary>
    /// Set a metadata property. Key format: "namespace:name" (e.g., "dc:title", "xmp:CreatorTool").
    /// </summary>
    public void Set(string key, string value)
    {
        if (key == "pdf:Producer") ProducerExplicitlySet = true;
        _properties[key] = value;
        _structured.Remove(key);
        _dirty = true;
        PersistToBackingStream();
    }

    /// <summary>The caller assigned pdf:Producer through the public mutators, so
    /// the save-time library stamp must not overwrite it (a value parsed from a
    /// loaded packet does not set this).</summary>
    internal bool ProducerExplicitlySet;

    /// <summary>Save-time stamp write: bypasses <see cref="ProducerExplicitlySet"/>.</summary>
    internal void SetStamped(string key, string value)
    {
        _properties[key] = value;
        _structured.Remove(key);
        _dirty = true;
        PersistToBackingStream();
    }

    /// <summary>Store a structured (nested array/struct) value under
    /// <paramref name="key"/>, replacing any flat string property with the same
    /// key. Serialised as nested RDF by <see cref="ToXmpBytes"/>.</summary>
    internal void SetStructured(string key, XmpValue value)
    {
        _structured[key] = value;
        _properties.Remove(key);
        _dirty = true;
        PersistToBackingStream();
    }

    /// <summary>
    /// Add a metadata property with a typed XmpValue.
    /// </summary>
    public void Add(string key, XmpValue value)
    {
        // A composite value (array/struct/named-values) is kept as a structured
        // entry so it round-trips; a scalar collapses to its string form.
        if (value is not null && (value.IsArray || value.IsStructure || value.IsNamedValues))
        {
            SetStructured(key, value);
            return;
        }
        if (key == "pdf:Producer") ProducerExplicitlySet = true;
        _properties[key] = value?.ToStringValue() ?? string.Empty;
        _structured.Remove(key);
        _dirty = true;
        PersistToBackingStream();
    }

    /// <summary>
    /// Add a metadata property with a string value. Convenience overload that
    /// matches the XmpMetadata.Add(string, string) public surface.
    /// </summary>
    public void Add(string key, string value)
    {
        if (key == "pdf:Producer") ProducerExplicitlySet = true;
        _properties[key] = value;
        _structured.Remove(key);
        _dirty = true;
        PersistToBackingStream();
    }

    /// <summary>
    /// Get a metadata property as a typed XmpValue, or null if not found.
    /// </summary>
    public XmpValue? GetXmpValue(string key)
    {
        if (!_properties.TryGetValue(key, out var raw)) return null;
        return ParseXmpValue(raw);
    }

    internal static XmpValue ParseXmpValue(string raw)
    {
        // The property is typed for the numeric/date accessors, but carries its
        // ORIGINAL spelling for stringification — the packet stores text, and a
        // value such as a "1.0" version must read back as it was written.
        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var intVal))
            return new XmpValue(intVal, raw);
        if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var dblVal))
            return new XmpValue(dblVal, raw);
        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dtVal))
            return new XmpValue(dtVal, raw);
        return new XmpValue(raw);
    }

    /// <summary>
    /// Remove a metadata property.
    /// </summary>
    public bool Remove(string key)
    {
        var removed = _properties.Remove(key);
        removed |= _structured.Remove(key);
        if (removed) { _dirty = true; PersistToBackingStream(); }
        return removed;
    }

    /// <summary>
    /// Get a metadata property as an array of values (for rdf:Seq/Bag properties like dc:creator, dc:subject).
    /// Returns the values split by "; " or an empty list if the key doesn't exist.
    /// </summary>
    public List<string> GetArray(string key)
    {
        var value = Get(key);
        if (value is null) return [];
        return value.Split(';').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
    }

    /// <summary>
    /// Set a metadata property as an array (serialized to rdf:Seq).
    /// </summary>
    public void SetArray(string key, IEnumerable<string> values)
    {
        Set(key, string.Join("; ", values));
    }

    /// <summary>
    /// Get PDF/A identification part (pdfaid:part), e.g. "1", "2", "3".
    /// </summary>
    public string? PdfAidPart
    {
        get => Get("pdfaid:part");
        set
        {
            if (value is null) Remove("pdfaid:part");
            else Set("pdfaid:part", value);
        }
    }

    /// <summary>
    /// Get PDF/A identification conformance level (pdfaid:conformance), e.g. "A", "B", "U".
    /// </summary>
    public string? PdfAidConformance
    {
        get => Get("pdfaid:conformance");
        set
        {
            if (value is null) Remove("pdfaid:conformance");
            else Set("pdfaid:conformance", value);
        }
    }

    /// <summary>Whether the metadata has been modified since loading.</summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// Register a custom namespace URI for a prefix so that properties using that prefix
    /// can be serialized with the correct namespace declaration in ToXml().
    /// </summary>
    public void RegisterNamespaceUri(string prefix, string namespaceUri)
    {
        if (string.IsNullOrEmpty(prefix)) throw new ArgumentException("Prefix cannot be null or empty.", nameof(prefix));
        if (string.IsNullOrEmpty(namespaceUri)) throw new ArgumentException("Namespace URI cannot be null or empty.", nameof(namespaceUri));
        _customNamespaces[prefix] = namespaceUri;
    }

    /// <summary>Custom namespace prefix → URI bindings, registered explicitly or
    /// recovered from a parsed packet's <c>xmlns:</c> declarations.</summary>
    internal IReadOnlyDictionary<string, string> CustomNamespaces => _customNamespaces;

    /// <summary>Registered PDF/A extension-schema descriptions, prefix → (URI, description).</summary>
    internal IReadOnlyDictionary<string, (string Uri, string Description)> ExtensionSchemas => _extensionSchemas;

    /// <summary>Record a PDF/A extension-schema description for <paramref name="prefix"/>
    /// (also registers the prefix → URI binding). Persisted via the pdfaExtension block.</summary>
    internal void SetExtensionSchema(string prefix, string namespaceUri, string description)
    {
        _customNamespaces[prefix] = namespaceUri;
        _extensionSchemas[prefix] = (namespaceUri, description);
        _dirty = true;
    }

    /// <summary>Drop a PDF/A extension-schema description and the prefix binding that goes
    /// with it, so neither the schema block nor the namespace survives serialisation.</summary>
    internal bool RemoveExtensionSchema(string prefix)
    {
        var removed = _extensionSchemas.Remove(prefix);
        removed |= _customNamespaces.Remove(prefix);
        if (removed) _dirty = true;
        return removed;
    }

    /// <summary>
    /// Serialize the metadata to an XMP/RDF XML string.
    /// </summary>
    public string ToXml() => Encoding.UTF8.GetString(ToXmpBytes());

    /// <summary>
    /// Serialize the metadata to XMP/RDF XML bytes.
    /// </summary>
    internal byte[] ToXmpBytes()
    {
        var xb = new XmpBytesState();
        xb.sb = new StringBuilder();
        xb.sb.AppendLine("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        xb.sb.AppendLine("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");
        xb.sb.AppendLine(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        xb.sb.AppendLine("  <rdf:Description rdf:about=\"\"");

        if (KeepOnePropertyPerName) DropShadowedLegacyProperties();
        xb.usedPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in _properties.Keys)
        {
            var colon = key.IndexOf(':');
            if (colon > 0)
                xb.usedPrefixes.Add(key[..colon]);
        }
        foreach (var key in _structured.Keys)
            AddPrefix(key, xb.usedPrefixes);
        foreach (var v in _structured.Values)
            CollectStructuredPrefixes(v, xb.usedPrefixes);

        // Emit namespace declarations
        foreach (var prefix in xb.usedPrefixes)
        {
            var ns = PrefixToNamespace(prefix);
            if (ns is not null)
                xb.sb.AppendLine($"   xmlns:{prefix}=\"{ns}\"");
        }
        xb.sb.AppendLine("  >");

        // Emit properties grouped by prefix
        foreach (var (key, value) in _properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            WriteXmpProperty(xb, key, value);
        }

        MergeRegisteredExtensionSchemas();

        // Structured (nested array/struct) properties — e.g. xmpMM:Manifest /
        // xmpMM:History — serialised as nested RDF so they round-trip.
        foreach (var (key, value) in _structured.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (key.IndexOf(':') <= 0) continue;
            AppendStructured(xb.sb, key, value, "   ");
        }

        xb.sb.AppendLine("  </rdf:Description>");

        // PDF/A extension-schema descriptions (pdfaExtension:schemas). One rdf:li
        // per registered custom schema carries its prefix, namespace URI and
        // human-readable description so they survive a save/reload round-trip.
        // When the packet brought its own pdfaExtension:schemas (written above with
        // its property descriptions), the registered schemas it lacks joined it
        // there: a second pdfaExtension:schemas would make the packet invalid.
        if (_extensionSchemas.Count > 0 && !_structured.ContainsKey(ExtensionSchemasKey))
        {
            WriteXmpExtensionSchemas(xb);
        }

        xb.sb.AppendLine(" </rdf:RDF>");
        xb.sb.AppendLine("</x:xmpmeta>");

        // XMP padding (spec recommends ~2KB for in-place edits)
        for (var i = 0; i < 20; i++)
            xb.sb.AppendLine(new string(' ', 100));

        xb.sb.Append("<?xpacket end=\"w\"?>");

        return Encoding.UTF8.GetBytes(xb.sb.ToString());
    }

    private const string ExtensionSchemasKey = "pdfaExtension:schemas";

    /// <summary>Add the registered extension schemas the packet's own pdfaExtension:schemas
    /// block does not describe yet to that block, so the packet carries one such block.</summary>
    private void MergeRegisteredExtensionSchemas()
    {
        if (_extensionSchemas.Count == 0 || !_structured.TryGetValue(ExtensionSchemasKey, out var block) || !block.IsArray)
            return;
        var items = block.ToArray().ToList();
        var described = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
            if (IsStructValue(item))
                foreach (var (k, v) in EnumerateStruct(item))
                    if (k == "pdfaSchema:prefix") described.Add(v.ToString());
        var added = false;
        foreach (var (prefix, schema) in _extensionSchemas)
        {
            if (described.Contains(prefix)) continue;
            items.Add(new XmpValue(new Dictionary<string, XmpValue>(StringComparer.Ordinal)
            {
                ["pdfaSchema:schema"] = new XmpValue(schema.Description),
                ["pdfaSchema:namespaceURI"] = new XmpValue(schema.Uri),
                ["pdfaSchema:prefix"] = new XmpValue(prefix),
            }));
            added = true;
        }
        if (added) _structured[ExtensionSchemasKey] = new XmpValue(items.ToArray());
    }

    // ── Structured (nested array/struct) serialization ──────────────────────

    private static void AddPrefix(string key, HashSet<string> set)
    {
        var c = key.IndexOf(':');
        if (c > 0) set.Add(key[..c]);
    }

    private static bool IsStructValue(XmpValue v) => v.IsStructure || v.IsNamedValues;

    private static IEnumerable<KeyValuePair<string, XmpValue>> EnumerateStruct(XmpValue v)
        => v.IsNamedValues ? v.ToNamedValues() : v.ToDictionary();

    private static void CollectStructuredPrefixes(XmpValue v, HashSet<string> set)
    {
        if (v.IsArray)
        {
            foreach (var item in v.ToArray()) CollectStructuredPrefixes(item, set);
            return;
        }
        if (IsStructValue(v))
            foreach (var (k, val) in EnumerateStruct(v))
            {
                AddPrefix(k, set);
                CollectStructuredPrefixes(val, set);
            }
    }

    // Emit a property (or a struct field — same shape) named <paramref name="key"/>:
    // an array becomes <key><rdf:Seq>…</rdf:Seq></key>, a struct becomes
    // <key rdf:parseType="Resource">fields</key>, a scalar becomes <key>value</key>.
    private void AppendStructured(StringBuilder sb, string key, XmpValue value, string ind)
    {
        if (value.IsArray)
        {
            sb.AppendLine($"{ind}<{key}>");
            sb.AppendLine($"{ind} <rdf:Seq>");
            foreach (var item in value.ToArray())
                AppendArrayItem(sb, item, ind + "  ");
            sb.AppendLine($"{ind} </rdf:Seq>");
            sb.AppendLine($"{ind}</{key}>");
        }
        else if (IsStructValue(value))
        {
            sb.AppendLine($"{ind}<{key} rdf:parseType=\"Resource\">");
            foreach (var (fk, fv) in EnumerateStruct(value))
                AppendStructured(sb, fk, fv, ind + " ");
            sb.AppendLine($"{ind}</{key}>");
        }
        else
        {
            sb.AppendLine($"{ind}<{key}>{EscapeXml(value.ToStringValue())}</{key}>");
        }
    }

    private void AppendArrayItem(StringBuilder sb, XmpValue item, string ind)
    {
        if (IsStructValue(item))
        {
            sb.AppendLine($"{ind}<rdf:li rdf:parseType=\"Resource\">");
            foreach (var (fk, fv) in EnumerateStruct(item))
                AppendStructured(sb, fk, fv, ind + " ");
            sb.AppendLine($"{ind}</rdf:li>");
        }
        else if (item.IsArray)
        {
            sb.AppendLine($"{ind}<rdf:li>");
            sb.AppendLine($"{ind} <rdf:Seq>");
            foreach (var sub in item.ToArray())
                AppendArrayItem(sb, sub, ind + "  ");
            sb.AppendLine($"{ind} </rdf:Seq>");
            sb.AppendLine($"{ind}</rdf:li>");
        }
        else
        {
            sb.AppendLine($"{ind}<rdf:li>{EscapeXml(item.ToStringValue())}</rdf:li>");
        }
    }

    private string? PrefixToNamespace(string prefix)
    {
        if (_customNamespaces.TryGetValue(prefix, out var custom))
            return custom;
        return prefix switch
        {
            "dc" => "http://purl.org/dc/elements/1.1/",
            "xmp" => "http://ns.adobe.com/xap/1.0/",
            "xmpMM" => "http://ns.adobe.com/xap/1.0/mm/",
            "pdf" => "http://ns.adobe.com/pdf/1.3/",
            "pdfaid" => "http://www.aiim.org/pdfa/ns/id/",
            // PDF/UA identification (ISO 14289-1 §5): under any other URI the claim is not one.
            "pdfuaid" => "http://www.aiim.org/pdfua/ns/id/",
            "pdfx" =>"http://ns.adobe.com/pdfx/1.3/",
            "pdfxid" => "http://www.npes.org/pdfx/ns/id/",
            "xmpRights" => "http://ns.adobe.com/xap/1.0/rights/",
            "photoshop" => "http://ns.adobe.com/photoshop/1.0/",
            "tiff" => "http://ns.adobe.com/tiff/1.0/",
            "exif" => "http://ns.adobe.com/exif/1.0/",
            _ => $"http://ns.custom/{prefix}/",
        };
    }

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");

    private void ParseXmp(string xml)
    {
        var xp = new XmpParseState();
        xp.xml = xml;
        xp.matches = PropertyPattern().Matches(xp.xml);
        foreach (Match m in xp.matches)
        {
            ParseXmpProperty(m);
        }

        // Attribute-form (shorthand) properties: XMP permits serialising a simple
        // property as an XML attribute of its rdf:Description element —
        //   <rdf:Description rdf:about="" pdfaid:part="2" pdfaid:conformance="A">
        // (Acrobat and many producers write pdfaid this way.) Element-form values
        // win when both exist (TryAdd keeps the first hit).
        foreach (Match d in DescriptionOpenTagPattern().Matches(xp.xml))
            foreach (Match a in AttributePropertyPattern().Matches(d.Groups[1].Value))
            {
                var prefix = a.Groups[1].Value;
                if (prefix is "xmlns" or "xml" or "rdf" or "x") continue;
                _properties.TryAdd($"{prefix}:{a.Groups[2].Value}", a.Groups[3].Value.Trim());
            }

        // Recover namespace prefix → URI bindings (xmlns:prefix="uri") so custom
        // namespaces round-trip through save/reload, not just property values.
        foreach (Match m in NamespacePattern().Matches(xp.xml))
        {
            var prefix = m.Groups[1].Value;
            if (prefix is "x" or "rdf") continue;
            _customNamespaces[prefix] = m.Groups[2].Value;
        }

        xp.listMatches = ListPropertyPattern().Matches(xp.xml);
        foreach (Match m in xp.listMatches)
        {
            ParseXmpListProperty(m);
        }

        xp.structPattern = new System.Text.RegularExpressions.Regex(
            @"<(\w+):(\w+)>\s*<rdf:Description[^>]*>(.*?)</rdf:Description>\s*</\1:\2>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        xp.fieldPattern = new System.Text.RegularExpressions.Regex(
            @"<(\w+):(\w+)>([^<]*)</\1:\2>");
        foreach (System.Text.RegularExpressions.Match m in xp.structPattern.Matches(xp.xml))
        {
            var key = $"{m.Groups[1].Value}:{m.Groups[2].Value}";
            var pairs = new List<KeyValuePair<string, XmpValue>>();
            foreach (System.Text.RegularExpressions.Match f in xp.fieldPattern.Matches(m.Groups[3].Value))
                pairs.Add(new KeyValuePair<string, XmpValue>(
                    $"{f.Groups[1].Value}:{f.Groups[2].Value}", new XmpValue(f.Groups[3].Value.Trim())));
            if (pairs.Count > 0)
                _structured[key] = new XmpValue(pairs.ToArray());
        }

        xp.liPattern = new System.Text.RegularExpressions.Regex(
            @"<rdf:li[^>]*>(.*?)</rdf:li>", System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (System.Text.RegularExpressions.Match li in xp.liPattern.Matches(xp.xml))
        {
            ParseXmpListItem(li);
        }

        // Array-of-structures properties (xmpMM:History, xmpMM:Manifest, …): a
        // property whose value is an rdf:Seq/Bag/Alt of structured rdf:li nodes
        // (each a parseType="Resource" struct, possibly recursively nested). The
        // regex passes above only cover scalar lists and single structs, so parse
        // these with a real XML reader into nested XmpValue arrays/dicts.
        ParseStructuredArrays(xp.xml);

        // Extension-schema description fields live only inside pdfaExtension:schemas; the
        // leaf pass above picks them up from in there, and as top-level properties they
        // would make the packet describe nothing.
        foreach (var key in _properties.Keys.Where(k => k.StartsWith("pdfaSchema:", StringComparison.Ordinal)
                     || k.StartsWith("pdfaProperty:", StringComparison.Ordinal)
                     || k.StartsWith("pdfaType:", StringComparison.Ordinal)
                     || k.StartsWith("pdfaField:", StringComparison.Ordinal)).ToList())
            _properties.Remove(key);
    }

    // Early XMP wrote the xap* prefixes for what are now the xmp* namespaces (the URIs are the
    // same): a packet naming a property under both would carry it twice, which makes it invalid.
    private static readonly (string Legacy, string Current)[] LegacyPrefixes =
    [
        ("xap", "xmp"), ("xapMM", "xmpMM"), ("xapRights", "xmpRights"), ("xapGImg", "xmpGImg"),
    ];

    /// <summary>Set by a conversion that must write a valid packet: every write then keeps one
    /// property per name (see <see cref="DropShadowedLegacyProperties"/>), values the save
    /// stamps included. Otherwise a packet keeps each prefix its author wrote.</summary>
    internal bool KeepOnePropertyPerName { get; set; }

    /// <summary>Drop each xap* property the packet also states under its current xmp* name -
    /// one property twice, which makes a packet invalid - keeping the value set under the
    /// current name.</summary>
    private void DropShadowedLegacyProperties()
    {
        var shadowed = new List<string>();
        foreach (var (legacy, current) in LegacyPrefixes)
            foreach (var key in _properties.Keys.Concat(_structured.Keys))
                if (key.StartsWith(legacy + ":", StringComparison.Ordinal))
                {
                    var name = current + key[legacy.Length..];
                    if (_properties.ContainsKey(name) || _structured.ContainsKey(name)) shadowed.Add(key);
                }
        if (shadowed.Count == 0) return;
        foreach (var key in shadowed)
        {
            _properties.Remove(key);
            _structured.Remove(key);
        }
        _dirty = true;
    }

    private static readonly XNamespace RdfNs =
        XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#");

    private void ParseStructuredArrays(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return; } // malformed XMP — leave the regex-parsed properties as-is
        foreach (var desc in doc.Descendants(RdfNs + "Description"))
        {
            foreach (var prop in desc.Elements())
            {
                if (prop.Name.Namespace == RdfNs) continue;
                var container = prop.Elements().FirstOrDefault(IsContainer);
                if (container is not null)
                {
                    var items = container.Elements(RdfNs + "li").ToList();
                    if (items.Count == 0 || !items.Any(IsStructLi)) continue; // scalar list — leave to regex pass
                    _structured[QName(prop)] = new XmpValue(items.Select(ParseStructNode).ToArray());
                    continue;
                }

                // Plain element-nested struct: the property directly wraps non-rdf child
                // elements (no rdf:Seq/Bag/Alt container and no rdf:Description wrapper), e.g.
                //   <ns:group><ns:item>
                //     <ns:fieldA>…</ns:fieldA> <ns:fieldB>…</ns:fieldB> </ns:item>
                //   </ns:group>
                // Parse the whole subtree into a nested struct so callers can traverse it via
                // ToNamedValues(). The single-rdf:Description struct form is handled by the
                // regex pass above; this catches the wrapper-less nesting it misses.
                // ...including the fully ABBREVIATED form, where the property is an
                // EMPTY element carrying every struct field as an attribute —
                //   <abaxmp:archive abaxmp:name="" abaxmp:title="…"/>
                if ((prop.Elements().Any(e => e.Name.Namespace != RdfNs) || HasStructAttributes(prop))
                    && !_structured.ContainsKey(QName(prop)))
                    _structured[QName(prop)] = ParseStructNode(prop);
            }
        }
    }

    private static bool IsContainer(XElement e) =>
        e.Name == RdfNs + "Seq" || e.Name == RdfNs + "Bag" || e.Name == RdfNs + "Alt";

    // An rdf:li is a structure (rather than a scalar string) when it is marked
    // parseType="Resource", carries child field elements, or carries struct fields
    // as attributes (the abbreviated form).
    private static bool IsStructLi(XElement li) =>
        (string?)li.Attribute(RdfNs + "parseType") == "Resource"
        || li.Elements().Any()
        || li.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name.Namespace != RdfNs
                                    && a.Name != XNamespace.Xml + "lang");

    private static string QName(XElement el)
    {
        var prefix = el.GetPrefixOfNamespace(el.Name.Namespace);
        return string.IsNullOrEmpty(prefix) ? el.Name.LocalName : $"{prefix}:{el.Name.LocalName}";
    }

    // Parse an element into a struct built from its struct-field attributes
    // and child field elements, recursing into nested structs/arrays. Falls back to
    // a string scalar when the element has no structured content. The struct is a
    // LIVE Dictionary — callers mutate it in place through ToDictionary() and
    // write the same XmpValue back (the manifest-edit contract), so the backing
    // store must be the dictionary the accessor hands out, not a copy.
    private static XmpValue ParseStructNode(XElement el)
    {
        var dict = new Dictionary<string, XmpValue>(StringComparer.Ordinal);
        CollectFields(el, dict);
        return dict.Count > 0 ? new XmpValue(dict) : new XmpValue(NormalizeXmpScalar(el.Value.Trim()));
    }

    // Canonicalise an XMP date-time's timezone offset to a two-digit hour
    // (e.g. "2013-05-16T14:00:00-7:00" → "2013-05-16T14:00:00-07:00"), matching how
    // XMP toolkits normalise dates. Non-date strings — and dates whose offset is
    // already two-digit — are returned unchanged.
    private static readonly Regex XmpDateTzPattern = new(
        @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?)([+-])(\d{1,2}):(\d{2})$");

    private static string NormalizeXmpScalar(string value)
    {
        var m = XmpDateTzPattern.Match(value);
        return m.Success
            ? $"{m.Groups[1].Value}{m.Groups[2].Value}{int.Parse(m.Groups[3].Value):D2}:{m.Groups[4].Value}"
            : value;
    }

    // Merge struct fields (from struct attributes and non-rdf child elements) into
    // dict, transparently unwrapping any nested rdf:Description — XMP serialises a
    // struct either as parseType="Resource" with inline fields, or as an
    // <rdf:Description> wrapper carrying fields as attributes and/or child elements.
    private static void CollectFields(XElement el, Dictionary<string, XmpValue> dict, List<string>? order = null)
    {
        void Set(string key, XmpValue v)
        {
            if (!dict.ContainsKey(key)) order?.Add(key);
            dict[key] = v;
        }
        foreach (var a in el.Attributes())
        {
            if (a.IsNamespaceDeclaration || a.Name.Namespace == RdfNs) continue;
            if (a.Name == XNamespace.Xml + "lang") continue;
            var ap = el.GetPrefixOfNamespace(a.Name.Namespace);
            Set(string.IsNullOrEmpty(ap) ? a.Name.LocalName : $"{ap}:{a.Name.LocalName}", new XmpValue(a.Value));
        }
        foreach (var child in el.Elements())
        {
            if (child.Name == RdfNs + "Description") { CollectFields(child, dict, order); continue; }
            if (child.Name.Namespace == RdfNs) continue;
            Set(QName(child), ParseFieldValue(child));
        }
    }

    private static bool HasStructAttributes(XElement el) =>
        el.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name.Namespace != RdfNs
                                 && a.Name != XNamespace.Xml + "lang");

    // Parse a struct field's value: a nested array (rdf:Seq/Bag/Alt), a nested
    // struct (parseType="Resource", struct attributes, an rdf:Description wrapper,
    // or non-rdf child elements), or a string scalar.
    private static XmpValue ParseFieldValue(XElement el)
    {
        var container = el.Elements().FirstOrDefault(IsContainer);
        if (container is not null)
            return new XmpValue(container.Elements(RdfNs + "li").Select(ParseStructNode).ToArray());
        if ((string?)el.Attribute(RdfNs + "parseType") == "Resource"
            || HasStructAttributes(el)
            || el.Elements().Any(e => e.Name == RdfNs + "Description" || e.Name.Namespace != RdfNs))
            return ParseStructNode(el);
        return new XmpValue(NormalizeXmpScalar(el.Value.Trim()));
    }

    // <prefix:Name>value</prefix:Name>  — also matches empty values
    private const string PropertyPatternText = @"<(\w+):(\w+)>([^<]*)</\w+:\w+>";
    // xmlns:prefix="uri"
    private const string NamespacePatternText = "xmlns:(\\w+)=\"([^\"]*)\"";
    // <prefix:Name><rdf:Seq>.<rdf:li>.</rdf:Seq></prefix:Name>
    private const string ListPropertyPatternText =
        @"<(\w+):(\w+)>\s*<rdf:(?:Seq|Bag|Alt)>(.*?)</rdf:(?:Seq|Bag|Alt)>\s*</\w+:\w+>";
    private const string ListItemPatternText = @"<rdf:li[^>]*>([^<]*)</rdf:li>";
    private const string DescriptionOpenTagPatternText = @"<rdf:Description\b([^>]*)>";
    private const string AttributePropertyPatternText = "([\\w]+):([\\w.-]+)=\"([^\"]*)\"";

#if NET7_0_OR_GREATER
    [GeneratedRegex(PropertyPatternText)]
    private static partial Regex PropertyPattern();

    [GeneratedRegex(NamespacePatternText)]
    private static partial Regex NamespacePattern();

    [GeneratedRegex(ListPropertyPatternText, RegexOptions.Singleline)]
    private static partial Regex ListPropertyPattern();

    [GeneratedRegex(ListItemPatternText)]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(DescriptionOpenTagPatternText)]
    private static partial Regex DescriptionOpenTagPattern();

    [GeneratedRegex(AttributePropertyPatternText)]
    private static partial Regex AttributePropertyPattern();
#else
    // The regex source generator needs .NET 7; the older targets compile the same
    // patterns once into static instances.
    private static readonly Regex s_propertyPattern = new(PropertyPatternText, RegexOptions.Compiled);
    private static readonly Regex s_namespacePattern = new(NamespacePatternText, RegexOptions.Compiled);
    private static readonly Regex s_listPropertyPattern = new(ListPropertyPatternText, RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex s_listItemPattern = new(ListItemPatternText, RegexOptions.Compiled);
    private static readonly Regex s_descriptionOpenTagPattern = new(DescriptionOpenTagPatternText, RegexOptions.Compiled);
    private static readonly Regex s_attributePropertyPattern = new(AttributePropertyPatternText, RegexOptions.Compiled);

    private static Regex PropertyPattern() => s_propertyPattern;
    private static Regex NamespacePattern() => s_namespacePattern;
    private static Regex ListPropertyPattern() => s_listPropertyPattern;
    private static Regex ListItemPattern() => s_listItemPattern;
    private static Regex DescriptionOpenTagPattern() => s_descriptionOpenTagPattern;
    private static Regex AttributePropertyPattern() => s_attributePropertyPattern;
#endif
}
