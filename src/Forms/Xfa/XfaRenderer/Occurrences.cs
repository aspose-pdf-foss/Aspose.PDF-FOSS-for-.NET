using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>The pageArea named by a body subform's &lt;breakBefore&gt;, or null.</summary>
    private static XmlElement? BreakTarget(XmlElement body, List<XmlElement> pageAreas)
    {
        var brk = body.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "breakBefore");
        if (brk is null) return null;
        var targetType = brk.GetAttribute("targetType");
        var name = brk.GetAttribute("target").TrimStart('#');
        if (name.Length == 0) return null;
        if (targetType == "pageArea")
            return pageAreas.FirstOrDefault(p => p.GetAttribute("name") == name);
        if (targetType == "contentArea")
        {
            // "PageAreaName.ContentAreaName" (or a bare content-area name): the break
            // lands in that content area's OWNING pageArea - Designer switches the
            // continuation master this way (breakBefore
            // target="MasterPage2.MasterPage2Content" startNew="1").
            var dot = name.IndexOf('.');
            if (dot > 0 && pageAreas.FirstOrDefault(
                    p => p.GetAttribute("name") == name.Substring(0, dot)) is { } byPageAreaName)
                return byPageAreaName;
            var caName = dot > 0 ? name.Substring(dot + 1) : name;
            return pageAreas.FirstOrDefault(p => p.ChildNodes.OfType<XmlElement>()
                .Any(c => c.LocalName == "contentArea" && c.GetAttribute("name") == caName));
        }
        return null;
    }

    /// <summary>The content-area NAME a breakBefore targets ("Page.Order_ContentArea"
    /// names the area after the dot; a bare name is the area itself), or null for
    /// non-contentArea targets.</summary>
    private static string? BreakContentAreaName(XmlElement brk)
    {
        if (brk.GetAttribute("targetType") != "contentArea") return null;
        var name = brk.GetAttribute("target").TrimStart('#');
        if (name.Length == 0) return null;
        var dot = name.IndexOf('.');
        return dot >= 0 ? name.Substring(dot + 1) : name;
    }

    // Placeholder for xfa.layout.pageCount() in emitted text — the total is known only
    // after pagination, so it is substituted into the finished items in a post-pass.
    private const string PageCountSentinel = "\uE0C7";

    /// <summary>The datasets data root (the first element under &lt;xfa:data&gt;), or null.</summary>
    /// <summary>The XFA "form" packet's root subform — the runtime instance DOM a viewer
    /// recorded on save (instance managers + per-instance subform entries). Null when the
    /// document has no form packet.</summary>
    private static XmlElement? LoadFormRoot(Document doc)
    {
        try
        {
            var xml = doc.Form.GetXfaFormXml();
            if (string.IsNullOrEmpty(xml)) return null;
            var d = new XmlDocument();
            d.LoadXml(xml);
            return d.DocumentElement?.ChildNodes.OfType<XmlElement>()
                .FirstOrDefault(e => e.LocalName == "subform");
        }
        catch { return null; }
    }

    /// <summary>Copy each form-packet element's resolved <c>presence</c> onto its
    /// template counterpart. Counterparts pair by (tag, name) in sibling order —
    /// the expanded template's instance list and the packet's recorded instances
    /// run parallel. An element the packet gives no presence keeps the template's.</summary>
    private static void OverlayFormPresence(XmlElement tmpl, XmlElement form)
    {
        static bool IsBox(XmlElement e) =>
            e.LocalName is "subform" or "field" or "draw" or "exclGroup"
                or "pageSet" or "pageArea";
        var formKids = new Dictionary<string, List<XmlElement>>(StringComparer.Ordinal);
        foreach (var f in form.ChildNodes.OfType<XmlElement>().Where(IsBox))
        {
            var key = f.LocalName + "\0" + f.GetAttribute("name");
            if (!formKids.TryGetValue(key, out var list)) formKids[key] = list = new List<XmlElement>();
            list.Add(f);
        }
        if (formKids.Count == 0) return;
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in tmpl.ChildNodes.OfType<XmlElement>().Where(IsBox).ToList())
        {
            var key = t.LocalName + "\0" + t.GetAttribute("name");
            var idx = seen.TryGetValue(key, out var n) ? n : 0;
            seen[key] = idx + 1;
            if (!formKids.TryGetValue(key, out var list) || idx >= list.Count) continue;
            var f = list[idx];
            var pres = f.GetAttribute("presence");
            if (pres.Length > 0) t.SetAttribute("presence", pres);
            // The packet also records the resolved <value> (script-computed titles,
            // language-resolved labels): it replaces the template default. Empty
            // packet values (cleared placeholders) don't erase template text.
            var fVal = f.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "value");
            if (fVal is not null && !string.IsNullOrWhiteSpace(fVal.InnerText))
            {
                var tVal = t.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "value");
                var imported = (XmlElement)t.OwnerDocument!.ImportNode(fVal, true);
                if (tVal is not null) t.ReplaceChild(imported, tVal);
                else t.AppendChild(imported);
            }
            // Captions resolve at runtime too (language-selected field labels).
            // Only the caption's <value> is overlaid — the template caption keeps
            // its layout (reserve width, placement, font).
            var fCapVal = f.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "caption")
                ?.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "value");
            if (fCapVal is not null && !string.IsNullOrWhiteSpace(fCapVal.InnerText))
            {
                var tCap = t.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "caption");
                var imported = (XmlElement)t.OwnerDocument!.ImportNode(fCapVal, true);
                if (tCap is null)
                {
                    tCap = t.OwnerDocument!.CreateElement("caption", t.NamespaceURI);
                    t.AppendChild(tCap);
                }
                var tCapVal = tCap.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "value");
                if (tCapVal is not null) tCap.ReplaceChild(imported, tCapVal);
                else tCap.AppendChild(imported);
            }
            OverlayFormPresence(t, f);
        }
    }

    private static XmlElement? LoadDataRoot(Document doc)
    {
        try
        {
            var xml = doc.Form.GetXfaDatasetsXml();
            if (string.IsNullOrEmpty(xml)) return null;
            var d = new XmlDocument();
            d.LoadXml(xml);
            var data = d.DocumentElement?.ChildNodes.OfType<XmlElement>()
                .FirstOrDefault(e => e.LocalName == "data");
            return data?.ChildNodes.OfType<XmlElement>().FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Images embedded for external hrefs under /Catalog /Names /XFAImages
    /// (the Designer convention for template &lt;image href="…"&gt; artwork).</summary>
    private static Dictionary<string, byte[]> LoadXfaImages(Document doc)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var reader = doc.Reader;
            if (reader is null) return result;
            var names = reader.ResolveDict(reader.Catalog.Get("Names"));
            var tree = names is null ? null : reader.ResolveDict(names.Get("XFAImages"));
            var arr = tree is null ? null : reader.Resolve(tree.Get("Names")) as Core.PdfArray;
            if (arr is null) return result;
            for (int i = 0; i + 1 < arr.Count; i += 2)
            {
                if (arr[i] is Core.PdfString s && reader.Resolve(arr[i + 1]) is Core.PdfStream st)
                    result[System.Text.Encoding.UTF8.GetString(s.Value)] = reader.DecodeStream(st);
            }
        }
        catch { }
        return result;
    }

    /// <summary>Clone the form root and duplicate each repeatable subform once per bound
    /// data group. Binding is SCOPE-AWARE: a subform's candidate groups are the direct
    /// same-name children of its parent's bound data group (document-order consumption
    /// within that scope — sibling sections of the same name split the scope's groups
    /// between them, but a repeat in one table can never steal groups nested inside a
    /// DIFFERENT container's data). A template subform with no matching group passes the
    /// current scope through to its children. Bound instances carry a <c>data-idx</c>
    /// attribute indexing <paramref name="groups"/>; a data-less subform with occur
    /// min=0 is removed. When the document carries an XFA "form" packet (the runtime
    /// instance DOM a viewer saved), a subform whose form-DOM scope holds its
    /// <c>instanceManager</c> gets at least as many instances as the packet records —
    /// a user may have added instances beyond the bound data ("Add another" buttons),
    /// and those extra instances render with template defaults.</summary>
    private static XmlElement ExpandOccurrences(XmlElement root, XmlElement? dataRoot, List<XmlElement> groups, XmlElement? formRoot = null)
    {
        var ox = new OccurrenceExpandState();
        ox.root = root;
        ox.dataRoot = dataRoot;
        ox.groups = groups;
        ox.formRoot = formRoot;
        ox.owner = new XmlDocument();
        ox.clone = (XmlElement)ox.owner.ImportNode(ox.root, true);
        ox.owner.AppendChild(ox.clone);
        if (ox.dataRoot is null && ox.formRoot is null) return ox.clone;
        // Instance expansion is the FORM packet's job — it records the runtime
        // instance set a viewer saved. Without one the renderer draws exactly
        // ONE instance per template subform and leaves repeated data groups
        // unbound (five <detail> data rows under occur min=2
        // render as a single empty row; singular values all bind).
        if (ox.formRoot is null) return ox.clone;

        ox.used = new HashSet<XmlElement>();
        ox.packetRecords = ox.formRoot is not null
            && ox.formRoot.SelectNodes(".//*")!.OfType<XmlElement>().Any(c => c.LocalName == "subform");

        OccWalk(ox, ox.clone, ox.dataRoot, ox.formRoot);
        return ox.clone;
    }

    /// <summary>Resolve a bound value for <paramref name="name"/>: the nearest expanded
    /// ancestor's data group child of that name, else null. When the data group carries
    /// SEVERAL same-name value nodes (repeated fields, e.g. a TOC's page-number column),
    /// the k-th same-name template field binds to the k-th data node in document order.</summary>
    private static string? BoundValue(Ctx ctx, XmlElement e, string name)
    {
        for (XmlElement? a = e; a is not null; a = a.ParentNode as XmlElement)
        {
            var idx = a.GetAttribute("data-idx");
            if (idx.Length == 0) continue;
            // Sentinel: an explicitly UNBOUND repeat instance — its fields stay
            // empty (the empty string blocks the flat datasets fallback too).
            if (idx == "-1") return string.Empty;
            if (!int.TryParse(idx, out var i) || i < 0 || i >= ctx.Groups.Count) continue;
            var matches = ctx.Groups[i].ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == name).ToList();
            if (matches.Count == 0) continue;
            if (matches.Count == 1) return matches[0].InnerText;
            int ord = 0;
            foreach (var f in a.SelectNodes(".//*")!.OfType<XmlElement>())
            {
                if (f.LocalName != e.LocalName || f.GetAttribute("name") != name) continue;
                if (ReferenceEquals(f, e)) break;
                ord++;
            }
            return matches[Math.Min(ord, matches.Count - 1)].InnerText;
        }
        return null;
    }
}
