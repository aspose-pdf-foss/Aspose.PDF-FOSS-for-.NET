using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    /// <summary>What the template's bind rules say about a field's data node.</summary>
    private enum XfaDataBinding
    {
        /// <summary>The template walk or a binding it meets is beyond these rules: the name lookups decide.</summary>
        Unknown,
        /// <summary>The template holds no container at the path.</summary>
        Foreign,
        /// <summary>The field binds no data (bind match="none", or an unnamed field).</summary>
        Unbound,
        /// <summary>The field binds a node the data does not hold yet.</summary>
        Absent,
        /// <summary>The field's reference names, by its index, a later instance than the data holds: the field
        /// is empty, never the value of an earlier instance.</summary>
        Missing,
        Found,
    }

    /// <summary>The state of one binding walk: the data context reached so far, the record, the template
    /// container whose data node the context is, and the instance index of every container name the walk has
    /// passed.</summary>
    private sealed class XfaBindWalk
    {
        public XmlNode dataRoot = default!;
        public XmlNode? record;
        public XmlNode? context;
        public XmlNode? scope;
        public bool create;
        // The data holds no node for a container passed: only a reference from the record or the data root finds data below.
        public bool lost;
        public readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);
    }

    /// <summary>The data node a field binds to, found by walking the template along the field's SOM path
    /// and applying each container's bind rule: a presentation-only subform (match="none") or an unnamed one
    /// adds no data level, a subform or field bound by name takes the same-named data child of the
    /// instance index its path segment gives, and one bound by reference follows the reference from its
    /// data context, the record or the data root, a "[*]" taking the instance index of the container of
    /// that name. With <paramref name="create"/> a node the data lacks is added along the bound path, in
    /// the namespace of the group it is added to. A member of an exclusion group binds through the group:
    /// <paramref name="member"/> is then the member's template field and <paramref name="node"/> the group's.</summary>
    private static XfaDataBinding BindXfaDataNode(XmlDocument templateDoc, XmlNode dataRoot, string path, bool create, out XmlNode? node, out XmlNode? member)
    {
        node = null;
        member = null;
        var parts = SplitSomPath(path);
        XmlNode? template = templateDoc.DocumentElement;
        if (template is null || parts.Length < 2) return XfaDataBinding.Unknown;
        // The whole path is found in the template before the data is walked, so a write adds nothing for a path the
        // template does not hold.
        var containers = new XmlNode[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            template = StepTemplate(template, parts[i]);
            if (template is null) return XfaDataBinding.Foreign;
            containers[i] = template;
        }
        var count = parts.Length;
        if (count > 2 && containers[count - 1].LocalName == "field" && containers[count - 2].LocalName == "exclGroup")
            member = containers[--count];
        var walk = new XfaBindWalk { dataRoot = dataRoot, create = create, scope = containers[0] };
        for (var i = 0; i < count; i++)
        {
            var (name, index) = SomSegmentParts(parts[i]);
            walk.indices[name] = index;
            var step = i == 0 ? BindXfaRoot(walk, containers[i], name) : BindXfaStep(walk, containers[i], name, index, i == count - 1);
            if (step == XfaDataBinding.Absent && i < count - 1 && !create)
            {
                // A section the data holds no group for may still hold fields bound from the record.
                walk.lost = true;
                walk.context = null;
                continue;
            }
            if (step == XfaDataBinding.Unbound)
            {
                // A field that binds no data keeps its value under the data group its containers reach, by its name.
                if (BindXfaChild(walk, name, index, leaf: true) == XfaDataBinding.Found) node = walk.context;
                return step;
            }
            if (step != XfaDataBinding.Found) return step;
        }
        node = walk.context;
        return node is null ? XfaDataBinding.Absent : XfaDataBinding.Found;
    }

    /// <summary>A SOM segment's name and instance index ("detail[3]" gives detail and 3; no index is 0).</summary>
    private static (string Name, int Index) SomSegmentParts(string segment)
    {
        var open = segment.LastIndexOf('[');
        if (open <= 0 || !segment.EndsWith("]", StringComparison.Ordinal)) return (segment, 0);
        var digits = segment.Substring(open + 1, segment.Length - open - 2);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? (segment.Substring(0, open), index)
            : (segment, 0);
    }

    /// <summary>The root subform binds the record: the data root's child of its name, which a write
    /// adds to an empty data root. A root bound otherwise is left to the name lookups; so is a write into data whose
    /// record has another name, while a read there follows only the references from the record.</summary>
    private static XfaDataBinding BindXfaRoot(XfaBindWalk walk, XmlNode template, string name)
    {
        var match = FindBindElement(template)?.Attributes?["match"]?.Value;
        if (match is not null && match != "once") return XfaDataBinding.Unknown;
        var record = walk.dataRoot.ChildNodes.OfType<XmlElement>().FirstOrDefault();
        if (record is null && walk.create) record = AppendDataNode(walk.dataRoot, name);
        if (record is null) return XfaDataBinding.Absent;
        if (record.LocalName != name)
        {
            if (walk.create) return XfaDataBinding.Unknown;
            walk.record = record;
            walk.lost = true;
            return XfaDataBinding.Found;
        }
        walk.record = walk.context = record;
        return XfaDataBinding.Found;
    }

    /// <summary>One container below the root, by its bind rule. An unnamed container bound by name adds no data
    /// level; an unnamed field bound so is left to the name lookups.</summary>
    private static XfaDataBinding BindXfaStep(XfaBindWalk walk, XmlNode template, string name, int index, bool leaf)
    {
        if (TakesNoDataLevel(template)) return leaf ? XfaDataBinding.Unknown : XfaDataBinding.Found;
        var bind = FindBindElement(template);
        var match = bind?.Attributes?["match"]?.Value ?? "once";
        if (match == "dataRef")
        {
            walk.scope = template;
            return bind!.Attributes?["ref"]?.Value is { Length: > 0 } dataRef ? BindXfaReference(walk, dataRef.Trim(), index, leaf) : XfaDataBinding.Unknown;
        }
        if (match == "none") return leaf ? XfaDataBinding.Unbound : XfaDataBinding.Found;
        if (IsUnnamedSomSegment(name)) return leaf ? XfaDataBinding.Unknown : XfaDataBinding.Found;
        if (match == "global") return leaf ? BindXfaGlobal(walk, name) : XfaDataBinding.Unknown;
        if (match != "once") return XfaDataBinding.Unknown;
        // Same-named containers that bind by name in one data scope take its same-named nodes in turn, whichever
        // presentation-only subforms hold them: the second page's field takes the second node, not the first's.
        var at = BoundBeforeInScope(walk.scope!, template) + (index > 0 ? Math.Max(0, index - SameNamedSiblingsBefore(template, name)) : 0);
        walk.scope = template;
        return BindXfaChild(walk, name, at, leaf);
    }

    /// <summary>True for a container that groups what it holds on the page alone - an area or a subform set - and
    /// so takes no data node of its own and adds no level to the paths of the data below it.</summary>
    private static bool TakesNoDataLevel(XmlNode template) => template.LocalName is "area" or "subformSet";

    /// <summary>For each data scope of a parsed template, the order of every container in it that binds by name
    /// among the same-named ones, listed once: a fill asks at every field. The template documents the walks read
    /// are never changed, so an entry stays right while its document lives.</summary>
    private static readonly ConditionalWeakTable<XmlNode, Dictionary<XmlNode, int>> XfaScopeOrdinals = new();

    /// <summary>How many containers of the target's name bind by name before it in the data scope of
    /// <paramref name="scope"/>: the scope's children and those of every container between that adds no data level
    /// (a presentation-only or unnamed subform, an area, a subform set), in document order.</summary>
    private static int BoundBeforeInScope(XmlNode scope, XmlNode target)
        => XfaScopeOrdinals.GetValue(scope, ListScopeOrdinals).TryGetValue(target, out var before) ? before : 0;

    private static Dictionary<XmlNode, int> ListScopeOrdinals(XmlNode scope)
    {
        var ordinals = new Dictionary<XmlNode, int>();
        ListScopeOrdinals(scope, ordinals, new Dictionary<string, int>(StringComparer.Ordinal));
        return ordinals;
    }

    private static void ListScopeOrdinals(XmlNode parent, Dictionary<XmlNode, int> ordinals, Dictionary<string, int> seen)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child is not XmlElement element || element.LocalName is not ("subform" or "field" or "exclGroup" or "area" or "subformSet")) continue;
            var match = FindBindElement(element)?.Attributes?["match"]?.Value ?? "once";
            var named = element.GetAttribute("name");
            if (named.Length > 0 && match == "once" && element.LocalName is "subform" or "field" or "exclGroup")
            {
                seen.TryGetValue(named, out var before);
                ordinals[element] = before;
                seen[named] = before + 1;
            }
            else if (TakesNoDataLevel(element) || (element.LocalName == "subform" && (named.Length == 0 || match == "none")))
                ListScopeOrdinals(element, ordinals, seen);
        }
    }

    /// <summary>How many same-named containers precede this one among its own siblings: the part of its path
    /// index that names a declared sibling rather than a repeated instance.</summary>
    private static int SameNamedSiblingsBefore(XmlNode template, string name)
    {
        // Counted forwards: a node's previous sibling is found by a walk from its parent's first child.
        var count = 0;
        foreach (XmlNode sibling in template.ParentNode!.ChildNodes)
        {
            if (ReferenceEquals(sibling, template)) break;
            if (sibling.LocalName is "subform" or "field" or "exclGroup" or "draw" or "area" or "subformSet"
                && sibling.Attributes?["name"]?.Value == name)
                count++;
        }
        return count;
    }

    /// <summary>A field bound globally shares the value of its name: the node of that name in its data context,
    /// else in the nearest group above it that holds one; one is added to the context when the data has none.</summary>
    private static XfaDataBinding BindXfaGlobal(XfaBindWalk walk, string name)
    {
        for (var group = walk.context; group is not null; group = ReferenceEquals(group, walk.dataRoot) ? null : group.ParentNode)
        {
            var value = group.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == name && !c.ChildNodes.OfType<XmlElement>().Any());
            if (value is null) continue;
            walk.context = value;
            return XfaDataBinding.Found;
        }
        if (walk.context is null) return walk.lost ? XfaDataBinding.Absent : XfaDataBinding.Unknown;
        if (!walk.create) return XfaDataBinding.Absent;
        walk.context = AppendDataNode(walk.context, name);
        return XfaDataBinding.Found;
    }

    /// <summary>A data reference: "$" is the data context itself, "$.", "$record." and "$data." start at the
    /// context, the record and the data root, and a reference without a root at the context. Any other
    /// root ($form, "!", an absolute "xfa." expression) and any predicate are left to the name lookups.</summary>
    private static XfaDataBinding BindXfaReference(XfaBindWalk walk, string dataRef, int index, bool leaf)
    {
        if (dataRef == "$") return XfaDataBinding.Found;
        string rest;
        if (dataRef.StartsWith("$record", StringComparison.Ordinal))
        {
            if (walk.record is null) return XfaDataBinding.Unknown;
            walk.context = walk.record;
            walk.lost = false;
            rest = dataRef.Substring("$record".Length);
        }
        else if (dataRef.StartsWith("$data", StringComparison.Ordinal))
        {
            walk.context = walk.dataRoot;
            walk.lost = false;
            rest = dataRef.Substring("$data".Length);
        }
        else if (dataRef.StartsWith("xfa.", StringComparison.Ordinal)) return XfaDataBinding.Unknown;
        else rest = dataRef[0] == '$' ? dataRef.Substring(1) : "." + dataRef;
        if (rest.Length == 0) return XfaDataBinding.Found;
        if (rest[0] != '.' || rest.IndexOfAny(new[] { '(', ' ', '$', '!', '#' }) >= 0) return XfaDataBinding.Unknown;
        var segments = rest.Substring(1).Split('.');
        for (var s = 0; s < segments.Length; s++)
        {
            var segment = segments[s];
            var all = segment.EndsWith("[*]", StringComparison.Ordinal);
            var (name, at) = all ? (segment.Substring(0, segment.Length - 3), 0) : SomSegmentParts(segment);
            if (name.Length == 0 || name.IndexOfAny(new[] { '[', ']', '*' }) >= 0) return XfaDataBinding.Unknown;
            // "[*]" binds each instance to its own node: the one of the instance of the container of that name
            // the path passed, else of the container the reference belongs to.
            if (all) at = walk.indices.TryGetValue(name, out var passed) ? passed : index;
            var step = BindXfaChild(walk, name, at, leaf && s == segments.Length - 1);
            if (step == XfaDataBinding.Absent && !all && at > 0 && !walk.lost) return XfaDataBinding.Missing;
            if (step != XfaDataBinding.Found) return step;
        }
        return XfaDataBinding.Found;
    }

    /// <summary>The <paramref name="index"/>-th data child of that name under the context (a field's value
    /// may be the context's attribute of its name), added with the empty instances before it when the walk
    /// creates; absent otherwise.</summary>
    private static XfaDataBinding BindXfaChild(XfaBindWalk walk, string name, int index, bool leaf)
    {
        var context = walk.context;
        if (context is null) return walk.lost ? XfaDataBinding.Absent : XfaDataBinding.Unknown;
        var named = context.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == name).ToList();
        if (index < named.Count)
        {
            walk.context = named[index];
            return XfaDataBinding.Found;
        }
        if (leaf && index == 0 && named.Count == 0 && context.Attributes?[name] is { } attribute)
        {
            walk.context = attribute;
            return XfaDataBinding.Found;
        }
        if (!walk.create) return XfaDataBinding.Absent;
        XmlNode added = context;
        for (var n = named.Count; n <= index; n++) added = AppendDataNode(context, name);
        walk.context = added;
        return XfaDataBinding.Found;
    }

    /// <summary>A new data element under a group, in the group's namespace (a record under the data root in none).</summary>
    private static XmlElement AppendDataNode(XmlNode group, string name)
    {
        var doc = group.OwnerDocument!;
        var element = group.LocalName == "data" && group.NamespaceURI == XfaDataNs
            ? doc.CreateElement(name)
            : doc.CreateElement(group.Prefix, name, group.NamespaceURI);
        group.AppendChild(element);
        return element;
    }

    /// <summary>The node a value written to a field lands on: a node the data holds at the field's own path,
    /// else the node its template binding gives - none for a field that binds no data - else, when the data
    /// lacks that node, one the loose name walk finds by the path (never a same-named node anywhere in the
    /// data), else the bound node added. A form whose template the binding cannot follow keeps the name
    /// lookups.</summary>
    private XmlNode? XfaWriteNode(XmlDocument doc, string path)
    {
        XmlDocument? templateDoc;
        try { templateDoc = GetXfaTemplateDocumentResolved(); } catch { templateDoc = null; }
        var dataRoot = XfaDataRootOf(doc);
        var foreign = false;
        if (templateDoc is not null && dataRoot is { LocalName: "data" })
        {
            if (ExactXfaDataNode(dataRoot, path) is { } exact) return exact;
            switch (BindXfaDataNode(templateDoc, dataRoot, path, create: false, out var bound, out _))
            {
                case XfaDataBinding.Foreign:
                    foreign = true;
                    break;
                case XfaDataBinding.Found: return bound;
                case XfaDataBinding.Unbound:
                    // A field that binds no data still keeps the value the caller gives it: in its widget, where a
                    // static form has one, else under the data group its containers reach, where the reference
                    // library puts it and the standard conversion finds it.
                    if (Type == FormType.Static && FindFieldOrNull(path) is not null) return null;
                    if (bound is null) BindXfaDataNode(templateDoc, dataRoot, path, create: true, out bound, out _);
                    return bound;
                case XfaDataBinding.Absent:
                case XfaDataBinding.Missing:
                    // An instance the data lacks is added, never taken for the last instance it holds.
                    var loose = FindXfaNode(doc, path, strict: false, out _, exact: true);
                    if (loose is not null) return loose;
                    if (BindXfaDataNode(templateDoc, dataRoot, path, create: true, out bound, out _) == XfaDataBinding.Found) return bound;
                    break;
            }
        }
        var node = FindXfaNode(doc, path, strict: false, out var leafGuess);
        // A path the template does not hold is written along itself, never onto a same-named value elsewhere.
        if (leafGuess && foreign) node = null;
        if (node is null && ResolveSomToDataPath(path) is { } resolved)
            node = FindXfaNode(doc, resolved);
        return node ?? CreateXfaNodePath(doc, path);
    }

    /// <summary>The datasets' data element, as the walks of that document found it, else looked up.</summary>
    private static XmlNode? XfaDataRootOf(XmlDocument doc) => XfaDataRoots.TryGetValue(doc, out var known) ? known : FindDatasetsDataNode(doc);

    /// <summary>The data node at exactly the field's path - each segment the same-named child of its index -
    /// or null when any segment has none.</summary>
    private static XmlNode? ExactXfaDataNode(XmlNode dataRoot, string path)
    {
        XmlNode? current = dataRoot;
        foreach (var part in SplitSomPath(path))
        {
            var (name, index) = SomSegmentParts(part);
            current = current.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == name).Skip(index).FirstOrDefault();
            if (current is null) return null;
        }
        return current;
    }

    /// <summary>What a field's template binding says about its value: <paramref name="value"/> is the bound node's
    /// value when the data holds that node - for a member of an exclusion group, its on value when the group holds
    /// it, else its off value or nothing.</summary>
    private XfaDataBinding BoundXfaFieldValue(XmlDocument datasets, string path, out string? value)
    {
        value = null;
        try
        {
            var templateDoc = GetXfaTemplateDocumentResolved();
            var dataRoot = XfaDataRootOf(datasets);
            if (templateDoc is null || dataRoot is not { LocalName: "data" }) return XfaDataBinding.Unknown;
            var binding = BindXfaDataNode(templateDoc, dataRoot, path, create: false, out var node, out var member);
            if (binding == XfaDataBinding.Unbound && node is not null) value = node.InnerText;
            if (binding != XfaDataBinding.Found) return binding;
            value = node!.InnerText;
            if (member is not null)
            {
                var states = member.SelectNodes("*[local-name()='items'][1]/*")!.Cast<XmlNode>().Select(n => n.InnerText.Trim()).ToList();
                if (states.Count > 0 && value.Trim() != states[0]) value = states.Count > 1 ? states[1] : string.Empty;
            }
            return binding;
        }
        catch { return XfaDataBinding.Unknown; }
    }
}
