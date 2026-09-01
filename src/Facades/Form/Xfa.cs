using System.Text;
using System.Xml;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

public sealed partial class Form
{
    private void ImportXmlXfa(XmlDocument xml)
    {
        // For XFA forms, replace the entire datasets with the imported XML
        var xfaForm = _doc!.Form;
        xfaForm.ReplaceXfaDatasets(xml);
        // Push the imported values into the AcroForm widgets (static XFA) so the two
        // representations agree — otherwise the save-time AcroForm→XFA sync writes the
        // stale widget state back over the imported datasets.
        xfaForm.SyncXfaToAcroForm();
    }

    private static void ImportXfaNode(XmlNode node, string path, Forms.Form form)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;

            var childPath = string.IsNullOrEmpty(path) ? child.LocalName : path + "." + child.LocalName;

            if (child.HasChildNodes && child.ChildNodes.Count == 1 && child.FirstChild!.NodeType == XmlNodeType.Text)
            {
                // Leaf value — try to set XFA field
                try { form.SetXfaFieldValue(childPath, child.InnerText); } catch { }
            }
            else
            {
                ImportXfaNode(child, childPath, form);
            }
        }
    }

    private void ExportXmlXfa(Stream output)
    {
        var form = _doc!.Form;
        var datasetsXml = form.GetXfaDatasetsXml();
        // If the datasets carry real form data (from an import/fill), export it directly.
        if (datasetsXml is not null)
        {
            try
            {
                var dsDoc = new XmlDocument();
                dsDoc.LoadXml(datasetsXml);

                // Locate a GENUINE XFA data root: the <data> element inside <datasets>
                // (xfa-data namespace), or a bare <data>/<datasets> root. GetXfaDatasetsXml
                // can return the whole XDP (preamble/config/template/. concatenated) when the
                // form has no datasets packet — that is presentation metadata, NOT form data,
                // so we must only take the rich path when a real <datasets>/<data> node exists;
                // otherwise we fall through to building the export from the template below.
                XmlNode? datasetsNode = dsDoc.SelectSingleNode("//*[local-name()='datasets']");
                XmlNode? dataNode = datasetsNode?.SelectSingleNode("*[local-name()='data']")
                    ?? (dsDoc.DocumentElement?.LocalName == "data" ? dsDoc.DocumentElement : null);

                XmlElement? dataRoot = null;
                if (dataNode is not null)
                {
                    foreach (XmlNode c in dataNode.ChildNodes)
                        if (c is XmlElement el) { dataRoot = el; break; }
                }

                // Only export the datasets verbatim when they carry a RICH, foreign
                // structure (an imported document root the template can't reproduce,
                // e.g. <us-request> with many nested elements). A sparse data root —
                // a few filled fields (FillField) or a form-shaped import —
                // must instead be merged onto the template below so the export carries
                // the COMPLETE field structure (all fields, empty ones included) and
                // any CDATA/text values (collected via InnerText) survive.
                int dataElementCount = 0;
                if (dataRoot is not null)
                    foreach (var _ in DescendantElements(dataRoot)) { if (++dataElementCount > 5) break; }

                if (dataRoot is not null && dataElementCount > 5)
                {
                    // Export with xfa:data wrapper
                    var settings = new XmlWriterSettings
                    {
                        Indent = true,
                        OmitXmlDeclaration = false,
                        Encoding = Encoding.UTF8
                    };
                    using var writer = XmlWriter.Create(output, settings);
                    writer.WriteStartElement("xfa", "data",
                        "http://www.xfa.org/schema/xfa-data/1.0/");
                    // Write the data root element (typically the imported XML's root,
                    // e.g. <us-request>). Preserves the document structure expected by callers.
                    ExportXfaNodeClean(dataRoot, writer);
                    writer.WriteEndElement(); // xfa:data
                    writer.Flush();
                    return;
                }
            }
            catch { }
        }

        // Datasets are sparse/empty — build complete XML from template + dataset values
        var templateXml = form.GetXfaTemplateXml();
        if (templateXml is null) { ExportXmlAcroForm(output); return; }

        var dataValues = new Dictionary<string, string>(StringComparer.Ordinal);
        if (datasetsXml is not null)
        {
            try
            {
                var dsDoc = new XmlDocument();
                dsDoc.LoadXml(datasetsXml);
                CollectDataValues(dsDoc.DocumentElement, "", dataValues);
            }
            catch { }
        }

        try
        {
            var tmplDoc = new XmlDocument();
            tmplDoc.LoadXml(templateXml);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = Encoding.UTF8
            };
            using var writer = XmlWriter.Create(output, settings);
            BuildXfaExportXml(tmplDoc.DocumentElement!, "", writer, dataValues);
            writer.Flush();
        }
        catch
        {
            ExportXmlAcroForm(output);
        }
    }

    private static IEnumerable<XmlElement> DescendantElements(XmlElement root)
    {
        foreach (XmlNode child in root.ChildNodes)
        {
            if (child is XmlElement el)
            {
                yield return el;
                foreach (var desc in DescendantElements(el))
                    yield return desc;
            }
        }
    }

    private static void ExportXfaNodeClean(XmlElement element, XmlWriter writer)
    {
        // Force the empty namespace so children of <xfa:data> don't inherit
        // the xfa prefix — XPath callers expect plain element names like
        // //form1/TextField1, not //xfa:form1/xfa:TextField1.
        writer.WriteStartElement(element.LocalName, string.Empty);
        foreach (XmlAttribute attr in element.Attributes)
        {
            if (attr.Prefix == "xmlns" || attr.Name == "xmlns") continue;
            writer.WriteAttributeString(attr.LocalName, attr.Value);
        }
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement childElement)
                ExportXfaNodeClean(childElement, writer);
            else if (child is XmlCharacterData cdata
                     && (child.NodeType == XmlNodeType.Text || child.NodeType == XmlNodeType.CDATA))
                writer.WriteString(cdata.Value ?? "");
        }
        writer.WriteEndElement();
    }

    /// <summary>Collect all leaf values from the datasets XML into a path→value map.</summary>
    private static void CollectDataValues(XmlNode? node, string path, Dictionary<string, string> values)
    {
        if (node is null) return;

        // Skip the datasets/data wrapper — descend to the data content root
        if (node.LocalName is "datasets")
        {
            foreach (XmlNode c in node.ChildNodes)
                if (c.NodeType == XmlNodeType.Element && c.LocalName == "data")
                    { CollectDataValues(c, path, values); return; }
            return;
        }

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var childPath = string.IsNullOrEmpty(path) ? child.LocalName : $"{path}/{child.LocalName}";

            bool hasElementChildren = false;
            foreach (XmlNode gc in child.ChildNodes)
                if (gc.NodeType == XmlNodeType.Element) { hasElementChildren = true; break; }

            if (hasElementChildren)
                CollectDataValues(child, childPath, values);
            else
                values[childPath] = child.InnerText ?? "";
        }
    }

    /// <summary>Build XFA export XML by walking the template and writing elements for each subform/field.</summary>
    private static void BuildXfaExportXml(XmlNode templateNode, string dataPath,
        XmlWriter writer, Dictionary<string, string> dataValues)
    {
        // Find the root subform (e.g. <subform name="form1">)
        foreach (XmlNode child in templateNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var localName = child.LocalName;
            var nameAttr = child.Attributes?["name"]?.Value;

            if (localName is "subform" or "subformSet" or "area")
            {
                if (nameAttr is not null)
                {
                    var childPath = string.IsNullOrEmpty(dataPath) ? nameAttr : $"{dataPath}/{nameAttr}";
                    writer.WriteStartElement(nameAttr);
                    BuildXfaExportXml(child, childPath, writer, dataValues);
                    writer.WriteEndElement();
                }
                else
                {
                    BuildXfaExportXml(child, dataPath, writer, dataValues);
                }
            }
            else if (localName == "field"
                || (localName == "draw" && nameAttr is not null
                    && templateNode.Attributes?["layout"]?.Value is not "row" and not "table"))
            {
                if (nameAttr is not null)
                {
                    var fieldPath = string.IsNullOrEmpty(dataPath) ? nameAttr : $"{dataPath}/{nameAttr}";
                    writer.WriteStartElement(nameAttr);
                    if (dataValues.TryGetValue(fieldPath, out var val))
                        writer.WriteString(val);
                    writer.WriteEndElement();
                }
            }
            else if (localName is "exclGroup")
            {
                if (nameAttr is not null)
                {
                    var fieldPath = string.IsNullOrEmpty(dataPath) ? nameAttr : $"{dataPath}/{nameAttr}";
                    writer.WriteStartElement(nameAttr);
                    if (dataValues.TryGetValue(fieldPath, out var val))
                        writer.WriteString(val);
                    writer.WriteEndElement();
                }
            }
            else
            {
                // Recurse into other template elements (e.g., pageArea, contentArea)
                BuildXfaExportXml(child, dataPath, writer, dataValues);
            }
        }
    }

    /// <summary>Build the XFA data tree (root e.g. &lt;form1&gt; with one child per
    /// field/subform) from the template, filling values from the datasets packet.
    /// Returns null when no XFA template is present.</summary>
    private XmlDocument? BuildXfaDataDocument()
    {
        var form = _doc!.Form;
        var templateXml = form.GetXfaTemplateXml();
        if (templateXml is null) return null;

        var dataValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var datasetsXml = form.GetXfaDatasetsXml();
        if (datasetsXml is not null)
        {
            try
            {
                var ds = new XmlDocument();
                ds.LoadXml(datasetsXml);
                CollectDataValues(ds.DocumentElement, "", dataValues);
            }
            catch { }
        }

        try
        {
            var tmpl = new XmlDocument();
            tmpl.LoadXml(templateXml);
            using var ms = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = true,
                Encoding = new UTF8Encoding(false),
            };
            using (var w = XmlWriter.Create(ms, settings))
            {
                BuildXfaExportXml(tmpl.DocumentElement!, "", w, dataValues);
                w.Flush();
            }
            var dataDoc = new XmlDocument();
            dataDoc.LoadXml(Encoding.UTF8.GetString(ms.ToArray()));
            return dataDoc;
        }
        catch { return null; }
    }

    private static List<XmlElement> ElementChildren(XmlNode node)
    {
        var list = new List<XmlElement>();
        foreach (XmlNode c in node.ChildNodes)
            if (c is XmlElement el) list.Add(el);
        return list;
    }

    private void ExportFdfXfa(Stream output)
    {
        var dataDoc = BuildXfaDataDocument();
        var fields = XfaFieldPathsNorm();
        var sb = new StringBuilder();
        sb.Append("%FDF-1.2\n1 0 obj\n<< /FDF << /Fields [\n");
        if (dataDoc?.DocumentElement is not null)
            EmitFdfLeaves(dataDoc.DocumentElement, sb, fields);
        sb.Append("] >> >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
        var bytes = Encoding.Latin1.GetBytes(sb.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Emit a flat <c>/T(leafName[0]) /V(value)</c> FDF entry for each
    /// leaf field (an element with no element children). When <paramref name="fields"/>
    /// is non-null, a leaf that isn't a genuine XFA field (an empty subform/draw the
    /// template build left behind) is skipped so the export carries only bound fields.</summary>
    private static void EmitFdfLeaves(XmlElement element, StringBuilder sb, List<string>? fields)
    {
        var children = ElementChildren(element);
        if (children.Count == 0)
        {
            if (fields is not null && !IsKnownXfaField(element.LocalName, fields)) return;
            sb.Append("  << /T(");
            sb.Append(EscapeFdf(element.LocalName + "[0]"));
            sb.Append(") /V(");
            sb.Append(EscapeFdf(element.InnerText));
            sb.Append(") >>\n");
            return;
        }
        foreach (var child in children)
            EmitFdfLeaves(child, sb, fields);
    }

    private static string EscapeFdf(string s)
        => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private void ExportXfdfXfa(Stream output)
    {
        // Template-driven structure (unbound template fields stay present as empty
        // self-closing entries), expanded
        // per DATASETS instance: a subform the datasets hold N times is emitted N
        // times (name="S[0]" … "S[N-1]"), each instance carrying its own values —
        // a flat template walk would collapse repeated subforms into one node with
        // whichever instance's values were visited last.
        const string ns = "http://ns.adobe.com/xfdf/";
        var doc = new XmlDocument();
        var xfdf = doc.CreateElement("xfdf", ns);
        doc.AppendChild(xfdf);
        var fieldsEl = doc.CreateElement("fields", ns);
        xfdf.AppendChild(fieldsEl);

        var templateXml = _doc!.Form.GetXfaTemplateXml();
        if (templateXml is not null)
        {
            try
            {
                var tmpl = new XmlDocument();
                tmpl.LoadXml(templateXml);
                if (tmpl.DocumentElement is not null)
                    EmitXfdfLevel(tmpl.DocumentElement, LoadDatasetsDataRoot(), fieldsEl, ns);
            }
            catch { }
        }

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
        };
        using var w = XmlWriter.Create(output, settings);
        doc.Save(w);
    }

    /// <summary>The datasets packet's <c>xfa:data</c> element (or the packet root when
    /// no data wrapper exists), null when the document has no datasets.</summary>
    private XmlElement? LoadDatasetsDataRoot()
    {
        var datasetsXml = _doc!.Form.GetXfaDatasetsXml();
        if (datasetsXml is null) return null;
        try
        {
            var ds = new XmlDocument();
            ds.LoadXml(datasetsXml);
            var root = ds.DocumentElement;
            if (root is null) return null;
            foreach (XmlNode c in root.ChildNodes)
                if (c is XmlElement el && el.LocalName == "data") return el;
            return root;
        }
        catch { return null; }
    }

    /// <summary>Same-name element children of <paramref name="ctx"/> — the dataset
    /// instances backing one template node.</summary>
    private static List<XmlElement> DataChildren(XmlElement? ctx, string name)
    {
        var list = new List<XmlElement>();
        if (ctx is null) return list;
        foreach (XmlNode c in ctx.ChildNodes)
            if (c is XmlElement el && el.LocalName == name) list.Add(el);
        return list;
    }

    /// <summary>Walk one template level, appending <c>&lt;field&gt;</c> entries to
    /// <paramref name="parent"/>. Subforms recurse per dataset instance; fields take
    /// their instance's value; draws mirror the ExportXml inclusion rule and stay
    /// value-less.</summary>
    private static void EmitXfdfLevel(XmlNode templateNode, XmlElement? dataCtx, XmlElement parent, string ns)
    {
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);

        int NextIndex(string name)
        {
            counters.TryGetValue(name, out var i);
            counters[name] = i + 1;
            return i;
        }

        void AppendContainer(XmlNode templateChild, XmlElement? inst, string name)
        {
            var fieldEl = parent.OwnerDocument!.CreateElement("field", ns);
            fieldEl.SetAttribute("name", $"{name}[{NextIndex(name)}]");
            parent.AppendChild(fieldEl);
            var inner = parent.OwnerDocument.CreateElement("fields", ns);
            EmitXfdfLevel(templateChild, inst, inner, ns);
            // A childless container gets no <fields> wrapper.
            if (inner.HasChildNodes) fieldEl.AppendChild(inner);
        }

        void AppendLeaf(string name, string? value)
        {
            var fieldEl = parent.OwnerDocument!.CreateElement("field", ns);
            fieldEl.SetAttribute("name", $"{name}[{NextIndex(name)}]");
            parent.AppendChild(fieldEl);
            if (!string.IsNullOrEmpty(value))
            {
                var valueEl = parent.OwnerDocument.CreateElement("value", ns);
                valueEl.InnerText = value;
                fieldEl.AppendChild(valueEl);
            }
        }

        foreach (XmlNode child in templateNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var localName = child.LocalName;
            var nameAttr = child.Attributes?["name"]?.Value;

            if (localName is "subform" or "subformSet" or "area")
            {
                if (nameAttr is null)
                {
                    EmitXfdfLevel(child, dataCtx, parent, ns);
                    continue;
                }
                var instances = DataChildren(dataCtx, nameAttr);
                if (instances.Count == 0) AppendContainer(child, null, nameAttr);
                else foreach (var inst in instances) AppendContainer(child, inst, nameAttr);
            }
            else if (localName is "field" or "exclGroup")
            {
                if (nameAttr is null) continue;
                var instances = DataChildren(dataCtx, nameAttr);
                if (instances.Count == 0) AppendLeaf(nameAttr, null);
                else foreach (var inst in instances) AppendLeaf(nameAttr, inst.InnerText);
            }
            else if (localName == "draw" && nameAttr is not null
                && templateNode.Attributes?["layout"]?.Value is not "row" and not "table")
            {
                AppendLeaf(nameAttr, null);
            }
            else
            {
                EmitXfdfLevel(child, dataCtx, parent, ns);
            }
        }
    }

    /// <summary>Emit a nested <c>&lt;field name="elem[0]"&gt;</c> element. A container
    /// holds a <c>&lt;fields&gt;</c> with its children; a leaf holds a <c>&lt;value&gt;</c>.
    /// A leaf that isn't a genuine XFA field (when <paramref name="fields"/> is non-null)
    /// is skipped so only bound fields carry a value entry.</summary>
    private static void EmitXfdfField(XmlElement element, XmlWriter w, string ns, List<string>? fields)
    {
        var children = ElementChildren(element);
        // A known XFA field is a value leaf; anything else (including an empty
        // subform such as a table header row) is a container that still exports a
        // <field><fields/></field> wrapper.
        bool isField = fields is not null && IsKnownXfaField(element.LocalName, fields);

        w.WriteStartElement("field", ns);
        w.WriteAttributeString("name", element.LocalName + "[0]");

        if (isField)
        {
            // Value field: emit <value> only when filled; an unfilled field is
            // self-closing (<field name="X[0]" />).
            var value = element.InnerText;
            if (!string.IsNullOrEmpty(value))
            {
                w.WriteStartElement("value", ns);
                w.WriteString(value);
                w.WriteEndElement(); // value
            }
        }
        else if (children.Count > 0)
        {
            // Container subform: wrap children in <fields>. A childless container
            // (e.g. an empty XFA subform such as a table header row) is emitted as a
            // self-closing <field name="X[0]" /> — the export omits the
            // empty <fields> wrapper rather than writing <fields />.
            w.WriteStartElement("fields", ns);
            foreach (var child in children)
                EmitXfdfField(child, w, ns, fields);
            w.WriteEndElement(); // fields
        }
        w.WriteEndElement(); // field
    }

    private void ImportFdfXfa(byte[] bytes)
    {
        var dataDoc = BuildXfaDataDocument();
        if (dataDoc?.DocumentElement is null) return;
        var text = Encoding.Latin1.GetString(bytes);
        foreach (var (name, value) in ParseFdfTV(text))
            SetDataDocValue(dataDoc.DocumentElement, name, value);
        _doc!.Form.ReplaceXfaDatasets(dataDoc);
    }

    private void ImportXfdfXfa(string xfdfXml)
    {
        var dataDoc = BuildXfaDataDocument();
        if (dataDoc?.DocumentElement is null) return;
        XmlDocument xfdf;
        try { xfdf = new XmlDocument(); xfdf.LoadXml(xfdfXml); }
        catch { return; }
        var fields = xfdf.DocumentElement?.SelectSingleNode("*[local-name()='fields']");
        if (fields is null) return;
        foreach (var (path, value) in CollectXfdfValues(fields, parentPath: null))
            SetDataDocValue(dataDoc.DocumentElement, path, value);
        _doc!.Form.ReplaceXfaDatasets(dataDoc);
    }

    /// <summary>Set a value in the XFA data document by a dotted path whose segments may
    /// carry [n] indices and may or may not include the data-root element. A bare leaf
    /// name (FDF) is matched anywhere in the tree.</summary>
    private static void SetDataDocValue(XmlElement root, string dottedName, string value)
    {
        var segments = dottedName.Split('.');
        // Drop a leading segment that names the data root itself (e.g. "form1[0]").
        int start = 0;
        if (segments.Length > 1 && StripIndex(segments[0]) == root.LocalName)
            start = 1;

        if (segments.Length - start == 1)
        {
            // Single segment: locate the leaf anywhere under the root by local name.
            var leaf = StripIndex(segments[start]);
            var node = leaf == root.LocalName ? root : FindDescendantByLocalName(root, leaf);
            if (node is not null) node.InnerText = value;
            return;
        }

        XmlElement? current = root;
        for (int i = start; i < segments.Length && current is not null; i++)
        {
            var seg = StripIndex(segments[i]);
            XmlElement? next = null;
            foreach (var c in ElementChildren(current))
                if (c.LocalName == seg) { next = c; break; }
            current = next;
        }
        if (current is not null) current.InnerText = value;
    }

    private static XmlElement? FindDescendantByLocalName(XmlElement root, string localName)
    {
        foreach (var c in ElementChildren(root))
        {
            if (c.LocalName == localName) return c;
            var found = FindDescendantByLocalName(c, localName);
            if (found is not null) return found;
        }
        return null;
    }

    private static string StripIndex(string name)
    {
        var br = name.IndexOf('[');
        return br < 0 ? name : name.Substring(0, br);
    }

    /// <summary>Strip the <c>[n]</c> occurrence index from every dotted segment
    /// (<c>form1[0].P1[0].Employee[0]</c> → <c>form1.P1.Employee</c>).</summary>
    private static string StripPathIndices(string path)
        => string.Join('.', path.Split('.').Select(StripIndex));

    /// <summary>Extract the document's XFA datasets XML to a stream.</summary>
    public void ExtractXfaData(Stream outputXmlStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        if (!_doc.Form.IsXfa)
            throw new InvalidOperationException("Document does not contain an XFA form.");
        var xml = _doc.Form.GetXfaDatasetsXml();
        if (xml is null) return;
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        outputXmlStream.Write(bytes, 0, bytes.Length);
        if (outputXmlStream.CanSeek) outputXmlStream.Position = 0;
    }

    /// <summary>Replace the document's XFA datasets XML from a stream.</summary>
    public void SetXfaData(Stream inputXmlStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        if (!_doc.Form.IsXfa) return;
        using var ms = new MemoryStream();
        if (inputXmlStream.CanSeek) inputXmlStream.Position = 0;
        inputXmlStream.CopyTo(ms);
        ImportXml(new MemoryStream(ms.ToArray()));
    }
}
