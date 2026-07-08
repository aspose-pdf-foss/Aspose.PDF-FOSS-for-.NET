using System.Xml;

namespace Aspose.Pdf.Forms;

/// <summary>
/// XmlNode-based accessor for the document's XFA packets (template / datasets /
/// config / form / xdp). Distinct from <see cref="XfaAccessor"/>; this surface
/// matches the Aspose.Pdf public API. Backed by the existing XFA storage in
/// <see cref="Form"/>; nodes are returned via temporary XmlDocuments built from
/// the underlying XML strings.
/// </summary>
public sealed class XFA
{
    private readonly Form _form;
    private readonly XmlNamespaceManager _nsMgr;

    internal XFA(Form form)
    {
        _form = form;
        var nt = new NameTable();
        _nsMgr = new XmlNamespaceManager(nt);
        _nsMgr.AddNamespace("tpl", "http://www.xfa.org/schema/xfa-template/2.6/");
        _nsMgr.AddNamespace("xfa", "http://www.xfa.org/schema/xfa-data/1.0/");
        _nsMgr.AddNamespace("datasets", "http://www.xfa.org/schema/xfa-datasets/2.6/");
        _nsMgr.AddNamespace("xdp", "http://ns.adobe.com/xdp/");
    }

    /// <summary>Namespace manager used when navigating the XFA XML packets. The <c>tpl</c>
    /// prefix is re-pointed at the document's actual xfa-template namespace version (2.6 /
    /// 2.8 / 3.0 / …) so a caller's <c>tpl:field</c> XPath matches regardless of version.</summary>
    public XmlNamespaceManager NamespaceManager { get { EnsureTemplateNamespace(); return _nsMgr; } }

    private bool _tplNsResolved;
    /// <summary>Re-register the <c>tpl</c> prefix with the template packet's real namespace
    /// URI (the hard-coded default is only the 2.6 version) so version-specific XPath
    /// queries against the template resolve.</summary>
    private void EnsureTemplateNamespace()
    {
        if (_tplNsResolved) return;
        _tplNsResolved = true;
        var ns = Template?.NamespaceURI;
        if (!string.IsNullOrEmpty(ns))
            _nsMgr.AddNamespace("tpl", ns);
    }

    /// <summary>Method-form alias for <see cref="NamespaceManager"/> (Aspose.Pdf API shape).</summary>
    public XmlNamespaceManager GetNamespaceManager() { EnsureTemplateNamespace(); return _nsMgr; }

    /// <summary>Resolve a template SOM path (e.g.
    /// "FormB101[0].Page1[0]…CourtName[0]") to its bound datasets value, applying the
    /// XFA template-to-data binding (skips presentation-only <c>bind match="none"</c>
    /// subforms, honours <c>bind match="dataRef"</c>). Backs <see cref="XfaField.Value"/>.</summary>
    internal string? ResolveFieldValueBySom(string somPath) => _form.GetXfaFieldValue(somPath);

    /// <summary>Return the XFA datasets data node for the named field (dotted path,
    /// e.g. "form1[0].#subform[0].ImageField[0]"), or null when absent. Anonymous
    /// template-only container segments (names starting with '#') are skipped, since
    /// the datasets tree flattens them.</summary>
    public XmlNode? GetFieldNode(string fieldName)
    {
        var datasets = Datasets;
        if (datasets is null) return null;
        // Datasets payload lives under <xfa:data>; descend into it when present.
        XmlNode root = datasets;
        foreach (XmlNode child in datasets.ChildNodes)
            if (child is XmlElement de && de.LocalName == "data") { root = de; break; }
        return WalkDatasetsByPath(root, fieldName);
    }

    private static XmlNode? WalkDatasetsByPath(XmlNode root, string path)
    {
        XmlNode? current = root;
        // XFA has a `Form` PROPERTY that shadows the Form class, so qualify fully.
        foreach (var part in Aspose.Pdf.Forms.Form.SplitSomPath(path))
        {
            if (current is null) return null;
            var m = System.Text.RegularExpressions.Regex.Match(part, @"^(.+)\[(\d+)\]$");
            var name = m.Success ? m.Groups[1].Value : part;
            var idx = m.Success ? int.Parse(m.Groups[2].Value) : 0;
            var seen = 0; XmlNode? found = null;
            foreach (XmlNode ch in current.ChildNodes)
            {
                if (ch is not XmlElement el || el.LocalName != name) continue;
                if (seen == idx) { found = el; break; }
                seen++;
            }
            // Anonymous container ("#subform[0]") absent in datasets: stay at the
            // current node and resolve the next real segment against it.
            if (found is null && name.StartsWith('#')) continue;
            current = found;
        }
        return current;
    }

    /// <summary>The XFA Datasets node, or null when the document has no XFA datasets.</summary>
    public XmlNode? Datasets => LoadAsNode(_form.GetXfaDatasetsXml());

    /// <summary>The XFA Template node, or null when absent.</summary>
    public XmlNode? Template => LoadAsNode(_form.GetXfaTemplateXml());

    /// <summary>The XFA Config node. Not yet exposed by the library; returns null.</summary>
    public XmlNode? Config => null;

    /// <summary>The XFA Form node. Not yet exposed by the library; returns null.</summary>
    public XmlNode? Form => null;

    /// <summary>Composite XDP document wrapping all XFA packets, or null when no XFA is present.</summary>
    public XmlDocument? XDP
    {
        get
        {
            if (!_form.IsXfa) return null;
            var doc = new XmlDocument();
            var root = doc.CreateElement("xdp", "xdp", "http://ns.adobe.com/xdp/");
            doc.AppendChild(root);
            AppendPacket(doc, root, "template", _form.GetXfaTemplateXml());
            AppendPacket(doc, root, "datasets", _form.GetXfaDatasetsXml());
            return doc;
        }
    }

    /// <summary>The XFA field names enumerated from the template.</summary>
    public string[] FieldNames => _form.GetXfaFieldNames();

    /// <summary>Read / write an XFA field by full name (dotted path).</summary>
    public string this[string fieldName]
    {
        get => _form.GetXfaFieldValue(fieldName) ?? string.Empty;
        set
        {
            _form.SetXfaFieldValue(fieldName, value);
            // Keep the AcroForm field in sync for static XFA forms so reading the
            // typed field (or saving) reflects the value written through XFA.
            _form.ApplyXfaValueToAcroField(fieldName, value);
        }
    }

    /// <summary>Return the XFA template node for the named field, or null when unknown.</summary>
    public XmlNode? GetFieldTemplate(string fieldName)
    {
        var tpl = Template;
        if (tpl?.OwnerDocument is null) return null;
        EnsureTemplateNamespace();

        // Rare: a node whose @name is literally the full dotted path.
        var direct = tpl.SelectSingleNode($".//*[@name='{fieldName}']", _nsMgr);
        if (direct is not null) return direct;

        // Template nodes are named by their leaf name only (no dotted path / occurrence
        // index), so walk the hierarchy segment by segment — named segments descend
        // through the matching subform/exclGroup/field node, anonymous class segments
        // ("#subform[0]") through the nth child of that class.
        return Aspose.Pdf.Forms.Form.WalkTemplateBySomPath(tpl, fieldName);
    }

    /// <summary>Return every XFA template field node.</summary>
    public XmlNodeList? GetFieldTemplates()
    {
        var tpl = Template;
        return tpl?.SelectNodes(".//*[@name]", _nsMgr);
    }

    /// <summary>
    /// Set the bytes of an XFA image field. Stored on the datasets node; the
    /// appearance stream is not currently regenerated.
    /// </summary>
    public void SetFieldImage(string fieldName, System.IO.Stream image)
    {
        if (image is null) return;
        using var ms = new System.IO.MemoryStream();
        if (image.CanSeek) image.Position = 0;
        image.CopyTo(ms);
        var base64 = System.Convert.ToBase64String(ms.ToArray());
        _form.SetXfaFieldValue(fieldName, base64);
    }

    private static XmlNode? LoadAsNode(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return null;
        var doc = new XmlDocument();
        try { doc.LoadXml(xml); } catch { return null; }
        return doc.DocumentElement;
    }

    private static void AppendPacket(XmlDocument owner, XmlElement parent, string localName, string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return;
        var tmp = new XmlDocument();
        try { tmp.LoadXml(xml); } catch { return; }
        if (tmp.DocumentElement is null) return;
        var imported = owner.ImportNode(tmp.DocumentElement, deep: true);
        var wrapper = owner.CreateElement(localName);
        wrapper.AppendChild(imported);
        parent.AppendChild(wrapper);
    }
}
