using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Transforms rich text annotation content (XHTML with arbitrarily nested spans)
/// into a flat structure where every leaf text node becomes a single
/// &lt;span style="."&gt; with the fully-merged CSS from all ancestor elements.
/// Mirrors <c>Aspose.Pdf.Annotations.RichTextToFlatStructureTransformer</c>.
/// </summary>
public static class RichTextToFlatStructureTransformer
{
    /// <summary>
    /// Transforms the given XHTML rich-text string into a flat span structure.
    /// </summary>
    /// <param name="input">
    /// An XML string with a &lt;body&gt; root element containing &lt;p&gt; and
    /// nested &lt;span&gt; / &lt;b&gt; / &lt;i&gt; elements.
    /// </param>
    /// <returns>
    /// An XML string with the same &lt;body&gt;&lt;p&gt; wrapper but with all
    /// content flattened into a sequence of non-nested &lt;span&gt; elements,
    /// each carrying the fully-merged <c>style</c> attribute.
    /// </returns>
    public static string Transform(string input)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(input);

        var body = doc.DocumentElement; // <body xmlns="...">

        // Walk the tree and collect flat (mergedStyle, textContent) pairs
        var spans = new List<(string Style, string Text)>();
        if (body != null)
        {
            foreach (XmlNode child in body.ChildNodes)
                WalkNode(child, new List<(string Key, string Value)>(), spans);
        }

        // Build output as a manually-assembled string for exact format control
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<body xmlns=\"http://www.w3.org/1999/xhtml\">");
        sb.Append("<p>");

        foreach (var (style, text) in spans)
        {
            sb.Append("<span");
            if (!string.IsNullOrEmpty(style))
            {
                sb.Append(" style=\"");
                sb.Append(style);
                sb.Append('"');
            }
            sb.Append('>');
            sb.Append(EscapeXml(text));
            sb.Append("</span>");
        }

        sb.Append("</p>");
        sb.Append("</body>");

        return sb.ToString();
    }

    // ── Tree walking ──────────────────────────────────────────────────────────

    private static void WalkNode(
        XmlNode node,
        List<(string Key, string Value)> parentStyles,
        List<(string Style, string Text)> spans)
    {
        if (node is XmlText textNode)
        {
            var text = textNode.Value ?? string.Empty;
            spans.Add((BuildStyleString(parentStyles), text));
            return;
        }

        if (node is XmlElement elem)
        {
            // Clone parent styles, then merge this element's own style attribute
            var elementStyles = new List<(string Key, string Value)>(parentStyles);

            var styleAttr = elem.GetAttribute("style");
            if (!string.IsNullOrEmpty(styleAttr))
            {
                foreach (var (key, value) in ParseStyle(styleAttr))
                    MergeStyle(elementStyles, key, value);
            }

            foreach (XmlNode child in elem.ChildNodes)
                WalkNode(child, elementStyles, spans);
        }
        // XmlComment, XmlCDataSection, etc. — ignored
    }

    // ── Style helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Merges a single CSS property into the ordered style list.
    /// If the key already exists (case-insensitive), updates the value in-place
    /// to preserve original insertion order.
    /// </summary>
    private static void MergeStyle(List<(string Key, string Value)> styles, string key, string value)
    {
        for (int i = 0; i < styles.Count; i++)
        {
            if (string.Equals(styles[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                styles[i] = (key, value);
                return;
            }
        }
        styles.Add((key, value));
    }

    private static IEnumerable<(string Key, string Value)> ParseStyle(string style)
    {
        foreach (var part in style.Split(';'))
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var colon = trimmed.IndexOf(':');
            if (colon < 0) continue;

            var key   = trimmed.Substring(0, colon).Trim();
            var value = trimmed.Substring(colon + 1).Trim();
            if (!string.IsNullOrEmpty(key))
                yield return (key, value);
        }
    }

    private static string BuildStyleString(List<(string Key, string Value)> styles)
    {
        if (styles.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var (key, value) in styles)
        {
            sb.Append(key);
            sb.Append(':');
            sb.Append(value);
            sb.Append(';');
        }
        return sb.ToString();
    }

    // ── XML helpers ───────────────────────────────────────────────────────────

    private static string EscapeXml(string text) =>
        text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
}
