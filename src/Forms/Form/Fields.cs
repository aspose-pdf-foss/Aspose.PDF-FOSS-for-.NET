using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Content;
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

            // 5. Last-segment fallback: match last path segment (without index) to partial name.
            // A name may carry literal dots escaped as "\." - those do not separate segments.
            var lastSegment = LastSomSegment(fullName);
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
            var leaf = LastSomSegment(fullName);
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

    /// <summary>The last segment of a dotted SOM/AcroForm path, honouring "\." escapes: a
    /// field whose own name contains dots ("F_0.P1_0.TextField1_2_") is ONE segment, so a
    /// request for it can only match a field carrying the whole dotted name.</summary>
    private static string LastSomSegment(string path)
    {
        for (int i = path.Length - 1; i >= 0; i--)
            if (path[i] == '.' && (i == 0 || path[i - 1] != '\\'))
                return path[(i + 1)..];
        return path;
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

    /// <summary>Caption reserve (in points) and placement for an XFA template field node.</summary>
    private static (double reserve, string placement) GetCaptionReserve(XmlElement fieldEl)
    {
        foreach (XmlNode ch in fieldEl.ChildNodes)
            if (ch is XmlElement el && el.LocalName == "caption")
                return (XfaMeasureToPt(el.GetAttribute("reserve")) ?? 0,
                        el.GetAttribute("placement") is { Length: > 0 } p ? p : "left");
        return (0, "left");
    }

    /// <summary>Origin (in points) of the first contentArea in the template — the offset
    /// between template coordinates and page coordinates on a static XFA form.</summary>
    private static (double x, double y) GetContentAreaOrigin(XmlElement templateRoot)
    {
        if (templateRoot.SelectSingleNode(".//*[local-name()='contentArea']") is XmlElement ca)
            return (XfaMeasureToPt(ca.GetAttribute("x")) ?? 0, XfaMeasureToPt(ca.GetAttribute("y")) ?? 0);
        return (0, 0);
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

    // ── Tagged caption ────────────────────────────────────────────────────────

    /// <summary>The caption a tagged page SHOWS for a widget. The widget's /StructParent leads
    /// (through /StructTreeRoot /ParentTree) to its structure element; the sibling elements
    /// under the same parent own the caption's marked-content sequences, and their shown text -
    /// every string operand in stream order, TJ pieces included - is the caption. Null for an
    /// untagged document or a widget without such a sibling.</summary>
    internal string? GetTaggedCaption(Field field)
    {
        var rdr = _reader ?? OwnerDocument?.Reader;
        var doc = OwnerDocument;
        if (rdr is null || doc is null) return null;
        try
        {
            var widget = TaggedWidgetOf(field.Dict, rdr);
            if (widget is null) return null;
            var structRoot = rdr.ResolveDict(rdr.Catalog.Get("StructTreeRoot"));
            var parentTree = structRoot is null ? null : rdr.ResolveDict(structRoot.Get("ParentTree"));
            if (parentTree is null) return null;
            var elem = NumberTreeLookup(parentTree, widget.GetInt("StructParent"), rdr);
            var parent = elem is null ? null : rdr.ResolveDict(elem.Get("P"));
            if (elem is null || parent is null) return null;
            var mcids = new List<int>();
            CollectSiblingMcids(parent.Get("K"), elem, rdr, mcids, 0);
            if (mcids.Count == 0) return null;
            Page? page = field.PageIndex >= 1 ? doc.Pages.At(field.PageIndex) : null;
            if (page is null)
            {
                var pageDict = rdr.ResolveDict(widget.Get("P"));
                for (int i = 1; pageDict is not null && i <= doc.Pages.Count; i++)
                    if (ReferenceEquals(doc.Pages.At(i).Dict, pageDict)) { page = doc.Pages.At(i); break; }
            }
            if (page is null) return null;
            var text = MarkedContentText(page, rdr, mcids);
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The dictionary carrying the widget's /StructParent: the field itself when it is
    /// a merged field/widget, else its first tagged widget kid.</summary>
    private static PdfDictionary? TaggedWidgetOf(PdfDictionary fieldDict, PdfReader rdr)
    {
        if (fieldDict.ContainsKey("StructParent")) return fieldDict;
        if (rdr.Resolve(fieldDict.Get("Kids")) is PdfArray kids)
            foreach (var k in kids)
            {
                var kd = rdr.ResolveDict(k);
                if (kd is not null && kd.ContainsKey("StructParent")) return kd;
            }
        return null;
    }

    private static PdfDictionary? NumberTreeLookup(PdfDictionary node, long key, PdfReader rdr)
    {
        if (rdr.Resolve(node.Get("Nums")) is PdfArray nums)
            for (int i = 0; i + 1 < nums.Count; i += 2)
                if (rdr.Resolve(nums[i]) is PdfInteger n && n.Value == key)
                    return rdr.ResolveDict(nums[i + 1]);
        if (rdr.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var k in kids)
            {
                var kd = rdr.ResolveDict(k);
                if (kd is null) continue;
                if (rdr.Resolve(kd.Get("Limits")) is PdfArray lim && lim.Count == 2
                    && rdr.Resolve(lim[0]) is PdfInteger lo && rdr.Resolve(lim[1]) is PdfInteger hi
                    && (key < lo.Value || key > hi.Value)) continue;
                var r = NumberTreeLookup(kd, key, rdr);
                if (r is not null) return r;
            }
        return null;
    }

    /// <summary>Gather the MCIDs of the marked-content sequences a /K value owns - an MCID, an
    /// MCR or structure-element dictionary, or an array of those - skipping the widget's own
    /// element and object references.</summary>
    private static void CollectSiblingMcids(PdfObject? kids, PdfDictionary self, PdfReader rdr, List<int> mcids, int depth)
    {
        if (depth > 8) return;
        switch (rdr.Resolve(kids))
        {
            case PdfInteger mcid:
                mcids.Add((int)mcid.Value);
                break;
            case PdfArray arr:
                foreach (var item in arr)
                    CollectSiblingMcids(item, self, rdr, mcids, depth + 1);
                break;
            case PdfDictionary d:
                if (ReferenceEquals(d, self)) break;
                var type = d.GetName("Type");
                if (type == "OBJR") break;
                if (type == "MCR")
                {
                    if (d.ContainsKey("MCID")) mcids.Add((int)d.GetInt("MCID"));
                    break;
                }
                CollectSiblingMcids(d.Get("K"), self, rdr, mcids, depth + 1);
                break;
        }
    }

    /// <summary>The text shown inside the marked-content sequences with the given MCIDs on
    /// <paramref name="page"/>, in stream order.</summary>
    private static string MarkedContentText(Page page, PdfReader rdr, List<int> mcids)
    {
        var content = page.GetDecodedContentBytes();
        if (content.Length == 0) return string.Empty;
        var resources = ResourcesOfPage(page.Dict, rdr);
        var fonts = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        var fontRes = resources is null ? null : rdr.ResolveDict(resources.Get("Font"));
        if (fontRes is not null)
            foreach (var name in fontRes.Keys)
            {
                var fd = rdr.ResolveDict(fontRes.Get(name));
                if (fd is not null) fonts[name] = fd;
            }
        var properties = resources is null ? null : rdr.ResolveDict(resources.Get("Properties"));
        var parser = new ContentStreamParser(rdr);
        var sb = new StringBuilder();
        var open = new Stack<bool>();
        int inside = 0;
        parser.OnMarkedContentBegin += (_, props) =>
        {
            bool hit = props is not null && props.ContainsKey("MCID") && mcids.Contains((int)props.GetInt("MCID"));
            open.Push(hit);
            if (hit) inside++;
        };
        parser.OnMarkedContentEnd += () =>
        {
            if (open.Count > 0 && open.Pop()) inside--;
        };
        parser.OnTextShown += (text, _, _) =>
        {
            if (inside > 0) sb.Append(text);
        };
        parser.Parse(content, fonts, properties: properties);
        return sb.ToString();
    }

    private static PdfDictionary? ResourcesOfPage(PdfDictionary pageDict, PdfReader rdr)
    {
        PdfDictionary? node = pageDict;
        for (int guard = 0; node is not null && guard < 64; guard++)
        {
            var res = rdr.ResolveDict(node.Get("Resources"));
            if (res is not null) return res;
            node = rdr.ResolveDict(node.Get("Parent"));
        }
        return null;
    }

}
