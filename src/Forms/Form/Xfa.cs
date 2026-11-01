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
    public string? GetXfaFieldValue(string path) => GetXfaFieldValueCore(path, strict: false);

    /// <summary>The value a field's path reads through <see cref="XFA"/>: as <see cref="GetXfaFieldValue"/>, and a
    /// field that binds no data reads the value of its widget, where the form keeps one.</summary>
    internal string? GetXfaFieldValueOrWidget(string path) => GetXfaFieldValueCore(path, strict: false, widgetWhenUnbound: true);

    /// <summary>Strict variant used when the document carries NO form-packet instance
    /// record: a path binds only when every segment resolves to exactly ONE data node
    /// (a repeated data group's fields stay empty in that case).</summary>
    internal string? GetXfaFieldValueStrict(string path) => GetXfaFieldValueCore(path, strict: true);

    /// <summary>The value the datasets hold for a field at its own path or where its binding leads, never one a
    /// name lookup guesses: what a merge of the datasets into the fields may take.</summary>
    internal string? BoundXfaFieldValueOrNull(string path) => GetXfaFieldValueCore(path, strict: false, boundOnly: true);

    private string? GetXfaFieldValueCore(string path, bool strict, bool widgetWhenUnbound = false, bool boundOnly = false)
    {
        var xv = new XfaFieldValueState();
        xv.path = path;
        xv.strict = strict;
        var (_, xml) = GetXfaPart("datasets");
        xv.reader = ResolvedReader;
        if (xml is null && xv.reader is not null)
        {
            var acroForm = xv.reader.ResolveDict(xv.reader.Catalog.Get("AcroForm"));
            if (acroForm is not null)
            {
                var xfaObj = xv.reader.Resolve(acroForm.Get("XFA"));
                if (xfaObj is PdfStream singleStream)
                {
                    var data = xv.reader.DecodeStream(singleStream);
                    xml = Encoding.UTF8.GetString(data);
                }
            }
        }
        if (xml is null) return null;
        xv.result = FindXfaNodeValue(xv, xml, xv.path);
        // A value matched by its leaf name alone is a guess: the template's binding, below, may
        // place the field in a section the data leaves empty, and then the guess must not show.
        if (!string.IsNullOrEmpty(xv.result) && !xv.leafFallback) return xv.result;
        if (xv.datasets is not null)
        {
            switch (BoundXfaFieldValue(xv.datasets, path, out var bound))
            {
                // A node the binding reaches is the field's, empty or not.
                case XfaDataBinding.Found: return bound;
                // An instance the reference names beyond the data is empty, not the last instance there.
                case XfaDataBinding.Missing: return string.Empty;
                // A field that binds no data keeps a value written to it under its data group, else in its widget,
                // where the form has one.
                case XfaDataBinding.Unbound when bound is not null: return bound;
                case XfaDataBinding.Unbound when widgetWhenUnbound && Type == FormType.Static && FindFieldOrNull(path) is { } widgetField:
                    return widgetField.Value;
            }
        }
        if (boundOnly) return null;

        // Template-based data binding resolution:
        // Walk the template to find the field, resolve <bind match="dataRef" ref="$.xxx"/>
        // and skip presentation-only subforms (those with <bind match="none"/>).
        var resolved = ResolveXfaFieldValue(xv, xml);
        // A choice a viewer saved for a field without a data node lives in the form packet alone.
        return string.IsNullOrEmpty(resolved) ? FormPacketValue(path) ?? resolved : resolved;
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
            // The template as the read path holds it, parsed once: a fill asks at every field.
            var templateDoc = GetXfaTemplateDocument();
            if (templateDoc?.DocumentElement is null) return null;

            var parts = SplitSomPath(path);
            if (parts.Length < 2) return null;

            XmlNode? templateNode = templateDoc.DocumentElement;
            for (int i = 0; i < parts.Length && templateNode is not null; i++)
                templateNode = StepTemplate(templateNode, parts[i]);
            if (templateNode is null) return null;

            var bindNode = FindBindElement(templateNode);
            string? bindRef = null;
            if (bindNode is not null)
            {
                var matchAttr = bindNode.Attributes?["match"];
                var refAttr = bindNode.Attributes?["ref"];
                if (matchAttr?.Value == "dataRef" && refAttr?.Value is { } r && r.StartsWith("$."))
                    bindRef = r.Substring(2); // may itself be multi-segment, e.g. "FilingInstitutionInformation.FilerName"
                else if (matchAttr?.Value == "dataRef" && RecordRelativeRef(refAttr?.Value) is { } fromRecord)
                    return RecordDataPath(parts, fromRecord);
            }

            var dataPathParts = AncestorDataPath(templateDoc, parts, out _, out _);
            dataPathParts.Add(bindRef ?? parts[^1]);

            var resolvedPath = string.Join(".", dataPathParts);
            return resolvedPath != path ? resolvedPath : null;
        }
        catch { return null; }
    }

    /// <summary>The head of a bind reference that starts at the data record instead of the field's own data context.</summary>
    private const string RecordRefHead = "$record.";

    /// <summary>The path below the record of a bind reference that starts there ("$record.A.B" gives
    /// "A.B"); null for any other reference, and for one that addresses a set of nodes ("[*]").</summary>
    private static string? RecordRelativeRef(string? bindRef)
        => bindRef is not null && bindRef.StartsWith(RecordRefHead, StringComparison.Ordinal) && !bindRef.Contains('*')
            ? bindRef.Substring(RecordRefHead.Length)
            : null;

    /// <summary>The data path of a reference from the record: the record is the data node of the
    /// form's root subform, the first segment of every field path, whatever subforms lie between.</summary>
    private static string RecordDataPath(string[] parts, string fromRecord) => parts[0] + "." + fromRecord;

    /// <summary>True for a SOM segment that addresses an unnamed node by its class ("#field[1]").</summary>
    private static bool IsUnnamedSomSegment(string segment) => segment.StartsWith('#');

    /// <summary>One step of a template walk along a SOM path: the child the segment names - the
    /// segment's index picks among same-named siblings the template declares (a table's rows), and a
    /// repeated instance beyond those takes the first - or, for an unnamed segment, the child of that
    /// class at the segment's index.</summary>
    private static XmlNode? StepTemplate(XmlNode parent, string segment)
    {
        var m = SafeRegex.Match(segment, @"^(.+?)(?:\[(\d+)\])?$");
        var name = m.Success ? m.Groups[1].Value : segment;
        var index = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        if (!IsUnnamedSomSegment(name)) return FindTemplateChild(parent, name, index);
        return FindClassTemplateNode(parent, name[1..], index);
    }

    /// <summary>Find a subform or field child by name in an XFA template node: the <paramref name="index"/>-th
    /// of the same-named children when the template declares that many, else the first. A named area or subform
    /// set is a step of the field paths the template lists, so a segment may name one.</summary>
    private static XmlNode? FindTemplateChild(XmlNode parent, string name, int index = 0)
    {
        XmlNode? first = null;
        var seen = 0;
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var ln = child.LocalName;
            if (ln is "subform" or "field" or "exclGroup" or "draw" or "area" or "subformSet" && child.Attributes?["name"]?.Value == name)
            {
                first ??= child;
                if (seen++ == index) return child;
            }
        }
        if (first is not null) return first;
        // Also search inside unnamed subforms, and inside areas and subform sets, which no SOM name passes through
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if ((child.LocalName == "subform" && child.Attributes?["name"] is null) || child.LocalName is "area" or "subformSet")
            {
                var found = FindTemplateChild(child, name, index);
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
    /// Set an XFA field value by dotted path in the datasets XML.
    /// </summary>
    public void SetXfaFieldValue(string path, string value)
        => SetXfaFieldValues(new[] { new KeyValuePair<string, string>(path, value) });

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
                var node = XfaWriteNode(doc, pair.Key);
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
            // A single-stream form's template packet is as it was: the resolved template stays.
            if (ReferenceEquals(stream, _xfaSingleSource)) _xfaSingleRaw = newData;
        }
        catch { /* XFA datasets that do not parse or write back keep the stream as it was */ }
    }

    /// <summary>The content type an image's bytes declare by their signature: how an XFA image
    /// field's data node is tagged (a JPEG as <c>image/jpg</c>, the tag viewers write).</summary>
    internal static string ImageContentType(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length >= 3 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return "image/gif";
        if (b.Length >= 2 && b[0] == 0x42 && b[1] == 0x4D) return "image/bmp";
        return "image/jpg";
    }

    /// <summary>Write a base64 image into the XFA datasets node for
    /// <paramref name="path"/> and tag it with the given <paramref name="contentType"/>
    /// (e.g. <c>image/jpg</c>) — how XFA image fields carry their picture. A form without a
    /// datasets packet is given one. Returns false when the node can't be resolved.</summary>
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
        if (xml is null || stream is null)
        {
            stream = EnsureXfaDatasetsStreamInArray();
            if (stream is not null) xml = Encoding.UTF8.GetString(stream.RawData);
        }
        if (xml is null || stream is null) return false;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            if (XfaWriteNode(doc, path) is not XmlElement el)
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
        // Pages laid out from the packets are behind once a packet changes.
        OwnerDocument.XfaPacketsChanged();
        foreach (var objectNumber in dirtyReader.XRefTable.Entries.Values.Select(entry => entry.ObjectNumber))
        {
            var resolved = dirtyReader.Resolve(
                new PdfIndirectRef(objectNumber, 0));
            if (ReferenceEquals(resolved, stream))
            {
                OwnerDocument.MarkDirty(objectNumber, stream);
                return;
            }
            // Also check if the resolved object's Dict matches the stream's Dict
            if (resolved is PdfStream s && ReferenceEquals(s.Dict, stream.Dict))
            {
                OwnerDocument.MarkDirty(objectNumber, stream);
                return;
            }
        }
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
