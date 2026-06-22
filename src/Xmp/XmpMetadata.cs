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

    internal XmpMetadata(PdfStream metadataStream, PdfReader reader)
    {
        var data = reader.DecodeStream(metadataStream);
        var xml = Encoding.UTF8.GetString(data);
        ParseXmp(xml);
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

    /// <summary>Indexer access (read/write). Returns string value for backward compatibility.</summary>
    public string? this[string key]
    {
        get => _properties.TryGetValue(key, out var v) ? v : null;
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
        _properties[key] = value;
        _dirty = true;
    }

    /// <summary>
    /// Add a metadata property with a typed XmpValue.
    /// </summary>
    public void Add(string key, XmpValue value)
    {
        _properties[key] = value.ToStringValue();
        _dirty = true;
    }

    /// <summary>
    /// Add a metadata property with a string value. Convenience overload that
    /// matches the Aspose.PDF for .NET XmpMetadata.Add(string, string) public surface.
    /// </summary>
    public void Add(string key, string value)
    {
        _properties[key] = value;
        _dirty = true;
    }

    /// <summary>
    /// Get a metadata property as a typed XmpValue, or null if not found.
    /// </summary>
    public XmpValue? GetXmpValue(string key)
    {
        if (!_properties.TryGetValue(key, out var raw)) return null;
        return ParseXmpValue(raw);
    }

    private static XmpValue ParseXmpValue(string raw)
    {
        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var intVal))
            return new XmpValue(intVal);
        if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var dblVal))
            return new XmpValue(dblVal);
        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dtVal))
            return new XmpValue(dtVal);
        return new XmpValue(raw);
    }

    /// <summary>
    /// Remove a metadata property.
    /// </summary>
    public bool Remove(string key)
    {
        var removed = _properties.Remove(key);
        if (removed) _dirty = true;
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

    /// <summary>
    /// Serialize the metadata to an XMP/RDF XML string.
    /// </summary>
    public string ToXml() => Encoding.UTF8.GetString(ToXmpBytes());

    /// <summary>
    /// Serialize the metadata to XMP/RDF XML bytes.
    /// </summary>
    internal byte[] ToXmpBytes()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        sb.AppendLine("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");
        sb.AppendLine(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        sb.AppendLine("  <rdf:Description rdf:about=\"\"");

        // Collect used namespace prefixes
        var usedPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in _properties.Keys)
        {
            var colon = key.IndexOf(':');
            if (colon > 0)
                usedPrefixes.Add(key[..colon]);
        }

        // Emit namespace declarations
        foreach (var prefix in usedPrefixes)
        {
            var ns = PrefixToNamespace(prefix);
            if (ns is not null)
                sb.AppendLine($"   xmlns:{prefix}=\"{ns}\"");
        }
        sb.AppendLine("  >");

        // Emit properties grouped by prefix
        foreach (var (key, value) in _properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var colon = key.IndexOf(':');
            if (colon <= 0) continue;

            // Dublin Core list properties
            if (key is "dc:creator" or "dc:subject" && value.Contains(';'))
            {
                var items = value.Split(';').Select(v => v.Trim()).Where(v => v.Length > 0);
                sb.AppendLine($"   <{key}>");
                sb.AppendLine("    <rdf:Seq>");
                foreach (var item in items)
                    sb.AppendLine($"     <rdf:li>{EscapeXml(item)}</rdf:li>");
                sb.AppendLine("    </rdf:Seq>");
                sb.AppendLine($"   </{key}>");
            }
            else if (key is "dc:title" or "dc:description")
            {
                sb.AppendLine($"   <{key}>");
                sb.AppendLine("    <rdf:Alt>");
                sb.AppendLine($"     <rdf:li xml:lang=\"x-default\">{EscapeXml(value)}</rdf:li>");
                sb.AppendLine("    </rdf:Alt>");
                sb.AppendLine($"   </{key}>");
            }
            else
            {
                sb.AppendLine($"   <{key}>{EscapeXml(value)}</{key}>");
            }
        }

        sb.AppendLine("  </rdf:Description>");

        // PDF/A extension-schema descriptions (pdfaExtension:schemas). One rdf:li
        // per registered custom schema carries its prefix, namespace URI and
        // human-readable description so they survive a save/reload round-trip.
        if (_extensionSchemas.Count > 0)
        {
            sb.AppendLine("  <rdf:Description rdf:about=\"\"");
            sb.AppendLine("   xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\"");
            sb.AppendLine("   xmlns:pdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\">");
            sb.AppendLine("   <pdfaExtension:schemas>");
            sb.AppendLine("    <rdf:Bag>");
            foreach (var (prefix, schema) in _extensionSchemas)
            {
                sb.AppendLine("     <rdf:li rdf:parseType=\"Resource\">");
                sb.AppendLine($"      <pdfaSchema:schema>{EscapeXml(schema.Description)}</pdfaSchema:schema>");
                sb.AppendLine($"      <pdfaSchema:namespaceURI>{EscapeXml(schema.Uri)}</pdfaSchema:namespaceURI>");
                sb.AppendLine($"      <pdfaSchema:prefix>{EscapeXml(prefix)}</pdfaSchema:prefix>");
                sb.AppendLine("     </rdf:li>");
            }
            sb.AppendLine("    </rdf:Bag>");
            sb.AppendLine("   </pdfaExtension:schemas>");
            sb.AppendLine("  </rdf:Description>");
        }

        sb.AppendLine(" </rdf:RDF>");
        sb.AppendLine("</x:xmpmeta>");

        // XMP padding (spec recommends ~2KB for in-place edits)
        for (var i = 0; i < 20; i++)
            sb.AppendLine(new string(' ', 100));

        sb.Append("<?xpacket end=\"w\"?>");

        return Encoding.UTF8.GetBytes(sb.ToString());
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
            "pdfx" => "http://ns.adobe.com/pdfx/1.3/",
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
        // Simple regex-based XMP parser for common properties
        // Handles <ns:Property>value</ns:Property> patterns (including empty values)
        var matches = PropertyPattern().Matches(xml);
        foreach (Match m in matches)
        {
            var prefix = m.Groups[1].Value;
            var name = m.Groups[2].Value;
            var value = m.Groups[3].Value.Trim();

            var key = $"{prefix}:{name}";
            _properties.TryAdd(key, value);
        }

        // Recover namespace prefix → URI bindings (xmlns:prefix="uri") so custom
        // namespaces round-trip through save/reload, not just property values.
        foreach (Match m in NamespacePattern().Matches(xml))
        {
            var prefix = m.Groups[1].Value;
            if (prefix is "x" or "rdf") continue;
            _customNamespaces[prefix] = m.Groups[2].Value;
        }

        // Also handle <rdf:li> lists inside properties (e.g., dc:creator)
        var listMatches = ListPropertyPattern().Matches(xml);
        foreach (Match m in listMatches)
        {
            var prefix = m.Groups[1].Value;
            var name = m.Groups[2].Value;
            var innerXml = m.Groups[3].Value;
            var key = $"{prefix}:{name}";

            if (_properties.ContainsKey(key)) continue;

            var items = ListItemPattern().Matches(innerXml);
            if (items.Count > 0)
            {
                var values = items.Select(li => li.Groups[1].Value.Trim())
                    .Where(v => !string.IsNullOrEmpty(v));
                _properties.TryAdd(key, string.Join("; ", values));
            }
        }

        // Structured (named-value) properties: a property element wrapping a
        // nested <rdf:Description> of child fields, e.g.
        //   <custprops:Property1><rdf:Description>
        //     <custprops:Name>TestProperty</custprops:Name>
        //     <custprops:Value>TestValue</custprops:Value>
        //   </rdf:Description></custprops:Property1>
        // Parsed into an XmpValue holding the ordered (childKey -> value) pairs.
        var structPattern = new System.Text.RegularExpressions.Regex(
            @"<(\w+):(\w+)>\s*<rdf:Description[^>]*>(.*?)</rdf:Description>\s*</\1:\2>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        var fieldPattern = new System.Text.RegularExpressions.Regex(
            @"<(\w+):(\w+)>([^<]*)</\1:\2>");
        foreach (System.Text.RegularExpressions.Match m in structPattern.Matches(xml))
        {
            var key = $"{m.Groups[1].Value}:{m.Groups[2].Value}";
            var pairs = new List<KeyValuePair<string, XmpValue>>();
            foreach (System.Text.RegularExpressions.Match f in fieldPattern.Matches(m.Groups[3].Value))
                pairs.Add(new KeyValuePair<string, XmpValue>(
                    $"{f.Groups[1].Value}:{f.Groups[2].Value}", new XmpValue(f.Groups[3].Value.Trim())));
            if (pairs.Count > 0)
                _structured[key] = new XmpValue(pairs.ToArray());
        }

        // PDF/A extension-schema descriptions: each <rdf:li> inside
        // <pdfaExtension:schemas> carries pdfaSchema:prefix / namespaceURI / schema
        // (the description). Recover them so ExtensionFields survives a reload.
        var liPattern = new System.Text.RegularExpressions.Regex(
            @"<rdf:li[^>]*>(.*?)</rdf:li>", System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (System.Text.RegularExpressions.Match li in liPattern.Matches(xml))
        {
            var body = li.Groups[1].Value;
            if (!body.Contains("pdfaSchema:prefix")) continue;
            string Field(string name)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    body, $"<pdfaSchema:{name}>(.*?)</pdfaSchema:{name}>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
            }
            var prefix = Field("prefix");
            if (prefix.Length == 0) continue;
            _extensionSchemas[prefix] = (Field("namespaceURI"), Field("schema"));
        }

        // Array-of-structures properties (xmpMM:History, xmpMM:Manifest, …): a
        // property whose value is an rdf:Seq/Bag/Alt of structured rdf:li nodes
        // (each a parseType="Resource" struct, possibly recursively nested). The
        // regex passes above only cover scalar lists and single structs, so parse
        // these with a real XML reader into nested XmpValue arrays/dicts.
        ParseStructuredArrays(xml);
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
                if (container is null) continue;
                var items = container.Elements(RdfNs + "li").ToList();
                if (items.Count == 0 || !items.Any(IsStructLi)) continue; // scalar list — leave to regex pass
                _structured[QName(prop)] = new XmpValue(items.Select(ParseStructNode).ToArray());
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

    // Parse an element into a struct (dict) built from its struct-field attributes
    // and child field elements, recursing into nested structs/arrays. Falls back to
    // a string scalar when the element has no structured content.
    private static XmpValue ParseStructNode(XElement el)
    {
        var dict = new Dictionary<string, XmpValue>(StringComparer.Ordinal);
        CollectFields(el, dict);
        return dict.Count > 0 ? new XmpValue(dict) : new XmpValue(el.Value.Trim());
    }

    // Merge struct fields (from struct attributes and non-rdf child elements) into
    // dict, transparently unwrapping any nested rdf:Description — XMP serialises a
    // struct either as parseType="Resource" with inline fields, or as an
    // <rdf:Description> wrapper carrying fields as attributes and/or child elements.
    private static void CollectFields(XElement el, Dictionary<string, XmpValue> dict)
    {
        foreach (var a in el.Attributes())
        {
            if (a.IsNamespaceDeclaration || a.Name.Namespace == RdfNs) continue;
            if (a.Name == XNamespace.Xml + "lang") continue;
            var ap = el.GetPrefixOfNamespace(a.Name.Namespace);
            dict[string.IsNullOrEmpty(ap) ? a.Name.LocalName : $"{ap}:{a.Name.LocalName}"] = new XmpValue(a.Value);
        }
        foreach (var child in el.Elements())
        {
            if (child.Name == RdfNs + "Description") { CollectFields(child, dict); continue; }
            if (child.Name.Namespace == RdfNs) continue;
            dict[QName(child)] = ParseFieldValue(child);
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
        return new XmpValue(el.Value.Trim());
    }

    // <prefix:Name>value</prefix:Name>  — also matches empty values
    [GeneratedRegex(@"<(\w+):(\w+)>([^<]*)</\w+:\w+>")]
    private static partial Regex PropertyPattern();

    // xmlns:prefix="uri"
    [GeneratedRegex("xmlns:(\\w+)=\"([^\"]*)\"")]
    private static partial Regex NamespacePattern();

    // <prefix:Name><rdf:Seq>.<rdf:li>.</rdf:Seq></prefix:Name>
    [GeneratedRegex(@"<(\w+):(\w+)>\s*<rdf:(?:Seq|Bag|Alt)>(.*?)</rdf:(?:Seq|Bag|Alt)>\s*</\w+:\w+>",
        RegexOptions.Singleline)]
    private static partial Regex ListPropertyPattern();

    [GeneratedRegex(@"<rdf:li[^>]*>([^<]*)</rdf:li>")]
    private static partial Regex ListItemPattern();
}
