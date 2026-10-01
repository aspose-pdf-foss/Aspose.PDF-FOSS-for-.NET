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
        var xd = new XfaDatasetsReplaceState();
        xd.importedXml = importedXml;
        var (stream, existingXml) = GetXfaPart("datasets");

        if (stream is not null && existingXml is not null)
        {
            if (!RewriteXfaDatasetsPacket(xd, stream, existingXml)) return;
        }

        xd.rdr = ResolvedReader;
        if (xd.rdr is null) return;
        xd.acroForm = xd.rdr.ResolveDict(xd.rdr.Catalog.Get("AcroForm"));
        if (xd.acroForm is null) return;
        xd.xfaObj = xd.rdr.Resolve(xd.acroForm.Get("XFA"));

        // Single-stream XFA: the entire XDP is in one stream.
        // Parse it, find/create <datasets><data>, replace content, write back.
        if (xd.xfaObj is PdfStream singleStream)
        {
            if (!ReplaceXfaDatasetsInStream(xd, singleStream)) return;
        }

        if (xd.xfaObj is PdfArray xfaArray)
        {
            ReplaceXfaDatasetsInArray(xd, xfaArray);
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
            if (xfaArray[i] is PdfString s && Compat.Latin1.GetString(s.Value) == "datasets"
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
            if (xfaArray[i] is PdfString s && Compat.Latin1.GetString(s.Value) == "postamble")
            { insertIdx = i; break; }
        xfaArray.Insert(insertIdx, new PdfString(Compat.Latin1.GetBytes("datasets")));
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
        var xn = new XfaNodeFindState();
        xn.doc = doc;
        xn.path = path;
        xn.strict = strict;
        xn.parts = SplitSomPath(xn.path);
        xn.current = xn.doc.DocumentElement;

        // First descend into the xfa:data element if present.
        // Prefer the <data> element inside <datasets> (not config's <data>).
        ResolveXfaNodeRoot(xn);

        xn.root = xn.current;
        foreach (var part in xn.parts)
        {
            if (!WalkXfaNodePart(xn, part)) break;
        }
        if (xn.abandoned) return null;

        if (xn.current is not null)
            return xn.current;

        // Fallback: XFA data XML may be flat (template path segments don't map to data hierarchy).
        // Search for the last segment as a descendant of the data root.
        if (xn.root is not null && xn.parts.Length > 0)
        {
            var lastPart = xn.parts[^1];
            var idxMatch = Regex.Match(lastPart, @"^(.+)\[(\d+)\]$");
            var leafName = idxMatch.Success ? idxMatch.Groups[1].Value : lastPart;
            var leafIdx = idxMatch.Success ? int.Parse(idxMatch.Groups[2].Value) : 0;
            // Only data VALUE nodes qualify: a dataGroup (a field sharing its subform's
            // name matches the group here) has an InnerText that concatenates every
            // descendant value — never a field's value. Rich values keep their xhtml
            // children.
            var leaves = xn.root.SelectNodes($".//*[local-name()='{leafName}']")
                ?.OfType<XmlElement>()
                .Where(n => n.GetAttribute("dataNode", "http://www.xfa.org/schema/xfa-data/1.0/") != "dataGroup"
                            && (!n.ChildNodes.OfType<XmlElement>().Any()
                                || n.ChildNodes.OfType<XmlElement>().All(c => c.NamespaceURI == "http://www.w3.org/1999/xhtml")))
                .ToList();
            if (xn.strict && leaves is { Count: > 1 }) return null;
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
