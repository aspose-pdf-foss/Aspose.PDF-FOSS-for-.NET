using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    /// <summary>Settings for <see cref="Document.Flatten(FlattenSettings)"/>.</summary>
    public sealed class FlattenSettings
    {
        /// <summary>When true, button widgets (XFA &lt;button&gt; nodes) are
        /// marked presence="hidden" before the flatten step so they are not
        /// rasterised into the resulting page content.</summary>
        public bool HideButtons { get; set; }

        /// <summary>When true, JavaScript and other field events are run
        /// during flatten (e.g. computed-field formulas refresh before the
        /// flatten captures their value). Stored only; XFA scripts are
        /// not currently executed.</summary>
        public bool CallEvents { get; set; }

        /// <summary>When true, each field's appearance stream is regenerated
        /// from its current value before being flattened into page content.
        /// Stored only; appearances are not currently rebuilt.</summary>
        public bool UpdateAppearances { get; set; }

        /// <summary>When true, redaction annotations are applied during the flatten pass. Stored only.</summary>
        public bool ApplyRedactions { get; set; }
    }

    private void FlattenXfa()
    {
        var flatReader = ResolvedReader;
        if (flatReader is null || !IsXfa) return;
        var catalog = flatReader.Catalog;
        var acroForm = flatReader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is null) return;

        // A dynamic XFA form carries its fields only in the XFA template — the AcroForm
        // has no widget fields. When flattening it to a standard AcroForm, materialise one
        // flat field per template field so the fields survive as findable AcroForm fields
        // (/T = the full dotted SOM path, matching GetXfaFieldNames). A static XFA form
        // already owns AcroForm widget fields, so leave those untouched (no duplication).
        var existing = flatReader.Resolve(acroForm.Get("Fields")) as PdfArray;
        bool hasWidgets = existing is not null && existing.Count > 0;
        if (!hasWidgets)
            GenerateFlatAcroFieldsFromXfaTemplate(flatReader, acroForm);
        // A DYNAMIC form's pages are only a saved preview - a conforming processor
        // re-renders from the XFA (NeedsRendering). The Standard
        // conversion replaces the preview pages with its own XFA layout (measured on
        // e.g. 8 preview pages -> a 3-page render with the continuation master's
        // slim header); a static XFA keeps its authoritative pages untouched.
        if (!hasWidgets || Type == FormType.Dynamic)
            RenderDynamicXfa();     // paint the form onto real pages (replaces the preview pages)

        // Remove XFA key from AcroForm — this converts XFA to standard AcroForm
        acroForm.Remove("XFA");
        MarkAcroFormDirty();
    }

    /// <summary>Materialise a flat AcroForm field for each RENDERED XFA field (only used when
    /// flattening a dynamic XFA form that has no AcroForm widgets). The rendered set is resolved
    /// by <see cref="Xfa.XfaFormEngine"/>, which walks the subform tree AND the master pages
    /// (pageSet/pageArea) and applies the template-decidable selection rules (static presence,
    /// barcode ui). Each field is a top-level /Fields entry whose /T is the entire dotted SOM
    /// path (so FullName == PartialName and FindByName resolves it), with /FT derived from the
    /// field's XFA &lt;ui&gt; control. Positions (/Rect) are not emitted — that needs the XFA
    /// layout engine and is not required to make the fields findable.</summary>
    /// <summary>Paint the dynamic-XFA form's content onto fresh PDF pages so a raster render shows
    /// the form rather than the XFA fallback page. Tolerant: a failure leaves pages untouched.</summary>
    private void RenderDynamicXfa()
    {
        var doc = OwnerDocument;
        if (doc is null) return;
        var xml = GetXfaTemplateXmlResolved() ?? GetXfaTemplateXml();
        if (string.IsNullOrEmpty(xml)) return;
        try
        {
            var tdoc = new XmlDocument();
            tdoc.LoadXml(xml);
            if (tdoc.DocumentElement is { } root)
                // Without a saved form-packet instance record, data binding is
                // STRICT: only a path whose every segment resolves to exactly one
                // data node binds (a repeated group's fields stay
                // empty — measured). A form packet keeps the lenient
                // resolver: its recorded instances disambiguate repeats.
                Xfa.XfaRenderer.Render(doc, root,
                    HasXfaFormInstanceRecord() ? GetXfaFieldValue : GetXfaFieldValueStrict);
        }
        catch { }
    }

    /// <summary>True when the /XFA "form" packet records at least one subform — the
    /// runtime instance state a viewer saved with the document.</summary>
    internal bool HasXfaFormInstanceRecord()
    {
        var formXml = GetXfaFormXml();
        if (string.IsNullOrEmpty(formXml)) return false;
        try
        {
            var fdoc = new XmlDocument();
            fdoc.LoadXml(formXml);
            return fdoc.DocumentElement is { } r
                && (r.LocalName == "subform"
                    || r.SelectSingleNode(".//*[local-name()='subform']") is not null);
        }
        catch { return false; }
    }

    private void GenerateFlatAcroFieldsFromXfaTemplate(PdfReader reader, PdfDictionary acroForm)
    {
        var doc = OwnerDocument;
        if (doc is null) return;
        var engine = Xfa.XfaFormEngine.TryCreate(GetXfaTemplateXmlResolved() ?? GetXfaTemplateXml());
        if (engine is null) return;
        // Give the engine the XFA data-binding resolver so its scripts can read field rawValues.
        // The engine must never break a flatten — fall back to no generated fields on any failure.
        List<Xfa.XfaFlatField> fields;
        try { fields = engine.BuildRenderedFields(GetXfaFieldValue); }
        catch { return; }
        if (fields.Count == 0) return;

        var fieldsArr = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fieldsArr is null) { fieldsArr = new PdfArray(); acroForm.Set("Fields", fieldsArr); }

        foreach (var f in fields)
        {
            var fld = new PdfDictionary();
            fld.Set("T", new PdfString(Encoding.UTF8.GetBytes(f.Path)));
            fld.Set("FT", new PdfName(f.Ft));
            if (f.Ff != 0) fld.Set("Ff", new PdfInteger((int)f.Ff));
            // Carry the field's bound datasets value onto the flat field's /V so the flattened
            // dynamic-XFA form keeps its data values (Field.Value). Text and
            // choice fields take a text /V; leave value-less and button/signature fields untouched.
            if (!string.IsNullOrEmpty(f.Value) && (f.Ft == "Tx" || f.Ft == "Ch"))
                fld.Set("V", new PdfString(Encoding.UTF8.GetBytes(f.Value)));
            // An XFA image field carries its picture as base64 in the datasets node —
            // materialise it as the flat pushbutton's /AP /N form so the image survives
            // the flatten (readable back via Appearance["N"].Resources.Images).
            if (f.IsImage && !string.IsNullOrEmpty(f.Value))
                TrySetFlatImageAppearance(fld, f.Value);
            int num = doc.AllocateObjectNumber();
            doc.AddNewObject(num, fld, registerOverlay: true);
            fieldsArr.Add(new PdfIndirectRef(num, 0));
        }
    }

    /// <summary>Build a normal-appearance form XObject holding the base64-decoded
    /// picture of a flattened XFA image field, at the image's natural pixel size
    /// (the flat field has no /Rect to fit into). Tolerant: undecodable data
    /// leaves the field without an appearance.</summary>
    private static void TrySetFlatImageAppearance(PdfDictionary fld, string base64)
    {
        byte[] imageBytes;
        try { imageBytes = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { return; }
        if (imageBytes.Length == 0) return;

        try
        {
            using var src = new MemoryStream(imageBytes, writable: false);
            var stamp = new ImageStamp(src);
            var imgStream = stamp.BuildImageXObject();
            double w = stamp.PixelWidth > 0 ? stamp.PixelWidth : 1;
            double h = stamp.PixelHeight > 0 ? stamp.PixelHeight : 1;

            var xobjects = new PdfDictionary();
            xobjects.Set("Im0", imgStream);
            var resources = new PdfDictionary();
            resources.Set("XObject", xobjects);

            var content = Encoding.ASCII.GetBytes(
                FormattableString.Invariant($"q {w:0.##} 0 0 {h:0.##} 0 0 cm /Im0 Do Q"));
            var apN = new PdfDictionary();
            apN.Set("Type", new PdfName("XObject"));
            apN.Set("Subtype", new PdfName("Form"));
            apN.Set("FormType", new PdfInteger(1));
            var bbox = new PdfArray();
            bbox.Add(new PdfInteger(0)); bbox.Add(new PdfInteger(0));
            bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
            apN.Set("BBox", bbox);
            apN.Set("Resources", resources);
            apN.Set("Length", new PdfInteger(content.Length));

            var ap = new PdfDictionary();
            ap.Set("N", new PdfStream(apN, content));
            fld.Set("AP", ap);
            var rect = new PdfArray();
            rect.Add(new PdfInteger(0)); rect.Add(new PdfInteger(0));
            rect.Add(new PdfReal(w)); rect.Add(new PdfReal(h));
            fld.Set("Rect", rect);
            fld.Set("Subtype", new PdfName("Widget"));
        }
        catch
        {
            // Unsupported image payload: keep the flat field, drop the appearance.
        }
    }

    /// <summary>
    /// Get all interactive field names from the XFA template. Returns an empty array for non-XFA forms.
    /// Excludes draw (decorative) elements.
    /// </summary>
    internal string[] GetXfaFieldNames()
    {
        if (!IsXfa) return [];
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return [];
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            var names = new List<string>();
            if (doc.DocumentElement is not null)
                CollectXfaFieldNames(doc.DocumentElement, "", names);
            return names.ToArray();
        }
        catch { return []; }
    }

    /// <summary>Enumerate the actual XFA datasets leaves as (full dotted path, value)
    /// pairs. Unlike <see cref="GetXfaFieldNames"/> (which walks the template and so
    /// only yields the index-0 instance of each repeated field), this walks the
    /// datasets tree and yields every repeated instance with its real sibling index
    /// (e.g. <c>movies[0].movie[13].countries[0].country[1]</c>).</summary>
    internal List<KeyValuePair<string, string>> GetXfaDatasetsFields()
    {
        var result = new List<KeyValuePair<string, string>>();
        if (!IsXfa) return result;
        var xml = GetXfaDatasetsXml();
        if (string.IsNullOrEmpty(xml)) return result;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var data = FindDatasetsDataNode(doc);
            if (data is not null) WalkXfaDatasets(data, string.Empty, result);
        }
        catch { }
        return result;
    }

    private static XmlNode? FindDatasetsDataNode(XmlDocument doc)
    {
        var root = doc.DocumentElement;
        if (root is null) return null;
        var datasets = root.SelectSingleNode("//*[local-name()='datasets']");
        var data = datasets?.SelectSingleNode("*[local-name()='data']");
        if (data is not null) return data;
        var allData = root.SelectNodes("//*[local-name()='data']");
        if (allData is not null)
        {
            foreach (XmlNode d in allData)
                if (d.ParentNode?.LocalName == "datasets") return d;
            if (allData.Count > 0) return allData[0];
        }
        return root;
    }

    private static void WalkXfaDatasets(XmlNode node, string prefix, List<KeyValuePair<string, string>> result)
    {
        var counts = new Dictionary<string, int>();
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var localName = child.LocalName;
            var index = counts.TryGetValue(localName, out var c) ? c : 0;
            counts[localName] = index + 1;
            var escName = EscapeSomSegment(localName);
            var path = prefix.Length == 0 ? $"{escName}[{index}]" : $"{prefix}.{escName}[{index}]";

            var hasElementChild = false;
            foreach (XmlNode grand in child.ChildNodes)
                if (grand.NodeType == XmlNodeType.Element) { hasElementChild = true; break; }

            if (hasElementChild)
                WalkXfaDatasets(child, path, result);
            else
                result.Add(new KeyValuePair<string, string>(path, child.InnerText));
        }
    }

    /// <summary>True when the XFA template marks the field at the given path as a
    /// multi-line text edit (<c>&lt;textEdit multiLine="1"&gt;</c>). Non-multi-line
    /// fields normalise embedded newlines on import.</summary>
    internal bool IsXfaFieldMultiline(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (string.IsNullOrEmpty(templateXml)) return false;
        var leaf = SplitSomPath(path)[^1];
        var match = Regex.Match(leaf, @"^(.+)\[\d+\]$");
        var name = match.Success ? match.Groups[1].Value : leaf;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            var field = doc.SelectSingleNode($"//*[local-name()='field'][@name='{name}']");
            var attr = field?.SelectSingleNode(".//@*[local-name()='multiLine']");
            return attr is not null && attr.Value == "1";
        }
        catch { return false; }
    }

    private static void CollectXfaFieldNames(XmlNode node, string parentPath, List<string> names)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var localName = child.LocalName;
            var nameAttr = child.Attributes?["name"]?.Value;

            if (localName is "subform" or "subformSet" or "area")
            {
                if (nameAttr is not null)
                {
                    int idx = CountPrecedingSiblings(child, localName, nameAttr);
                    var escName = EscapeSomSegment(nameAttr);
                    var currentPath = parentPath.Length > 0
                        ? $"{parentPath}.{escName}[{idx}]"
                        : $"{escName}[{idx}]";
                    CollectXfaFieldNames(child, currentPath, names);
                }
                else
                    CollectXfaFieldNames(child, parentPath, names);
            }
            else if (localName is "field" or "exclGroup")
            {
                if (nameAttr is not null)
                {
                    int idx = CountPrecedingSiblings(child, localName, nameAttr);
                    var escName = EscapeSomSegment(nameAttr);
                    var fieldPath = parentPath.Length > 0
                        ? $"{parentPath}.{escName}[{idx}]"
                        : $"{escName}[{idx}]";
                    names.Add(fieldPath);
                }
                // Don't recurse into exclGroup — the group itself is the field,
                // individual options are not separate fields.
            }
            else
            {
                CollectXfaFieldNames(child, parentPath, names);
            }
        }
    }

    /// <summary>
    /// Get the XFA datasets XML, or null if not an XFA form.
    /// </summary>
    public string? GetXfaDatasetsXml()
    {
        // First try to get just the "datasets" part from an XFA array
        var (_, datasetsXml) = GetXfaPart("datasets");
        if (datasetsXml is not null) return datasetsXml;

        var reader = ResolvedReader;
        if (reader is null) return null;
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return null;

        var xfaObj = reader.Resolve(acroForm.Get("XFA"));

        // Single-stream XFA: entire XDP in one stream
        if (xfaObj is PdfStream xfaStream)
        {
            var data = reader.DecodeStream(xfaStream);
            return StripBom(Encoding.UTF8.GetString(data));
        }

        // XFA array without a named "datasets" part:
        // Concatenate all streams to reconstruct the full XDP
        if (xfaObj is PdfArray xfaArray)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < xfaArray.Count; i++)
            {
                var item = reader.Resolve(xfaArray[i]);
                if (item is PdfStream s)
                {
                    var data = reader.DecodeStream(s);
                    sb.Append(Encoding.UTF8.GetString(data));
                }
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
        return null;
    }

    /// <summary>
    /// Get the XFA "form" packet XML (the recorded runtime instance DOM — subform
    /// instance counts and value overrides as last saved by a viewer), or null when
    /// the document carries none.
    /// </summary>
    internal string? GetXfaFormXml() => GetXfaPart("form").xml;

    /// <summary>
    /// <summary>
    /// Get the caption text for an XFA field by walking the template XML.
    /// Returns the text from &lt;caption&gt;&lt;value&gt;&lt;text&gt; inside the field element.
    /// </summary>
    internal string? GetXfaFieldCaption(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is null) return null;
            var fieldNode = FindXfaTemplateNode(doc.DocumentElement, path, 0);
            if (fieldNode is null) return null;

            // Look for <caption><value><text>.</text></value></caption>
            foreach (XmlNode child in fieldNode.ChildNodes)
            {
                if (child.LocalName == "caption")
                {
                    // Try <value><text> first
                    foreach (XmlNode vc in child.ChildNodes)
                    {
                        if (vc.LocalName == "value")
                        {
                            foreach (XmlNode tc in vc.ChildNodes)
                            {
                                if (tc.LocalName == "text")
                                    return tc.InnerText;
                            }
                        }
                    }
                    // Fallback: direct text content
                    return child.InnerText;
                }
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Resolve the XFA template UI widget kind for a field path — the local name of
    /// the element under the field's &lt;ui&gt; (e.g. "textEdit", "choiceList",
    /// "button"). A multi-select &lt;choiceList&gt; reports "choiceListMulti".
    /// Returns "exclGroup" for an exclusion (radio) group and "textEdit" for a
    /// &lt;field&gt; that declares no &lt;ui&gt; (the XFA default). Returns null when
    /// the path resolves to no template field node.
    /// </summary>
    internal string? GetXfaFieldUiKind(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is null) return null;
            var fieldNode = FindXfaTemplateNode(doc.DocumentElement, path, 0);
            if (fieldNode is null) return null;
            if (fieldNode.LocalName == "exclGroup") return "exclGroup";

            foreach (XmlNode child in fieldNode.ChildNodes)
            {
                if (child.LocalName != "ui") continue;
                foreach (XmlNode uiChild in child.ChildNodes)
                {
                    if (uiChild.NodeType != XmlNodeType.Element) continue;
                    if (uiChild.LocalName == "choiceList")
                    {
                        // XFA: open="always"/"multiSelect" is an expanded list box;
                        // "userControl"/"onEntry"/absent is a drop-down combo.
                        var open = uiChild.Attributes?["open"]?.Value;
                        return open is "always" or "multiSelect" ? "choiceListMulti" : "choiceList";
                    }
                    return uiChild.LocalName;
                }
            }
            // A <field> with no explicit <ui> renders as a plain text edit in XFA.
            return fieldNode.LocalName == "field" ? "textEdit" : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Get radio button option values from the XFA template.
    /// Looks for &lt;items&gt; children containing &lt;integer&gt; or &lt;text&gt; values.
    /// </summary>
    internal List<string>? GetXfaRadioButtonItems(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is null) return null;
            var fieldNode = FindXfaTemplateNode(doc.DocumentElement, path, 0);
            if (fieldNode is null) return null;

            // Collect items from <items> children (direct or in child <field> elements)
            var result = new List<string>();
            CollectXfaItems(fieldNode, result);

            // For exclGroup: items are on child <field> elements, not the group itself
            if (result.Count == 0 && fieldNode.LocalName == "exclGroup")
            {
                foreach (XmlNode child in fieldNode.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element && child.LocalName == "field")
                        CollectXfaItems(child, result);
                }
            }
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    private static void CollectXfaItems(XmlNode node, List<string> result)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.LocalName != "items") continue;
            foreach (XmlNode item in child.ChildNodes)
            {
                if (item.NodeType == XmlNodeType.Element)
                    result.Add(item.InnerText);
            }
        }
    }
}
