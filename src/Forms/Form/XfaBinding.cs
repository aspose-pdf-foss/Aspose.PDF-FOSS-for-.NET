using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    /// <summary>For a static XFA form, copy each AcroForm terminal field's current
    /// value into the XFA datasets, keyed by the field's fully-qualified name, so
    /// the datasets (and <see cref="XFA"/>[field]) stay in sync with values set
    /// through the typed field API. Called automatically before save. Dynamic XFA
    /// forms (whose data is driven by the template) are left untouched.</summary>
    /// <summary>Snapshot the current XFA datasets as a full-path → value map, for
    /// checkbox on/off-token preservation during <see cref="SyncAcroFormToXfa"/>.</summary>
    private Dictionary<string, string> BuildDatasetsValueMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in GetXfaDatasetsFields()) map[kv.Key] = kv.Value;
        return map;
    }

    /// <summary>Push every XFA datasets leaf value into its matching AcroForm field
    /// (static XFA forms only) so the widget representation reflects data that was
    /// replaced wholesale in the datasets (e.g. by <c>ImportXml</c>). Without this the
    /// AcroForm fields keep their old values and the save-time
    /// <see cref="SyncAcroFormToXfa"/> would push those stale values back over the
    /// freshly-imported datasets (notably clobbering checkbox "1"/"0" with "Off").</summary>
    internal void SyncXfaToAcroForm()
    {
        if (Type != FormType.Static) return;
        foreach (var kv in GetXfaDatasetsFields())
            ApplyXfaValueToAcroField(kv.Key, kv.Value);
        // Name-walk matching above only covers forms whose datasets tree mirrors the
        // field hierarchy. Designer forms with a data connection bind fields
        // EXPLICITLY (<bind match="dataRef" ref="$record...">) to a foreign-shaped
        // record — resolve those against the datasets and push the values too.
        ApplyTemplateDataRefBindings();
    }

    /// <summary>Resolve the template's explicit data bindings — every field /
    /// exclGroup carrying <c>&lt;bind match="dataRef" ref="$record…"/&gt;</c> — against
    /// the datasets and push each resolved value into the matching AcroForm field.
    /// Static-XFA widget names mirror the template tree, so a template field is paired
    /// with the acro field whose LEAF name and occurrence index ([n]) match the
    /// template's same-named fields in document order.</summary>
    private void ApplyTemplateDataRefBindings()
    {
        var templateXml = GetXfaTemplateXml();
        var datasetsXml = GetXfaDatasetsXml();
        if (string.IsNullOrEmpty(templateXml) || string.IsNullOrEmpty(datasetsXml)) return;
        XmlNode? dataNode;
        var refsByLeaf = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
        try
        {
            var tplDoc = new XmlDocument();
            tplDoc.LoadXml(templateXml);
            if (tplDoc.DocumentElement is null) return;
            CollectTemplateDataRefs(tplDoc.DocumentElement, refsByLeaf);
            if (refsByLeaf.Count == 0) return;

            var dsDoc = new XmlDocument();
            dsDoc.LoadXml(datasetsXml);
            dataNode = FindDatasetsDataNode(dsDoc);
        }
        catch { return; }
        if (dataNode is null) return;

        foreach (var field in _fields)
        {
            var full = field.FullName;
            if (string.IsNullOrEmpty(full)) continue;
            // Leaf segment ("Medewerker_Naam[1]") → name + occurrence index.
            var leaf = full;
            var dot = leaf.LastIndexOf('.');
            if (dot >= 0) leaf = leaf[(dot + 1)..];
            var occurrence = 0;
            var br = leaf.IndexOf('[');
            if (br >= 0)
            {
                var close = leaf.IndexOf(']', br);
                if (close > br) int.TryParse(leaf[(br + 1)..close], out occurrence);
                leaf = leaf[..br];
            }
            if (!refsByLeaf.TryGetValue(leaf, out var refs)) continue;
            var dataRef = occurrence < refs.Count ? refs[occurrence] : refs[^1];
            if (string.IsNullOrEmpty(dataRef)) continue;
            var value = ResolveXfaDataRef(dataNode, dataRef!);
            if (string.IsNullOrEmpty(value)) continue;
            ApplyXfaValueToAcroField(full, value!);
        }
    }

    /// <summary>Collect, per template field leaf NAME (document order), the field's
    /// explicit dataRef bind reference (null when the field has no dataRef bind).
    /// exclGroups count as leaves (their radio kids are not walked).</summary>
    private static void CollectTemplateDataRefs(XmlNode node, Dictionary<string, List<string?>> map)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName is "field" or "exclGroup")
            {
                var name = (child as XmlElement)?.GetAttribute("name");
                if (!string.IsNullOrEmpty(name))
                {
                    string? dataRef = null;
                    foreach (XmlNode c in child.ChildNodes)
                    {
                        if (c.LocalName != "bind" || c is not XmlElement bind) continue;
                        if (bind.GetAttribute("match") == "dataRef")
                        {
                            var r = bind.GetAttribute("ref");
                            if (!string.IsNullOrEmpty(r)) dataRef = r;
                        }
                        break;
                    }
                    if (!map.TryGetValue(name!, out var list)) map[name!] = list = new List<string?>();
                    list.Add(dataRef);
                }
                continue;
            }
            CollectTemplateDataRefs(child, map);
        }
    }

    /// <summary>Resolve a template bind reference (<c>$record.A.B</c>, <c>$data.A.B</c>,
    /// or a bare relative <c>A.B</c>) against the datasets <c>&lt;xfa:data&gt;</c> node.
    /// <c>$record</c> is the first element child of the data node (the record root).
    /// Segments may carry an occurrence index (<c>Name[2]</c>).</summary>
    private static string? ResolveXfaDataRef(XmlNode dataNode, string dataRef)
    {
        static XmlNode? FirstElementChild(XmlNode n)
        {
            foreach (XmlNode c in n.ChildNodes)
                if (c.NodeType == XmlNodeType.Element) return c;
            return null;
        }

        var path = dataRef.Trim();
        XmlNode? cur;
        if (path.StartsWith("$record.", StringComparison.Ordinal))
        {
            cur = FirstElementChild(dataNode);
            path = path["$record.".Length..];
        }
        else if (path.StartsWith("$data.", StringComparison.Ordinal))
        {
            cur = dataNode;
            path = path["$data.".Length..];
        }
        else if (path.StartsWith("$", StringComparison.Ordinal))
        {
            return null; // other pseudo-roots ($form, $host, …) are not data paths
        }
        else
        {
            cur = FirstElementChild(dataNode);
        }
        if (cur is null) return null;

        foreach (var rawSeg in path.Split('.'))
        {
            var seg = rawSeg;
            var idx = 0;
            var br = seg.IndexOf('[');
            if (br >= 0)
            {
                var close = seg.IndexOf(']', br);
                if (close > br) int.TryParse(seg[(br + 1)..close], out idx);
                seg = seg[..br];
            }
            if (seg.Length == 0) return null;
            XmlNode? next = null;
            var seen = 0;
            foreach (XmlNode c in cur.ChildNodes)
            {
                if (c.NodeType != XmlNodeType.Element || c.LocalName != seg) continue;
                if (seen++ == idx) { next = c; break; }
            }
            if (next is null) return null;
            cur = next;
        }
        return cur.InnerText;
    }

    internal void SyncAcroFormToXfa()
    {
        if (Type != FormType.Static) return;
        var pairs = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string>? existingDs = null;
        foreach (var field in _fields)
        {
            // Only terminal value-bearing fields map to a datasets leaf. Subform /
            // container nodes (the base Field hierarchy entries) carry no value, and
            // writing an empty value to a container path would wipe its whole subtree.
            // CheckboxField.Value is a `new` shadow → dispatch explicitly; the others
            // override Value and resolve through the base reference.
            string? val;
            switch (field)
            {
                case CheckboxField cb:
                    // An arbitrary non-state value assigned to an XFA checkbox
                    // (e.g. Field.Value = "1234") is stored VERBATIM in the datasets,
                    // even though the AcroForm appearance normalised it to "Off".
                    if (cb.RawNonStateValue is string rawCb)
                    {
                        val = rawCb;
                        break;
                    }
                    // Preserve the datasets' own on/off token (XFA forms conventionally
                    // bind "1"/"0") when the checkbox state already agrees with it — only
                    // overwrite on a genuine state change. Otherwise the AcroForm off
                    // export-name ("Off") would clobber an imported "0".
                    var cbName = field.FullName;
                    existingDs ??= BuildDatasetsValueMap();
                    if (cbName is not null && existingDs.TryGetValue(cbName, out var curVal))
                    {
                        bool curOn = !(string.IsNullOrEmpty(curVal) || curVal == "0"
                            || curVal.Equals("Off", StringComparison.OrdinalIgnoreCase));
                        if (curOn == cb.Checked) continue; // datasets token already matches → keep it
                    }
                    val = cb.Value;
                    break;
                case ChoiceField ch:
                    // Resolve the canonical group field (a radio kid instance carries
                    // no /Opt list) and use the selected option's export value — the
                    // field's own /V can lag the selection for radio groups.
                    var group = FindFieldOrNull(ch.FullName ?? "") as ChoiceField ?? ch;
                    var sel = group.Selected;
                    val = sel >= 1 && sel <= group.Options.Count ? group.Options[sel].Value : group.Value;
                    break;
                case TextBoxField:
                    val = field.Value;
                    break;
                default:
                    val = null;
                    break;
            }
            if (val is null) continue;
            var name = field.FullName;
            if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
            pairs.Add(new KeyValuePair<string, string>(name, val));
        }
        if (pairs.Count > 0) SetXfaFieldValues(pairs);
    }

    /// <summary>For a static XFA form, push a value written to the XFA datasets
    /// (via <see cref="XFA"/>[field]) into the matching AcroForm field so the two
    /// representations stay in sync. Text fields take the value verbatim; choice
    /// fields select the option whose export value matches; a checkbox is checked
    /// unless the value is empty / "0" / "Off".</summary>
    internal void ApplyXfaValueToAcroField(string name, string value)
    {
        if (Type != FormType.Static || string.IsNullOrEmpty(name)) return;
        switch (FindFieldOrNull(name))
        {
            case CheckboxField cb:
                cb.Checked = !(string.IsNullOrEmpty(value) || value == "0" || value == "Off");
                break;
            case ChoiceField ch:
                for (int i = 1; i <= ch.Options.Count; i++)
                {
                    if (ch.Options[i].Value == value) { ch.Selected = i; break; }
                }
                break;
            case TextBoxField tb:
                // Honour the field's /MaxLen: an imported value longer than the
                // field allows is truncated to fit (as a viewer would on entry).
                tb.Value = tb.MaxLen > 0 && value is not null && value.Length > tb.MaxLen
                    ? value.Substring(0, tb.MaxLen)
                    : value;
                break;
        }
    }
}
