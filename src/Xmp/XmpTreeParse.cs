using System.Xml.Linq;

namespace Aspose.Pdf;

public sealed partial class XmpMetadata
{
    /// <summary>The scalar arrays the packet stated, by property: the container, the items with their language,
    /// and the text they join to in the flat store. While the property still holds that text it is written back
    /// with these items; a new value keeps the container.</summary>
    private readonly Dictionary<string, (string Kind, (string Text, string? Lang)[] Items, string Joined)> _arrays =
        new(StringComparer.Ordinal);

    private const string ResourceParseType = "Resource";

    /// <summary>The packet as an XML tree, with the positions of its nodes in <paramref name="text"/>, the XML it was
    /// read from; null when it is not well-formed.</summary>
    private static XDocument? TryParseXml(string xml, out string text)
    {
        // A byte-order mark decoded into the text and the NUL padding some writers leave after the packet are
        // not part of the XML.
        text = xml.TrimStart('﻿').TrimEnd('\0');
        try
        {
            return XDocument.Parse(text, System.Xml.Linq.LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>The rdf:Description elements directly under rdf:RDF: the packet's properties are their children and
    /// attributes, never the fields nested in a property's value.</summary>
    private static List<XElement> TopLevelDescriptions(XDocument tree) =>
        tree.Descendants(RdfNs + "RDF").Elements(RdfNs + "Description").ToList();

    private void ParseRdfTree(List<XElement> descriptions, string text)
    {
        var lineStarts = LineStarts(text);
        // The prefix bindings the properties are written back under, however the packet quoted them.
        foreach (var binding in descriptions[0].Document!.Descendants().Attributes().Where(a => a.IsNamespaceDeclaration))
        {
            var prefix = binding.Name.Namespace == XNamespace.Xmlns ? binding.Name.LocalName : string.Empty;
            if (prefix is not ("" or "x" or "rdf")) _customNamespaces[prefix] = binding.Value;
        }

        foreach (var description in descriptions)
            foreach (var property in description.Elements().Where(e => e.Name.Namespace != RdfNs))
                ParseTopLevelProperty(property, WrittenName(property, QName(property), text, lineStarts));

        // Attribute-form (shorthand) properties, e.g. <rdf:Description pdfaid:part="2">. The element form wins
        // when both exist (TryAdd keeps the first).
        foreach (var description in descriptions)
            foreach (var attribute in description.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace == RdfNs
                    || attribute.Name.Namespace == XNamespace.Xml || attribute.Name.Namespace == XNamespace.None)
                    continue;
                var prefix = description.GetPrefixOfNamespace(attribute.Name.Namespace);
                if (!string.IsNullOrEmpty(prefix))
                    _properties.TryAdd(WrittenName(attribute, $"{prefix}:{attribute.Name.LocalName}", text, lineStarts),
                        attribute.Value.Trim());
            }
    }

    /// <summary>One property of a top-level description: a scalar array, an array of structures, a structure, or
    /// a simple value (its text decoded; empty when the element is).</summary>
    private void ParseTopLevelProperty(XElement property, string key)
    {
        var container = property.Elements().FirstOrDefault(IsContainer);
        if (container is not null)
        {
            var items = container.Elements(RdfNs + "li").ToList();
            if (items.Any(IsStructLi))
                _structured.TryAdd(key, new XmpValue(items.Select(ParseStructNode).ToArray(), container.Name.LocalName));
            else
                AddScalarArray(key, container.Name.LocalName, items);
            return;
        }
        if ((string?)property.Attribute(RdfNs + "parseType") == ResourceParseType || HasStructAttributes(property)
            || property.Elements().Any())
        {
            _structured.TryAdd(key, StructOf(property));
            return;
        }
        _properties.TryAdd(key, (string?)property.Attribute(RdfNs + "resource") ?? property.Value.Trim());
    }

    /// <summary>A structure: one written as an rdf:Description of simple fields keeps the named-value shape the
    /// pattern pass gave it; any other form is read field by field, nested values included.</summary>
    private static XmpValue StructOf(XElement property)
    {
        var children = property.Elements().ToList();
        if (children.Count == 1 && children[0].Name == RdfNs + "Description" && !HasStructAttributes(children[0])
            && children[0].HasElements && children[0].Elements().All(f => !f.HasElements && f.Name.Namespace != RdfNs))
        {
            return new XmpValue(children[0].Elements()
                .Select(f => new KeyValuePair<string, XmpValue>(QName(f), new XmpValue(f.Value.Trim()))).ToArray());
        }
        return ParseStructNode(property);
    }

    /// <summary>The name as the packet wrote it, prefix included. A namespace may be bound to two prefixes - the
    /// legacy xap and the current xmp of one namespace, say - and a property keeps the one it was written under,
    /// which the tree alone cannot tell; <paramref name="resolved"/> when the text does not show it.</summary>
    private static string WrittenName(System.Xml.IXmlLineInfo node, string resolved, string text, List<int> lineStarts)
    {
        if (!node.HasLineInfo() || node.LineNumber > lineStarts.Count) return resolved;
        var start = lineStarts[node.LineNumber - 1] + node.LinePosition - 1;
        var end = start;
        while (end < text.Length && text[end] is not (' ' or '\t' or '\r' or '\n' or '=' or '>' or '/')) end++;
        if (start < 0 || start >= text.Length) return resolved;
        var written = text[start..end];
        var colon = written.IndexOf(':');
        var localName = resolved[(resolved.IndexOf(':') + 1)..];
        return colon > 0 && written.Length - colon - 1 == localName.Length
               && string.CompareOrdinal(written, colon + 1, localName, 0, localName.Length) == 0 ? written : resolved;
    }

    /// <summary>The offset at which each line of <paramref name="text"/> starts, as XML counts lines.</summary>
    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 == text.Length || text[i + 1] != '\n')))
                starts.Add(i + 1);
        return starts;
    }

    private void AddScalarArray(string key, string kind, List<XElement> items)
    {
        var read = items.Select(li => (li.Value.Trim(), (string?)li.Attribute(XNamespace.Xml + "lang"))).ToArray();
        var joined = string.Join("; ", read.Select(item => item.Item1).Where(text => text.Length > 0));
        if (_properties.TryAdd(key, joined)) _arrays[key] = (kind, read, joined);
    }
}
