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

    /// <summary>Strict variant used when the document carries NO form-packet instance
    /// record: a path binds only when every segment resolves to exactly ONE data node
    /// (a repeated data group's fields stay empty in that case).</summary>
    internal string? GetXfaFieldValueStrict(string path) => GetXfaFieldValueCore(path, strict: true);

    private string? GetXfaFieldValueCore(string path, bool strict)
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
        var result = FindXfaNodeValue(xml, path, strict);
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
            if (Environment.GetEnvironmentVariable("XFA_BIND_DEBUG") == "1")
                Console.Error.WriteLine($"[bind] path={path} bindRef={bindRef} resolvedPath={resolvedPath}");
            if (resolvedPath != path)
            {
                var resolved = FindXfaNodeValue(xml, resolvedPath, strict);
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
                    var resolved = FindXfaNodeValue(xml, candidate, strict);
                    if (Environment.GetEnvironmentVariable("XFA_BIND_DEBUG") == "1")
                        Console.Error.WriteLine($"[bind]   k={k} candidate={candidate} -> '{resolved}'");
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
