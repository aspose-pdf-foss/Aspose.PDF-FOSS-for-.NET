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
        var owner = new XmlDocument();
        var clone = (XmlElement)owner.ImportNode(root, true);
        owner.AppendChild(clone);
        if (dataRoot is null && formRoot is null) return clone;
        // Instance expansion is the FORM packet's job — it records the runtime
        // instance set a viewer saved. Without one the renderer draws exactly
        // ONE instance per template subform and leaves repeated data groups
        // unbound (five <detail> data rows under occur min=2
        // render as a single empty row; singular values all bind).
        if (formRoot is null) return clone;

        var used = new HashSet<XmlElement>();
        static bool IsGroup(XmlElement el) =>
            el.ChildNodes.OfType<XmlElement>().Any()
            || el.GetAttribute("dataNode", "http://www.xfa.org/schema/xfa-data/1.0/") == "dataGroup";

        // A NON-TRIVIAL form packet records the instance set a viewer last saved —
        // under it, a data-less min-0 subform absent from the packet stays removed.
        // Without such a record the merge runs from scratch and every subform gets
        // its <occur initial> instances (the spec default is 1 even when min is 0 —
        // Designer sections like optional bordered tables render once, empty).
        var packetRecords = formRoot is not null
            && formRoot.SelectNodes(".//*")!.OfType<XmlElement>().Any(c => c.LocalName == "subform");

        void Walk(XmlElement e, XmlElement? scope, XmlElement? fScope)
        {
            foreach (var sub in e.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == "subform").ToList())
            {
                var name = sub.GetAttribute("name");
                var occ = sub.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "occur");
                int max = 1, min = 1;
                if (occ is not null)
                {
                    var maxs = occ.GetAttribute("max"); var mins = occ.GetAttribute("min");
                    if (maxs.Length > 0 && int.TryParse(maxs, out var m)) max = m;
                    if (mins.Length > 0 && int.TryParse(mins, out var mi)) min = mi;
                    // occur with only a min (e.g. min="5"): the subform still repeats
                    // at least min times — an absent max never caps below it.
                    if (max >= 0 && max < min) max = min;
                }
                var avail = name.Length > 0 && scope is not null
                    ? scope.ChildNodes.OfType<XmlElement>()
                        .Where(c => c.LocalName == name && IsGroup(c) && !used.Contains(c)).ToList()
                    : new List<XmlElement>();
                var deepMatched = false;
                if (avail.Count == 0 && name.Length > 0 && scope is not null)
                {
                    // No direct child of the scope matches: fall back to scope DESCENDANTS
                    // (XFA's lenient matching for data nested deeper than the template).
                    avail = scope.SelectNodes(".//*")!.OfType<XmlElement>()
                        .Where(c => c.LocalName == name && IsGroup(c) && !used.Contains(c)).ToList();
                    // Same-name groups under DIFFERENT parents belong to different
                    // template sections (Debtor1's aliases vs Debtor2's): each
                    // repeating subform consumes only the first unconsumed parent's
                    // run, and the next section's search starts after it.
                    if (avail.Count > 1)
                    {
                        var firstParent = avail[0].ParentNode;
                        avail = avail.Where(c => ReferenceEquals(c.ParentNode, firstParent)).ToList();
                    }
                    deepMatched = avail.Count > 0;
                }
                if (avail.Count == 0 && name.Length > 0 && scope is not null && occ is not null)
                {
                    // Still nothing: a repeating subform whose own scope group is empty
                    // matches data one scope UP (a container bound to an empty marker
                    // group — e.g. <SubsequentSF/> — with the real repeat groups
                    // recorded as its siblings). Data-scope matching ascends.
                    for (var up = scope.ParentNode as XmlElement; up is not null && avail.Count == 0;
                         up = up.ParentNode as XmlElement)
                        avail = up.ChildNodes.OfType<XmlElement>()
                            .Where(c => c.LocalName == name && IsGroup(c) && !used.Contains(c)).ToList();
                }
                // Explicit <occur initial> (clamped up to min); the spec default is 1.
                int initial = 1;
                if (occ?.GetAttribute("initial") is { Length: > 0 } inis && int.TryParse(inis, out var ii))
                    initial = ii;
                if (initial < min) initial = min;
                int n = avail.Count == 0
                    // The data-less min=0 removal only applies when the document both
                    // carries data to bind against AND a form packet recording the saved
                    // instance set (the subform's absence from it means it was removed).
                    // A first-time merge instead creates the occur INITIAL instances.
                    // A data-less min>1 still renders its min instances (empty).
                    ? (occ is not null && dataRoot is not null && min == 0
                        ? (packetRecords ? 0 : initial)
                        : Math.Max(1, occ is null ? 1 : min))
                    : Math.Min(avail.Count, max < 0 ? avail.Count : Math.Max(max, 1));
                // Data present but fewer groups than the occur minimum: the template
                // minimum still governs the rendered instance count (trailing
                // instances stay empty).
                if (avail.Count > 0 && occ is not null && n < min) n = min;
                // Form-packet instances: when this subform's instanceManager appears in the
                // form DOM, honour the recorded instance count (never shrinking below the
                // data-driven count — stale packets must not drop bound data). Only an
                // OCCUR-LESS subform takes the boost: without <occur> the template alone
                // clamps to one instance and the packet is the only record of user-added
                // repeats, while a subform with an explicit <occur> already resolves its
                // count from the data (a packet layered on top double-counts).
                var fInst = new List<XmlElement>();
                var packetAuthoritative = false;
                if (fScope is not null && name.Length > 0)
                {
                    fInst = fScope.ChildNodes.OfType<XmlElement>()
                        .Where(c => c.LocalName == "subform" && c.GetAttribute("name") == name).ToList();
                    bool managed = fScope.ChildNodes.OfType<XmlElement>()
                        .Any(c => c.LocalName == "instanceManager" && c.GetAttribute("name") == "_" + name);
                    bool containerRepeats = e.ChildNodes.OfType<XmlElement>().Any(c => c.LocalName == "occur");
                    if (managed && packetRecords && !containerRepeats && fInst.Count == 0)
                    {
                        // A manager the packet records with ZERO instances is a
                        // DELIBERATE removal: the one template default renders
                        // UNBOUND (probed — five <detail> data rows under
                        // such a packet render as a single empty row). A manager
                        // with recorded instances is NOT trusted to cap the count:
                        // generator-produced packets under-record,
                        // and the data-driven merge stays the authority there.
                        packetAuthoritative = true;
                        n = 1;
                    }
                    else if (managed && occ is null && !containerRepeats && fInst.Count > n)
                        n = fInst.Count;
                }
                if (n == 0) { e.RemoveChild(sub); continue; }
                var instances = new List<XmlElement> { sub };
                for (int k = 1; k < n; k++)
                {
                    var copy = (XmlElement)sub.CloneNode(true);
                    e.InsertAfter(copy, instances[k - 1]);
                    instances.Add(copy);
                }
                for (int k = 0; k < instances.Count; k++)
                {
                    var fk = k < fInst.Count ? fInst[k] : null;
                    // Under an authoritative packet only RECORDED instances bind data;
                    // the template-default filler stays explicitly unbound.
                    if (packetAuthoritative && k >= fInst.Count)
                    {
                        instances[k].SetAttribute("data-idx", "-1");
                        Walk(instances[k], scope, null);
                        continue;
                    }
                    if (k < avail.Count)
                    {
                        used.Add(avail[k]);
                        instances[k].SetAttribute("data-idx", groups.Count.ToString());
                        groups.Add(avail[k]);
                        Walk(instances[k], avail[k], fk);
                    }
                    else
                    {
                        // A deep-matched repeat that ran out of groups is explicitly
                        // UNBOUND: its fields must stay empty rather than scavenge
                        // same-name leaves from another section's data.
                        if (deepMatched) instances[k].SetAttribute("data-idx", "-1");
                        Walk(instances[k], scope, fk);
                    }
                }
            }
        }
        Walk(clone, dataRoot, formRoot);
        return clone;
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
