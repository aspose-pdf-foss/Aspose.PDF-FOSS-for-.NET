#nullable disable

using System.Collections.Generic;
using System.Xml;

namespace Aspose.Pdf;

/// <summary>Whether a PDF/A XMP-extension property is for external (publicly visible)
/// or internal (private) use.</summary>
public enum XmpPdfAExtensionCategoryType
{
    /// <summary>The property is publicly visible.</summary>
    External = 0,
    /// <summary>The property is for internal/private use.</summary>
    Internal = 1,
}

/// <summary>
/// One PDF/A XMP-extension property/value pair. Carries a description
/// string and a writable value; serialisation to XML is provided by
/// <see cref="GetXml(XmlDocument)"/>.
/// </summary>
public class XmpPdfAExtensionObject
{
    /// <summary>Construct an extension object with description + value.</summary>
    public XmpPdfAExtensionObject(string description, string value)
    {
        Description = description;
        Value = value;
    }

    /// <summary>Description of what this extension property carries.</summary>
    public string Description { get; }

    /// <summary>Current value (writable so callers can update before serialising).</summary>
    public string Value { get; set; }

    /// <summary>Render this extension object as XML elements rooted in <paramref name="xmlDocument"/>.</summary>
    public virtual List<XmlElement> GetXml(XmlDocument xmlDocument) => new();
}

/// <summary>One field of a PDF/A XMP-extension structured value type.</summary>
public class XmpPdfAExtensionField : XmpPdfAExtensionObject
{
    /// <summary>Construct a field with name, value, value type, and description.</summary>
    public XmpPdfAExtensionField(string name, string value, string valueType, string description)
        : base(description, value)
    {
        Name = name;
        ValueType = valueType;
    }

    /// <summary>Field name.</summary>
    public string Name { get; }

    /// <summary>Field value type (XMP type reference).</summary>
    public string ValueType { get; }

    /// <inheritdoc />
    public override List<XmlElement> GetXml(XmlDocument xmlDocument) => new();
}

/// <summary>One PDF/A XMP-extension property declaration (name, value, value type, category, description).</summary>
public class XmpPdfAExtensionProperty : XmpPdfAExtensionField
{
    /// <summary>Construct a property with full metadata.</summary>
    public XmpPdfAExtensionProperty(string name, string value, string valueType,
                                    XmpPdfAExtensionCategoryType category, string description)
        : base(name, value, valueType, description)
    {
        Category = category;
    }

    /// <summary>External (public) or internal (private) visibility.</summary>
    public XmpPdfAExtensionCategoryType Category { get; }

    /// <inheritdoc />
    public override List<XmlElement> GetXml(XmlDocument xmlDocument) => new();
}

/// <summary>One PDF/A XMP-extension structured value type, holding its fields.</summary>
public class XmpPdfAExtensionValueType
{
    private readonly List<XmpPdfAExtensionField> _fields = new();

    /// <summary>Construct with type name, namespace URI, prefix, and description.</summary>
    public XmpPdfAExtensionValueType(string type, string namespaceUri, string prefix, string description)
    {
        Type = type;
        NamespaceUri = namespaceUri;
        Prefix = prefix;
        Description = description;
    }

    /// <summary>Type name as referenced from properties' valueType.</summary>
    public string Type { get; }

    /// <summary>Namespace URI declaring this value type.</summary>
    public string NamespaceUri { get; }

    /// <summary>Namespace prefix bound to <see cref="NamespaceUri"/>.</summary>
    public string Prefix { get; }

    /// <summary>Human-readable description of the value type.</summary>
    public string Description { get; }

    /// <summary>Fields declared by this value type.</summary>
    public IList<XmpPdfAExtensionField> Fields => _fields;

    /// <summary>Add a single field.</summary>
    public void Add(XmpPdfAExtensionField field) => _fields.Add(field);

    /// <summary>Add an array of fields.</summary>
    public void AddRange(XmpPdfAExtensionField[] fields)
    {
        if (fields is null) return;
        foreach (var f in fields) _fields.Add(f);
    }

    /// <summary>Remove every field.</summary>
    public void Clear() => _fields.Clear();

    /// <summary>Remove the given field.</summary>
    public void Remove(XmpPdfAExtensionField field) => _fields.Remove(field);

    /// <summary>Render this value type as XML elements.</summary>
    public List<XmlElement> GetXml(XmlDocument xmlDocument) => new();
}

/// <summary>
/// One PDF/A XMP-extension schema, holding a description and its
/// associated <see cref="XmpPdfAExtensionObject"/>s.
/// </summary>
public class XmpPdfAExtensionSchema
{
    /// <summary>Default prefix used for the extension namespace declaration.</summary>
    public const string DefaultExtensionNamespacePrefix = "pdfaExtension";

    /// <summary>Default URI used for the extension namespace declaration.</summary>
    public const string DefaultExtensionNamespaceUri = "http://www.aiim.org/pdfa/ns/extension/";

    /// <summary>Default prefix used for the extension-field namespace declaration.</summary>
    public const string DefaultFieldNamespacePrefix = "pdfaField";

    /// <summary>Default URI used for the extension-field namespace declaration.</summary>
    public const string DefaultFieldNamespaceUri = "http://www.aiim.org/pdfa/ns/field#";

    /// <summary>Default prefix used for the extension-property namespace declaration.</summary>
    public const string DefaultPropertyNamespacePrefix = "pdfaProperty";

    /// <summary>Default URI used for the extension-property namespace declaration.</summary>
    public const string DefaultPropertyNamespaceUri = "http://www.aiim.org/pdfa/ns/property#";

    /// <summary>Default prefix used for the extension-schema namespace declaration.</summary>
    public const string DefaultSchemaNamespacePrefix = "pdfaSchema";

    /// <summary>Default URI used for the extension-schema namespace declaration.</summary>
    public const string DefaultSchemaNamespaceUri = "http://www.aiim.org/pdfa/ns/schema#";

    /// <summary>Default URI used for value-type values.</summary>
    public const string DefaultValueNamespaceUri = "http://www.aiim.org/pdfa/ns/type#";

    /// <summary>Default prefix used for value-type declarations.</summary>
    public const string DefaultValueTypeNamespacePrefix = "pdfaType";

    /// <summary>URI for the RDF namespace used to wrap PDF/A extension metadata.</summary>
    public const string RdfNamespaceURI = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>Prefix conventionally bound to <see cref="RdfNamespaceURI"/>.</summary>
    public const string RdfPrefix = "rdf";

    /// <summary>Construct a schema with the given description.</summary>
    public XmpPdfAExtensionSchema(XmpPdfAExtensionSchemaDescription description)
    {
        Description = description;
        Objects = new List<XmpPdfAExtensionObject>();
    }

    /// <summary>Schema description.</summary>
    public XmpPdfAExtensionSchemaDescription Description { get; }

    /// <summary>Extension objects belonging to this schema.</summary>
    public List<XmpPdfAExtensionObject> Objects { get; }

    /// <summary>Append <paramref name="obj"/> to <see cref="Objects"/>.</summary>
    public void Add(XmpPdfAExtensionObject obj) => Objects.Add(obj);

    /// <summary>Whether <see cref="Objects"/> contains <paramref name="obj"/>.</summary>
    public bool Contains(XmpPdfAExtensionObject obj) => Objects.Contains(obj);

    /// <summary>Remove <paramref name="obj"/> from <see cref="Objects"/> if present.</summary>
    public void Remove(XmpPdfAExtensionObject obj) => Objects.Remove(obj);

    /// <summary>Locate a property by name; returns null when none match.</summary>
    public XmpPdfAExtensionProperty GetProperty(string name)
    {
        if (name is null) return null;
        foreach (var o in Objects)
        {
            if (o is XmpPdfAExtensionProperty p && p.Name == name) return p;
        }
        return null;
    }

    /// <summary>Render the schema header (description + namespaces) as XML.</summary>
    public XmlElement GetSchemaXml(XmlDocument xmlDocument) => null;

    /// <summary>Render the schema values (property/field entries) into <paramref name="rootElement"/>.</summary>
    public void GetValuesXml(XmlDocument xmlDocument, XmlElement rootElement) { }
}

/// <summary>Human-readable description of a PDF/A XMP extension schema.</summary>
public class XmpPdfAExtensionSchemaDescription
{
    /// <summary>Construct a description with prefix, namespace URI, and free-form text.</summary>
    public XmpPdfAExtensionSchemaDescription(string prefix, string namespaceURI, string description)
    {
        Prefix = prefix;
        NamespaceURI = namespaceURI;
        Description = description;
    }

    /// <summary>Schema description.</summary>
    public string Description { get; }

    /// <summary>Namespace URI declared by this schema.</summary>
    public string NamespaceURI { get; }

    /// <summary>Namespace prefix bound to <see cref="NamespaceURI"/>.</summary>
    public string Prefix { get; }

    /// <summary>Render this description as XML elements.</summary>
    public List<XmlElement> GetXml(XmlDocument xmlDocument) => new();
}
