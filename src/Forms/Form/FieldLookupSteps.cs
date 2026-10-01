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
    /// <summary>The lookup steps of the field finder: the XFA path resolution for a bracketed name, and the index-stripped match for a plain one.</summary>
    private Field? FindFieldByStrippedIndices(string fullName)
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
        return null;
    }

    /// <summary></summary>
    private Field? FindFieldByXfaPath(string fullName)
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
        return null;
    }
}
