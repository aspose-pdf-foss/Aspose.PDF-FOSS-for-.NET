using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One element or text node of the InfoPath form's markup tree.</summary>
    private sealed class IpNode
    {
        public string Tag = "";                                   // "" for a text node
        public string Text = "";                                  // a text node's decoded text
        public Dictionary<string, string> Attrs = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Style = new(StringComparer.OrdinalIgnoreCase);
        public List<IpNode> Children = new();
        public IpNode? Parent;

        public bool IsText => Tag.Length == 0;
        public string Attr(string name) => Attrs.TryGetValue(name, out var v) ? v : "";
        public string Css(string name) => Style.TryGetValue(name, out var v) ? v : "";
        public bool HasClass(string cls)
        {
            foreach (var c in Attr("class").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (c.Equals(cls, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        public IEnumerable<IpNode> Elements(string tag)
        {
            foreach (var c in Children)
                if (c.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) yield return c;
        }
        public IpNode? First(string tag)
        {
            foreach (var c in Children)
                if (c.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }
    }

    private static readonly Regex IpTagRx = new("<!--[\\s\\S]*?-->|<(/?)([a-zA-Z][a-zA-Z0-9:]*)((?:[^>\"']|\"[^\"]*\"|'[^']*')*)>", RegexOptions.Compiled);
    private static readonly Regex IpAttrRx = new("([a-zA-Z_:][-a-zA-Z0-9_:.]*)\\s*(?:=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\"'>]+)))?", RegexOptions.Compiled);
    private static readonly HashSet<string> IpVoidTags = new(StringComparer.OrdinalIgnoreCase) { "input", "img", "br", "col", "meta", "link", "hr" };
    private static readonly HashSet<string> IpDroppedTags = new(StringComparer.OrdinalIgnoreCase) { "script", "style", "head", "title", "xml" };

    /// <summary>Parse the document body the way the reference converter (and a browser) reads
    /// this XHTML: a non-void element written `&lt;span …/&gt;` is an OPEN tag that swallows
    /// its following siblings until an enclosing close tag; a `&lt;td&gt;` closes an open cell,
    /// a `&lt;tr&gt;` an open row; a close tag pops to its nearest matching open element.</summary>
    private static IpNode IpParse(string html)
    {
        var root = new IpNode { Tag = "body" };
        var stack = new List<IpNode> { root };
        var skipUntil = "";
        foreach (Match m in IpTagRx.Matches(html))
        {
            if (m.Value.StartsWith("<!--", StringComparison.Ordinal)) continue;
            var close = m.Groups[1].Value.Length > 0;
            var tag = m.Groups[2].Value.ToLowerInvariant();
            if (skipUntil.Length > 0)
            {
                if (close && tag == skipUntil) skipUntil = "";
                continue;
            }
            if (close)
            {
                IpPop(stack, tag);
                continue;
            }
            if (IpDroppedTags.Contains(tag)) { skipUntil = tag; continue; }
            if (tag == "td" || tag == "th") IpPop(stack, "td", "tr");
            else if (tag == "tr") IpPop(stack, "tr", "tbody", "table");
            var node = new IpNode { Tag = tag == "th" ? "td" : tag };
            IpReadAttrs(node, m.Groups[3].Value);
            var parent = stack[^1];
            node.Parent = parent;
            parent.Children.Add(node);
            if (!IpVoidTags.Contains(tag)) stack.Add(node);
        }
        IpAttachText(root, html);
        return root;
    }

    /// <summary>Pop the open-element stack to just below the nearest open element with the
    /// tag, unless one of the stop tags is met first (a `&lt;td&gt;` never closes its row).</summary>
    private static void IpPop(List<IpNode> stack, string tag, params string[] stopAt)
    {
        for (var i = stack.Count - 1; i > 0; i--)
        {
            var t = stack[i].Tag;
            if (Array.IndexOf(stopAt, t) >= 0) return;
            if (t == tag)
            {
                stack.RemoveRange(i, stack.Count - i);
                return;
            }
        }
    }

    private static void IpReadAttrs(IpNode node, string attrs)
    {
        foreach (Match a in IpAttrRx.Matches(attrs))
        {
            var name = a.Groups[1].Value;
            if (name == "/") continue;
            var value = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value;
            node.Attrs[name] = DecodeEntities(value);
        }
        if (!node.Attrs.TryGetValue("style", out var style)) return;
        foreach (var decl in style.Split(';'))
        {
            var colon = decl.IndexOf(':');
            if (colon <= 0) continue;
            node.Style[decl[..colon].Trim()] = decl[(colon + 1)..].Trim();
        }
    }

    /// <summary>Second pass: the text between the tags, attached to the element open at that
    /// point of the same walk, whitespace collapsed to single spaces (a text made of
    /// whitespace only is kept as one space; the line model trims it where it cannot show).</summary>
    private static void IpAttachText(IpNode root, string html)
    {
        var stack = new List<IpNode> { root };
        var pos = 0;
        var skipUntil = "";
        var cursor = new Dictionary<IpNode, int>();                // next child index to descend into
        foreach (Match m in IpTagRx.Matches(html))
        {
            if (m.Index > pos && skipUntil.Length == 0) IpInsertText(stack[^1], html[pos..m.Index], cursor);
            pos = m.Index + m.Length;
            if (m.Value.StartsWith("<!--", StringComparison.Ordinal)) continue;
            var close = m.Groups[1].Value.Length > 0;
            var tag = m.Groups[2].Value.ToLowerInvariant();
            if (skipUntil.Length > 0)
            {
                if (close && tag == skipUntil) skipUntil = "";
                continue;
            }
            if (close) { IpPop(stack, tag); continue; }
            if (IpDroppedTags.Contains(tag)) { skipUntil = tag; continue; }
            if (tag == "td" || tag == "th") IpPop(stack, "td", "tr");
            else if (tag == "tr") IpPop(stack, "tr", "tbody", "table");
            var parent = stack[^1];
            // the element created for this tag in the first pass: the next element child in order
            cursor.TryGetValue(parent, out var ci);
            IpNode? node = null;
            while (ci < parent.Children.Count)
            {
                var c = parent.Children[ci++];
                if (!c.IsText) { node = c; break; }
            }
            cursor[parent] = ci;
            if (node is null) continue;
            if (!IpVoidTags.Contains(tag)) stack.Add(node);
        }
    }

    /// <summary>A text node in document order: before the parent's next element child.</summary>
    private static void IpInsertText(IpNode parent, string raw, Dictionary<IpNode, int> cursor)
    {
        var text = Regex.Replace(DecodeEntities(raw), "[ \\t\\r\\n]+", " ");
        if (text.Length == 0) return;
        cursor.TryGetValue(parent, out var ci);
        parent.Children.Insert(Math.Min(ci, parent.Children.Count), new IpNode { Text = text, Parent = parent });
        cursor[parent] = ci + 1;
    }
}
