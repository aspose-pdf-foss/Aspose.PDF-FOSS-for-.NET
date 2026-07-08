using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

/// <summary>A single rendered XFA field materialised as a flat AcroForm field descriptor.</summary>
internal sealed class XfaFlatField
{
    /// <summary>Full dotted SOM path (e.g. <c>TopForm[0].Page1[0].BasicData[0]…Address_1[0]</c>).</summary>
    public string Path = string.Empty;
    /// <summary>AcroForm field type name: <c>Tx</c>, <c>Ch</c>, <c>Btn</c> or <c>Sig</c>.</summary>
    public string Ft = "Tx";
    /// <summary>AcroForm field flags (/Ff).</summary>
    public long Ff;
    /// <summary>Bound datasets value for this field, if any (drives the generated /V so a
    /// flattened dynamic-XFA form keeps its data values on the flat AcroForm fields).</summary>
    public string? Value;
}

/// <summary>
/// Dynamic-XFA → static-AcroForm engine. Given an XFA <c>&lt;template&gt;</c> (and a data-binding
/// resolver) it produces the set of RENDERED fields: it builds the merged form DOM
/// (<see cref="XfaFormModel"/> — body subforms plus the master pages' <c>pageSet</c>/<c>pageArea</c>
/// content, which the plain field-name enumeration never entered), runs the load-time XFA scripts
/// (<see cref="XfaScriptRunner"/>: <c>initialize</c> events + <c>calculate</c> scripts, which set
/// node presence), then emits one flat field per field/exclGroup that is not
/// <see cref="XfaNode.EffectiveHidden"/>. This resolves both the statically-decidable exclusions
/// (a template <c>presence</c>, barcode <c>ui</c>, ui-less pseudo-fields) and the script-driven
/// ones (fields hidden by their own or a related node's initialize/calculate script).
/// </summary>
internal sealed class XfaFormEngine
{
    private readonly XmlElement _root;

    private XfaFormEngine(XmlElement root) => _root = root;

    /// <summary>Parse a template XML string into an engine, or null if it does not parse.</summary>
    internal static XfaFormEngine? TryCreate(string? templateXml)
    {
        if (string.IsNullOrEmpty(templateXml)) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            return doc.DocumentElement is { } root ? new XfaFormEngine(root) : null;
        }
        catch { return null; }
    }

    /// <summary>Resolve the flat rendered field set. <paramref name="resolveRawValue"/> maps a field
    /// SOM path to its bound data value (for the scripts' data-dependent conditions); may be null.</summary>
    internal List<XfaFlatField> BuildRenderedFields(Func<string, string?>? resolveRawValue)
    {
        var model = XfaFormModel.Build(_root, resolveRawValue);
        XfaScriptRunner.Run(model);

        var fields = new List<XfaFlatField>();

        // Body fields (not on a master page) render once regardless of which physical page they
        // flow onto — emit each visible one.
        foreach (var n in model.All)
            if (n.IsField && !n.EffectiveHidden && n.PageAreaAncestor is null)
                fields.Add(new XfaFlatField { Path = n.SomPath, Ft = n.Ft, Ff = n.Ff, Value = n.RawValue });

        // Master page(s): the active master (the pageArea the rendered body breaks onto) is emitted
        // once per physical page. The page count comes from flowing the body through the content
        // area; page-context-dependent master fields (e.g. a last-page-only block) are resolved by
        // re-running the master calculates with the page-count fields set for each page.
        EmitMasterPages(model, fields);

        return fields;
    }

    private static void EmitMasterPages(XfaFormModel model, List<XfaFlatField> fields)
    {
        var master = ActiveMaster(model);
        if (master is null) return;

        double contentH = XfaLayout.ContentAreaHeightMm(master);
        int pages = XfaLayout.PageCount(TopBodyBlocks(model), contentH);

        var present = model.FindByName("MyPresentPageCount");
        var total = model.FindByName("MyTotalPageCount");
        string baseSom = StripLastIndex(master.SomPath);        // e.g. TopForm[0].MPs[0].MP1
        int suffixStart = master.SomPath.Length;                // suffix begins after the pageArea path

        // The page-count fields' own calculates need a full layout engine (FormCalc/xfa.layout); we
        // inject the values instead and pin them so the re-run does not overwrite them.
        var pinned = new HashSet<string> { "MyPresentPageCount", "MyTotalPageCount" };
        for (int i = 0; i < pages; i++)
        {
            // Set the page context and re-evaluate page-dependent presence for this physical page.
            if (present is not null) present.RawValue = (i + 1).ToString();
            if (total is not null) total.RawValue = pages.ToString();
            if (present is not null || total is not null) XfaScriptRunner.RunCalculates(model, pinned);

            foreach (var n in model.All)
            {
                if (!n.IsField || n.EffectiveHidden || !ReferenceEquals(n.PageAreaAncestor, master)) continue;
                var suffix = n.SomPath.Substring(suffixStart);  // ".Footer[0]…"
                fields.Add(new XfaFlatField { Path = $"{baseSom}[{i}]{suffix}", Ft = n.Ft, Ff = n.Ff, Value = n.RawValue });
            }
        }
    }

    /// <summary>The master pageArea the rendered body flows onto: the target of a visible top-level
    /// body subform's breakBefore, else the pageArea that has any visible field.</summary>
    private static XfaNode? ActiveMaster(XfaFormModel model)
    {
        foreach (var b in TopBodyBlocks(model))
        {
            if (b.EffectiveHidden) continue;
            var target = BreakBeforeTarget(b.Template);
            if (target is not null)
                foreach (var pa in model.All)
                    if (pa.Kind == "pageArea" && pa.Name == target) return pa;
        }
        foreach (var pa in model.All)
            if (pa.Kind == "pageArea" && HasVisibleField(model, pa)) return pa;
        return null;
    }

    /// <summary>Top-level body subforms (direct subform children of the root) — the flowing content.</summary>
    private static IEnumerable<XfaNode> TopBodyBlocks(XfaFormModel model)
    {
        foreach (var c in model.Root.Children)
            if (c.Kind is "subform" or "subformSet" or "area") yield return c;
    }

    private static bool HasVisibleField(XfaFormModel model, XfaNode pageArea)
    {
        foreach (var n in model.All)
            if (n.IsField && !n.EffectiveHidden && ReferenceEquals(n.PageAreaAncestor, pageArea)) return true;
        return false;
    }

    private static string? BreakBeforeTarget(System.Xml.XmlNode subform)
    {
        foreach (System.Xml.XmlNode c in subform.ChildNodes)
        {
            if (c.NodeType != System.Xml.XmlNodeType.Element) continue;
            if (c.LocalName == "breakBefore") return c.Attributes?["target"]?.Value;
            if (c.LocalName == "break") return c.Attributes?["beforeTarget"]?.Value;
        }
        return null;
    }

    private static string StripLastIndex(string somPath)
    {
        int b = somPath.LastIndexOf('[');
        return b >= 0 ? somPath.Substring(0, b) : somPath;
    }

    // ----- shared template helpers (used by XfaFormModel) --------------------------------------

    /// <summary>The first element child of a field's &lt;ui&gt; (the concrete edit control,
    /// e.g. textEdit / choiceList / checkButton / button / barcode), or null if it has no &lt;ui&gt;.</summary>
    internal static XmlNode? FindUiControl(XmlNode field)
    {
        foreach (XmlNode c in field.ChildNodes)
        {
            if (c.NodeType != XmlNodeType.Element || c.LocalName != "ui") continue;
            foreach (XmlNode u in c.ChildNodes)
                if (u.NodeType == XmlNodeType.Element) return u;
            return null;
        }
        return null;
    }

    /// <summary>Map an XFA field/exclGroup to an AcroForm (/FT, /Ff) pair via its &lt;ui&gt; control:
    /// textEdit→Tx, choiceList→Ch(combo), checkButton→Btn(checkbox), button→Btn(pushbutton),
    /// exclGroup→Btn(radio), signature→Sig.</summary>
    internal static (string ft, long ff) XfaUiToFieldType(XmlNode field, string localName)
    {
        if (localName == "exclGroup") return ("Btn", 1L << 15);      // radio group
        return FindUiControl(field)?.LocalName switch
        {
            "choiceList" => ("Ch", 1L << 17),   // combo box
            "checkButton" => ("Btn", 0),         // checkbox
            "button" => ("Btn", 1L << 16),        // pushbutton
            "signature" => ("Sig", 0),
            _ => ("Tx", 0),                        // textEdit / numericEdit / dateTimeEdit
        };
    }

    internal static int CountPrecedingSiblings(XmlNode node, string localName, string nameAttr)
    {
        int count = 0;
        var sibling = node.PreviousSibling;
        while (sibling is not null)
        {
            if (sibling.NodeType == XmlNodeType.Element &&
                sibling.LocalName == localName &&
                sibling.Attributes?["name"]?.Value == nameAttr)
                count++;
            sibling = sibling.PreviousSibling;
        }
        return count;
    }
}
