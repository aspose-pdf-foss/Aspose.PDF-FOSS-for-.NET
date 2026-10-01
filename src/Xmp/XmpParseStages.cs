using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class XmpMetadata
{
    /// <summary>The stages of the XMP parse: the simple properties, the list properties and the list items.</summary>
    private void ParseXmpListItem(System.Text.RegularExpressions.Match li)
    {
        var body = li.Groups[1].Value;
        if (!body.Contains("pdfaSchema:prefix")) return;
        string Field(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                body, $"<pdfaSchema:{name}>(.*?)</pdfaSchema:{name}>",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
        }
        var prefix = Field("prefix");
        if (prefix.Length == 0) return;
        _extensionSchemas[prefix] = (Field("namespaceURI"), Field("schema"));
    }

    /// <summary>The stages of the XMP parse: the simple properties, the list properties and the list items.</summary>
    private void ParseXmpListProperty(System.Text.RegularExpressions.Match m)
    {
        var prefix = m.Groups[1].Value;
        var name = m.Groups[2].Value;
        var innerXml = m.Groups[3].Value;
        var key = $"{prefix}:{name}";

        if (_properties.ContainsKey(key)) return;

        var items = ListItemPattern().Matches(innerXml);
        if (items.Count > 0)
        {
            var values = items.Cast<Match>().Select(li => li.Groups[1].Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v));
            _properties.TryAdd(key, string.Join("; ", values));
        }
    }

    /// <summary>The stages of the XMP parse: the simple properties, the list properties and the list items.</summary>
    private void ParseXmpProperty(System.Text.RegularExpressions.Match m)
    {
        var prefix = m.Groups[1].Value;
        var name = m.Groups[2].Value;
        var value = m.Groups[3].Value.Trim();

        // rdf:/x: elements (rdf:li, rdf:value, rdf:Description, …) are RDF
        // structure, not metadata properties — they must not surface as keys.
        if (prefix is "rdf" or "x") return;

        var key = $"{prefix}:{name}";
        _properties.TryAdd(key, value);
    }
}
