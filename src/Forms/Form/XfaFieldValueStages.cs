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

            // Template name attributes are un-indexed (name="insuredFullName") while a
            // SOM path segment carries its occurrence index (insuredFullName[0]) —
            // strip it for the template walk.
            static string BareSeg(string p)
            {
                var m = Regex.Match(p, @"^(.+)\[(\d+)\]$");
                return m.Success ? m.Groups[1].Value : p;
            }

            // Walk the template by path segments to find the field node
            XmlNode? templateNode = templateDoc.DocumentElement;
            for (int i = 0; i < parts.Length && templateNode is not null; i++)
            {
                templateNode = FindTemplateChild(templateNode, BareSeg(parts[i]));
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
            }

            // Build the data path by walking up, skipping bind="none" subforms
            var dataPathParts = new List<string>();
            for (int i = 0; i < parts.Length - 1; i++) // exclude the field itself
            {
                // Check if this subform is presentation-only (bind match="none")
                XmlNode? checkNode = templateDoc.DocumentElement;
                for (int j = 0; j <= i && checkNode is not null; j++)
                    checkNode = FindTemplateChild(checkNode, BareSeg(parts[j]));

                if (checkNode is not null && HasBindNone(checkNode))
                    continue; // skip presentation-only subform

                dataPathParts.Add(parts[i]);
            }

            // Append the resolved field name (from bind ref or original field name)
            dataPathParts.Add(bindRef ?? parts[^1]);

            var resolvedPath = string.Join(".", dataPathParts);
            if (Environment.GetEnvironmentVariable("XFA_BIND_DEBUG") == "1")
                Console.Error.WriteLine($"[bind] path={xv.path} bindRef={bindRef} resolvedPath={resolvedPath}");
            if (resolvedPath != xv.path)
            {
                var resolved = FindXfaNodeValue(xml, resolvedPath, xv.strict);
                if (!string.IsNullOrEmpty(resolved)) return resolved;
            }

            // A dataRef's "$" is the field's data CONTEXT — the nearest ANCESTOR
            // subform that actually binds to a data node. Wrapper subforms with no
            // matching data group (common in single-record letter templates, e.g.
            // DocumentTemplateModel > PolicyJacketCoverLetter > field bound to
            // $.Insured.FullName) are transparent to binding, so retry the ref
            // against each shorter ancestor prefix, deepest first.
            if (bindRef is not null)
            {
                for (int k = dataPathParts.Count - 2; k >= 0; k--)
                {
                    var candidate = string.Join(".",
                        dataPathParts.Take(k).Append(bindRef));
                    var resolved = FindXfaNodeValue(xml, candidate, xv.strict);
                    if (Environment.GetEnvironmentVariable("XFA_BIND_DEBUG") == "1")
                        Console.Error.WriteLine($"[bind]   k={k} candidate={candidate} -> '{resolved}'");
                    if (!string.IsNullOrEmpty(resolved)) return resolved;
                }
            }
        }
        catch { /* template resolution failed — return original result */ }

        return xv.result;
    }
}
