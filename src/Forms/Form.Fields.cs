using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Forms;

public sealed partial class Form : ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    /// <summary>Null-returning core of <see cref="FindByName"/>.</summary>
    internal Field? FindFieldOrNull(string fullName)
    {
        // A null/empty request (e.g. GetFieldType on a non-field annotation whose FullName
        // is null) matches no field — return null rather than dereferencing it downstream.
        if (string.IsNullOrEmpty(fullName)) return null;
        // 1. Named radio group reconstruction. A radio group is read back as its
        // individual option widgets (each /Parent → the group dict); when the group
        // itself is named, surface it as one RadioButtonField so callers can look it
        // up by name and read its options. This takes priority over the direct match
        // below because an option widget inherits the group's full name, and a caller
        // asking for that name wants the group (with its /Opt), not a single widget.
        var rdr = _reader ?? OwnerDocument?.Reader;
        if (rdr is not null)
            foreach (var field in _fields)
            {
                var parentRef = field.Dict.Get("Parent");
                var parent = rdr.ResolveDict(parentRef);
                if (parent is null || parent.GetName("FT") != "Btn") continue;
                var ff = parent.ContainsKey("Ff") ? parent.GetInt("Ff") : 0;
                if ((ff & (1 << 15)) == 0) continue; // not a radio group
                var group = new RadioButtonField(parent, rdr);
                // Carry the owning document and the group's real object number (the
                // kid's /Parent points at the group's indirect object) so value changes
                // on the reconstructed group can be marked dirty for incremental save.
                group.OwnerDocument = OwnerDocument;
                if (parentRef is PdfIndirectRef pref) group.ObjectNumber = pref.ObjectNumber;
                if (string.Equals(group.FullName, fullName, StringComparison.Ordinal))
                    return group;
            }

        // 1a. Direct match by AcroForm full name
        foreach (var field in _fields)
        {
            if (string.Equals(field.FullName, fullName, StringComparison.Ordinal))
                return field;
        }

        // 1b. Group/parent prefix match — if fullName is a parent path,
        // return the first child field whose name starts with it
        var dotPrefix = fullName + ".";
        foreach (var field in _fields)
        {
            if (field.FullName?.StartsWith(dotPrefix, StringComparison.Ordinal) == true)
                return field;
        }

        // 1c. Anonymous-container–insensitive match. An XFA SOM address omits
        // unnamed container segments, so the test/caller asks for
        // "form1[0].TextField1[0]" while the fully-qualified AcroForm name is
        // "form1[0].#subform[0].TextField1[0]". Compare both names with their
        // "#..."-segments stripped so either spelling resolves to the same field.
        // Runs after the exact (1a) and prefix (1b) matches so literal names win.
        var canonicalRequest = StripAnonymousContainers(fullName);
        foreach (var field in _fields)
        {
            var fn = field.FullName;
            if (fn is null) continue;
            if (string.Equals(StripAnonymousContainers(fn), canonicalRequest, StringComparison.Ordinal))
                return field;
        }

        // 2. XFA path resolution fallback — try for any path with bracket indices
        if (fullName.Contains('['))
        {
            var mapping = GetXfaPathMapping();
            if (mapping.TryGetValue(fullName, out var acroFieldName))
            {
                foreach (var field in _fields)
                {
                    if (string.Equals(field.FullName, acroFieldName, StringComparison.Ordinal))
                        return field;
                }
            }

            // 3. Group node prefix match: if the requested path is a non-terminal
            // group (subform), return the first child field whose XFA path starts with it.
            var groupPrefix = fullName + ".";
            foreach (var kvp in mapping)
            {
                if (kvp.Key.StartsWith(groupPrefix, StringComparison.Ordinal))
                {
                    foreach (var field in _fields)
                    {
                        if (string.Equals(field.FullName, kvp.Value, StringComparison.Ordinal))
                            return field;
                    }
                }
            }

            // 4. Strip [N] indices and try matching as dotted AcroForm name
            var stripped = System.Text.RegularExpressions.Regex.Replace(fullName, @"\[\d+\]", "");
            foreach (var field in _fields)
            {
                if (string.Equals(field.FullName, stripped, StringComparison.Ordinal))
                    return field;
                // Also check if field's full name starts with the stripped path (group node)
                if (field.FullName?.StartsWith(stripped + ".", StringComparison.Ordinal) == true)
                    return field;
            }

            // 5. Last-segment fallback: match last path segment (without index) to partial name
            var lastDot = fullName.LastIndexOf('.');
            var lastSegment = lastDot >= 0 ? fullName.Substring(lastDot + 1) : fullName;
            // Strip [N] index from segment
            var bracketIdx = lastSegment.IndexOf('[');
            if (bracketIdx >= 0)
                lastSegment = lastSegment.Substring(0, bracketIdx);

            foreach (var field in _fields)
            {
                if (string.Equals(field.PartialName, lastSegment, StringComparison.Ordinal))
                    return field;
                if (string.Equals(field.FullName, lastSegment, StringComparison.Ordinal))
                    return field;
            }
        }

        // 6. Bracket-stripped XFA-style match (no '[' in input but fields have '[N]')
        // Strip [N] indices from each stored FullName/PartialName and compare.
        // Only return a hit if exactly one field's stripped name matches.
        if (!fullName.Contains('['))
        {
            static string StripIdx(string s) =>
                System.Text.RegularExpressions.Regex.Replace(s, @"\[\d+\]", "");

            Field? unique = null;
            foreach (var field in _fields)
            {
                if (field.FullName is null) continue;
                if (string.Equals(StripIdx(field.FullName), fullName, StringComparison.Ordinal))
                {
                    if (unique is not null) { unique = null; break; }
                    unique = field;
                }
            }
            if (unique is not null) return unique;

            // 7. Last-segment fallback for non-bracket inputs
            var lastDot2 = fullName.LastIndexOf('.');
            var leaf = lastDot2 >= 0 ? fullName.Substring(lastDot2 + 1) : fullName;
            Field? leafMatch = null;
            foreach (var field in _fields)
            {
                var partial = field.PartialName is null ? null : StripIdx(field.PartialName);
                if (string.Equals(partial, leaf, StringComparison.Ordinal))
                {
                    if (leafMatch is not null) { leafMatch = null; break; }
                    leafMatch = field;
                }
            }
            if (leafMatch is not null) return leafMatch;
        }

        return null;
    }

    /// <summary>
    /// Remove anonymous XFA container segments ("#subform[0]", "#area[1]",
    /// "#exclGroup[0]", …) from a dotted field path. XFA SOM addresses omit
    /// these unnamed containers, whereas the fully-qualified AcroForm field
    /// name includes them. A segment is anonymous when it begins with '#'.
    /// </summary>
    private static string StripAnonymousContainers(string dottedName)
    {
        if (string.IsNullOrEmpty(dottedName) || dottedName.IndexOf('#') < 0) return dottedName;
        var parts = dottedName.Split('.');
        var kept = new List<string>(parts.Length);
        foreach (var p in parts)
            if (p.Length == 0 || p[0] != '#')
                kept.Add(p);
        return string.Join(".", kept);
    }

    /// <summary>
    /// Split an XFA SOM (Scripting Object Model) path into its segments on the
    /// <b>unescaped</b> '.' separators, un-escaping any <c>\.</c> inside a segment
    /// back to a literal '.'. XFA field (and AcroForm /T) names may legitimately
    /// contain a '.' — e.g. a leaf named <c>SRC.C_ACTION</c> — which the SOM syntax
    /// writes escaped as <c>SRC\.C_ACTION</c>. A naive <c>Split('.')</c> would split
    /// such a leaf into two bogus segments. For backward compatibility (and speed) a
    /// path with no backslash falls straight through to <c>Split('.')</c>.
    /// </summary>
    internal static string[] SplitSomPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path is null ? Array.Empty<string>() : new[] { path };
        if (path.IndexOf('\\') < 0) return path.Split('.');
        var segs = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];
            if (c == '\\' && i + 1 < path.Length && path[i + 1] == '.')
            {
                sb.Append('.');
                i++; // consume the escaped dot
            }
            else if (c == '.')
            {
                segs.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        segs.Add(sb.ToString());
        return segs.ToArray();
    }

    /// <summary>Escape a single SOM path segment (a leaf/subform name) so that a
    /// literal '.' inside it round-trips as <c>\.</c> when the segment is joined
    /// into a dotted SOM path. Mirrors <see cref="SplitSomPath"/>.</summary>
    internal static string EscapeSomSegment(string segment)
        => string.IsNullOrEmpty(segment) || segment.IndexOf('.') < 0
            ? segment
            : segment.Replace(".", "\\.");

    private Dictionary<string, string>? _xfaPathMap;

    /// <summary>
    /// Build a mapping from XFA template paths to AcroForm field names.
    /// Parses the XFA template XML and walks the subform/field hierarchy.
    /// </summary>
    private Dictionary<string, string> GetXfaPathMapping()
    {
        if (_xfaPathMap is not null) return _xfaPathMap;
        _xfaPathMap = new Dictionary<string, string>(StringComparer.Ordinal);

        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return _xfaPathMap;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is not null)
            {
                WalkXfaTemplate(doc.DocumentElement, "", _xfaPathMap);
            }
        }
        catch { /* malformed template — return empty map */ }

        return _xfaPathMap;
    }

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

    /// <summary>The form type (Standard, Static, Dynamic).</summary>
    public FormType Type
    {
        get
        {
            if (!IsXfa) return FormType.Standard;
            // Detect Static vs Dynamic from the config packet.
            // Dynamic XFA uses client-side rendering (<renderPolicy>client</renderPolicy>).
            var (_, configXml) = GetXfaPart("config");
            if (configXml is not null &&
                configXml.Contains("<renderPolicy") && configXml.Contains("client"))
                return FormType.Dynamic;
            // Fallback: check template for dynamicRender
            var templateXml = GetXfaTemplateXml();
            if (templateXml is not null && templateXml.Contains("dynamicRender"))
                return FormType.Dynamic;
            return FormType.Static;
        }
        set
        {
            if (value == FormType.Standard || value == FormType.Static)
            {
                FlattenXfa();
            }
        }
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

    /// <summary>Caption reserve (in points) and placement for an XFA template field node.</summary>
    private static (double reserve, string placement) GetCaptionReserve(XmlElement fieldEl)
    {
        foreach (XmlNode ch in fieldEl.ChildNodes)
            if (ch is XmlElement el && el.LocalName == "caption")
                return (XfaMeasureToPt(el.GetAttribute("reserve")) ?? 0,
                        el.GetAttribute("placement") is { Length: > 0 } p ? p : "left");
        return (0, "left");
    }

    /// <summary>The caption's literal text (caption/value/text) for an XFA template field node.</summary>
    private static string? GetCaptionText(XmlElement fieldEl)
    {
        foreach (XmlNode ch in fieldEl.ChildNodes)
            if (ch is XmlElement { LocalName: "caption" } cap)
                return cap.SelectSingleNode(".//*[local-name()='text']")?.InnerText;
        return null;
    }

    /// <summary>Origin (in points) of the first contentArea in the template — the offset
    /// between template coordinates and page coordinates on a static XFA form.</summary>
    private static (double x, double y) GetContentAreaOrigin(XmlElement templateRoot)
    {
        if (templateRoot.SelectSingleNode(".//*[local-name()='contentArea']") is XmlElement ca)
            return (XfaMeasureToPt(ca.GetAttribute("x")) ?? 0, XfaMeasureToPt(ca.GetAttribute("y")) ?? 0);
        return (0, 0);
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

    /// <summary>Settings for <see cref="Document.Flatten(FlattenSettings)"/>.</summary>
    public sealed class FlattenSettings
    {
        /// <summary>When true, button widgets (XFA &lt;button&gt; nodes) are
        /// marked presence="hidden" before the flatten step so they are not
        /// rasterised into the resulting page content.</summary>
        public bool HideButtons { get; set; }

        /// <summary>When true, JavaScript and other field events are run
        /// during flatten (e.g. computed-field formulas refresh before the
        /// flatten captures their value). Stored only; XFA scripts are
        /// not currently executed.</summary>
        public bool CallEvents { get; set; }

        /// <summary>When true, each field's appearance stream is regenerated
        /// from its current value before being flattened into page content.
        /// Stored only; appearances are not currently rebuilt.</summary>
        public bool UpdateAppearances { get; set; }

        /// <summary>When true, redaction annotations are applied during the flatten pass. Stored only.</summary>
        public bool ApplyRedactions { get; set; }
    }

    private void FlattenXfa()
    {
        var flatReader = ResolvedReader;
        if (flatReader is null || !IsXfa) return;
        var catalog = flatReader.Catalog;
        var acroForm = flatReader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is null) return;

        // A dynamic XFA form carries its fields only in the XFA template — the AcroForm
        // has no widget fields. When flattening it to a standard AcroForm, materialise one
        // flat field per template field so the fields survive as findable AcroForm fields
        // (/T = the full dotted SOM path, matching GetXfaFieldNames). A static XFA form
        // already owns AcroForm widget fields, so leave those untouched (no duplication).
        var existing = flatReader.Resolve(acroForm.Get("Fields")) as PdfArray;
        bool hasWidgets = existing is not null && existing.Count > 0;
        if (!hasWidgets)
        {
            GenerateFlatAcroFieldsFromXfaTemplate(flatReader, acroForm);
            RenderDynamicXfa();     // paint the form onto real pages (replaces the XFA fallback page)
        }

        // Remove XFA key from AcroForm — this converts XFA to standard AcroForm
        acroForm.Remove("XFA");
        MarkAcroFormDirty();
    }

    /// <summary>Materialise a flat AcroForm field for each RENDERED XFA field (only used when
    /// flattening a dynamic XFA form that has no AcroForm widgets). The rendered set is resolved
    /// by <see cref="Xfa.XfaFormEngine"/>, which walks the subform tree AND the master pages
    /// (pageSet/pageArea) and applies the template-decidable selection rules (static presence,
    /// barcode ui). Each field is a top-level /Fields entry whose /T is the entire dotted SOM
    /// path (so FullName == PartialName and FindByName resolves it), with /FT derived from the
    /// field's XFA &lt;ui&gt; control. Positions (/Rect) are not emitted — that needs the XFA
    /// layout engine and is not required to make the fields findable.</summary>
    /// <summary>Paint the dynamic-XFA form's content onto fresh PDF pages so a raster render shows
    /// the form rather than the XFA fallback page. Tolerant: a failure leaves pages untouched.</summary>
    private void RenderDynamicXfa()
    {
        var doc = OwnerDocument;
        if (doc is null) return;
        var xml = GetXfaTemplateXmlResolved() ?? GetXfaTemplateXml();
        if (string.IsNullOrEmpty(xml)) return;
        try
        {
            var tdoc = new XmlDocument();
            tdoc.LoadXml(xml);
            if (tdoc.DocumentElement is { } root)
                Xfa.XfaRenderer.Render(doc, root, GetXfaFieldValue);
        }
        catch { }
    }

    private void GenerateFlatAcroFieldsFromXfaTemplate(PdfReader reader, PdfDictionary acroForm)
    {
        var doc = OwnerDocument;
        if (doc is null) return;
        var engine = Xfa.XfaFormEngine.TryCreate(GetXfaTemplateXmlResolved() ?? GetXfaTemplateXml());
        if (engine is null) return;
        // Give the engine the XFA data-binding resolver so its scripts can read field rawValues.
        // The engine must never break a flatten — fall back to no generated fields on any failure.
        List<Xfa.XfaFlatField> fields;
        try { fields = engine.BuildRenderedFields(GetXfaFieldValue); }
        catch { return; }
        if (fields.Count == 0) return;

        var fieldsArr = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fieldsArr is null) { fieldsArr = new PdfArray(); acroForm.Set("Fields", fieldsArr); }

        foreach (var f in fields)
        {
            var fld = new PdfDictionary();
            fld.Set("T", new PdfString(Encoding.UTF8.GetBytes(f.Path)));
            fld.Set("FT", new PdfName(f.Ft));
            if (f.Ff != 0) fld.Set("Ff", new PdfInteger((int)f.Ff));
            // Carry the field's bound datasets value onto the flat field's /V so the flattened
            // dynamic-XFA form keeps its data values (Field.Value). Text and
            // choice fields take a text /V; leave value-less and button/signature fields untouched.
            if (!string.IsNullOrEmpty(f.Value) && (f.Ft == "Tx" || f.Ft == "Ch"))
                fld.Set("V", new PdfString(Encoding.UTF8.GetBytes(f.Value)));
            // An XFA image field carries its picture as base64 in the datasets node —
            // materialise it as the flat pushbutton's /AP /N form so the image survives
            // the flatten (readable back via Appearance["N"].Resources.Images).
            if (f.IsImage && !string.IsNullOrEmpty(f.Value))
                TrySetFlatImageAppearance(fld, f.Value);
            int num = doc.AllocateObjectNumber();
            doc.AddNewObject(num, fld, registerOverlay: true);
            fieldsArr.Add(new PdfIndirectRef(num, 0));
        }
    }

    /// <summary>Build a normal-appearance form XObject holding the base64-decoded
    /// picture of a flattened XFA image field, at the image's natural pixel size
    /// (the flat field has no /Rect to fit into). Tolerant: undecodable data
    /// leaves the field without an appearance.</summary>
    private static void TrySetFlatImageAppearance(PdfDictionary fld, string base64)
    {
        byte[] imageBytes;
        try { imageBytes = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { return; }
        if (imageBytes.Length == 0) return;

        try
        {
            using var src = new MemoryStream(imageBytes, writable: false);
            var stamp = new ImageStamp(src);
            var imgStream = stamp.BuildImageXObject();
            double w = stamp.PixelWidth > 0 ? stamp.PixelWidth : 1;
            double h = stamp.PixelHeight > 0 ? stamp.PixelHeight : 1;

            var xobjects = new PdfDictionary();
            xobjects.Set("Im0", imgStream);
            var resources = new PdfDictionary();
            resources.Set("XObject", xobjects);

            var content = Encoding.ASCII.GetBytes(
                FormattableString.Invariant($"q {w:0.##} 0 0 {h:0.##} 0 0 cm /Im0 Do Q"));
            var apN = new PdfDictionary();
            apN.Set("Type", new PdfName("XObject"));
            apN.Set("Subtype", new PdfName("Form"));
            apN.Set("FormType", new PdfInteger(1));
            var bbox = new PdfArray();
            bbox.Add(new PdfInteger(0)); bbox.Add(new PdfInteger(0));
            bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
            apN.Set("BBox", bbox);
            apN.Set("Resources", resources);
            apN.Set("Length", new PdfInteger(content.Length));

            var ap = new PdfDictionary();
            ap.Set("N", new PdfStream(apN, content));
            fld.Set("AP", ap);
            var rect = new PdfArray();
            rect.Add(new PdfInteger(0)); rect.Add(new PdfInteger(0));
            rect.Add(new PdfReal(w)); rect.Add(new PdfReal(h));
            fld.Set("Rect", rect);
            fld.Set("Subtype", new PdfName("Widget"));
        }
        catch
        {
            // Unsupported image payload: keep the flat field, drop the appearance.
        }
    }

    /// <summary>Whether this form is an XFA form.</summary>
    public bool IsXfa
    {
        get
        {
            var reader = _reader ?? OwnerDocument?.Reader;
            return reader is not null &&
                reader.ResolveDict(reader.Catalog.Get("AcroForm")) is { } acro &&
                acro.ContainsKey("XFA");
        }
    }

    /// <summary>
    /// Get all interactive field names from the XFA template. Returns an empty array for non-XFA forms.
    /// Excludes draw (decorative) elements.
    /// </summary>
    internal string[] GetXfaFieldNames()
    {
        if (!IsXfa) return [];
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return [];
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            var names = new List<string>();
            if (doc.DocumentElement is not null)
                CollectXfaFieldNames(doc.DocumentElement, "", names);
            return names.ToArray();
        }
        catch { return []; }
    }

    /// <summary>Enumerate the actual XFA datasets leaves as (full dotted path, value)
    /// pairs. Unlike <see cref="GetXfaFieldNames"/> (which walks the template and so
    /// only yields the index-0 instance of each repeated field), this walks the
    /// datasets tree and yields every repeated instance with its real sibling index
    /// (e.g. <c>movies[0].movie[13].countries[0].country[1]</c>).</summary>
    internal List<KeyValuePair<string, string>> GetXfaDatasetsFields()
    {
        var result = new List<KeyValuePair<string, string>>();
        if (!IsXfa) return result;
        var xml = GetXfaDatasetsXml();
        if (string.IsNullOrEmpty(xml)) return result;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var data = FindDatasetsDataNode(doc);
            if (data is not null) WalkXfaDatasets(data, string.Empty, result);
        }
        catch { }
        return result;
    }

    private static XmlNode? FindDatasetsDataNode(XmlDocument doc)
    {
        var root = doc.DocumentElement;
        if (root is null) return null;
        var datasets = root.SelectSingleNode("//*[local-name()='datasets']");
        var data = datasets?.SelectSingleNode("*[local-name()='data']");
        if (data is not null) return data;
        var allData = root.SelectNodes("//*[local-name()='data']");
        if (allData is not null)
        {
            foreach (XmlNode d in allData)
                if (d.ParentNode?.LocalName == "datasets") return d;
            if (allData.Count > 0) return allData[0];
        }
        return root;
    }

    private static void WalkXfaDatasets(XmlNode node, string prefix, List<KeyValuePair<string, string>> result)
    {
        var counts = new Dictionary<string, int>();
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var localName = child.LocalName;
            var index = counts.TryGetValue(localName, out var c) ? c : 0;
            counts[localName] = index + 1;
            var escName = EscapeSomSegment(localName);
            var path = prefix.Length == 0 ? $"{escName}[{index}]" : $"{prefix}.{escName}[{index}]";

            var hasElementChild = false;
            foreach (XmlNode grand in child.ChildNodes)
                if (grand.NodeType == XmlNodeType.Element) { hasElementChild = true; break; }

            if (hasElementChild)
                WalkXfaDatasets(child, path, result);
            else
                result.Add(new KeyValuePair<string, string>(path, child.InnerText));
        }
    }

    /// <summary>True when the XFA template marks the field at the given path as a
    /// multi-line text edit (<c>&lt;textEdit multiLine="1"&gt;</c>). Non-multi-line
    /// fields normalise embedded newlines on import.</summary>
    internal bool IsXfaFieldMultiline(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (string.IsNullOrEmpty(templateXml)) return false;
        var leaf = SplitSomPath(path)[^1];
        var match = Regex.Match(leaf, @"^(.+)\[\d+\]$");
        var name = match.Success ? match.Groups[1].Value : leaf;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            var field = doc.SelectSingleNode($"//*[local-name()='field'][@name='{name}']");
            var attr = field?.SelectSingleNode(".//@*[local-name()='multiLine']");
            return attr is not null && attr.Value == "1";
        }
        catch { return false; }
    }

    private static void CollectXfaFieldNames(XmlNode node, string parentPath, List<string> names)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var localName = child.LocalName;
            var nameAttr = child.Attributes?["name"]?.Value;

            if (localName is "subform" or "subformSet" or "area")
            {
                if (nameAttr is not null)
                {
                    int idx = CountPrecedingSiblings(child, localName, nameAttr);
                    var escName = EscapeSomSegment(nameAttr);
                    var currentPath = parentPath.Length > 0
                        ? $"{parentPath}.{escName}[{idx}]"
                        : $"{escName}[{idx}]";
                    CollectXfaFieldNames(child, currentPath, names);
                }
                else
                    CollectXfaFieldNames(child, parentPath, names);
            }
            else if (localName is "field" or "exclGroup")
            {
                if (nameAttr is not null)
                {
                    int idx = CountPrecedingSiblings(child, localName, nameAttr);
                    var escName = EscapeSomSegment(nameAttr);
                    var fieldPath = parentPath.Length > 0
                        ? $"{parentPath}.{escName}[{idx}]"
                        : $"{escName}[{idx}]";
                    names.Add(fieldPath);
                }
                // Don't recurse into exclGroup — the group itself is the field,
                // individual options are not separate fields.
            }
            else
            {
                CollectXfaFieldNames(child, parentPath, names);
            }
        }
    }

    /// <summary>
    /// Get the XFA datasets XML, or null if not an XFA form.
    /// </summary>
    public string? GetXfaDatasetsXml()
    {
        // First try to get just the "datasets" part from an XFA array
        var (_, datasetsXml) = GetXfaPart("datasets");
        if (datasetsXml is not null) return datasetsXml;

        var reader = ResolvedReader;
        if (reader is null) return null;
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return null;

        var xfaObj = reader.Resolve(acroForm.Get("XFA"));

        // Single-stream XFA: entire XDP in one stream
        if (xfaObj is PdfStream xfaStream)
        {
            var data = reader.DecodeStream(xfaStream);
            return StripBom(Encoding.UTF8.GetString(data));
        }

        // XFA array without a named "datasets" part:
        // Concatenate all streams to reconstruct the full XDP
        if (xfaObj is PdfArray xfaArray)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < xfaArray.Count; i++)
            {
                var item = reader.Resolve(xfaArray[i]);
                if (item is PdfStream s)
                {
                    var data = reader.DecodeStream(s);
                    sb.Append(Encoding.UTF8.GetString(data));
                }
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
        return null;
    }

    /// <summary>
    /// Get the XFA "form" packet XML (the recorded runtime instance DOM — subform
    /// instance counts and value overrides as last saved by a viewer), or null when
    /// the document carries none.
    /// </summary>
    internal string? GetXfaFormXml() => GetXfaPart("form").xml;

    /// <summary>
    /// Get a specific XFA part stream (e.g. "datasets") from the XFA array.
    /// </summary>
    private (PdfStream? stream, string? xml) GetXfaPart(string partName)
    {
        var reader = ResolvedReader;
        if (reader is null) return (null, null);
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return (null, null);
        var xfaObj = reader.Resolve(acroForm.Get("XFA"));
        if (xfaObj is PdfArray xfaArray)
        {
            for (int i = 0; i < xfaArray.Count - 1; i += 2)
            {
                if (xfaArray[i] is PdfString s && Encoding.Latin1.GetString(s.Value) == partName)
                {
                    var stream = reader.Resolve(xfaArray[i + 1]) as PdfStream;
                    if (stream is not null)
                    {
                        var data = reader.DecodeStream(stream);
                        return (stream, Encoding.UTF8.GetString(data));
                    }
                }
            }
        }
        return (null, null);
    }

    /// <summary>
    /// <summary>
    /// Get the caption text for an XFA field by walking the template XML.
    /// Returns the text from &lt;caption&gt;&lt;value&gt;&lt;text&gt; inside the field element.
    /// </summary>
    internal string? GetXfaFieldCaption(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is null) return null;
            var fieldNode = FindXfaTemplateNode(doc.DocumentElement, path, 0);
            if (fieldNode is null) return null;

            // Look for <caption><value><text>.</text></value></caption>
            foreach (XmlNode child in fieldNode.ChildNodes)
            {
                if (child.LocalName == "caption")
                {
                    // Try <value><text> first
                    foreach (XmlNode vc in child.ChildNodes)
                    {
                        if (vc.LocalName == "value")
                        {
                            foreach (XmlNode tc in vc.ChildNodes)
                            {
                                if (tc.LocalName == "text")
                                    return tc.InnerText;
                            }
                        }
                    }
                    // Fallback: direct text content
                    return child.InnerText;
                }
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Resolve the XFA template UI widget kind for a field path — the local name of
    /// the element under the field's &lt;ui&gt; (e.g. "textEdit", "choiceList",
    /// "button"). A multi-select &lt;choiceList&gt; reports "choiceListMulti".
    /// Returns "exclGroup" for an exclusion (radio) group and "textEdit" for a
    /// &lt;field&gt; that declares no &lt;ui&gt; (the XFA default). Returns null when
    /// the path resolves to no template field node.
    /// </summary>
    internal string? GetXfaFieldUiKind(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is null) return null;
            var fieldNode = FindXfaTemplateNode(doc.DocumentElement, path, 0);
            if (fieldNode is null) return null;
            if (fieldNode.LocalName == "exclGroup") return "exclGroup";

            foreach (XmlNode child in fieldNode.ChildNodes)
            {
                if (child.LocalName != "ui") continue;
                foreach (XmlNode uiChild in child.ChildNodes)
                {
                    if (uiChild.NodeType != XmlNodeType.Element) continue;
                    if (uiChild.LocalName == "choiceList")
                    {
                        // XFA: open="always"/"multiSelect" is an expanded list box;
                        // "userControl"/"onEntry"/absent is a drop-down combo.
                        var open = uiChild.Attributes?["open"]?.Value;
                        return open is "always" or "multiSelect" ? "choiceListMulti" : "choiceList";
                    }
                    return uiChild.LocalName;
                }
            }
            // A <field> with no explicit <ui> renders as a plain text edit in XFA.
            return fieldNode.LocalName == "field" ? "textEdit" : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Get radio button option values from the XFA template.
    /// Looks for &lt;items&gt; children containing &lt;integer&gt; or &lt;text&gt; values.
    /// </summary>
    internal List<string>? GetXfaRadioButtonItems(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is null) return null;
            var fieldNode = FindXfaTemplateNode(doc.DocumentElement, path, 0);
            if (fieldNode is null) return null;

            // Collect items from <items> children (direct or in child <field> elements)
            var result = new List<string>();
            CollectXfaItems(fieldNode, result);

            // For exclGroup: items are on child <field> elements, not the group itself
            if (result.Count == 0 && fieldNode.LocalName == "exclGroup")
            {
                foreach (XmlNode child in fieldNode.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element && child.LocalName == "field")
                        CollectXfaItems(child, result);
                }
            }
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    private static void CollectXfaItems(XmlNode node, List<string> result)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.LocalName != "items") continue;
            foreach (XmlNode item in child.ChildNodes)
            {
                if (item.NodeType == XmlNodeType.Element)
                    result.Add(item.InnerText);
            }
        }
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
