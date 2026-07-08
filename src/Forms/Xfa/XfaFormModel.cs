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
    /// <summary>Bound data value (null when unbound or empty — XFA treats an empty field as null).</summary>
    public string? RawValue;

    // AcroForm emit info (fields/exclGroups only).
    public bool IsField;                               // field-with-ui or exclGroup — an emittable widget
    public string Ft = "Tx";
    public long Ff;

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

    /// <summary>First child named <paramref name="name"/> (SOM navigation is name-based).</summary>
    public XfaNode? Child(string name)
    {
        foreach (var c in Children)
            if (c.Name == name) return c;
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

    private void WalkChildren(XmlNode node, XfaNode parent, string parentPath, Func<string, string?>? resolve)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var local = child.LocalName;

            if (local == "pageSet")
            {
                WalkPageSet(child, parent, parentPath, resolve);
                continue;
            }

            var nameAttr = child.Attributes?["name"]?.Value;
            if (local is "subform" or "subformSet" or "area")
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
        int idx = XfaFormEngine.CountPrecedingSiblings(el, kind, name);
        var seg = $"{Form.EscapeSomSegment(name)}[{idx}]";
        var node = new XfaNode
        {
            Name = name,
            Kind = kind,
            Template = (XmlElement)el,
            Parent = parent,
            SomPath = parentPath.Length > 0 ? $"{parentPath}.{seg}" : seg,
            Presence = el.Attributes?["presence"]?.Value ?? "visible",
            Access = el.Attributes?["access"]?.Value ?? "open",
        };
        parent.Children.Add(node);
        _all.Add(node);
        return node;
    }

    private static string? Nullify(string? v) => string.IsNullOrEmpty(v) ? null : v;
}
