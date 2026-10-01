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
    /// <summary>Descends one path part: an indexed or named child of the current node, or a named descendant when the walk is loose.</summary>
    private static bool WalkXfaNodePart(XfaNodeFindState xn, string part)
    {
        if (xn.current is null) return false;
        var match = Regex.Match(part, @"^(.+)\[(\d+)\]$");
        if (match.Success)
        {
            var name = match.Groups[1].Value;
            var idx = int.Parse(match.Groups[2].Value);
            var nodes = FindChildrenByLocalName(xn.current, name);
            // XFA occurrence binding: when the template repeats a field name
            // (Season[0], Season[1]) but the datasets carries fewer data nodes, the
            // surplus instances bind to the existing (often single) node rather than
            // resolving to nothing. Fall back to the last available node when the
            // requested index is out of range. An in-range index is unchanged, so
            // fields that DO carry one node per instance still resolve distinctly.
            if (xn.strict && nodes.Count > 1) { xn.abandoned = true; return false; }
            if (idx < nodes.Count) xn.current = nodes[idx];
            else xn.current = nodes.Count > 0 ? nodes[nodes.Count - 1] : null;
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
            foreach (XmlNode c in xn.current.ChildNodes)
                if (c.NodeType == XmlNodeType.Element && c.LocalName == part) direct.Add(c);
            if (direct.Count == 0 && xn.current.Attributes?[part] is { } attr)
            {
                xn.current = attr;
                return true;
            }
            var nodes = direct.Count > 0 ? direct : FindChildrenByLocalName(xn.current, part);
            if (xn.strict && nodes.Count > 1) { xn.abandoned = true; return false; }
            xn.current = nodes.Count > 0 ? nodes[0] : null;
        }
        return true;
    }

    /// <summary>Resolves the walk's starting node from the path head: the template or datasets subtree, the form packet, or the named root element.</summary>
    private static void ResolveXfaNodeRoot(XfaNodeFindState xn)
    {
        if (xn.current is not null)
        {
            XmlNode? dataNode = null;
            // Try to find <datasets>/<data> first
            var datasetsNode = xn.current.SelectSingleNode("//*[local-name()='datasets']");
            if (datasetsNode is not null)
                dataNode = datasetsNode.SelectSingleNode("*[local-name()='data']");
            // Fallback: find <data> that contains form-like children (not config <data>)
            if (dataNode is null)
            {
                var allData = xn.current.SelectNodes("//*[local-name()='data']");
                if (allData is not null) dataNode = PickXfaDataNode(allData, xn.parts);
            }
            if (dataNode is not null) xn.current = dataNode;
        }
    }

    /// <summary>Which of the document's &lt;data&gt; elements holds the form data: the one under
    /// &lt;datasets&gt;, else the first whose children match the path's first part, else the
    /// first of all.</summary>
    private static XmlNode? PickXfaDataNode(XmlNodeList allData, string[] parts)
    {
        foreach (XmlNode d in allData)
        {
            // Skip config <data> — it has adjustData, xsl etc. as children
            // The real XFA data has form-field-like children
            if (d.ParentNode is not null && d.ParentNode.LocalName == "datasets") return d;
        }
        // Last resort: use first <data> with child elements matching path start
        if (allData.Count > 0)
        {
            var firstPart = parts[0];
            var partMatch = Regex.Match(firstPart, @"^(.+)\[(\d+)\]$");
            var partName = partMatch.Success ? partMatch.Groups[1].Value : firstPart;
            foreach (XmlNode d in allData)
            {
                if (FindChildrenByLocalName(d, partName).Count > 0) return d;
            }
        }
        return allData.Count > 0 ? allData[0] : null;
    }
}
