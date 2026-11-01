using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form : ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>
{
// The stages of the XFA field value read.

    // The diagnostic switch of the binding walk, read once: the walk runs for every field read.
    private static readonly bool BindDebug = Environment.GetEnvironmentVariable("XFA_BIND_DEBUG") == "1";

    /// <summary>The template walk of <see cref="GetXfaFieldValueCore"/>: finds the field in the
    /// template, follows its data binding into the datasets and returns the bound value; on any
    /// failure of the walk the datasets result found so far stands.</summary>
    private string? ResolveXfaFieldValue(XfaFieldValueState xv, string xml)
    {
        try
        {
            var templateDoc = GetXfaTemplateDocument();
            if (templateDoc?.DocumentElement is null) return xv.result;

            var parts = SplitSomPath(xv.path);
            if (parts.Length < 2) return xv.result;

            // Walk the template by path segments to find the field node
            XmlNode? templateNode = templateDoc.DocumentElement;
            for (int i = 0; i < parts.Length && templateNode is not null; i++)
            {
                templateNode = StepTemplate(templateNode, parts[i]);
            }

            if (templateNode is null) return xv.result;

            // Check for <bind match="dataRef" ref="$.xxx"/>
            var bindNode = FindBindElement(templateNode);
            string? bindRef = null;
            if (bindNode is not null)
            {
                var matchAttr = bindNode.Attributes?["match"];
                var refAttr = bindNode.Attributes?["ref"];
                if (matchAttr?.Value == "dataRef" && refAttr?.Value is { } r && r.StartsWith("$."))
                    bindRef = r.Substring(2); // strip "$."
                // A reference naming no root ("Name.FirstName") is relative to the field's data
                // context, the same as one written "$.Name.FirstName".
                else if (matchAttr?.Value == "dataRef" && refAttr?.Value is { Length: > 0 } relative
                         && IsContextRelativeRef(relative))
                    bindRef = relative;
                else if (matchAttr?.Value == "dataRef" && RecordRelativeRef(refAttr?.Value) is { } fromRecord)
                {
                    var bound = FindXfaNodeValue(xv, xml, RecordDataPath(parts, fromRecord));
                    return string.IsNullOrEmpty(bound) ? xv.result : bound;
                }
            }

            var dataPathParts = AncestorDataPath(templateDoc, parts, out var scopeStart, out var scopeEnd);
            // Under a subform bound by an explicit reference the template says where the data is: when
            // nothing is there, a same-named leaf elsewhere in the data must not stand in for it.
            var scoped = scopeEnd > 0;
            if (scoped && xv.leafFallback) xv.result = null;

            // Append the resolved field name (from bind ref or original field name)
            dataPathParts.Add(bindRef ?? parts[^1]);

            var resolvedPath = string.Join(".", dataPathParts);
            if (BindDebug)
                Console.Error.WriteLine($"[bind] path={xv.path} bindRef={bindRef} resolvedPath={resolvedPath}");
            if (resolvedPath != xv.path)
            {
                var resolved = FindXfaNodeValue(xv, xml, resolvedPath);
                if (!string.IsNullOrEmpty(resolved) && !(scoped && xv.leafFallback)) return resolved;
            }

            // A dataRef's "$" is the field's data CONTEXT — the nearest ANCESTOR
            // subform that actually binds to a data node. Wrapper subforms with no
            // matching data group (common in single-record letter templates, e.g.
            // DocumentTemplateModel > PolicyJacketCoverLetter > field bound to
            // $.Insured.FullName) are transparent to binding, so retry the ref
            // against each shorter ancestor prefix, deepest first. A reference-bound
            // subform's scope is never climbed out of: only the wrappers above it may go.
            if (bindRef is not null)
            {
                for (int k = dataPathParts.Count - 2; k >= scopeEnd; k--)
                {
                    foreach (var prefix in ScopeCandidates(dataPathParts, k, scopeStart))
                    {
                        var candidate = string.Join(".", prefix.Append(bindRef));
                        var resolved = FindXfaNodeValue(xv, xml, candidate);
                        if (BindDebug)
                            Console.Error.WriteLine($"[bind]   k={k} candidate={candidate} -> '{resolved}'");
                        if (!string.IsNullOrEmpty(resolved) && !(scoped && xv.leafFallback)) return resolved;
                    }
                }
            }
        }
        catch { /* template resolution failed — return original result */ }

        return xv.result;
    }

    /// <summary>The value the saved form packet records under a field's own path: a viewer keeps a choice
    /// made in a field that binds no data node there. Null when the packet holds no such value.</summary>
    private string? FormPacketValue(string path)
    {
        XmlNode? node = GetXfaFormDocument()?.DocumentElement;
        if (node is null) return null;
        foreach (var part in SplitSomPath(path))
        {
            var match = SafeRegex.Match(part, @"^(.+)\[(\d+)\]$");
            var name = match.Success ? match.Groups[1].Value : part;
            var index = match.Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            node = node.ChildNodes.OfType<XmlElement>()
                .Where(c => c.LocalName is "subform" or "field" or "exclGroup" && c.GetAttribute("name") == name)
                .Skip(index).FirstOrDefault();
            if (node is null) return null;
        }
        var text = node.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "value")?.InnerText.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>The data path of a field's ancestors, the field itself excluded: a named subform adds its
    /// segment, a presentation-only or unnamed one nothing, and a subform bound by reference adds the
    /// reference's segments instead - from the record for "$record.X", from its own context otherwise -
    /// so a section whose data lies under wrapper groups the template does not mirror still reaches it.
    /// <paramref name="scopeStart"/> and <paramref name="scopeEnd"/> delimit the segments the last
    /// reference-bound subform contributed (both 0 when no ancestor binds by reference).</summary>
    private static List<string> AncestorDataPath(XmlDocument templateDoc, string[] parts, out int scopeStart, out int scopeEnd)
    {
        var dataPathParts = new List<string>();
        scopeStart = 0;
        scopeEnd = 0;
        XmlNode? node = templateDoc.DocumentElement;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            node = node is null ? null : StepTemplate(node, parts[i]);
            if (node is not null && (HasBindNone(node) || TakesNoDataLevel(node))) continue;
            if (IsUnnamedSomSegment(parts[i])) continue;
            var subformRef = node is null ? null : SubformDataRef(node);
            if (subformRef is null)
            {
                dataPathParts.Add(parts[i]);
                continue;
            }
            if (subformRef.StartsWith(RecordRefHead, StringComparison.Ordinal))
            {
                dataPathParts.Clear();
                dataPathParts.Add(parts[0]);
                scopeStart = dataPathParts.Count;
                dataPathParts.AddRange(subformRef.Substring(RecordRefHead.Length).Split('.'));
            }
            else if (subformRef.StartsWith("$.", StringComparison.Ordinal))
            {
                scopeStart = dataPathParts.Count;
                dataPathParts.AddRange(subformRef.Substring(2).Split('.'));
            }
            else if (IsContextRelativeRef(subformRef))
            {
                scopeStart = dataPathParts.Count;
                dataPathParts.AddRange(subformRef.Split('.'));
            }
            else
            {
                dataPathParts.Add(parts[i]);
                continue;
            }
            scopeEnd = dataPathParts.Count;
        }
        return dataPathParts;
    }

    /// <summary>The ancestor prefixes of length <paramref name="k"/> to retry a reference against: the prefix
    /// itself, and - when the reference scope sits below wrapper subforms - the same prefix with the wrappers
    /// between the record and the scope left out.</summary>
    private static IEnumerable<IEnumerable<string>> ScopeCandidates(List<string> dataPathParts, int k, int scopeStart)
    {
        yield return dataPathParts.Take(k);
        if (scopeStart > 1 && k >= scopeStart)
            yield return dataPathParts.Take(1).Concat(dataPathParts.Skip(scopeStart).Take(k - scopeStart));
    }

    /// <summary>A subform's data reference with the all-instances marker dropped, or null when it binds by name.</summary>
    private static string? SubformDataRef(XmlNode subform)
    {
        var bind = FindBindElement(subform);
        if (bind?.Attributes?["match"]?.Value != "dataRef") return null;
        var dataRef = bind.Attributes?["ref"]?.Value?.Replace("[*]", "");
        return string.IsNullOrEmpty(dataRef) ? null : dataRef;
    }

    /// <summary>True for a data reference written without a root: not a "$" pseudo-root, not an
    /// absolute "xfa." expression, not a "!" shortcut, and no wildcard or predicate the walk cannot follow.</summary>
    private static bool IsContextRelativeRef(string bindRef) =>
        bindRef[0] != '$' && bindRef[0] != '!' && !bindRef.StartsWith("xfa.", StringComparison.Ordinal)
        && bindRef.IndexOfAny(new[] { '*', '(', ' ' }) < 0;
}
