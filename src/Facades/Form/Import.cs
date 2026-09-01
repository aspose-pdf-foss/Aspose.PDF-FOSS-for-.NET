using System.Text;
using System.Xml;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

public sealed partial class Form
{
    /// <summary>
    /// Import form field values from an XML stream.
    /// For AcroForm: expects XML with field names as element names, values as text content.
    /// For XFA: replaces the XFA datasets.
    /// </summary>
    public void ImportXml(Stream inputXmlStream, bool IgnoreFormTemplateChanges)
    {
        var xmlStream = inputXmlStream;
        var ignoreFormTemplateChanges = IgnoreFormTemplateChanges;
        if (_doc is null) throw new InvalidOperationException("No document bound.");

        var xml = new XmlDocument();
        // The XML is read through the Windows default codepage unless
        // the stream carries a BOM or an explicit <?xml encoding=...?> declaration, so
        // UTF-8 bytes in a bare XML arrive as Windows-1252 characters. Field values
        // written that way round-trip through the XFA datasets verbatim; decode the
        // same way so imported values match.
        using (var buffer = new MemoryStream())
        {
            xmlStream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
                || bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF);
            bool hasDeclaredEncoding = false;
            if (!hasBom && bytes.Length >= 5 && bytes[0] == (byte)'<' && bytes[1] == (byte)'?')
            {
                var declEnd = System.Array.IndexOf(bytes, (byte)'>', 0, Math.Min(bytes.Length, 256));
                if (declEnd > 0)
                    hasDeclaredEncoding = System.Text.Encoding.ASCII
                        .GetString(bytes, 0, declEnd).Contains("encoding=", StringComparison.OrdinalIgnoreCase);
            }
            if (hasBom || hasDeclaredEncoding)
                xml.Load(new MemoryStream(bytes));
            else if (IsValidUtf8(bytes))
                // Bare XML that decodes cleanly as UTF-8 is UTF-8 (the XML default);
                // only byte sequences that can't be UTF-8 take the legacy codepage read.
                xml.LoadXml(new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes));
            else
                xml.LoadXml(Aspose.Pdf.Text.Cp1252.GetString(bytes));
        }

        // Check if this is an XFA form
        if (_doc.Form.IsXfa)
        {
            ImportXmlXfa(xml);
            // XFA-side import doesn't go through FindByName; skip per-field
            // tracking. Callers can still inspect form-level success via the
            // imported XFA datasets state.
            _importResult = Array.Empty<FormImportResult>();
            return;
        }

        // AcroForm: walk XML elements and fill fields
        var names = new List<string>();
        if (xml.DocumentElement is not null)
        {
            CollectXmlFieldNames(xml.DocumentElement, parentPath: null, names);
            // If no <field name="..."> elements found, fall back to XFA-datasets
            // format: each leaf element is a field, dotted path = nested element names.
            if (names.Count == 0)
                CollectXfaDatasetsLeafNames(xml.DocumentElement, parentPath: null, names);
        }
        ImportXmlAcroForm(xml.DocumentElement!);
        _importResult = TrackResults(names);
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(bytes);
            return true;
        }
        catch (System.Text.DecoderFallbackException)
        {
            return false;
        }
    }

    private static void CollectXmlFieldNames(XmlNode node, string? parentPath, List<string> result)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is not XmlElement el || el.Name != "field") continue;
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name)) continue;
            var path = parentPath is null ? name : $"{parentPath}.{name}";
            // <field><fields><field name="..."> nesting → dotted child path
            var nestedFields = el.SelectSingleNode("fields");
            if (nestedFields is not null)
                CollectXmlFieldNames(nestedFields, path, result);
            else
                result.Add(path);
        }
    }

    private static void CollectXfaDatasetsLeafNames(XmlNode node, string? parentPath, List<string> result)
    {
        // For XFA <xfa:data> XML, the root element is the data wrapper. Skip it
        // and use its first child (the form root, e.g. <form1>) as the implicit
        // top of the dotted path so the names match XFA's "form1[0]..." pattern.
        if (parentPath is null && node.LocalName == "data")
        {
            foreach (XmlNode firstLevel in node.ChildNodes)
            {
                if (firstLevel is XmlElement)
                {
                    CollectXfaDatasetsLeafNames(firstLevel, parentPath: null, result);
                    return;
                }
            }
            return;
        }

        var elementChildren = new List<XmlElement>();
        foreach (XmlNode c in node.ChildNodes)
            if (c is XmlElement ec) elementChildren.Add(ec);

        if (elementChildren.Count == 0)
        {
            // Leaf — emit the path
            if (parentPath is not null) result.Add(parentPath);
            return;
        }

        foreach (var child in elementChildren)
        {
            var seg = child.LocalName;
            var path = parentPath is null ? seg : $"{parentPath}.{seg}";
            CollectXfaDatasetsLeafNames(child, path, result);
        }
    }

    /// <summary>
    /// Import form field data from an XFDF stream.
    /// </summary>
    public void ImportXfdf(Stream inputXfdfStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        using var reader = new StreamReader(inputXfdfStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true, bufferSize: 4096);
        var xfdfXml = reader.ReadToEnd();
        if (_doc.Form.IsXfa)
        {
            // Persist into the XFA datasets, but still report per-field import
            // status (TrackResults resolves names against the XFA field paths).
            // A self-closing <field/> (no <fields>, no <value>) is ambiguous: it is
            // either an unfilled leaf field or a childless container — both self-close
            // since the export omits the empty <fields> wrapper. Only a genuine field
            // should surface as an import result; a childless container carries no data
            // and must not be reported as FieldNotFound. Gate self-closing entries on
            // the XFA template so containers are dropped while unfilled leaves are kept.
            var xfaPaths = XfaFieldPathsNorm();
            var xfaNames = ParseXfdfFieldNames(xfdfXml,
                selfClosingIsField: path => xfaPaths is not null && IsKnownXfaField(path, xfaPaths));
            ImportXfdfXfa(xfdfXml);
            _importResult = TrackResults(xfaNames);
            return;
        }
        var names = ParseXfdfFieldNames(xfdfXml);
        _doc.Form.ImportXfdf(xfdfXml);
        _importResult = TrackResults(names);
    }

    /// <summary>
    /// Import form field data from an FDF stream.
    /// </summary>
    public void ImportFdf(Stream inputFdfStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        using var ms = new MemoryStream();
        inputFdfStream.CopyTo(ms);
        var bytes = ms.ToArray();
        if (_doc.Form.IsXfa)
        {
            var xfaNames = ParseFdfFieldNames(bytes);
            ImportFdfXfa(bytes);
            _importResult = TrackResults(xfaNames);
            return;
        }
        var names = ParseFdfFieldNames(bytes);
        _doc.Form.ImportFdf(bytes);
        // An FDF exported from a review carries ANNOTATIONS (/FDF /Annots), not
        // field values — the facade imports those too, page by their
        // 0-based /Page, whole appearance graphs included.
        FdfImport.ImportAnnots(_doc, bytes);
        _importResult = TrackResults(names);
    }

    private FormImportResult[] TrackResults(IEnumerable<string> fieldNames)
    {
        if (_doc is null) return Array.Empty<FormImportResult>();
        var form = _doc.Form;
        // For XFA forms, AcroForm FindByName won't match XFA dotted paths;
        // resolve names against the set of XFA field paths instead.
        // For XFA forms, AcroForm FindByName won't match XFA dotted paths. Resolve
        // names against the XFA field paths, index-insensitively: a flat leaf name
        // (from a flat-FDF /T(Employee[0])) matches the full path form1[0]…Employee[0],
        // and a full dotted path matches exactly. Only genuine template fields are in
        // the set, so a field named in the import but absent from the form (e.g.
        // an FDF entry naming a field the template never declares) reports FieldNotFound.
        List<string>? xfaPathsNorm = XfaFieldPathsNorm();

        var list = new List<FormImportResult>();
        foreach (var name in fieldNames)
        {
            bool found = xfaPathsNorm is not null
                ? IsKnownXfaField(name, xfaPathsNorm)
                : form.FindFieldOrNull(name) is not null;
            var status = found ? ImportStatus.Success : ImportStatus.FieldNotFound;
            list.Add(new FormImportResult { FieldName = name, Status = status });
        }
        return list.ToArray();
    }

    /// <summary>The XFA template's field paths with per-segment <c>[n]</c> indices stripped,
    /// or null when the form is not XFA.</summary>
    private List<string>? XfaFieldPathsNorm()
    {
        var form = _doc?.Form;
        if (form is null || !form.IsXfa || form.XFA is null) return null;
        return form.XFA.FieldNames.Select(StripPathIndices).ToList();
    }

    /// <summary>True when <paramref name="name"/> (a flat leaf like <c>Employee[0]</c> or a full
    /// dotted path) resolves to a genuine XFA template field — index-insensitively, matched as a
    /// full path or a trailing segment-suffix of one.</summary>
    private static bool IsKnownXfaField(string name, List<string> normPaths)
    {
        var n = StripPathIndices(name);
        return normPaths.Any(p => p == n || p.EndsWith("." + n, StringComparison.Ordinal));
    }

    private static List<string> ParseXfdfFieldNames(string xfdfXml,
        Func<string, bool>? selfClosingIsField = null)
    {
        var result = new List<string>();
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xfdfXml);
            var ns = doc.Root?.GetDefaultNamespace() ?? System.Xml.Linq.XNamespace.None;
            var fields = doc.Root?.Element(ns + "fields");
            if (fields is null) return result;
            CollectXfdfFieldNames(fields, ns, parentPath: null, result, selfClosingIsField);
        }
        catch { /* malformed XFDF — leave list empty */ }
        return result;
    }

    // <paramref name="selfClosingIsField"/> decides whether a self-closing <field/> (one
    // with neither a nested <fields> nor a <value>) names a real importable field. When
    // null every self-closing entry is treated as a field (AcroForm behaviour); the XFA
    // path passes a predicate so childless-container fields — which self-close since the
    // export dropped their empty <fields> wrapper — are not mistaken for missing leaves.
    private static void CollectXfdfFieldNames(System.Xml.Linq.XElement fieldsContainer,
        System.Xml.Linq.XNamespace ns, string? parentPath, List<string> result,
        Func<string, bool>? selfClosingIsField)
    {
        foreach (var fieldEl in fieldsContainer.Elements(ns + "field"))
        {
            var name = fieldEl.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;
            var path = parentPath is null ? name : $"{parentPath}.{name}";
            // <field><fields><field name="..."> nesting → recurse
            var nestedFields = fieldEl.Element(ns + "fields");
            if (nestedFields is not null)
                CollectXfdfFieldNames(nestedFields, ns, path, result, selfClosingIsField);
            else if (fieldEl.Element(ns + "value") is not null)
                result.Add(path); // leaf carrying a value
            else if (selfClosingIsField is null || selfClosingIsField(path))
                result.Add(path); // self-closing: only genuine (unfilled) leaf fields
        }
    }

    private static List<string> ParseFdfFieldNames(byte[] fdfData)
    {
        // FDF /Fields encodes a tree of dicts: each entry has an optional /T
        // (partial name), an optional /V (value), and an optional /Kids (array
        // of child entries). Leaves are entries without /Kids; their full
        // field name is the dotted join of /T values from the root.
        //
        // Recursive-descent parser handles nested <<...>> dicts and [...]
        // arrays without flattening to bare /T scans.
        var result = new List<string>();
        var text = Encoding.Latin1.GetString(fdfData);
        var fieldsIdx = text.IndexOf("/Fields", StringComparison.Ordinal);
        if (fieldsIdx < 0) return result;
        var pos = text.IndexOf('[', fieldsIdx);
        if (pos < 0) return result;
        pos++; // step past '['
        ParseFdfFieldsArray(text, ref pos, parentPath: null, result);
        return result;
    }

    private static void ParseFdfFieldsArray(string t, ref int pos, string? parentPath, List<string> result)
    {
        while (pos < t.Length)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            if (t[pos] == ']') { pos++; return; }
            if (pos + 1 < t.Length && t[pos] == '<' && t[pos + 1] == '<')
            {
                pos += 2;
                ParseFdfFieldDict(t, ref pos, parentPath, result);
            }
            else
            {
                pos++; // tolerate stray bytes
            }
        }
    }

    private static void ParseFdfFieldDict(string t, ref int pos, string? parentPath, List<string> result)
    {
        string? partialName = null;
        int kidsStart = -1;

        while (pos < t.Length)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            if (pos + 1 < t.Length && t[pos] == '>' && t[pos + 1] == '>')
            {
                pos += 2;
                break;
            }
            if (t[pos] != '/') { pos++; continue; }
            pos++; // step past '/'
            int kStart = pos;
            while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++;
            var key = t.Substring(kStart, pos - kStart);
            FdfSkipWS(t, ref pos);
            if (key == "T" && pos < t.Length && t[pos] == '(')
            {
                partialName = FdfReadStringLiteral(t, ref pos);
            }
            else if (key == "Kids" && pos < t.Length && t[pos] == '[')
            {
                kidsStart = pos + 1; // remember; consume below
                FdfSkipArray(t, ref pos);
            }
            else
            {
                FdfSkipValue(t, ref pos);
            }
        }

        var fullPath = (parentPath, partialName) switch
        {
            (null, null) => null,
            (null, _) => partialName,
            (_, null) => parentPath,
            _ => $"{parentPath}.{partialName}",
        };

        if (kidsStart >= 0)
        {
            int kp = kidsStart;
            ParseFdfFieldsArray(t, ref kp, fullPath, result);
        }
        else if (fullPath is not null)
        {
            result.Add(fullPath);
        }
    }

    private static void FdfSkipWS(string t, ref int pos)
    {
        while (pos < t.Length)
        {
            char c = t[pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\0') pos++;
            else if (c == '%') { while (pos < t.Length && t[pos] != '\n') pos++; }
            else break;
        }
    }

    private static bool IsFdfDelimOrWS(char c) =>
        c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\0'
        || c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']'
        || c == '/' || c == '%';

    private static string FdfReadStringLiteral(string t, ref int pos)
    {
        pos++; // step past '('
        var sb = new StringBuilder();
        int depth = 1;
        while (pos < t.Length && depth > 0)
        {
            char c = t[pos++];
            if (c == '\\')
            {
                if (pos >= t.Length) break;
                char esc = t[pos++];
                switch (esc)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '(': sb.Append('('); break;
                    case ')': sb.Append(')'); break;
                    case '\\': sb.Append('\\'); break;
                    case '\n': break;
                    case '\r': if (pos < t.Length && t[pos] == '\n') pos++; break;
                    default: sb.Append(esc); break;
                }
            }
            else if (c == '(') { depth++; sb.Append(c); }
            else if (c == ')') { depth--; if (depth > 0) sb.Append(c); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static void FdfSkipValue(string t, ref int pos)
    {
        FdfSkipWS(t, ref pos);
        if (pos >= t.Length) return;
        char c = t[pos];
        if (c == '(') { FdfReadStringLiteral(t, ref pos); }
        else if (c == '[') { FdfSkipArray(t, ref pos); }
        else if (c == '<' && pos + 1 < t.Length && t[pos + 1] == '<') { FdfSkipDict(t, ref pos); }
        else if (c == '<') { pos++; while (pos < t.Length && t[pos] != '>') pos++; if (pos < t.Length) pos++; }
        else if (c == '/') { pos++; while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++; }
        else { while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++; }
    }

    private static void FdfSkipArray(string t, ref int pos)
    {
        pos++; // step past '['
        int depth = 1;
        while (pos < t.Length && depth > 0)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) break;
            char c = t[pos];
            if (c == '[') { pos++; depth++; }
            else if (c == ']') { pos++; depth--; }
            else if (c == '<' && pos + 1 < t.Length && t[pos + 1] == '<') FdfSkipDict(t, ref pos);
            else if (c == '(') FdfReadStringLiteral(t, ref pos);
            else FdfSkipValue(t, ref pos);
        }
    }

    private static void FdfSkipDict(string t, ref int pos)
    {
        pos += 2; // step past '<<'
        int depth = 1;
        while (pos < t.Length && depth > 0)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) break;
            char c = t[pos];
            if (pos + 1 < t.Length && c == '<' && t[pos + 1] == '<') { pos += 2; depth++; }
            else if (pos + 1 < t.Length && c == '>' && t[pos + 1] == '>') { pos += 2; depth--; }
            else if (c == '(') FdfReadStringLiteral(t, ref pos);
            else if (c == '[') FdfSkipArray(t, ref pos);
            else FdfSkipValue(t, ref pos);
        }
    }

    /// <summary>
    /// Imports field values from a JSON stream into the bound document, matching
    /// fields by their full names.
    /// </summary>
    public void ImportJson(Stream inputJsonStream)
    {
        if (inputJsonStream is null)
            throw new ArgumentNullException(nameof(inputJsonStream));
        if (_doc is null)
            throw new InvalidOperationException("No document bound.");

        FormJsonSerializer.ImportFieldData(_doc, inputJsonStream);
    }

    private void ImportXmlAcroForm(XmlElement root)
    {
        var form = _doc!.Form;
        var editor = new FormEditor(_doc);

        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;

            // Support both formats:
            // 1. <field name="FieldName"><value>val</value></field> (field-element format)
            // 2. <FieldName>val</FieldName> (simple format)
            var nameAttr = node.Attributes?["name"]?.Value;
            var fieldName = nameAttr ?? node.LocalName;

            // Extract value: check for <value> child element first. Strip the
            // newline characters introduced by pretty-printed (indented) XML —
            // the value's spaces are kept but the layout newlines dropped,
            // so "\n            Product\n        " imports as "            Product        ".
            var valueNode = node.SelectSingleNode("value");
            var fieldValue = (valueNode?.InnerText ?? node.InnerText).Replace("\r", "").Replace("\n", "");

            var field = form.FindFieldOrNull(fieldName);
            if (field is not null)
            {
                editor.SetField(fieldName, fieldValue);
            }
            else
            {
                // Try with full path from nested elements
                ImportXmlNode(node, "", editor, form);
            }
        }
    }

    private static void ImportXmlNode(XmlNode node, string prefix, FormEditor editor, Forms.Form form)
    {
        var name = string.IsNullOrEmpty(prefix) ? node.LocalName : prefix + "." + node.LocalName;

        if (node.HasChildNodes && node.FirstChild!.NodeType == XmlNodeType.Text)
        {
            var field = form.FindFieldOrNull(name);
            if (field is not null)
                editor.SetField(name, node.InnerText);
            return;
        }

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element)
                ImportXmlNode(child, name, editor, form);
        }
    }

    /// <summary>Read a PDF/FDF string literal beginning at <paramref name="open"/> (the '(').</summary>
    private static string ReadFdfLiteral(string s, int open, out int afterClose)
    {
        var sb = new StringBuilder();
        int depth = 0;
        int i = open;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length) { sb.Append(s[++i]); continue; }
            if (c == '(') { if (depth > 0) sb.Append(c); depth++; continue; }
            if (c == ')') { depth--; if (depth == 0) { i++; break; } sb.Append(c); continue; }
            if (depth > 0) sb.Append(c);
        }
        afterClose = i;
        return sb.ToString();
    }

    /// <summary>
    /// Import form field values from an XML stream (parameterless overload — same as
    /// <see cref="ImportXml(Stream, bool)"/> with <c>ignoreFormTemplateChanges=false</c>).
    /// </summary>
    public void ImportXml(Stream inputXmlStream) => ImportXml(inputXmlStream, IgnoreFormTemplateChanges: false);
}
