using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Forms;

public sealed partial class Form : ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    /// <summary>
    /// Get an XFA field value by dotted path in the datasets XML.
    /// When direct lookup fails, resolves XFA template data-binding (bind match="dataRef")
    /// to map template field paths to their corresponding data nodes.
    /// </summary>
    public string? GetXfaFieldValue(string path)
    {
        var (_, xml) = GetXfaPart("datasets");
        // Fallback: single-stream XFA
        var reader = ResolvedReader;
        if (xml is null && reader is not null)
        {
            var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
            if (acroForm is not null)
            {
                var xfaObj = reader.Resolve(acroForm.Get("XFA"));
                if (xfaObj is PdfStream singleStream)
                {
                    var data = reader.DecodeStream(singleStream);
                    xml = Encoding.UTF8.GetString(data);
                }
            }
        }
        if (xml is null) return null;
        var result = FindXfaNodeValue(xml, path);
        if (!string.IsNullOrEmpty(result)) return result;

        // Template-based data binding resolution:
        // Walk the template to find the field, resolve <bind match="dataRef" ref="$.xxx"/>
        // and skip presentation-only subforms (those with <bind match="none"/>).
        try
        {
            var templateXml = GetXfaTemplateXml();
            if (templateXml is null) return result;

            var templateDoc = new XmlDocument();
            templateDoc.LoadXml(templateXml);
            if (templateDoc.DocumentElement is null) return result;

            var parts = SplitSomPath(path);
            if (parts.Length < 2) return result;

            // Template name attributes are un-indexed (name="insuredFullName") while a
            // SOM path segment carries its occurrence index (insuredFullName[0]) —
            // strip it for the template walk.
            static string BareSeg(string p)
            {
                var m = Regex.Match(p, @"^(.+)\[(\d+)\]$");
                return m.Success ? m.Groups[1].Value : p;
            }

            // Walk the template by path segments to find the field node
            XmlNode? templateNode = templateDoc.DocumentElement;
            for (int i = 0; i < parts.Length && templateNode is not null; i++)
            {
                templateNode = FindTemplateChild(templateNode, BareSeg(parts[i]));
            }

            if (templateNode is null) return result;

            // Check for <bind match="dataRef" ref="$.xxx"/>
            var bindNode = FindBindElement(templateNode);
            string? bindRef = null;
            if (bindNode is not null)
            {
                var matchAttr = bindNode.Attributes?["match"];
                var refAttr = bindNode.Attributes?["ref"];
                if (matchAttr?.Value == "dataRef" && refAttr?.Value is { } r && r.StartsWith("$."))
                    bindRef = r.Substring(2); // strip "$."
            }

            // Build the data path by walking up, skipping bind="none" subforms
            var dataPathParts = new List<string>();
            for (int i = 0; i < parts.Length - 1; i++) // exclude the field itself
            {
                // Check if this subform is presentation-only (bind match="none")
                XmlNode? checkNode = templateDoc.DocumentElement;
                for (int j = 0; j <= i && checkNode is not null; j++)
                    checkNode = FindTemplateChild(checkNode, BareSeg(parts[j]));

                if (checkNode is not null && HasBindNone(checkNode))
                    continue; // skip presentation-only subform

                dataPathParts.Add(parts[i]);
            }

            // Append the resolved field name (from bind ref or original field name)
            dataPathParts.Add(bindRef ?? parts[^1]);

            var resolvedPath = string.Join(".", dataPathParts);
            if (resolvedPath != path)
            {
                var resolved = FindXfaNodeValue(xml, resolvedPath);
                if (!string.IsNullOrEmpty(resolved)) return resolved;
            }

            // A dataRef's "$" is the field's data CONTEXT — the nearest ANCESTOR
            // subform that actually binds to a data node. Wrapper subforms with no
            // matching data group (common in single-record letter templates, e.g.
            // DocumentTemplateModel > PolicyJacketCoverLetter > field bound to
            // $.Insured.FullName) are transparent to binding, so retry the ref
            // against each shorter ancestor prefix, deepest first.
            if (bindRef is not null)
            {
                for (int k = dataPathParts.Count - 2; k >= 0; k--)
                {
                    var candidate = string.Join(".",
                        dataPathParts.Take(k).Append(bindRef));
                    var resolved = FindXfaNodeValue(xml, candidate);
                    if (!string.IsNullOrEmpty(resolved)) return resolved;
                }
            }
        }
        catch { /* template resolution failed — return original result */ }

        return result;
    }

    /// <summary>Resolve a SOM (template) field path to the corresponding XFA *datasets* path using
    /// the template's bind rules — honour a leaf <c>&lt;bind match="dataRef" ref="$.xxx"/&gt;</c> and
    /// skip presentation-only subforms (<c>&lt;bind match="none"/&gt;</c>). Returns the resolved
    /// dotted data path, or null when the template can't be walked or the path is unchanged. This
    /// mirrors the SOM→data mapping <see cref="GetXfaFieldValue"/> applies on READ; the WRITE path
    /// (<see cref="SetXfaFieldValues"/>) reuses it so a value lands on the same datasets node reading
    /// returns (e.g. a <c>filerName</c> field bound to a <c>&lt;sarx:FilerName&gt;</c> data node).</summary>
    private string? ResolveSomToDataPath(string path)
    {
        try
        {
            var templateXml = GetXfaTemplateXml();
            if (templateXml is null) return null;
            var templateDoc = new XmlDocument();
            templateDoc.LoadXml(templateXml);
            if (templateDoc.DocumentElement is null) return null;

            var parts = SplitSomPath(path);
            if (parts.Length < 2) return null;

            // The template's name attributes are UN-indexed (name="FilerNameSub"), while SOM parts
            // carry an occurrence index (FilerNameSub[0]); strip it for the template walk.
            static string Bare(string p)
            {
                var m = Regex.Match(p, @"^(.+)\[(\d+)\]$");
                return m.Success ? m.Groups[1].Value : p;
            }

            XmlNode? templateNode = templateDoc.DocumentElement;
            for (int i = 0; i < parts.Length && templateNode is not null; i++)
                templateNode = FindTemplateChild(templateNode, Bare(parts[i]));
            if (templateNode is null) return null;

            var bindNode = FindBindElement(templateNode);
            string? bindRef = null;
            if (bindNode is not null)
            {
                var matchAttr = bindNode.Attributes?["match"];
                var refAttr = bindNode.Attributes?["ref"];
                if (matchAttr?.Value == "dataRef" && refAttr?.Value is { } r && r.StartsWith("$."))
                    bindRef = r.Substring(2); // may itself be multi-segment, e.g. "FilingInstitutionInformation.FilerName"
            }

            var dataPathParts = new List<string>();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                XmlNode? checkNode = templateDoc.DocumentElement;
                for (int j = 0; j <= i && checkNode is not null; j++)
                    checkNode = FindTemplateChild(checkNode, Bare(parts[j]));
                if (checkNode is not null && HasBindNone(checkNode)) continue; // presentation-only subform: not in data
                dataPathParts.Add(parts[i]);
            }
            dataPathParts.Add(bindRef ?? parts[^1]);

            var resolvedPath = string.Join(".", dataPathParts);
            return resolvedPath != path ? resolvedPath : null;
        }
        catch { return null; }
    }

    /// <summary>Find a subform or field child by name in an XFA template node.</summary>
    private static XmlNode? FindTemplateChild(XmlNode parent, string name)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var ln = child.LocalName;
            if (ln is "subform" or "field" or "exclGroup" or "draw")
            {
                if (child.Attributes?["name"]?.Value == name)
                    return child;
            }
        }
        // Also search inside unnamed subforms
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "subform" && child.Attributes?["name"] is null)
            {
                var found = FindTemplateChild(child, name);
                if (found is not null) return found;
            }
        }
        return null;
    }

    /// <summary>Find a &lt;bind&gt; element within a template field/subform.</summary>
    private static XmlNode? FindBindElement(XmlNode node)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element && child.LocalName == "bind")
                return child;
        }
        return null;
    }

    /// <summary>Check if a template subform has bind match="none" (presentation-only).</summary>
    private static bool HasBindNone(XmlNode node)
    {
        var bind = FindBindElement(node);
        return bind?.Attributes?["match"]?.Value == "none";
    }

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

    /// <summary>Mark the catalog's /AcroForm object dirty so an in-place edit of its
    /// /XFA array (a newly-added datasets part) is re-serialised on save.</summary>
    private void MarkAcroFormDirty()
    {
        var rdr = ResolvedReader;
        if (OwnerDocument is null || rdr is null) return;
        if (rdr.Catalog.Get("AcroForm") is not PdfIndirectRef acroRef) return;
        var acroDict = rdr.ResolveDict(acroRef);
        if (acroDict is not null)
            OwnerDocument.MarkDirty(acroRef.ObjectNumber, acroDict);
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

    /// <summary>
    /// Set an XFA field value by dotted path in the datasets XML.
    /// </summary>
    public void SetXfaFieldValue(string path, string value)
        => SetXfaFieldValues(new[] { new KeyValuePair<string, string>(path, value) });

    /// <summary>For a static XFA form, copy each AcroForm terminal field's current
    /// value into the XFA datasets, keyed by the field's fully-qualified name, so
    /// the datasets (and <see cref="XFA"/>[field]) stay in sync with values set
    /// through the typed field API. Called automatically before save. Dynamic XFA
    /// forms (whose data is driven by the template) are left untouched.</summary>
    /// <summary>Snapshot the current XFA datasets as a full-path → value map, for
    /// checkbox on/off-token preservation during <see cref="SyncAcroFormToXfa"/>.</summary>
    private Dictionary<string, string> BuildDatasetsValueMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in GetXfaDatasetsFields()) map[kv.Key] = kv.Value;
        return map;
    }

    /// <summary>Push every XFA datasets leaf value into its matching AcroForm field
    /// (static XFA forms only) so the widget representation reflects data that was
    /// replaced wholesale in the datasets (e.g. by <c>ImportXml</c>). Without this the
    /// AcroForm fields keep their old values and the save-time
    /// <see cref="SyncAcroFormToXfa"/> would push those stale values back over the
    /// freshly-imported datasets (notably clobbering checkbox "1"/"0" with "Off").</summary>
    internal void SyncXfaToAcroForm()
    {
        if (Type != FormType.Static) return;
        foreach (var kv in GetXfaDatasetsFields())
            ApplyXfaValueToAcroField(kv.Key, kv.Value);
        // Name-walk matching above only covers forms whose datasets tree mirrors the
        // field hierarchy. Designer forms with a data connection bind fields
        // EXPLICITLY (<bind match="dataRef" ref="$record...">) to a foreign-shaped
        // record — resolve those against the datasets and push the values too.
        ApplyTemplateDataRefBindings();
    }

    /// <summary>Resolve the template's explicit data bindings — every field /
    /// exclGroup carrying <c>&lt;bind match="dataRef" ref="$record…"/&gt;</c> — against
    /// the datasets and push each resolved value into the matching AcroForm field.
    /// Static-XFA widget names mirror the template tree, so a template field is paired
    /// with the acro field whose LEAF name and occurrence index ([n]) match the
    /// template's same-named fields in document order.</summary>
    private void ApplyTemplateDataRefBindings()
    {
        var templateXml = GetXfaTemplateXml();
        var datasetsXml = GetXfaDatasetsXml();
        if (string.IsNullOrEmpty(templateXml) || string.IsNullOrEmpty(datasetsXml)) return;
        XmlNode? dataNode;
        var refsByLeaf = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
        try
        {
            var tplDoc = new XmlDocument();
            tplDoc.LoadXml(templateXml);
            if (tplDoc.DocumentElement is null) return;
            CollectTemplateDataRefs(tplDoc.DocumentElement, refsByLeaf);
            if (refsByLeaf.Count == 0) return;

            var dsDoc = new XmlDocument();
            dsDoc.LoadXml(datasetsXml);
            dataNode = FindDatasetsDataNode(dsDoc);
        }
        catch { return; }
        if (dataNode is null) return;

        foreach (var field in _fields)
        {
            var full = field.FullName;
            if (string.IsNullOrEmpty(full)) continue;
            // Leaf segment ("Medewerker_Naam[1]") → name + occurrence index.
            var leaf = full;
            var dot = leaf.LastIndexOf('.');
            if (dot >= 0) leaf = leaf[(dot + 1)..];
            var occurrence = 0;
            var br = leaf.IndexOf('[');
            if (br >= 0)
            {
                var close = leaf.IndexOf(']', br);
                if (close > br) int.TryParse(leaf[(br + 1)..close], out occurrence);
                leaf = leaf[..br];
            }
            if (!refsByLeaf.TryGetValue(leaf, out var refs)) continue;
            var dataRef = occurrence < refs.Count ? refs[occurrence] : refs[^1];
            if (string.IsNullOrEmpty(dataRef)) continue;
            var value = ResolveXfaDataRef(dataNode, dataRef!);
            if (string.IsNullOrEmpty(value)) continue;
            ApplyXfaValueToAcroField(full, value!);
        }
    }

    /// <summary>Collect, per template field leaf NAME (document order), the field's
    /// explicit dataRef bind reference (null when the field has no dataRef bind).
    /// exclGroups count as leaves (their radio kids are not walked).</summary>
    private static void CollectTemplateDataRefs(XmlNode node, Dictionary<string, List<string?>> map)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName is "field" or "exclGroup")
            {
                var name = (child as XmlElement)?.GetAttribute("name");
                if (!string.IsNullOrEmpty(name))
                {
                    string? dataRef = null;
                    foreach (XmlNode c in child.ChildNodes)
                    {
                        if (c.LocalName != "bind" || c is not XmlElement bind) continue;
                        if (bind.GetAttribute("match") == "dataRef")
                        {
                            var r = bind.GetAttribute("ref");
                            if (!string.IsNullOrEmpty(r)) dataRef = r;
                        }
                        break;
                    }
                    if (!map.TryGetValue(name!, out var list)) map[name!] = list = new List<string?>();
                    list.Add(dataRef);
                }
                continue;
            }
            CollectTemplateDataRefs(child, map);
        }
    }

    /// <summary>Resolve a template bind reference (<c>$record.A.B</c>, <c>$data.A.B</c>,
    /// or a bare relative <c>A.B</c>) against the datasets <c>&lt;xfa:data&gt;</c> node.
    /// <c>$record</c> is the first element child of the data node (the record root).
    /// Segments may carry an occurrence index (<c>Name[2]</c>).</summary>
    private static string? ResolveXfaDataRef(XmlNode dataNode, string dataRef)
    {
        static XmlNode? FirstElementChild(XmlNode n)
        {
            foreach (XmlNode c in n.ChildNodes)
                if (c.NodeType == XmlNodeType.Element) return c;
            return null;
        }

        var path = dataRef.Trim();
        XmlNode? cur;
        if (path.StartsWith("$record.", StringComparison.Ordinal))
        {
            cur = FirstElementChild(dataNode);
            path = path["$record.".Length..];
        }
        else if (path.StartsWith("$data.", StringComparison.Ordinal))
        {
            cur = dataNode;
            path = path["$data.".Length..];
        }
        else if (path.StartsWith("$", StringComparison.Ordinal))
        {
            return null; // other pseudo-roots ($form, $host, …) are not data paths
        }
        else
        {
            cur = FirstElementChild(dataNode);
        }
        if (cur is null) return null;

        foreach (var rawSeg in path.Split('.'))
        {
            var seg = rawSeg;
            var idx = 0;
            var br = seg.IndexOf('[');
            if (br >= 0)
            {
                var close = seg.IndexOf(']', br);
                if (close > br) int.TryParse(seg[(br + 1)..close], out idx);
                seg = seg[..br];
            }
            if (seg.Length == 0) return null;
            XmlNode? next = null;
            var seen = 0;
            foreach (XmlNode c in cur.ChildNodes)
            {
                if (c.NodeType != XmlNodeType.Element || c.LocalName != seg) continue;
                if (seen++ == idx) { next = c; break; }
            }
            if (next is null) return null;
            cur = next;
        }
        return cur.InnerText;
    }

    internal void SyncAcroFormToXfa()
    {
        if (Type != FormType.Static) return;
        var pairs = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string>? existingDs = null;
        foreach (var field in _fields)
        {
            // Only terminal value-bearing fields map to a datasets leaf. Subform /
            // container nodes (the base Field hierarchy entries) carry no value, and
            // writing an empty value to a container path would wipe its whole subtree.
            // CheckboxField.Value is a `new` shadow → dispatch explicitly; the others
            // override Value and resolve through the base reference.
            string? val;
            switch (field)
            {
                case CheckboxField cb:
                    // An arbitrary non-state value assigned to an XFA checkbox
                    // (e.g. Field.Value = "1234") is stored VERBATIM in the datasets,
                    // even though the AcroForm appearance normalised it to "Off".
                    if (cb.RawNonStateValue is string rawCb)
                    {
                        val = rawCb;
                        break;
                    }
                    // Preserve the datasets' own on/off token (XFA forms conventionally
                    // bind "1"/"0") when the checkbox state already agrees with it — only
                    // overwrite on a genuine state change. Otherwise the AcroForm off
                    // export-name ("Off") would clobber an imported "0".
                    var cbName = field.FullName;
                    existingDs ??= BuildDatasetsValueMap();
                    if (cbName is not null && existingDs.TryGetValue(cbName, out var curVal))
                    {
                        bool curOn = !(string.IsNullOrEmpty(curVal) || curVal == "0"
                            || curVal.Equals("Off", StringComparison.OrdinalIgnoreCase));
                        if (curOn == cb.Checked) continue; // datasets token already matches → keep it
                    }
                    val = cb.Value;
                    break;
                case ChoiceField ch:
                    // Resolve the canonical group field (a radio kid instance carries
                    // no /Opt list) and use the selected option's export value — the
                    // field's own /V can lag the selection for radio groups.
                    var group = FindFieldOrNull(ch.FullName ?? "") as ChoiceField ?? ch;
                    var sel = group.Selected;
                    val = sel >= 1 && sel <= group.Options.Count ? group.Options[sel].Value : group.Value;
                    break;
                case TextBoxField:
                    val = field.Value;
                    break;
                default:
                    val = null;
                    break;
            }
            if (val is null) continue;
            var name = field.FullName;
            if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
            pairs.Add(new KeyValuePair<string, string>(name, val));
        }
        if (pairs.Count > 0) SetXfaFieldValues(pairs);
    }

    /// <summary>For a static XFA form, push a value written to the XFA datasets
    /// (via <see cref="XFA"/>[field]) into the matching AcroForm field so the two
    /// representations stay in sync. Text fields take the value verbatim; choice
    /// fields select the option whose export value matches; a checkbox is checked
    /// unless the value is empty / "0" / "Off".</summary>
    internal void ApplyXfaValueToAcroField(string name, string value)
    {
        if (Type != FormType.Static || string.IsNullOrEmpty(name)) return;
        switch (FindFieldOrNull(name))
        {
            case CheckboxField cb:
                cb.Checked = !(string.IsNullOrEmpty(value) || value == "0" || value == "Off");
                break;
            case ChoiceField ch:
                for (int i = 1; i <= ch.Options.Count; i++)
                {
                    if (ch.Options[i].Value == value) { ch.Selected = i; break; }
                }
                break;
            case TextBoxField tb:
                // Honour the field's /MaxLen: an imported value longer than the
                // field allows is truncated to fit (as a viewer would on entry).
                tb.Value = tb.MaxLen > 0 && value is not null && value.Length > tb.MaxLen
                    ? value.Substring(0, tb.MaxLen)
                    : value;
                break;
        }
    }

    /// <summary>Set several XFA field values in one datasets parse/serialise cycle.
    /// Much cheaper than calling <see cref="SetXfaFieldValue"/> per field when
    /// importing a whole form.</summary>
    public void SetXfaFieldValues(IReadOnlyList<KeyValuePair<string, string>> values)
    {
        if (values is null || values.Count == 0) return;
        var (stream, xml) = GetXfaPart("datasets");
        if (xml is null || stream is null)
        {
            // Fallback: XFA might be a single stream (not an array with named parts)
            var reader = ResolvedReader;
            if (reader is not null)
            {
                var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
                if (acroForm is not null && reader.Resolve(acroForm.Get("XFA")) is PdfStream singleStream)
                {
                    var data = reader.DecodeStream(singleStream);
                    xml = Encoding.UTF8.GetString(data);
                    stream = singleStream;
                }
            }
        }
        if (xml is null || stream is null)
        {
            // XFA is an array with no "datasets" part (a template-only dynamic form):
            // create an empty datasets packet and wire it into the /XFA array so the
            // value has somewhere to persist.
            stream = EnsureXfaDatasetsStreamInArray();
            if (stream is not null) xml = Encoding.UTF8.GetString(stream.RawData);
        }
        if (xml is null || stream is null) return;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var changed = false;
            foreach (var pair in values)
            {
                // Prefer the field's own SOM path; if that names no existing datasets node, fall
                // back to the template-resolved data path (honours a <bind match="dataRef"> + skips
                // bind="none" subforms — the SAME mapping the read path uses) BEFORE creating a new
                // node, so a bound field (e.g. filerName → <sarx:FilerName>) lands on the node the
                // reader returns rather than spawning a stray sibling.
                var node = FindXfaNode(doc, pair.Key);
                if (node is null && ResolveSomToDataPath(pair.Key) is { } resolved)
                    node = FindXfaNode(doc, resolved);
                node ??= CreateXfaNodePath(doc, pair.Key);
                if (node is null) continue;
                node.InnerText = pair.Value;
                changed = true;
            }
            if (!changed) return;

            // Write the modified XML back to the stream (uncompressed).
            using var ms = new MemoryStream();
            SaveXmlNoBom(doc, ms);
            var newData = ms.ToArray();
            stream.ReplaceData(newData);
            stream.Dict.Set("Length", new PdfInteger(newData.Length));
            stream.Dict.Remove("Filter");
            MarkXfaStreamDirty(stream);
        }
        catch { }
    }

    /// <summary>Write a base64 image into the XFA datasets node for
    /// <paramref name="path"/> and tag it with the given <paramref name="contentType"/>
    /// (e.g. <c>image/jpg</c>) — how XFA image fields carry their picture. Returns
    /// false when there is no datasets packet or the node can't be resolved.</summary>
    public bool SetXfaFieldImage(string path, string base64, string contentType)
    {
        var (stream, xml) = GetXfaPart("datasets");
        if (xml is null || stream is null)
        {
            // Fallback: XFA might be a single stream (not an array with named parts)
            var reader = ResolvedReader;
            if (reader is not null)
            {
                var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
                if (acroForm is not null && reader.Resolve(acroForm.Get("XFA")) is PdfStream singleStream)
                {
                    xml = Encoding.UTF8.GetString(reader.DecodeStream(singleStream));
                    stream = singleStream;
                }
            }
        }
        if (xml is null || stream is null) return false;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            if ((FindXfaNode(doc, path) ?? CreateXfaNodePath(doc, path)) is not XmlElement el)
                return false;
            el.InnerText = base64;
            el.SetAttribute("contentType", contentType);

            using var ms = new MemoryStream();
            SaveXmlNoBom(doc, ms);
            var newData = ms.ToArray();
            stream.ReplaceData(newData);
            stream.Dict.Set("Length", new PdfInteger(newData.Length));
            stream.Dict.Remove("Filter");
            MarkXfaStreamDirty(stream);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Mark an XFA stream as dirty so incremental save includes it.
    /// Scans the xref table to find the object number for the stream.
    /// </summary>
    private void MarkXfaStreamDirty(PdfStream stream)
    {
        var dirtyReader = ResolvedReader;
        if (OwnerDocument is null || dirtyReader is null) return;
        foreach (var entry in dirtyReader.XRefTable.Entries.Values)
        {
            var resolved = dirtyReader.Resolve(
                new PdfIndirectRef(entry.ObjectNumber, 0));
            if (ReferenceEquals(resolved, stream))
            {
                OwnerDocument.MarkDirty(entry.ObjectNumber, stream);
                return;
            }
            // Also check if the resolved object's Dict matches the stream's Dict
            if (resolved is PdfStream s && ReferenceEquals(s.Dict, stream.Dict))
            {
                OwnerDocument.MarkDirty(entry.ObjectNumber, stream);
                return;
            }
        }
    }

    private static XmlNode? FindXfaNode(XmlDocument doc, string path)
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
                if (idx < nodes.Count) current = nodes[idx];
                else current = nodes.Count > 0 ? nodes[nodes.Count - 1] : null;
            }
            else
            {
                var nodes = FindChildrenByLocalName(current, part);
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

    private static string? FindXfaNodeValue(string xml, string path)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var node = FindXfaNode(doc, path);
            return node?.InnerText;
        }
        catch { return null; }
    }

    /// <summary>XFA accessor exposing the underlying XML packets (template,
    /// datasets, …). Returns <c>null</c> when the form has no XFA part —
    /// so callers can branch on
    /// <c>Form.XFA is null</c>.
    /// </summary>
    public XFA? XFA => IsXfa ? (_xfa ??= new XFA(this)) : null;

    private XFA? _xfa;

    private PdfReader? _reader;

    private PdfDictionary? _acroForm;

    /// <summary>The raw AcroForm dictionary (facades register /DR fonts through it).</summary>
    internal PdfDictionary? AcroFormDict => _acroForm;

    /// <summary>Register a simple font in the AcroForm /DR /Font under
    /// <paramref name="resourceName"/> (no-op when already present).</summary>
    internal void RegisterDefaultResourceFont(string resourceName, string baseFont, string subtype = "Type1")
    {
        if (_acroForm is null) return;
        EnsureDefaultResources(_acroForm);
        if ((_acroForm.Get("DR") as PdfDictionary)?.Get("Font") is not PdfDictionary fontDict) return;
        if (fontDict.ContainsKey(resourceName)) return;
        var f = new PdfDictionary();
        f.Set("Type", new PdfName("Font"));
        f.Set("Subtype", new PdfName(subtype));
        f.Set("BaseFont", new PdfName(baseFont));
        if (subtype == "Type1")
            f.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set(resourceName, f);
        // Refresh the cached DefaultResources wrapper so readers see the new font.
        if (_reader is not null && _acroForm.Get("DR") is PdfDictionary dr2)
            DefaultResources = new Aspose.Pdf.Resources(dr2, _reader);
    }

    internal void SetReader(PdfReader reader) => _reader = reader;

    /// <summary>Resolve the AcroForm dictionary backing this form (preferring the
    /// one captured at load time, then the document catalog's /AcroForm).</summary>
    private PdfDictionary? ResolveAcroForm()
    {
        if (_acroForm is not null) return _acroForm;
        var reader = _reader ?? OwnerDocument?.Reader;
        return reader is null ? null : reader.ResolveDict(reader.Catalog.Get("AcroForm"));
    }

    /// <summary>
    /// Resolve the PDF reader: prefer explicitly set reader, fall back to OwnerDocument's reader.
    /// </summary>
    private PdfReader? ResolvedReader => _reader ?? OwnerDocument?.Reader;

    /// <summary>
    /// Flatten all form fields — render their visual appearance into page content
    /// and remove the interactive form. After flattening, fields are no longer editable.
    /// Uses the owning document from the form dictionary.
    /// </summary>
    public void Flatten()
    {
        var doc = _ownerDocument ?? throw new InvalidOperationException("Form is not associated with a Document.");
        Flatten(doc);
    }

    /// <summary>
    /// Flatten all form fields — render their visual appearance into page content
    /// and remove the interactive form. After flattening, fields are no longer editable.
    /// </summary>
    public void Flatten(Document document)
    {
        Flatten(document, settings: null);
    }

    /// <summary>Settings-aware overload exposed to Document.Flatten(settings).</summary>
    internal void FlattenWithSettings(Document document, FlattenSettings? settings)
        => Flatten(document, settings);

    /// <summary>Internal entry point that honours <paramref name="settings"/>.
    /// When settings.UpdateAppearances is true (or the flag is unspecified —
    /// Flatten() always refreshes appearances
    /// from the current field values) each field's /AP/N is rebuilt from its
    /// current /V before the page's widgets are folded into the page content.
    /// Without this, a flatten of a PDF whose fields were programmatically
    /// re-valued shows the original (stale) appearance.</summary>
    internal void Flatten(Document document, FlattenSettings? settings)
        => Flatten(document, settings, frmStartIndex: 0, flattenNonWidgets: false);
}
