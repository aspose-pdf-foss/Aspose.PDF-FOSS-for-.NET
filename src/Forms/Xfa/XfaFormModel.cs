using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

/// <summary>
/// A node in the merged XFA form DOM: a subform / field / exclGroup / pageArea from the
/// template, carrying the mutable runtime state that scripts read and write (presence,
/// access, rawValue). The tree mirrors the rendered structure (body subforms + master
/// <c>pageArea</c> content) so a node's <see cref="SomPath"/> equals the flat AcroForm /T.
/// </summary>
internal sealed class XfaNode
{
    public string Name = string.Empty;
    public string Kind = string.Empty;                 // subform / subformSet / area / field / exclGroup / pageArea / pageSet
    public XmlElement Template = default!;
    public XfaNode? Parent;
    public readonly List<XfaNode> Children = new();

    /// <summary>Full dotted SOM path with indices, e.g. <c>TopForm[0].Page1[0]…Address_1[0]</c>.</summary>
    public string SomPath = string.Empty;

    /// <summary>XFA presence: visible / hidden / invisible / inactive. Mutable by scripts.</summary>
    public string Presence = "visible";
    /// <summary>XFA access: open / readOnly / protected / nonInteractive. Mutable by scripts.</summary>
    public string Access = "open";
    /// <summary>Bound data value (null when unbound or empty — XFA treats an empty field as null).
    /// A render model resolves it on the first read through <see cref="RawSource"/>, so only the
    /// values a script asks for are looked up.</summary>
    public string? RawValue
    {
        get
        {
            if (RawSource is { } source) { RawSource = null; _rawValue = source(this); }
            return _rawValue;
        }
        set
        {
            RawSource = null;
            if (_rawValue != value) Writes++;
            _rawValue = value;
        }
    }
    private string? _rawValue;
    public Func<XfaNode, string?>? RawSource;
    /// <summary>How often a script changed the value: a calculate that read it is run again after each change,
    /// also when a later script restores the value it had read.</summary>
    public int Writes;
    /// <summary>True once a script assigned the value (the render pass shows such a value).</summary>
    public bool RawAssigned;
    /// <summary>True once a render dropped a script's write of the value as one it could not follow: the
    /// script's result is unknown then, whatever an earlier statement of it assigned.</summary>
    public bool RawWriteDropped;
    /// <summary>The size and position attributes a script assigned (w, h, x, y and the min/max sizes), or null.</summary>
    public Dictionary<string, string>? Geometry;
    /// <summary>The caption text a script assigned (the render pass shows it), or null.</summary>
    public string? CaptionText;
    /// <summary>The presence the node was built with, to tell what a script changed.</summary>
    public string BuiltPresence = "visible";

    // AcroForm emit info (fields/exclGroups only).
    public bool IsField;                               // field-with-ui or exclGroup — an emittable widget
    public string Ft = "Tx";
    public long Ff;
    public bool IsImage;                               // <ui><imageEdit>: RawValue is base64 picture data

    /// <summary>True when this node or any ancestor has a non-rendering presence.</summary>
    public bool EffectiveHidden
    {
        get
        {
            for (var n = this; n is not null; n = n.Parent)
                if (n.Presence is "hidden" or "invisible" or "inactive")
                    return true;
            return false;
        }
    }

    /// <summary>First child named <paramref name="name"/> (SOM navigation is name-based). A nameless
    /// container is transparent to names, so its children are reached as if they were this node's.</summary>
    public XfaNode? Child(string name)
    {
        foreach (var c in Children)
            if (c.Name == name) return c;
        foreach (var c in Children)
            if (c.Name.Length == 0 && c.Child(name) is { } inner) return inner;
        return null;
    }

    /// <summary>The nearest <c>pageArea</c> ancestor (master page), or null if this node is body
    /// content. Master content is replicated once per physical page; body content is emitted once.</summary>
    public XfaNode? PageAreaAncestor
    {
        get
        {
            for (var n = this; n is not null; n = n.Parent)
                if (n.Kind == "pageArea") return n;
            return null;
        }
    }
}

/// <summary>
/// Builds the merged form DOM (<see cref="XfaNode"/> tree) from an XFA template and resolves
/// each field's initial rawValue via a caller-supplied binding resolver (so the real XFA
/// data-binding — <see cref="Form.GetXfaFieldValue"/> — is reused rather than re-implemented).
/// Also provides SOM path resolution used by the script runtime.
/// </summary>
internal sealed class XfaFormModel
{
    public XfaNode Root = default!;
    private readonly List<XfaNode> _all = new();

    /// <summary>The data the form is merged with (the xfa:data element) that a render's scripts read through
    /// xfa.data; null when the file carries none.</summary>
    public XmlElement? Data;

    /// <summary>All nodes in document order.</summary>
    public IReadOnlyList<XfaNode> All => _all;

    /// <summary>First node with the given name (used to locate the page-count helper fields).</summary>
    public XfaNode? FindByName(string name)
    {
        foreach (var n in _all) if (n.Name == name) return n;
        return null;
    }

    /// <summary>Build the model from a template element. <paramref name="resolveRawValue"/> maps a
    /// field's SOM path to its bound data value (empty string is normalised to null per XFA).</summary>
    public static XfaFormModel Build(XmlElement templateRoot, Func<string, string?>? resolveRawValue)
    {
        var model = new XfaFormModel();
        // The template root (<template>) usually holds a single root subform (TopForm). Create a
        // synthetic container so top-level subforms get their SOM segment, then discard it as Root.
        var synthetic = new XfaNode { Name = "", Kind = "template", Template = templateRoot };
        model.WalkChildren(templateRoot, synthetic, string.Empty, resolveRawValue);
        // Root = the single top subform if there is exactly one, else the synthetic container.
        model.Root = synthetic.Children.Count == 1 ? synthetic.Children[0] : synthetic;
        model.Root.Parent = null;
        return model;
    }

    // Render model only: the tree the pages are painted from. Its draws and nameless boxes are nodes
    // too (a script hides them, and "parent" counts every level), the master pages come from the
    // template the renderer paints them from, and values are resolved when a script reads them.
    private bool _render;
    private XmlElement? _masterSource;
    private XmlElement? _bodyRoot;
    private Func<XfaNode, string?>? _rawSource;

    /// <summary>Build the model of a render: <paramref name="root"/> is the root subform whose body is
    /// laid out (its repeated subforms already expanded), <paramref name="masterSource"/> the root
    /// subform whose pageSet the master pages are painted from.</summary>
    public static XfaFormModel BuildForRender(XmlElement root, XmlElement masterSource, Func<XfaNode, string?> rawSource)
    {
        var model = new XfaFormModel { _render = true, _bodyRoot = root, _masterSource = masterSource, _rawSource = rawSource };
        var synthetic = new XfaNode { Name = "", Kind = "template", Template = root };
        var top = model.MakeNode(root, synthetic, string.Empty, "subform", root.GetAttribute("name"));
        model.WalkChildren(root, top, top.SomPath, null);
        top.Parent = null;
        model.Root = top;
        return model;
    }

    /// <summary>The pageSet of the master source that stands where <paramref name="pageSet"/> stands in the body tree.</summary>
    private XmlNode MasterPageSet(XmlNode pageSet)
    {
        if (_masterSource is null || !ReferenceEquals(pageSet.ParentNode, _bodyRoot)) return pageSet;
        int index = XfaFormEngine.CountPrecedingSiblings(pageSet, "pageSet", pageSet.Attributes?["name"]?.Value ?? "");
        foreach (XmlNode candidate in _masterSource.ChildNodes)
        {
            if (candidate.NodeType != XmlNodeType.Element || candidate.LocalName != "pageSet") continue;
            if ((candidate.Attributes?["name"]?.Value ?? "") != (pageSet.Attributes?["name"]?.Value ?? "")) continue;
            if (index-- == 0) return candidate;
        }
        return pageSet;
    }

    private void WalkChildren(XmlNode node, XfaNode parent, string parentPath, Func<string, string?>? resolve)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var local = child.LocalName;

            if (local == "pageSet")
            {
                WalkPageSet(_render ? MasterPageSet(child) : child, parent, parentPath, resolve);
                continue;
            }

            var nameAttr = child.Attributes?["name"]?.Value;
            if (_render && local is "subform" or "subformSet" or "area" or "field" or "exclGroup" or "draw")
            {
                var n = MakeNode(child, parent, parentPath, local, nameAttr ?? "");
                if (local is "field" or "exclGroup" or "draw") n.RawSource = _rawSource;
                else WalkChildren(child, n, n.SomPath, resolve);
                if (local == "exclGroup") AddGroupMembers(child, n);
            }
            else if (local is "subform" or "subformSet" or "area")
            {
                if (nameAttr is null) { WalkChildren(child, parent, parentPath, resolve); continue; }
                var n = MakeNode(child, parent, parentPath, local, nameAttr);
                WalkChildren(child, n, n.SomPath, resolve);
            }
            else if (local is "field" or "exclGroup")
            {
                if (nameAttr is null) continue;
                var n = MakeNode(child, parent, parentPath, local, nameAttr);
                var ui = XfaFormEngine.FindUiControl(child);
                // A field renders a widget only via its <ui>; exclGroup's controls live on its options.
                bool emittable = local == "exclGroup" || (ui is not null && ui.LocalName != "barcode");
                n.IsField = emittable;
                if (emittable)
                {
                    var (ft, ff) = XfaFormEngine.XfaUiToFieldType(child, local);
                    n.Ft = ft; n.Ff = ff;
                    n.IsImage = ui?.LocalName == "imageEdit";
                    n.RawValue = Nullify(resolve?.Invoke(n.SomPath));
                }
                // exclGroup / field: do not descend (the group is itself the field).
            }
            else
            {
                WalkChildren(child, parent, parentPath, resolve);   // structural wrapper
            }
        }
    }

    /// <summary>The named fields of an exclusive group, so a script reaches each one by name: a member's value is
    /// its own on value - its first item - while the group's value is that, and null otherwise, as a member of
    /// a group holds its on or its off state.</summary>
    private void AddGroupMembers(XmlNode group, XfaNode groupNode)
    {
        foreach (XmlNode member in group.ChildNodes)
        {
            if (member.NodeType != XmlNodeType.Element || member.LocalName != "field") continue;
            var name = member.Attributes?["name"]?.Value;
            if (string.IsNullOrEmpty(name)) continue;
            var on = member.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "items")?
                .ChildNodes.OfType<XmlElement>().FirstOrDefault()?.InnerText.Trim();
            var node = MakeNode(member, groupNode, groupNode.SomPath, "field", name);
            node.RawSource = _ => !string.IsNullOrEmpty(on) && groupNode.RawValue?.Trim() == on ? on : null;
        }
    }

    private void WalkPageSet(XmlNode pageSet, XfaNode parent, string parentPath, Func<string, string?>? resolve)
    {
        var psName = pageSet.Attributes?["name"]?.Value;
        var psNode = psName is not null ? MakeNode(pageSet, parent, parentPath, "pageSet", psName) : parent;
        var psPath = psName is not null ? psNode.SomPath : parentPath;
        foreach (XmlNode area in pageSet.ChildNodes)
        {
            if (area.NodeType != XmlNodeType.Element || area.LocalName != "pageArea") continue;
            var paName = area.Attributes?["name"]?.Value;
            var paNode = paName is not null ? MakeNode(area, psNode, psPath, "pageArea", paName) : psNode;
            WalkChildren(area, paNode, paNode.SomPath, resolve);
        }
    }

    private XfaNode MakeNode(XmlNode el, XfaNode parent, string parentPath, string kind, string name)
    {
        // A nameless box adds no segment: its children are addressed through it.
        var seg = name.Length == 0 ? "" : $"{Form.EscapeSomSegment(name)}[{XfaFormEngine.CountPrecedingSiblings(el, kind, name)}]";
        var node = new XfaNode
        {
            Name = name,
            Kind = kind,
            Template = (XmlElement)el,
            Parent = parent,
            SomPath = parentPath.Length > 0 && seg.Length > 0 ? $"{parentPath}.{seg}" : parentPath + seg,
            Presence = el.Attributes?["presence"]?.Value ?? "visible",
            Access = el.Attributes?["access"]?.Value ?? "open",
        };
        node.BuiltPresence = node.Presence;
        parent.Children.Add(node);
        _all.Add(node);
        return node;
    }

    private static string? Nullify(string? v) => string.IsNullOrEmpty(v) ? null : v;
}
