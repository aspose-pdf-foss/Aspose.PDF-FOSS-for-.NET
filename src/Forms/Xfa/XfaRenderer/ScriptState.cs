using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>Runs the form's load-time scripts once over the tree the pages are painted from and
    /// writes what they resolved back onto it: a box a script hid or showed carries that presence as
    /// its attribute, which is all the layout reads. The template and its scripts decide the presence;
    /// the saved form packet's is overlaid afterwards only for a form rendered as signed.</summary>
    private static void ResolveScriptState(XfaRenderState xr)
    {
        // A template without a script has nothing to resolve: the tree stays as it is.
        if (xr.root is null || xr.template.GetElementsByTagName("script").Item(0) is null) return;
        var ctx = new Ctx
        {
            RawValue = xr.rawValue, Groups = xr.groups, DataRoot = xr.dataRoot, StrictBinding = xr.formRoot is null,
        };
        Dictionary<string, List<XmlElement>>? leaves = null;
        var model = XfaFormModel.BuildForRender(xr.root, xr.origRoot, n => ScriptRawValue(ctx, n.Template, ref leaves));
        model.Data = xr.dataRoot?.ParentNode as XmlElement;
        var runner = XfaScriptRunner.RunForRender(model);
        // The fields whose layout-ready scripts ask for page numbers are answered page by page as
        // they are painted; a form with none keeps no script state past this point.
        if (runner?.PageScriptNodes() is { Count: > 0 } paged)
            xr.pageScripts = new PageScripts(runner, paged, e => ScriptRawValue(ctx, e, ref leaves, withDefault: false) is not null);
        foreach (var n in model.All)
        {
            if (n.Presence != n.BuiltPresence) n.Template.SetAttribute("presence", n.Presence);
            if (n.CaptionText is { } captionText) SetCaptionText(n.Template, captionText);
            if (n.Geometry is { } geometry)
                foreach (var attribute in geometry) n.Template.SetAttribute(attribute.Key, attribute.Value);
            // A value a script assigned shows only where the form's data leaves the field empty: the data
            // the file carries for a field is preferred to a recomputation of it. A template default gives
            // way to the script only in a form with no saved state: a saved state's values are data too.
            if (n.RawAssigned && n.Kind == "field" && !string.IsNullOrEmpty(n.RawValue)
                && ScriptRawValue(ctx, n.Template, ref leaves, withDefault: xr.formRoot is not null) is null)
                n.Template.SetAttribute(ScriptValueAttribute, n.RawValue);
            // A field the template computes shows what its calculation gives where the data holds no node for it:
            // the value a saved form state stored there is an earlier result, stale once the inputs changed.
            // A calculation the render could not follow to its last write of the value gives no result: a
            // placeholder its first statement assigned must not replace the value the saved state holds.
            if (n.RawAssigned && !n.RawWriteDropped && n.Kind == "field" && !string.IsNullOrEmpty(n.RawValue)
                && FirstChild(n.Template, "calculate") is not null && !HasDataNode(ctx, n.Template, ref leaves))
                n.Template.SetAttribute(CalculatedValueAttribute, n.RawValue);
        }
    }

    /// <summary>True when the form's data holds a node for the field: one its binding reaches, or a data leaf of its name.</summary>
    private static bool HasDataNode(Ctx ctx, XmlElement e, ref Dictionary<string, List<XmlElement>>? leaves)
    {
        if (BindsNoData(e)) return false;
        var name = e.GetAttribute("name");
        if (BoundValue(ctx, e, name) is not null) return true;
        if (ctx.DataRoot is null || name.Length == 0) return false;
        leaves ??= DataLeavesByName(ctx.DataRoot);
        return leaves.ContainsKey(name);
    }

    /// <summary>Marks a field of the render tree with the value its own calculation gave it.</summary>
    private const string CalculatedValueAttribute = "data-calculated-value";

    /// <summary>The value a field's own calculation gave it where the data holds none, or null.</summary>
    private static string? CalculatedValue(XmlElement e) =>
        e.GetAttribute(CalculatedValueAttribute) is { Length: > 0 } calculated ? calculated : null;

    /// <summary>Gives a box the caption text a script assigned: the caption keeps its layout (reserve,
    /// placement, font) and takes the text as its value.</summary>
    private static void SetCaptionText(XmlElement e, string text)
    {
        var caption = FirstChild(e, "caption");
        if (caption is null) caption = (XmlElement)e.AppendChild(e.OwnerDocument.CreateElement("caption", e.NamespaceURI))!;
        var value = e.OwnerDocument.CreateElement("value", e.NamespaceURI);
        var textNode = e.OwnerDocument.CreateElement("text", e.NamespaceURI);
        textNode.InnerText = text;
        value.AppendChild(textNode);
        if (FirstChild(caption, "value") is { } old) caption.ReplaceChild(value, old);
        else caption.AppendChild(value);
    }

    /// <summary>The script run kept for the fields whose text depends on the page they are painted on.</summary>
    private sealed class PageScripts
    {
        private readonly XfaScriptRunner _runner;
        private readonly Dictionary<XmlElement, XfaNode> _nodes;
        // True for a field the form's data gives a value: a value a script computes for it is not shown.
        private readonly Func<XmlElement, bool> _hasData;
        public PageScripts(XfaScriptRunner runner, Dictionary<XmlElement, XfaNode> nodes, Func<XmlElement, bool> hasData)
        {
            (_runner, _nodes, _hasData) = (runner, nodes, hasData);
        }

        /// <summary>The value the field's layout-ready scripts give it on that page (the page count as
        /// its placeholder), or null for a field with no such script or one that resolves nothing.</summary>
        public string? MeasuredOn(XmlElement field, int pageNum) =>
            ValueOn(field, pageNum)?.Replace(PageCountSentinel, CountStandIn, StringComparison.Ordinal);

        /// <summary>The digits a page count is measured as before the pages are counted.</summary>
        private const string CountStandIn = "00";

        public string? ValueOn(XmlElement field, int pageNum) =>
            _nodes.TryGetValue(field, out var node) ? _runner.PageValue(node, pageNum, PageCountSentinel) : null;

        /// <summary>Settles the form's presence for one page of a counted set: the fields whose layout-ready scripts
        /// ask for page numbers take that page's answers, the calculates that read them run again, and every box
        /// carries the presence that leaves it with (a block shown on the last page only is hidden on the others).</summary>
        public void ResolveMasterOn(int pageNum, int pageCount)
        {
            var changed = _runner.ResolvePage(_nodes.Values, pageNum, pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // A value the page's scripts computed is the field's value on that page where the data gives it none; a
            // master page's field takes it over what a saved form state carried, which is one page's instance.
            foreach (var n in changed)
                if (n.PageAreaAncestor is not null || !_hasData(n.Template)) SetValueText(n.Template, n.RawValue ?? string.Empty);
        }
    }

    /// <summary>Gives a box the value text a script assigned, in place of the text its value element held.</summary>
    private static void SetValueText(XmlElement e, string text)
    {
        var value = FirstChild(e, "value") ?? (XmlElement)e.AppendChild(e.OwnerDocument.CreateElement("value", e.NamespaceURI))!;
        var textNode = e.OwnerDocument.CreateElement("text", e.NamespaceURI);
        textNode.InnerText = text;
        value.RemoveAll();
        value.AppendChild(textNode);
    }

    /// <summary>Marks a field of the render tree with the value a load-time script gave it.</summary>
    private const string ScriptValueAttribute = "data-script-value";

    /// <summary>What a field with no data value shows. In a form with no saved state the value a load-time
    /// script assigned comes before the template's default, which is only a placeholder there; with a saved
    /// state the value element holds what the viewer saved, and it comes first.</summary>
    private static string? DefaultOrScriptValue(Ctx ctx, XmlElement e)
    {
        var first = ctx.StrictBinding ? ScriptAssignedValue(e) : InnerText(e, "value");
        if (!string.IsNullOrEmpty(first)) return first;
        return ctx.StrictBinding ? InnerText(e, "value") : ScriptAssignedValue(e);
    }

    /// <summary>The value a load-time script assigned to a field the data and the template leave empty, or null.</summary>
    private static string? ScriptAssignedValue(XmlElement e) =>
        e.GetAttribute(ScriptValueAttribute) is { Length: > 0 } assigned ? assigned : null;

    /// <summary>The value a script reads from a box: what the render would show in it before any
    /// picture is applied - the bound data value, else the template's default - or null when empty.
    /// <paramref name="leaves"/> indexes the data leaves by name the first time a field falls back
    /// to them, so a form whose scripts read many fields walks the data once.</summary>
    private static string? ScriptRawValue(Ctx ctx, XmlElement e, ref Dictionary<string, List<XmlElement>>? leaves, bool withDefault = true)
    {
        string? val = null;
        var name = e.GetAttribute("name");
        // An exclusion group whose option carries a value of its own reads that value, before the data (measured:
        // a channel group whose first option holds 1 reads 1 to its scripts although the data says 2).
        if (e.LocalName == "exclGroup" && OptionDefault(e) is { } chosen) return chosen;
        if (e.LocalName != "draw" && !BindsNoData(e))
        {
            val = BoundValue(ctx, e, name);
            if (val is null && ctx.RawValue is not null && PathSegment(e) is not null) val = ctx.RawValue(SomPathOf(e));
            val ??= ChainedReferenceValue(ctx, e);
            if (val is null && ctx.DataRoot is not null && name.Length > 0)
            {
                leaves ??= DataLeavesByName(ctx.DataRoot);
                if (leaves.TryGetValue(name, out var named) && (named.Count == 1 || !ctx.StrictBinding))
                    val = named[0].InnerText;
            }
        }
        // A field bound to a data node that is empty is empty to its scripts: the template default
        // stands in only where no data node was found at all.
        if (withDefault) val ??= InnerText(e, "value");
        return string.IsNullOrEmpty(val) ? null : val;
    }

    /// <summary>The value the first option of an exclusion group declares for itself, or null when none does.</summary>
    private static string? OptionDefault(XmlElement group)
    {
        foreach (var option in group.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == "field"))
            if (InnerText(option, "value") is { Length: > 0 } own) return own;
        return null;
    }

    private static Dictionary<string, List<XmlElement>> DataLeavesByName(XmlElement dataRoot)
    {
        var byName = new Dictionary<string, List<XmlElement>>(StringComparer.Ordinal);
        foreach (var d in dataRoot.SelectNodes(".//*")!.OfType<XmlElement>())
        {
            if (d.ChildNodes.OfType<XmlElement>().Any()) continue;
            if (!byName.TryGetValue(d.LocalName, out var list)) byName[d.LocalName] = list = new List<XmlElement>();
            list.Add(d);
        }
        return byName;
    }
}
