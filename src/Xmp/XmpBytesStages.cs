using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class XmpMetadata
{
    /// <summary>The stages of the XMP serialisation: one property.</summary>
    private void WriteXmpProperty(XmpBytesState xb, string key, string value)
    {
        var colon = key.IndexOf(':');
        if (colon <= 0) return;

        // An array the packet stated keeps its container, and its items while the value is the text they join to.
        // Dublin Core list properties are arrays whatever the number of their items: the authors an ordered one,
        // the subject words an unordered one, the title and description a language alternative. A plain-text value
        // there is not the property the schema defines, and PDF/A (ISO 19005-1 6.7.9) rejects it.
        var stated = _arrays.TryGetValue(key, out var read);
        var kind = stated ? read.Kind : key switch
        {
            "dc:creator" => "Seq",
            "dc:subject" => "Bag",
            "dc:title" or "dc:description" => "Alt",
            _ => null,
        };
        if (kind is null)
        {
            xb.sb.AppendLine($"   <{key}>{EscapeXml(value)}</{key}>");
            return;
        }
        xb.sb.AppendLine($"   <{key}>");
        xb.sb.AppendLine($"    <rdf:{kind}>");
        foreach (var (text, lang) in stated && read.Joined == value ? read.Items : ArrayItemsOf(kind, value))
        {
            var langAttribute = lang is null ? string.Empty : $" xml:lang=\"{EscapeXml(lang)}\"";
            xb.sb.AppendLine($"     <rdf:li{langAttribute}>{EscapeXml(text)}</rdf:li>");
        }
        xb.sb.AppendLine($"    </rdf:{kind}>");
        xb.sb.AppendLine($"   </{key}>");
    }

    /// <summary>The items a flat value stands for: one default-language text in an alternative, else the "; "-joined
    /// items - an empty value being one empty item, as the empty /Author it mirrors is one author left blank.</summary>
    private static IEnumerable<(string Text, string? Lang)> ArrayItemsOf(string kind, string value)
    {
        if (kind == "Alt") return [(value, "x-default")];
        if (value.Length == 0) return [(string.Empty, null)];
        return value.Split(';').Select(v => v.Trim()).Where(v => v.Length > 0).Select(v => (v, (string?)null));
    }
}
