namespace Aspose.Pdf;

public sealed partial class XmpMetadata
{
#pragma warning disable S5332 // a namespace name, never fetched
    private const string ExtensionNamespace = "http://www.aiim.org/pdfa/ns/extension/";
    private const string SchemaNamespace = "http://www.aiim.org/pdfa/ns/schema#";
    private const string PropertyNamespace = "http://www.aiim.org/pdfa/ns/property#";

    /// <summary>The namespaces whose properties PDF/A predefines - the schemas of the XMP specification, the PDF/A
    /// identification schema and the extension-schema machinery itself: a packet states them without describing
    /// them.</summary>
    private static readonly HashSet<string> PredefinedNamespaces = new(StringComparer.Ordinal)
    {
        "http://purl.org/dc/elements/1.1/",
        "http://ns.adobe.com/xap/1.0/",
        "http://ns.adobe.com/xap/1.0/rights/",
        "http://ns.adobe.com/xap/1.0/mm/",
        "http://ns.adobe.com/xap/1.0/bj/",
        "http://ns.adobe.com/xap/1.0/t/pg/",
        "http://ns.adobe.com/xmp/1.0/DynamicMedia/",
        "http://ns.adobe.com/pdf/1.3/",
        "http://ns.adobe.com/photoshop/1.0/",
        "http://ns.adobe.com/camera-raw-settings/1.0/",
        "http://ns.adobe.com/tiff/1.0/",
        "http://ns.adobe.com/exif/1.0/",
        "http://ns.adobe.com/exif/1.0/aux/",
        "http://www.aiim.org/pdfa/ns/id/",
        ExtensionNamespace,
        SchemaNamespace,
        PropertyNamespace,
        "http://www.aiim.org/pdfa/ns/type#",
        "http://www.aiim.org/pdfa/ns/field#",
    };
#pragma warning restore S5332

    private const string PdfAPartKey = "pdfaid:part";
    private const string TextValueType = "Text";
    private const string ExternalCategory = "external";

    /// <summary>The value type of a property stated as an array of text, by its RDF container.</summary>
    private static string ArrayValueType(string kind) => kind switch
    {
        "Bag" => "bag Text",
        "Alt" => "Lang Alt",
        _ => "seq Text",
    };

    /// <summary>The property descriptions given for extension schemas: prefix → name → (value type, category,
    /// description).</summary>
    private readonly Dictionary<string, Dictionary<string, (string ValueType, string Category, string Description)>>
        _extensionProperties = new(StringComparer.Ordinal);

    /// <summary>Describe an extension property the caller added under <paramref name="prefix"/>, registering its schema
    /// when the packet has none for the prefix yet (a schema needs its namespace, so without one nothing is
    /// described).</summary>
    internal void AddExtensionProperty(string prefix, string? namespaceUri, string? schemaDescription,
        XmpPdfAExtensionProperty property)
    {
        if (!_extensionSchemas.ContainsKey(prefix))
        {
            if (string.IsNullOrEmpty(namespaceUri)) return;
            SetExtensionSchema(prefix, namespaceUri!, string.IsNullOrEmpty(schemaDescription) ? prefix : schemaDescription!);
        }
        if (!_extensionProperties.TryGetValue(prefix, out var described))
            _extensionProperties[prefix] = described = new(StringComparer.Ordinal);
        var category = property.Category == XmpPdfAExtensionCategoryType.Internal ? "internal" : ExternalCategory;
        described[property.Name] = (property.ValueType, category, property.Description);
        _dirty = true;
    }

    /// <summary>The pdfaExtension:schemas block the packet is written with: its own block with every schema it does
    /// not describe yet added - the registered ones, and in a packet that claims PDF/A each namespace outside the
    /// predefined ones that it states properties of - or null when there is nothing to describe.</summary>
    private XmpValue? ExtensionSchemasToWrite()
    {
        _structured.TryGetValue(ExtensionSchemasKey, out var block);
        var items = block is { IsArray: true } ? block.ToArray().ToList() : new List<XmpValue>();
        var described = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.Where(IsStructValue))
            foreach (var (k, v) in EnumerateStruct(item))
                if (k is "pdfaSchema:prefix" or "pdfaSchema:namespaceURI") described.Add(v.ToString());
        var added = false;
        foreach (var (prefix, uri, description) in SchemasToDescribe())
        {
            if (described.Contains(prefix) || described.Contains(uri)) continue;
            items.Add(SchemaEntry(prefix, uri, description));
            described.Add(prefix);
            added = true;
        }
        if (!added) return block;
        return new XmpValue(items.ToArray(), block is { IsArray: true } ? block.ArrayKind : "Bag");
    }

    private IEnumerable<(string Prefix, string Uri, string Description)> SchemasToDescribe()
    {
        foreach (var (prefix, schema) in _extensionSchemas)
            yield return (prefix, schema.Uri, schema.Description);
        if (string.IsNullOrEmpty(Get(PdfAPartKey))) yield break;
        var prefixes = _properties.Keys.Concat(_structured.Keys.Where(k => k != ExtensionSchemasKey))
            .Select(k => k.IndexOf(':') is var colon and > 0 ? k[..colon] : null)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        foreach (var prefix in prefixes)
        {
            if (_extensionSchemas.ContainsKey(prefix)) continue;
            var uri = PrefixToNamespace(prefix);
            if (uri is null || PredefinedNamespaces.Contains(uri)) continue;
            yield return (prefix, uri, prefix);
        }
    }

    /// <summary>One schema entry: the schema, its namespace and prefix, and a description of each property - the
    /// ones given for it, then each the packet states under the prefix, typed by its form. A structured property
    /// needs a value type of its own and is not described here.</summary>
    private XmpValue SchemaEntry(string prefix, string uri, string description)
    {
        var given = _extensionProperties.TryGetValue(prefix, out var explicitly)
            ? explicitly : new Dictionary<string, (string ValueType, string Category, string Description)>();
        var properties = given.Select(p => PropertyEntry(p.Key, p.Value.ValueType, p.Value.Category, p.Value.Description)).ToList();
        foreach (var key in _properties.Keys.Where(k => k.StartsWith(prefix + ":", StringComparison.Ordinal)))
        {
            var name = key[(prefix.Length + 1)..];
            if (given.ContainsKey(name)) continue;
            var valueType = _arrays.TryGetValue(key, out var array) ? ArrayValueType(array.Kind) : TextValueType;
            properties.Add(PropertyEntry(name, valueType, ExternalCategory, name));
        }
        var entry = new Dictionary<string, XmpValue>(StringComparer.Ordinal)
        {
            ["pdfaSchema:schema"] = new XmpValue(description),
            ["pdfaSchema:namespaceURI"] = new XmpValue(uri),
            ["pdfaSchema:prefix"] = new XmpValue(prefix),
        };
        if (properties.Count > 0) entry["pdfaSchema:property"] = new XmpValue(properties.ToArray(), "Seq");
        return new XmpValue(entry);
    }

    private static XmpValue PropertyEntry(string name, string valueType, string category, string description) =>
        new(new Dictionary<string, XmpValue>(StringComparer.Ordinal)
        {
            ["pdfaProperty:name"] = new XmpValue(name),
            ["pdfaProperty:valueType"] = new XmpValue(valueType),
            ["pdfaProperty:category"] = new XmpValue(category),
            ["pdfaProperty:description"] = new XmpValue(description),
        });
}
