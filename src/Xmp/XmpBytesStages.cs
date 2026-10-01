using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class XmpMetadata
{
    /// <summary>The stages of the XMP serialisation: one property, then the extension schemas.</summary>
    private void WriteXmpExtensionSchemas(XmpBytesState xb)
    {
        xb.sb.AppendLine("  <rdf:Description rdf:about=\"\"");
        xb.sb.AppendLine("   xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\"");
        xb.sb.AppendLine("   xmlns:pdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\">");
        xb.sb.AppendLine("   <pdfaExtension:schemas>");
        xb.sb.AppendLine("    <rdf:Bag>");
        foreach (var (prefix, schema) in _extensionSchemas)
        {
            xb.sb.AppendLine("     <rdf:li rdf:parseType=\"Resource\">");
            xb.sb.AppendLine($"      <pdfaSchema:schema>{EscapeXml(schema.Description)}</pdfaSchema:schema>");
            xb.sb.AppendLine($"      <pdfaSchema:namespaceURI>{EscapeXml(schema.Uri)}</pdfaSchema:namespaceURI>");
            xb.sb.AppendLine($"      <pdfaSchema:prefix>{EscapeXml(prefix)}</pdfaSchema:prefix>");
            xb.sb.AppendLine("     </rdf:li>");
        }
        xb.sb.AppendLine("    </rdf:Bag>");
        xb.sb.AppendLine("   </pdfaExtension:schemas>");
        xb.sb.AppendLine("  </rdf:Description>");
    }

    /// <summary>The stages of the XMP serialisation: one property, then the extension schemas.</summary>
    private void WriteXmpProperty(XmpBytesState xb, string key, string value)
    {
        var colon = key.IndexOf(':');
        if (colon <= 0) return;

        // Dublin Core list properties
        if (key is "dc:creator" or "dc:subject" && value.Contains(';'))
        {
            var items = value.Split(';').Select(v => v.Trim()).Where(v => v.Length > 0);
            xb.sb.AppendLine($"   <{key}>");
            xb.sb.AppendLine("    <rdf:Seq>");
            foreach (var item in items)
                xb.sb.AppendLine($"     <rdf:li>{EscapeXml(item)}</rdf:li>");
            xb.sb.AppendLine("    </rdf:Seq>");
            xb.sb.AppendLine($"   </{key}>");
        }
        else if (key is "dc:title" or "dc:description")
        {
            xb.sb.AppendLine($"   <{key}>");
            xb.sb.AppendLine("    <rdf:Alt>");
            xb.sb.AppendLine($"     <rdf:li xml:lang=\"x-default\">{EscapeXml(value)}</rdf:li>");
            xb.sb.AppendLine("    </rdf:Alt>");
            xb.sb.AppendLine($"   </{key}>");
        }
        else
        {
            xb.sb.AppendLine($"   <{key}>{EscapeXml(value)}</{key}>");
        }
    }
}
