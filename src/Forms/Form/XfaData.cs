using System.Collections;
using System.Globalization;
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

    /// <summary>The conversion to a standard form of a document whose catalog asks a viewer to build the
    /// pages from the XFA packets: the pages are laid out whatever the packets say of the form's kind.</summary>
    internal void ConvertXfaAwaitingRender() => FlattenXfa(layOutPages: true);

    /// <summary>Lays the form out onto the document's pages and leaves the XFA packets as they are.</summary>
    internal void LayOutXfaPages() => RenderDynamicXfa(placeFields: false);

    private void FlattenXfa(bool placeWidgets = true, bool layOutPages = false)
    {
        var flatReader = ResolvedReader;
        if (flatReader is null || !IsXfa) return;
        var catalog = flatReader.Catalog;
        var acroForm = flatReader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is null) return;

        // A dynamic XFA form carries its fields only in the XFA template — the AcroForm
        // has no widget fields. When flattening it to a standard AcroForm, the fields are the
        // instances the render laid out: one per painted widget, a master page's once per page
        // (/T = the full dotted SOM path with the instance index). A static XFA form
        // already owns AcroForm widget fields, so leave those untouched (no duplication).
        var existing = flatReader.Resolve(acroForm.Get("Fields")) as PdfArray;
        bool hasWidgets = existing is not null && existing.Any(f => !IsUnseenSignature(flatReader, f));
        bool placed = false;
        // A DYNAMIC form's pages are only a saved preview - a conforming processor
        // re-renders from the XFA (NeedsRendering). The Standard
        // conversion replaces the preview pages with its own XFA layout (measured on
        // e.g. 8 preview pages -> a 3-page render with the continuation master's
        // slim header); a static XFA keeps its authoritative pages untouched.
        bool laysOut = !hasWidgets || layOutPages || Type == FormType.Dynamic;
        if (laysOut)
        {
            // paint the form onto real pages (replaces the preview pages); each field the render painted
            // becomes a field of the form with a widget where it was painted.
            var placements = RenderDynamicXfa(placeFields: placeWidgets && !hasWidgets);
            if (placements is not null) placed = PlaceXfaFieldWidgets(flatReader, acroForm, placements);
        }
        // A form listed without widgets, or one the renderer could not lay out, lists one flat field
        // per template field so the fields survive as findable AcroForm fields.
        if (!hasWidgets && !placed) GenerateFlatAcroFieldsFromXfaTemplate(flatReader, acroForm);
        // A static form keeps its pages, so a widget the template alone paints (a barcode)
        // needs an appearance of its own to survive the conversion.
        if (!laysOut)
        {
            // The pages and widgets stay: the values the datasets alone hold go into the widgets before the XFA goes.
            MergeXfaDataIntoEmptyFields();
            GenerateXfaBarcodeAppearances(flatReader, acroForm);
        }

        // Remove XFA key from AcroForm — this converts XFA to standard AcroForm
        // ... and the catalog no longer asks a viewer to build the pages from it
        acroForm.Remove("XFA");
        catalog.Remove("NeedsRendering");
        MarkAcroFormDirty();
        // The fields the conversion made are the form's fields from here on: a caller that sets their flags
        // or values before the save edits the dictionaries the save writes.
        ListFieldsOfEmptyForm();
    }

    /// <summary>True for a signature field whose widgets all have no area: the document signature a dynamic form
    /// carries beside its XFA, which says nothing of the form's own fields.</summary>
    private static bool IsUnseenSignature(PdfReader reader, PdfObject? entry)
    {
        if (reader.ResolveDict(entry) is not { } field) return false;
        var widgets = reader.Resolve(field.Get("Kids")) is PdfArray kids
            ? kids.Select(reader.ResolveDict).ToList()
            : new List<PdfDictionary?> { field };
        if (widgets.Count == 0) return false;
        foreach (var widget in widgets)
        {
            if (widget is null || (widget.GetName("FT") ?? field.GetName("FT")) != "Sig") return false;
            if (reader.Resolve(widget.Get("Rect")) is PdfArray r && r.Count == 4 && Side(r, 0) != 0 && Side(r, 1) != 0)
                return false;
        }
        return true;

        static double Side(PdfArray rect, int axis) =>
            Aspose.Pdf.Functions.PdfArrayHelper.GetDouble(rect, axis + 2) - Aspose.Pdf.Functions.PdfArrayHelper.GetDouble(rect, axis);
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
    private List<Xfa.XfaFieldPlacement>? RenderDynamicXfa(bool placeFields)
    {
        var doc = OwnerDocument;
        if (doc is null) return null;
        var xml = GetXfaTemplateXmlResolved() ?? GetXfaTemplateXml();
        if (string.IsNullOrEmpty(xml)) return null;
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
                return Xfa.XfaRenderer.Render(doc, root,
                    HasXfaFormInstanceRecord() ? GetXfaFieldValue : GetXfaFieldValueStrict, placeFields);
        }
        catch { /* an XFA form the renderer cannot lay out keeps the static pages */ }
        return null;
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

    private Dictionary<string, (PdfIndirectRef Ref, PdfDictionary Dict)>? GenerateFlatAcroFieldsFromXfaTemplate(PdfReader reader, PdfDictionary acroForm)
    {
        var doc = OwnerDocument;
        if (doc is null) return null;
        var engine = XfaTemplateEngine();
        if (engine is null) return null;
        // Give the engine the XFA data-binding resolver so its scripts can read field rawValues.
        // The engine must never break a flatten — fall back to no generated fields on any failure.
        List<Xfa.XfaFlatField> fields;
        try { fields = engine.BuildRenderedFields(GetXfaFieldValue); }
        catch { return null; }
        var byPath = new Dictionary<string, (PdfIndirectRef Ref, PdfDictionary Dict)>(fields.Count, StringComparer.Ordinal);
        if (fields.Count == 0) return byPath;

        var fieldsArr = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fieldsArr is null) { fieldsArr = new PdfArray(); acroForm.Set("Fields", fieldsArr); }

        foreach (var f in fields)
        {
            var fld = FlatXfaFieldDict(f);
            int num = doc.AllocateObjectNumber();
            doc.AddNewObject(num, fld, registerOverlay: true);
            var reference = new PdfIndirectRef(num, 0);
            fieldsArr.Add(reference);
            // The render names a field by its plain path: a dot inside a name is not escaped there.
            byPath[f.Path.Replace("\\.", ".")] = (reference, fld);
        }
        return byPath;
    }

    /// <summary>The dictionary of a field the template declares: its path as the name, the type and flags of its
    /// control, the value the data binds to it, and for an image field the picture as its appearance.</summary>
    private static PdfDictionary FlatXfaFieldDict(Xfa.XfaFlatField f)
    {
        var fld = new PdfDictionary();
        fld.Set("T", Field.EncodePdfTextString(f.Path));
        fld.Set("FT", new PdfName(f.Ft));
        if (f.Ff != 0) fld.Set("Ff", new PdfInteger((int)f.Ff));
        // Text and choice fields take the bound datasets value as a text /V; value-less and
        // button/signature fields are left untouched.
        if (!string.IsNullOrEmpty(f.Value) && (f.Ft == "Tx" || f.Ft == "Ch"))
            fld.Set("V", Field.EncodePdfTextString(f.Value));
        // An XFA image field carries its picture as base64 in the datasets node -
        // materialise it as the flat pushbutton's /AP /N form so the image survives
        // the flatten (readable back via Appearance["N"].Resources.Images).
        if (f.IsImage && !string.IsNullOrEmpty(f.Value))
            TrySetFlatImageAppearance(fld, f.Value);
        return fld;
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
                Compat.Format(CultureInfo.InvariantCulture, $"q {w:0.##} 0 0 {h:0.##} 0 0 cm /Im0 Do Q"));
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
                CollectXfaFieldNames(doc.DocumentElement, "", names, RepeatedDataScope(templateXml));
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
        catch { /* malformed XFA/XML leaves the values as they were */ }
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
        var match = SafeRegex.Match(leaf, @"^(.+)\[\d+\]$");
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

    // The occur attribute that makes a subform repeatable, as the template writes it; a
    // template without one lists its fields from the template alone.
    private const string OccurMaxAttribute = " max=\"";

    // The data node parsed for the field list, kept while the datasets text is the same
    // instance (that text is itself kept while its stream is unchanged): the list is asked
    // for once per looked-up name, and the walk only reads the node.
    private string? _fieldListDataXml;
    private XmlNode? _fieldListDataScope;

    /// <summary>The datasets node whose children are the records, for a template that can
    /// repeat a subform; null when nothing repeats or there is no data, so the datasets are
    /// parsed only for the forms whose field list depends on them.</summary>
    private XmlNode? RepeatedDataScope(string templateXml)
    {
        if (templateXml.IndexOf(OccurMaxAttribute, StringComparison.Ordinal) < 0) return null;
        var xml = GetXfaDatasetsXml();
        if (string.IsNullOrEmpty(xml)) return null;
        if (ReferenceEquals(xml, _fieldListDataXml)) return _fieldListDataScope;
        XmlNode? scope = null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            scope = FindDatasetsDataNode(doc);
        }
        catch (XmlException) { /* data that does not parse repeats nothing */ }
        _fieldListDataXml = xml;
        _fieldListDataScope = scope;
        return scope;
    }

    /// <summary>How many instances the data holds of a repeatable template subform: the
    /// same-named data groups of the scope, capped by the subform's occur maximum; one when
    /// the subform cannot repeat or the data does not repeat it.</summary>
    private static int RepeatedInstanceCount(XmlNode subform, List<XmlNode> groups)
    {
        const int Unbounded = -1;
        if (groups.Count < 2) return 1;
        foreach (XmlNode c in subform.ChildNodes)
        {
            if (c.LocalName != "occur" || c.Attributes?["max"]?.Value is not { } text
                || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max)) continue;
            if (max == Unbounded) return groups.Count;
            return Math.Max(1, Math.Min(max, groups.Count));
        }
        return 1;
    }

    /// <summary>The data groups of the scope that carry the subform's name.</summary>
    private static List<XmlNode> NamedDataGroups(XmlNode? scope, string name)
    {
        var groups = new List<XmlNode>();
        if (scope is null) return groups;
        foreach (XmlNode c in scope.ChildNodes)
        {
            if (c.NodeType == XmlNodeType.Element && c.LocalName == name
                && c.ChildNodes.OfType<XmlElement>().Any())
                groups.Add(c);
        }
        return groups;
    }

    /// <summary>The data groups a subform bound by a reference to all instances ("$.A.B[*]") takes from the scope:
    /// every node named as the reference's last step under the node its earlier steps reach. Empty for a subform
    /// bound any other way.</summary>
    private static List<XmlNode> ReferencedDataGroups(XmlNode subform, XmlNode? scope)
    {
        const string Head = "$.", AllInstances = "[*]";
        var bind = FindBindElement(subform);
        var reference = bind?.Attributes?["ref"]?.Value;
        if (scope is null || bind?.Attributes?["match"]?.Value != "dataRef" || reference is null
            || !reference.StartsWith(Head, StringComparison.Ordinal) || !reference.EndsWith(AllInstances, StringComparison.Ordinal))
            return new List<XmlNode>();
        var steps = reference.Substring(Head.Length, reference.Length - Head.Length - AllInstances.Length).Split('.');
        XmlNode? node = scope;
        for (int i = 0; i < steps.Length - 1 && node is not null; i++)
            node = node.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == steps[i]);
        // A record the reference names is an instance even while it holds nothing yet.
        return node is null
            ? new List<XmlNode>()
            : node.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == steps[^1]).Cast<XmlNode>().ToList();
    }

    /// <summary>Lists the fields under <paramref name="node"/>. <paramref name="scope"/> is the
    /// data node the subforms here bind under (null: no data to repeat by): a repeatable
    /// subform the data holds several groups of is listed once per group, each instance
    /// under its own index.</summary>
    private static void CollectXfaFieldNames(XmlNode node, string parentPath, List<string> names, XmlNode? scope)
    {
        Dictionary<string, int>? shifted = null;
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
                    if (shifted is not null && shifted.TryGetValue(nameAttr, out var extra)) idx += extra;
                    var groups = NamedDataGroups(scope, nameAttr);
                    if (groups.Count == 0) groups = ReferencedDataGroups(child, scope);
                    int count = RepeatedInstanceCount(child, groups);
                    var escName = EscapeSomSegment(nameAttr);
                    for (int k = 0; k < count; k++)
                    {
                        var currentPath = parentPath.Length > 0
                            ? $"{parentPath}.{escName}[{idx + k}]"
                            : $"{escName}[{idx + k}]";
                        var inner = groups.Count == 0 ? scope : groups[Math.Min(idx + k, groups.Count - 1)];
                        CollectXfaFieldNames(child, currentPath, names, inner);
                    }
                    if (count > 1)
                    {
                        // Later same-named siblings are numbered after the added instances.
                        shifted ??= new Dictionary<string, int>(StringComparer.Ordinal);
                        shifted[nameAttr] = (shifted.TryGetValue(nameAttr, out var had) ? had : 0) + count - 1;
                    }
                }
                else
                    CollectXfaFieldNames(child, parentPath, names, scope);
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
                CollectXfaFieldNames(child, parentPath, names, scope);
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

    private string? _xfaFormXmlParsed;
    private XmlDocument? _xfaFormDoc;

    /// <summary>The form packet parsed once per text the part reader hands out, for the field reads that
    /// fall back to the values a viewer saved; a packet that is no XML reads as none.</summary>
    private XmlDocument? GetXfaFormDocument()
    {
        var xml = GetXfaFormXml();
        if (xml is null) return null;
        if (!ReferenceEquals(xml, _xfaFormXmlParsed))
        {
            XmlDocument? doc = new XmlDocument();
            try { doc.LoadXml(xml); } catch (XmlException) { doc = null; }
            _xfaFormDoc = doc;
            _xfaFormXmlParsed = xml;
        }
        return _xfaFormDoc;
    }

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
                if (child.LocalName == "caption") return XfaCaptionText(child);
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>The text of a template &lt;caption&gt;: its &lt;value&gt;&lt;text&gt; when present,
    /// else the caption's own text content.</summary>
    private static string XfaCaptionText(XmlNode caption)
    {
        // Try <value><text> first
        foreach (XmlNode vc in caption.ChildNodes)
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
        return caption.InnerText;
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
                        // XFA: open="always"/"multiSelect" is an expanded list box; "userControl"/"onEntry"/absent
                        // is a drop-down combo.
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
