using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    /// <summary>
    /// Recursively walk XFA template nodes to build path-to-field-name mapping.
    /// Subform nodes build up the path; field/draw/exclGroup nodes are leaf entries.
    /// </summary>
    private static void WalkXfaTemplate(XmlNode node, string parentPath, Dictionary<string, string> map)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;

            var localName = child.LocalName;
            var nameAttr = child.Attributes?["name"]?.Value;

            if (localName == "subform" || localName == "subformSet")
            {
                if (nameAttr is not null)
                {
                    // Count same-named siblings at this level to determine index
                    int idx = CountPrecedingSiblings(child, localName, nameAttr);
                    var escName = EscapeSomSegment(nameAttr);
                    var currentPath = parentPath.Length > 0
                        ? $"{parentPath}.{escName}[{idx}]"
                        : $"{escName}[{idx}]";
                    WalkXfaTemplate(child, currentPath, map);
                }
                else
                {
                    // Unnamed subform — pass through parent path
                    WalkXfaTemplate(child, parentPath, map);
                }
            }
            else if (localName == "field" || localName == "draw" || localName == "exclGroup")
            {
                if (nameAttr is not null)
                {
                    int idx = CountPrecedingSiblings(child, localName, nameAttr);
                    var escName = EscapeSomSegment(nameAttr);
                    var fieldPath = parentPath.Length > 0
                        ? $"{parentPath}.{escName}[{idx}]"
                        : $"{escName}[{idx}]";

                    // Map XFA path -> AcroForm field name (partial name). The value is
                    // matched against Field.FullName, which the AcroForm side escapes too.
                    if (!map.ContainsKey(fieldPath))
                        map[fieldPath] = escName;
                }

                // exclGroup can contain fields (radio buttons)
                if (localName == "exclGroup")
                    WalkXfaTemplate(child, parentPath, map);
            }
            else
            {
                // Other elements might contain subforms/fields — recurse
                WalkXfaTemplate(child, parentPath, map);
            }
        }
    }

    /// <summary>
    /// Count preceding siblings with the same local name and name attribute.
    /// </summary>
    private static int CountPrecedingSiblings(XmlNode node, string localName, string nameAttr)
    {
        int count = 0;
        var sibling = node.PreviousSibling;
        while (sibling is not null)
        {
            if (sibling.NodeType == XmlNodeType.Element &&
                sibling.LocalName == localName &&
                sibling.Attributes?["name"]?.Value == nameAttr)
            {
                count++;
            }
            sibling = sibling.PreviousSibling;
        }
        return count;
    }

    internal string? GetXfaTemplateXml()
    {
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null) return null;
        var catalog = reader.Catalog;
        var acroForm = reader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is null) return null;
        var xfaObj = reader.Resolve(acroForm.Get("XFA"));
        if (xfaObj is PdfArray arr)
        {
            for (int i = 0; i < arr.Count - 1; i += 2)
            {
                if (arr[i] is PdfString s && Encoding.Latin1.GetString(s.Value) == "template")
                {
                    var stream = reader.Resolve(arr[i + 1]) as PdfStream;
                    if (stream is not null)
                        return Encoding.UTF8.GetString(reader.DecodeStream(stream));
                }
            }
        }
        return null;
    }

    /// <summary>Replace the /XFA template part with the given XML. Used by
    /// Document.Flatten(FlattenSettings) to mark fields hidden before flatten.
    /// Drops the stream's /Filter entry and rewrites /Length so the new
    /// uncompressed bytes can be re-read after save without going through
    /// a decoder.</summary>
    internal void SetXfaTemplateXml(string xml)
    {
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null) return;
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return;
        var xfaObj = reader.Resolve(acroForm.Get("XFA"));
        if (xfaObj is PdfArray arr)
        {
            for (int i = 0; i < arr.Count - 1; i += 2)
            {
                if (arr[i] is PdfString s && Encoding.Latin1.GetString(s.Value) == "template")
                {
                    if (reader.Resolve(arr[i + 1]) is PdfStream stream)
                    {
                        var bytes = Encoding.UTF8.GetBytes(xml);
                        stream.ReplaceData(bytes);
                        stream.Dict.Remove("Filter");
                        stream.Dict.Remove("DecodeParms");
                        stream.Dict.Set("Length", new PdfInteger(bytes.Length));
                    }
                    return;
                }
            }
        }
    }

    /// <summary>Write <paramref name="url"/> into the XFA template's
    /// <c>&lt;submit target&gt;</c> for the named button field and persist it back
    /// into the /XFA template stream. Returns false when the form has no XFA
    /// template, the field/submit node can't be located, or the write fails.
    /// Mirrors the AcroForm SubmitForm /F update so both stay in sync.</summary>
    internal bool SetXfaSubmitUrl(string fieldName, string url)
    {
        var xml = GetXfaTemplateXml();
        if (xml is null) return false;
        XmlDocument doc = new();
        try { doc.LoadXml(xml); } catch { return false; }
        if (doc.DocumentElement is null) return false;

        // Locate the field's template node by walking the leaf-name segments
        // (template nodes are named by leaf name only, no dotted path / [n] index).
        XmlNode current = doc.DocumentElement;
        foreach (var rawSeg in SplitSomPath(fieldName))
        {
            var seg = System.Text.RegularExpressions.Regex.Replace(rawSeg, @"\[\d+\]$", "");
            var next = FindNamedTemplateNode(current, seg);
            if (next is null) return false;
            current = next;
        }
        if (ReferenceEquals(current, doc.DocumentElement)) return false;

        // The <submit> element (xfa-template ns) is a descendant of the field node,
        // typically <field><event><submit target="…"/></event></field>.
        if (current.SelectSingleNode(".//*[local-name()='submit']") is not XmlElement submit)
            return false;

        submit.SetAttribute("target", url);
        SetXfaTemplateXml(doc.OuterXml);
        return true;
    }

    /// <summary>Append a display/export item pair to an XFA choice-list field's template
    /// <c>&lt;items&gt;</c> lists and persist the template back into the /XFA stream.
    /// The display text goes into the plain <c>&lt;items&gt;</c> list, the export value
    /// into the bound-value list (<c>&lt;items save="1" presence="hidden"&gt;</c>); either
    /// list is created when absent. When the hidden list is first created next to a
    /// display list that already has entries, it is back-filled with those display texts
    /// so earlier items keep a 1:1 export value. Returns false when the form has no XFA
    /// template or the field's template node can't be located.</summary>
    internal bool AddXfaListItem(string fieldName, string display, string export)
    {
        var xml = GetXfaTemplateXml();
        if (xml is null) return false;
        XmlDocument doc = new();
        try { doc.LoadXml(xml); } catch { return false; }
        if (doc.DocumentElement is null) return false;

        // Locate the field's template node by walking the leaf-name segments
        // (template nodes are named by leaf name only, no dotted path / [n] index).
        XmlNode current = doc.DocumentElement;
        foreach (var rawSeg in SplitSomPath(fieldName))
        {
            var seg = System.Text.RegularExpressions.Regex.Replace(rawSeg, @"\[\d+\]$", "");
            var next = FindNamedTemplateNode(current, seg);
            if (next is null) return false;
            current = next;
        }
        if (current is not XmlElement field || ReferenceEquals(current, doc.DocumentElement)) return false;

        var ns = doc.DocumentElement.NamespaceURI;
        XmlElement? displayList = null, hiddenList = null;
        foreach (XmlNode ch in field.ChildNodes)
        {
            if (ch is not XmlElement el || el.LocalName != "items") continue;
            if (el.GetAttribute("save") == "1" && el.GetAttribute("presence") == "hidden")
                hiddenList ??= el;
            else
                displayList ??= el;
        }
        if (displayList is null)
        {
            displayList = doc.CreateElement("items", ns);
            field.AppendChild(displayList);
        }
        if (hiddenList is null)
        {
            hiddenList = doc.CreateElement("items", ns);
            hiddenList.SetAttribute("save", "1");
            hiddenList.SetAttribute("presence", "hidden");
            // Back-fill export values for items that predate the hidden list.
            foreach (XmlNode t in displayList.ChildNodes)
                if (t is XmlElement te && te.LocalName == "text")
                    AppendItemText(doc, hiddenList, ns, te.InnerText);
            field.AppendChild(hiddenList);
        }
        AppendItemText(doc, displayList, ns, display);
        AppendItemText(doc, hiddenList, ns, export);
        SetXfaTemplateXml(doc.OuterXml);
        return true;
    }

    private static void AppendItemText(XmlDocument doc, XmlElement list, string ns, string value)
    {
        var text = doc.CreateElement("text", ns);
        text.InnerText = value;
        list.AppendChild(text);
    }

    /// <summary>Return the XFA template XML from either a named "template" array part or,
    /// when the form's /XFA is a single-stream XDP (no named parts), the <c>&lt;template&gt;</c>
    /// element extracted from that XDP. <see cref="GetXfaTemplateXml"/> only handles the
    /// array form and returns null for a single-stream XDP.</summary>
    internal string? GetXfaTemplateXmlResolved()
    {
        var direct = GetXfaTemplateXml();
        if (direct is not null) return direct;
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null) return null;
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return null;
        if (reader.Resolve(acroForm.Get("XFA")) is not PdfStream single) return null;
        try
        {
            var xdp = StripBom(Encoding.UTF8.GetString(reader.DecodeStream(single)));
            var d = new XmlDocument();
            d.LoadXml(xdp);
            // Select the genuine xfa-template packet by namespace — NOT the config's
            // <common><template><base>. element (a different, xci namespace).
            var t = d.DocumentElement?.SelectSingleNode(
                "//*[local-name()='template' and contains(namespace-uri(),'xfa-template')]");
            return (t as XmlElement)?.OuterXml;
        }
        catch { return null; }
    }

    /// <summary>Strictly resolve a dotted SOM path against the XFA template — follow the
    /// container hierarchy segment by segment, skipping only anonymous (unnamed) subform
    /// wrappers, WITHOUT the lenient "any descendant with the leaf name" fallback that
    /// <see cref="FindXfaTemplateNode"/> applies. Returns true only when every segment
    /// resolves and the final node is a fillable field. Robust where the template-field
    /// enumeration is incomplete (some templates enumerate to zero fields).</summary>
    internal bool XfaTemplateFieldExists(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var xml = GetXfaTemplateXmlResolved();
        if (xml is null) return false;
        XmlDocument doc = new();
        try { doc.LoadXml(xml); } catch { return false; }
        if (doc.DocumentElement is null) return false;
        var node = ResolveXfaTemplateStrict(doc.DocumentElement, SplitSomPath(path), 0);
        return node is XmlElement el && (el.LocalName == "field" || el.LocalName == "exclGroup");
    }

    private static XmlNode? ResolveXfaTemplateStrict(XmlNode current, string[] parts, int idx)
    {
        if (idx >= parts.Length) return current;
        var seg = parts[idx];
        int occ = 0;
        var br = seg.IndexOf('[');
        var name = seg;
        if (br >= 0)
        {
            name = seg[..br];
            int.TryParse(seg[(br + 1)..seg.IndexOf(']')], out occ);
        }
        bool byLocal = name.StartsWith('#');
        var matchName = byLocal ? name[1..] : name;

        // Phase 1: a direct child matching this segment's @name (or #local-name).
        int count = 0;
        foreach (XmlNode child in current.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            bool matches = byLocal
                ? child.LocalName == matchName && child.Attributes?["name"] is null
                : child.Attributes?["name"]?.Value == matchName;
            if (!matches) continue;
            if (count == occ)
            {
                var r = ResolveXfaTemplateStrict(child, parts, idx + 1);
                if (r is not null) return r;
            }
            count++;
        }
        // Phase 2: descend transparently through containers the SOM data path collapses —
        // anonymous unnamed subforms, the always-structural pageSet/pageArea, AND named
        // subforms that don't bind data (bind match="none", e.g. a layout "page" subform
        // that hosts the real data subforms beneath it).
        foreach (XmlNode child in current.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            bool structural = child.LocalName is "pageArea" or "pageSet";
            bool named = !structural && child.Attributes?["name"] is not null;
            if (named && !HasBindNone(child)) continue;
            if (child.LocalName is "subform" or "subformSet" or "area" or "pageSet" or "pageArea")
            {
                var r = ResolveXfaTemplateStrict(child, parts, idx);
                if (r is not null) return r;
            }
        }
        return null;
    }

    /// <summary>Mark every XFA template field read-only (<c>access="readOnly"</c>) and persist
    /// it back into the /XFA template stream. Used by <c>Facades.Form.FlattenAllFields</c> to
    /// lock a dynamic XFA form's fields (which have no AcroForm widgets to flatten).</summary>
    internal void SetXfaFieldsReadOnly()
    {
        var xml = GetXfaTemplateXml();
        if (xml is null) return;
        XmlDocument doc = new();
        try { doc.LoadXml(xml); } catch { return; }
        if (doc.DocumentElement is null) return;
        var fields = doc.DocumentElement.SelectNodes(".//*[local-name()='field']");
        if (fields is null || fields.Count == 0) return;
        bool changed = false;
        foreach (XmlNode f in fields)
            if (f is XmlElement el) { el.SetAttribute("access", "readOnly"); changed = true; }
        if (changed) SetXfaTemplateXml(doc.DocumentElement.OuterXml);
    }

    /// <summary>First descendant (child-first) element whose @name equals
    /// <paramref name="name"/>, skipping anonymous wrapper subforms between levels.</summary>
    private static XmlNode? FindNamedTemplateNode(XmlNode parent, string name)
    {
        foreach (XmlNode child in parent.ChildNodes)
            if (child is XmlElement el && el.GetAttribute("name") == name) return el;
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child is not XmlElement) continue;
            var found = FindNamedTemplateNode(child, name);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Walk the XFA template hierarchy along a dotted SOM path
    /// ("formulaire1[0].#subform[0].FIELD[0]"). Named segments descend via
    /// <see cref="FindNamedTemplateNode"/> (which skips anonymous wrapper subforms);
    /// anonymous class segments ("#subform[1]") resolve to the nth direct child of that
    /// XFA class. Returns null when a segment fails to resolve or the path never leaves
    /// the root.</summary>
    internal static XmlNode? WalkTemplateBySomPath(XmlNode templateRoot, string somPath)
    {
        XmlNode current = templateRoot;
        foreach (var rawSeg in SplitSomPath(somPath))
        {
            var m = Regex.Match(rawSeg, @"^(.*?)(?:\[(\d+)\])?$");
            var seg = m.Groups[1].Value;
            var idx = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            var next = seg.StartsWith('#')
                ? FindClassTemplateNode(current, seg[1..], idx)
                : FindNamedTemplateNode(current, seg);
            if (next is null) return null;
            current = next;
        }
        return ReferenceEquals(current, templateRoot) ? null : current;
    }

    /// <summary>Resolve an anonymous SOM class segment ("#subform") to the
    /// <paramref name="index"/>th direct child of that XFA class carrying no name of
    /// its own, falling back to any direct child of the class.</summary>
    private static XmlNode? FindClassTemplateNode(XmlNode parent, string className, int index)
    {
        int seen = 0;
        foreach (XmlNode child in parent.ChildNodes)
            if (child is XmlElement el && el.LocalName == className
                && el.GetAttribute("name").Length == 0 && seen++ == index)
                return el;
        seen = 0;
        foreach (XmlNode child in parent.ChildNodes)
            if (child is XmlElement el && el.LocalName == className && seen++ == index)
                return el;
        return null;
    }

    /// <summary>Mirror a moved widget's rectangle back into the static-XFA template —
    /// x/y/w/h rewritten in "px" (1px = 1pt), with the caption reserve and the
    /// contentArea origin folded out and the field's own insets ignored — then replace
    /// the page's content with a fresh render of its fields (border + caption) so the
    /// page shows the form at the new geometry. This keeps
    /// the template and the designer-baked static render in sync when AcroForm field
    /// geometry changes on an XFA form. No-op for non-XFA documents and for fields
    /// without a template node.</summary>
    internal void SyncXfaWidgetGeometry(Field field)
    {
        if (!IsXfa || _reader is null) return;
        var fullName = field.FullName;
        if (string.IsNullOrEmpty(fullName)) return;
        if (_reader.Resolve(field.Dict.Get("Rect")) is not PdfArray ra || ra.Count < 4) return;
        var rect = Rectangle.FromPdfArray(ra, _reader);
        if (rect is null) return;

        var xml = GetXfaTemplateXml();
        if (xml is null) return;
        XmlDocument tdoc = new();
        try { tdoc.LoadXml(xml); } catch { return; }
        if (tdoc.DocumentElement is null) return;
        if (WalkTemplateBySomPath(tdoc.DocumentElement, fullName!) is not XmlElement fieldEl
            || fieldEl.LocalName != "field") return;

        var doc = OwnerDocument;
        var pageIndex = field.PageIndex;
        if (doc is null || pageIndex < 1 || pageIndex > doc.Pages.Count) return;
        var page = doc.Pages[pageIndex];
        double pageH = page.Rect.Height;

        var (reserve, placement) = GetCaptionReserve(fieldEl);
        var (caX, caY) = GetContentAreaOrigin(tdoc.DocumentElement);

        double x = rect.LLX - caX, y = pageH - rect.URY - caY;
        double w = rect.Width, h = rect.Height;
        switch (placement)
        {
            case "right": case "inline": w += reserve; break;
            case "top": y -= reserve; h += reserve; break;
            case "bottom": h += reserve; break;
            default: x -= reserve; w += reserve; break; // left — the XFA default
        }
        fieldEl.SetAttribute("x", PxAttr(x));
        fieldEl.SetAttribute("y", PxAttr(y));
        fieldEl.SetAttribute("w", PxAttr(w));
        fieldEl.SetAttribute("h", PxAttr(h));
        SetXfaTemplateXml(tdoc.DocumentElement.OuterXml);

        RegenerateXfaStaticPageContent(page, tdoc.DocumentElement);
    }

    /// <summary>Replace the page's content with a fresh static render of the XFA form
    /// fields on it — one stroked border rectangle per widget plus its caption text at
    /// the caption-reserve position — dropping the original designer-baked render, which
    /// still draws the fields at their pre-move positions.</summary>
    private void RegenerateXfaStaticPageContent(Page page, XmlElement templateRoot)
    {
        if (_reader is null) return;
        var sb = new StringBuilder();
        string? fontRes = null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => System.Math.Round(v, 3).ToString("0.###", ci);

        foreach (var f in Fields)
        {
            if (f.PageIndex != page.Number) continue;
            if (_reader.Resolve(f.Dict.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
            var r = Rectangle.FromPdfArray(ra, _reader);
            if (r is null) continue;

            double bw = 1;
            if (_reader.ResolveDict(f.Dict.Get("BS")) is { } bs
                && _reader.Resolve(bs.Get("W")) is { } wObj)
                bw = wObj switch { PdfInteger i => i.Value, PdfReal d => d.Value, _ => 1 };

            sb.Append("q\n0 G\n").Append(F(bw)).Append(" w\n")
              .Append(F(r.LLX)).Append(' ').Append(F(r.LLY)).Append(' ')
              .Append(F(r.Width)).Append(' ').Append(F(r.Height)).Append(" re\nS\nQ\n");

            var tplNode = f.FullName is { Length: > 0 } fn
                ? WalkTemplateBySomPath(templateRoot, fn) as XmlElement
                : null;
            var caption = tplNode is null ? null : GetCaptionText(tplNode);
            if (string.IsNullOrEmpty(caption)) continue;

            var (reserve, placement) = GetCaptionReserve(tplNode!);
            double fs = 10;
            double capX = placement switch
            {
                "right" or "inline" => r.URX,
                "top" or "bottom" => r.LLX,
                _ => r.LLX - reserve,
            };
            double capY = (r.LLY + r.URY) / 2 - fs / 2;
            fontRes ??= Annotations.RedactionAnnotation.RegisterOverlayFont(page);
            var esc = caption!.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            sb.Append("BT\n0 g\n/").Append(fontRes).Append(' ').Append(F(fs)).Append(" Tf\n")
              .Append(F(capX)).Append(' ').Append(F(capY)).Append(" Td\n(")
              .Append(esc).Append(") Tj\nET\n");
        }

        if (sb.Length == 0) return;
        page.SetContentStream(Encoding.Latin1.GetBytes(sb.ToString()));
        page.ResetContentsCache();
    }

    private static string PxAttr(double v) =>
        System.Math.Round(v, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "px";

    /// <summary>The caption's literal text (caption/value/text) for an XFA template field node.</summary>
    private static string? GetCaptionText(XmlElement fieldEl)
    {
        foreach (XmlNode ch in fieldEl.ChildNodes)
            if (ch is XmlElement { LocalName: "caption" } cap)
                return cap.SelectSingleNode(".//*[local-name()='text']")?.InnerText;
        return null;
    }

    /// <summary>Parse an XFA measurement ("25mm", "0.25in", "10pt", "12px", bare number)
    /// to points; XFA "px" is treated as 1pt, the same unit the write-back
    /// path uses.</summary>
    internal static double? XfaMeasureToPt(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        foreach (var (u, f) in new[] { ("mm", 72.0 / 25.4), ("cm", 720.0 / 25.4), ("in", 72.0), ("pt", 1.0), ("px", 1.0) })
            if (v.EndsWith(u, StringComparison.Ordinal)
                && double.TryParse(v[..^u.Length], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d * f;
        return double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var raw) ? raw : null;
    }

    /// <summary>
    /// Walk the XFA template tree to find a node at the given dotted path.
    /// Path segments are "name[index]" or just "name". Unnamed containers
    /// (subforms without a name attribute) are transparently descended into.
    /// </summary>
    private static XmlNode? FindXfaTemplateNode(XmlNode root, string path, int startSegment)
    {
        var parts = SplitSomPath(path);
        return FindXfaTemplateNodeRecursive(root, parts, startSegment);
    }

    private static XmlNode? FindXfaTemplateNodeRecursive(XmlNode current, string[] parts, int partIndex)
    {
        if (partIndex >= parts.Length) return current;

        var seg = parts[partIndex];
        int idx = 0;
        var bracketPos = seg.IndexOf('[');
        string name;
        if (bracketPos >= 0)
        {
            name = seg[..bracketPos];
            int.TryParse(seg[(bracketPos + 1)..seg.IndexOf(']')], out idx);
        }
        else
        {
            name = seg;
        }

        // #-prefixed segments match unnamed elements by local name
        bool matchByLocalName = name.StartsWith('#');
        var matchName = matchByLocalName ? name[1..] : name;

        // Search direct children first
        int count = 0;
        foreach (XmlNode child in current.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            bool matches = matchByLocalName
                ? child.LocalName == matchName && child.Attributes?["name"] is null
                : child.Attributes?["name"]?.Value == matchName;
            if (matches)
            {
                if (count == idx)
                {
                    var result = FindXfaTemplateNodeRecursive(child, parts, partIndex + 1);
                    if (result is not null) return result;
                }
                count++;
            }
        }

        // If not found, descend into unnamed containers and structural elements
        // (pageArea/pageSet are always transparent in XFA paths even when named)
        foreach (XmlNode child in current.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            // pageArea and pageSet are structural — always descend even if named
            bool isStructural = child.LocalName is "pageArea" or "pageSet";
            if (!isStructural && child.Attributes?["name"] is not null) continue;
            if (child.LocalName is "subform" or "pageSet" or "pageArea" or "area" or "subformSet")
            {
                var result = FindXfaTemplateNodeRecursive(child, parts, partIndex);
                if (result is not null) return result;
            }
        }

        // Last-segment fallback: if strict walk fails, search for the final named segment
        // as a descendant. XFA paths from AcroForm may use different subform index numbering.
        if (partIndex < parts.Length && parts.Length > 1)
        {
            var lastSeg = parts[^1];
            var idxMatch = Regex.Match(lastSeg, @"^(.+)\[(\d+)\]$");
            var leafName = idxMatch.Success ? idxMatch.Groups[1].Value : lastSeg;
            var leafIdx = idxMatch.Success ? int.Parse(idxMatch.Groups[2].Value) : 0;
            // Search by name attribute (field, exclGroup, subform)
            var allMatches = current.SelectNodes($".//*[@name='{leafName}']");
            if (allMatches is not null && allMatches.Count > leafIdx)
                return allMatches[leafIdx];
        }

        return null;
    }
}
