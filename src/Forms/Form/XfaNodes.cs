using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    /// <summary>
    /// Replace the entire XFA datasets stream with new XML content.
    /// Used by ImportXml to wholesale replace data rather than individual field updates.
    /// </summary>
    internal void ReplaceXfaDatasets(XmlDocument importedXml)
    {
        var (stream, existingXml) = GetXfaPart("datasets");

        if (stream is not null && existingXml is not null)
        {
            // Existing datasets part — merge imported data into it
            var existingDoc = new XmlDocument();
            existingDoc.LoadXml(existingXml);

            var dataNs = existingDoc.DocumentElement?.SelectSingleNode("//*[local-name()='data']");
            // If <data> doesn't exist (only <dataDescription>), create it
            if (dataNs is null && existingDoc.DocumentElement is not null)
            {
                var ns = existingDoc.DocumentElement.NamespaceURI;
                var prefix = existingDoc.DocumentElement.Prefix;
                dataNs = string.IsNullOrEmpty(prefix)
                    ? existingDoc.CreateElement("data", ns)
                    : existingDoc.CreateElement(prefix, "data", ns);
                existingDoc.DocumentElement.AppendChild(dataNs);
            }
            if (dataNs is null) return;

            dataNs.InnerXml = "";
            // Unwrap xfa:data / xfa:datasets wrapper if present in the imported XML,
            // so we don't double-nest (e.g. <data><xfa:data><form1>.)
            ImportDataChildren(importedXml, existingDoc, dataNs);

            using var ms = new MemoryStream();
            SaveXmlNoBom(existingDoc, ms);
            var newData = ms.ToArray();
            stream.ReplaceData(newData);
            stream.Dict.Set("Length", new PdfInteger(newData.Length));
            stream.Dict.Remove("Filter");
            MarkXfaStreamDirty(stream);
            return;
        }

        // No "datasets" part in the XFA array
        var rdr = ResolvedReader;
        if (rdr is null) return;
        var acroForm = rdr.ResolveDict(rdr.Catalog.Get("AcroForm"));
        if (acroForm is null) return;
        var xfaObj = rdr.Resolve(acroForm.Get("XFA"));

        // Single-stream XFA: the entire XDP is in one stream.
        // Parse it, find/create <datasets><data>, replace content, write back.
        if (xfaObj is PdfStream singleStream)
        {
            var xdpData = rdr.DecodeStream(singleStream);
            var xdpXml = Encoding.UTF8.GetString(xdpData);
            var xdpDoc = new XmlDocument();
            xdpDoc.LoadXml(xdpXml);

            // Find or create the <datasets> element
            var datasetsEl = xdpDoc.DocumentElement?.SelectSingleNode("//*[local-name()='datasets']");
            if (datasetsEl is null && xdpDoc.DocumentElement is not null)
            {
                // Create <xfa:datasets> element and insert before postamble
                const string xfaNs = "http://www.xfa.org/schema/xfa-data/1.0/";
                datasetsEl = xdpDoc.CreateElement("xfa", "datasets", xfaNs);
                // Try to insert before the closing </xdp:xdp> (last child or before postamble)
                xdpDoc.DocumentElement.AppendChild(datasetsEl);
            }
            if (datasetsEl is null) return;

            // Find or create the <data> element inside <datasets>
            var dataEl = datasetsEl.SelectSingleNode("*[local-name()='data']");
            if (dataEl is null)
            {
                var ns = datasetsEl.NamespaceURI;
                var prefix = datasetsEl.Prefix;
                dataEl = string.IsNullOrEmpty(prefix)
                    ? xdpDoc.CreateElement("data", ns)
                    : xdpDoc.CreateElement(prefix, "data", ns);
                datasetsEl.AppendChild(dataEl);
            }

            // Clear existing data and import the root element of the imported XML
            dataEl.InnerXml = "";
            ImportDataChildren(importedXml, xdpDoc, dataEl);

            // Write updated XDP back to the stream (no BOM)
            using var ms = new MemoryStream();
            SaveXmlNoBom(xdpDoc, ms);
            var newData = ms.ToArray();
            singleStream.ReplaceData(newData);
            singleStream.Dict.Set("Length", new PdfInteger(newData.Length));
            singleStream.Dict.Remove("Filter");
            MarkXfaStreamDirty(singleStream);
            return;
        }

        if (xfaObj is PdfArray xfaArray)
        {
            // XFA array without a named "datasets" part — create one
            const string xfaNs2 = "http://www.xfa.org/schema/xfa-data/1.0/";
            var datasetsDoc = new XmlDocument();
            var datasetsEl2 = datasetsDoc.CreateElement("xfa", "datasets", xfaNs2);
            datasetsDoc.AppendChild(datasetsEl2);
            var dataEl2 = datasetsDoc.CreateElement("xfa", "data", xfaNs2);
            datasetsEl2.AppendChild(dataEl2);

            ImportDataChildren(importedXml, datasetsDoc, dataEl2);

            using var ms = new MemoryStream();
            SaveXmlNoBom(datasetsDoc, ms);
            var newData = ms.ToArray();
            var newStream = new PdfStream(new PdfDictionary(), newData);
            newStream.Dict.Set("Length", new PdfInteger(newData.Length));

            // Insert "datasets" name + stream before "postamble" (or at end)
            int insertIdx = xfaArray.Count;
            for (int i = 0; i < xfaArray.Count - 1; i += 2)
            {
                if (xfaArray[i] is PdfString s &&
                    Encoding.Latin1.GetString(s.Value) == "postamble")
                {
                    insertIdx = i;
                    break;
                }
            }
            xfaArray.Insert(insertIdx, new PdfString(Encoding.Latin1.GetBytes("datasets")));
            xfaArray.Insert(insertIdx + 1, newStream);
        }
    }

    /// <summary>Ensure the /XFA array carries a "datasets" part, creating an empty
    /// <c>&lt;xfa:datasets&gt;&lt;xfa:data/&gt;&lt;/xfa:datasets&gt;</c> stream and wiring it into the
    /// array (before any "postamble") when absent. Marks the AcroForm dict dirty so the
    /// added array entry + stream are re-serialised on save. Returns the datasets stream,
    /// or null when the form's XFA is not an array (single-stream is handled elsewhere).</summary>
    private PdfStream? EnsureXfaDatasetsStreamInArray()
    {
        var rdr = ResolvedReader;
        if (rdr is null) return null;
        var acroForm = rdr.ResolveDict(rdr.Catalog.Get("AcroForm"));
        if (acroForm is null) return null;
        if (rdr.Resolve(acroForm.Get("XFA")) is not PdfArray xfaArray) return null;

        for (int i = 0; i + 1 < xfaArray.Count; i += 2)
            if (xfaArray[i] is PdfString s && Encoding.Latin1.GetString(s.Value) == "datasets"
                && rdr.Resolve(xfaArray[i + 1]) is PdfStream existing)
                return existing;

        const string xfaNs = "http://www.xfa.org/schema/xfa-data/1.0/";
        var doc = new XmlDocument();
        var dsEl = doc.CreateElement("xfa", "datasets", xfaNs);
        doc.AppendChild(dsEl);
        dsEl.AppendChild(doc.CreateElement("xfa", "data", xfaNs));
        using var ms = new MemoryStream();
        SaveXmlNoBom(doc, ms);
        var bytes = ms.ToArray();
        var newStream = new PdfStream(new PdfDictionary(), bytes);
        newStream.Dict.Set("Length", new PdfInteger(bytes.Length));

        int insertIdx = xfaArray.Count;
        for (int i = 0; i < xfaArray.Count - 1; i += 2)
            if (xfaArray[i] is PdfString s && Encoding.Latin1.GetString(s.Value) == "postamble")
            { insertIdx = i; break; }
        xfaArray.Insert(insertIdx, new PdfString(Encoding.Latin1.GetBytes("datasets")));
        xfaArray.Insert(insertIdx + 1, newStream);

        // The array lives on the AcroForm dict — mark it dirty so the new "datasets"
        // entry (and its inline stream) are written out.
        MarkAcroFormDirty();
        return newStream;
    }

    /// <summary>
    /// Import data from an imported XML document into a target data node.
    /// Unwraps xfa:data and xfa:datasets wrappers so we don't double-nest
    /// (e.g. avoid &lt;data&gt;&lt;xfa:data&gt;&lt;form1&gt;.).
    /// </summary>
    private static void ImportDataChildren(
        XmlDocument importedXml, XmlDocument targetDoc, XmlNode targetDataNode)
    {
        if (importedXml.DocumentElement is null) return;

        var root = importedXml.DocumentElement;
        // Unwrap: an XDP envelope (<xdp:xdp>, e.g. an exported .xdp/.xfdf data file)
        // carries the form data in its <xfa:datasets> child packet — drill into it,
        // or the whole envelope (pdf href, config, …) would land inside <data>.
        if (root.LocalName == "xdp"
            && root.SelectSingleNode("*[local-name()='datasets']") is XmlElement dsEl)
            root = dsEl;

        // Unwrap: if root is xfa:datasets, drill into xfa:data child
        if (root.LocalName == "datasets")
        {
            var dataChild = root.SelectSingleNode("*[local-name()='data']");
            if (dataChild is not null) root = (XmlElement)dataChild;
        }

        // Unwrap: if root is xfa:data, import its children (the actual form data)
        if (root.LocalName == "data" &&
            (root.NamespaceURI.Contains("xfa") || root.NamespaceURI == ""))
        {
            foreach (XmlNode child in root.ChildNodes)
            {
                var imported = ImportNodeStripNamespaces(targetDoc, child);
                if (imported is not null) targetDataNode.AppendChild(imported);
            }
        }
        else
        {
            // Not a wrapper — import the element directly
            var imported = ImportNodeStripNamespaces(targetDoc, root);
            if (imported is not null) targetDataNode.AppendChild(imported);
        }
    }

    /// <summary>Deep-copy an imported data node into <paramref name="targetDoc"/> with all
    /// namespaces stripped (element + attribute local names only, xmlns declarations dropped),
    /// preserving attributes, text and CDATA. The XFA data model ($data) is namespace-less, so
    /// foreign source XML (e.g. an <c>efile:</c>-namespaced e-file wrapper) must land as
    /// namespace-less nodes for the form's SOM/XPath to resolve them.</summary>
    private static XmlNode? ImportNodeStripNamespaces(XmlDocument targetDoc, XmlNode src)
    {
        switch (src.NodeType)
        {
            case XmlNodeType.Text: return targetDoc.CreateTextNode(src.Value ?? "");
            case XmlNodeType.CDATA: return targetDoc.CreateCDataSection(src.Value ?? "");
            case XmlNodeType.Element: break;
            default: return null; // drop comments / PIs / whitespace-only handled by children walk
        }
        var el = targetDoc.CreateElement(src.LocalName);
        if (src.Attributes is not null)
            foreach (XmlAttribute a in src.Attributes)
            {
                if (a.Prefix == "xmlns" || a.LocalName == "xmlns") continue; // drop ns declarations
                el.SetAttribute(a.LocalName, a.Value);
            }
        foreach (XmlNode c in src.ChildNodes)
        {
            var ic = ImportNodeStripNamespaces(targetDoc, c);
            if (ic is not null) el.AppendChild(ic);
        }
        return el;
    }

    private static XmlNode? FindXfaNode(XmlDocument doc, string path, bool strict = false)
    {
        var parts = SplitSomPath(path);
        XmlNode? current = doc.DocumentElement;

        // First descend into the xfa:data element if present.
        // Prefer the <data> element inside <datasets> (not config's <data>).
        if (current is not null)
        {
            XmlNode? dataNode = null;
            // Try to find <datasets>/<data> first
            var datasetsNode = current.SelectSingleNode("//*[local-name()='datasets']");
            if (datasetsNode is not null)
                dataNode = datasetsNode.SelectSingleNode("*[local-name()='data']");
            // Fallback: find <data> that contains form-like children (not config <data>)
            if (dataNode is null)
            {
                var allData = current.SelectNodes("//*[local-name()='data']");
                if (allData is not null)
                {
                    foreach (XmlNode d in allData)
                    {
                        // Skip config <data> — it has adjustData, xsl etc. as children
                        // The real XFA data has form-field-like children
                        if (d.ParentNode is not null && d.ParentNode.LocalName == "datasets")
                        {
                            dataNode = d;
                            break;
                        }
                    }
                    // Last resort: use first <data> with child elements matching path start
                    if (dataNode is null && allData.Count > 0)
                    {
                        var firstPart = parts[0];
                        var partMatch = Regex.Match(firstPart, @"^(.+)\[(\d+)\]$");
                        var partName = partMatch.Success ? partMatch.Groups[1].Value : firstPart;
                        foreach (XmlNode d in allData)
                        {
                            if (FindChildrenByLocalName(d, partName).Count > 0)
                            {
                                dataNode = d;
                                break;
                            }
                        }
                    }
                    dataNode ??= allData.Count > 0 ? allData[0] : null;
                }
            }
            if (dataNode is not null) current = dataNode;
        }

        // Try strict path walk first
        var root = current;
        foreach (var part in parts)
        {
            if (current is null) break;
            var match = Regex.Match(part, @"^(.+)\[(\d+)\]$");
            if (match.Success)
            {
                var name = match.Groups[1].Value;
                var idx = int.Parse(match.Groups[2].Value);
                var nodes = FindChildrenByLocalName(current, name);
                // XFA occurrence binding: when the template repeats a field name
                // (Season[0], Season[1]) but the datasets carries fewer data nodes, the
                // surplus instances bind to the existing (often single) node rather than
                // resolving to nothing. Fall back to the last available node when the
                // requested index is out of range. An in-range index is unchanged, so
                // fields that DO carry one node per instance still resolve distinctly.
                if (strict && nodes.Count > 1) return null;
                if (idx < nodes.Count) current = nodes[idx];
                else current = nodes.Count > 0 ? nodes[nodes.Count - 1] : null;
            }
            else
            {
                // XFA's data model exposes an element's ATTRIBUTES as value nodes:
                // a record-shaped datasets (Designer data connections) carries its
                // leaves as attributes (<county countyName="ADAMS"/>), and a dataRef
                // like $.californiaCaption.county.countyName must resolve to them.
                // The attribute outranks the DESCENDANT fallback — a deep same-name
                // element (caseNumber's empty <courtType>) must not shadow the
                // current node's own courtType="COUNTY".
                var direct = new List<XmlNode>();
                foreach (XmlNode c in current.ChildNodes)
                    if (c.NodeType == XmlNodeType.Element && c.LocalName == part) direct.Add(c);
                if (direct.Count == 0 && current.Attributes?[part] is { } attr)
                {
                    current = attr;
                    continue;
                }
                var nodes = direct.Count > 0 ? direct : FindChildrenByLocalName(current, part);
                if (strict && nodes.Count > 1) return null;
                current = nodes.Count > 0 ? nodes[0] : null;
            }
        }

        if (current is not null)
            return current;

        // Fallback: XFA data XML may be flat (template path segments don't map to data hierarchy).
        // Search for the last segment as a descendant of the data root.
        if (root is not null && parts.Length > 0)
        {
            var lastPart = parts[^1];
            var idxMatch = Regex.Match(lastPart, @"^(.+)\[(\d+)\]$");
            var leafName = idxMatch.Success ? idxMatch.Groups[1].Value : lastPart;
            var leafIdx = idxMatch.Success ? int.Parse(idxMatch.Groups[2].Value) : 0;
            // Only data VALUE nodes qualify: a dataGroup (a field sharing its subform's
            // name matches the group here) has an InnerText that concatenates every
            // descendant value — never a field's value. Rich values keep their xhtml
            // children.
            var leaves = root.SelectNodes($".//*[local-name()='{leafName}']")
                ?.OfType<XmlElement>()
                .Where(n => n.GetAttribute("dataNode", "http://www.xfa.org/schema/xfa-data/1.0/") != "dataGroup"
                            && (!n.ChildNodes.OfType<XmlElement>().Any()
                                || n.ChildNodes.OfType<XmlElement>().All(c => c.NamespaceURI == "http://www.w3.org/1999/xhtml")))
                .ToList();
            if (strict && leaves is { Count: > 1 }) return null;
            if (leaves is { Count: > 0 })
                // XFA occurrence binding (see the strict walk above): a repeated template
                // instance whose datasets has fewer data nodes binds to the existing node
                // rather than resolving to nothing, so clamp an out-of-range index to the
                // last available match. An in-range index resolves distinctly as before.
                return leaves[leafIdx < leaves.Count ? leafIdx : leaves.Count - 1];
        }

        return null;
    }

    /// <summary>
    /// Create the full path of nodes in the XFA data section.
    /// Used when setting a value on a node that doesn't exist yet.
    /// </summary>
    private static XmlNode? CreateXfaNodePath(XmlDocument doc, string path)
    {
        XmlNode? current = doc.DocumentElement;
        if (current is null) return null;

        // Find the correct <data> node (inside <datasets>, not config)
        XmlNode? dataNode = null;
        var datasetsNode = current.SelectSingleNode("//*[local-name()='datasets']");
        if (datasetsNode is not null)
        {
            dataNode = datasetsNode.SelectSingleNode("*[local-name()='data']");
            if (dataNode is null)
            {
                // Create <xfa:data> inside <datasets>
                var ns = datasetsNode.NamespaceURI;
                dataNode = doc.CreateElement("xfa", "data", ns);
                datasetsNode.AppendChild(dataNode);
            }
        }
        else
        {
            // Fallback: find any <data> whose parent is datasets
            var allData = current.SelectNodes("//*[local-name()='data']");
            if (allData is not null)
            {
                foreach (XmlNode d in allData)
                {
                    if (d.ParentNode?.LocalName == "datasets") { dataNode = d; break; }
                }
            }
            if (dataNode is null)
            {
                dataNode = current.SelectSingleNode("//*[local-name()='data']");
            }
        }
        if (dataNode is null) return null;
        current = dataNode;

        var parts = SplitSomPath(path);
        foreach (var part in parts)
        {
            var match = Regex.Match(part, @"^(.+)\[(\d+)\]$");
            var name = match.Success ? match.Groups[1].Value : part;
            var idx = match.Success ? int.Parse(match.Groups[2].Value) : 0;

            var children = FindChildrenByLocalName(current, name);
            // Create missing nodes up to the required index
            while (children.Count <= idx)
            {
                var newNode = doc.CreateElement(name);
                current.AppendChild(newNode);
                children.Add(newNode);
            }
            current = children[idx];
        }
        return current;
    }

    private static List<XmlNode> FindChildrenByLocalName(XmlNode parent, string localName)
    {
        var result = new List<XmlNode>();
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.LocalName == localName)
                result.Add(child);
        }
        // Also search all descendants if not found in direct children
        if (result.Count == 0)
        {
            var descendants = parent.SelectNodes($".//*[local-name()='{localName}']");
            if (descendants is not null)
                foreach (XmlNode d in descendants)
                    result.Add(d);
        }
        return result;
    }

    private static string StripBom(string s) =>
        s.Length > 0 && s[0] == '\uFEFF' ? s.Substring(1) : s;

    /// <summary>Save XmlDocument to a stream without BOM.</summary>
    private static void SaveXmlNoBom(XmlDocument doc, MemoryStream ms)
    {
        var settings = new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false
        };
        using var writer = System.Xml.XmlWriter.Create(ms, settings);
        doc.Save(writer);
    }

    private static string? FindXfaNodeValue(string xml, string path, bool strict = false)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var node = FindXfaNode(doc, path, strict);
            return node?.InnerText;
        }
        catch { return null; }
    }
}
