using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The XML-viewer render helpers: the before-pseudo text of a class, a run at a pen, one leaf line with its indent guide, and the recursive walk of the element tree.
    // The `.cls:before` pseudo marker (`content: '+'; color: red; left: -1em`).
    private static (string text, Color color, double left)? Before(XmlViewerState xv, string cls)
    {
        var m = Regex.Match(xv.sheet,
            @"\.\w[\w-]*\s+\." + Regex.Escape(cls) + @":before\s*\{([^}]*)\}",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var d = m.Groups[1].Value;
        var cm = Regex.Match(d, @"content\s*:\s*'([^']*)'|content\s*:\s*""([^""]*)""");
        if (!cm.Success) return null;
        var text = cm.Groups[1].Success ? cm.Groups[1].Value : cm.Groups[2].Value;
        var col = Regex.Match(d, @"(?<![-\w])color\s*:\s*([^;]+)") is { Success: true } colM
            ? ParseCssColor(colM.Groups[1].Value.Trim()) ?? Color.FromArgb(0, 0, 0)
            : Color.FromArgb(0, 0, 0);
        var left = CssEmLen(d, "left", xv.fs) ?? 0;
        return (text.Trim(), col, left);
    }

    private static void Run(XmlViewerState xv, double x, double yTop, string text, Color c, bool bold)
    {
        if (text.Length == 0) return;
        xv.sb.AppendLine(Compat.Format(xv.inv,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg " +
            $"BT /{(bold ? "F9" : "F8")} {xv.fs:0.##} Tf 1 0 0 1 {x:F2} {xv.pageHeight - (yTop + xv.drop):F2} Tm " +
            $"({EscapePdfString(text)}) Tj ET Q"));
    }

    // One rendered line: the leaf's flattened runs at the container indent
    // plus its own margin-left, the optional :before marker hanging left.
    private static void EmitLine(XmlViewerState xv, string leafTag, string leafAttrs, string leafInner, double indent)
    {
        var style = AttrValue("<x " + leafAttrs + ">", "style") ?? "";
        var ownMargin = ParseInlineMarginBox(style, xv.fs).left;
        var lineX = indent + ownMargin;
        if (xv.y + xv.lineBox > xv.pageHeight - XvMarginBottomPt)
        {
            xv.page.AddContentStream(Encoding.ASCII.GetBytes(xv.sb.ToString()));
            xv.sb.Clear();
            xv.page = xv.doc.Pages.Add(xv.pageWidth, xv.pageHeight);
            EnsureFonts(xv.page, xv.docFontDict);
            EnsureFont(xv.page, xv.faceRes, "F8");
            EnsureFont(xv.page, xv.faceRes + "Bold", "F9");
            xv.y = XvMarginTopPt;
        }
        var cls = AttrValue("<x " + leafAttrs + ">", "class") ?? "";
        foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (Before(xv, c) is { } marker)
                Run(xv, lineX + marker.left, xv.y, marker.text, marker.color, false);

        // Flatten to runs: text nodes carry the innermost span's inline
        // color/weight; the leaf's own inline color is the default.
        var defColor = Regex.Match(style, @"(?<![-\w])color\s*:\s*([^;]+)") is { Success: true } dc
            ? ParseCssColor(dc.Groups[1].Value.Trim()) ?? Color.FromArgb(0, 0, 0)
            : Color.FromArgb(0, 0, 0);
        var runs = new List<(string text, Color color, bool bold)>();
        var stack = new Stack<(Color color, bool bold)>();
        stack.Push((defColor, false));
        var pos = 0;
        foreach (Match t in Regex.Matches(leafInner, @"<(/?)(\w+)([^>]*?)(/?)>"))
        {
            var txt = leafInner[pos..t.Index];
            if (txt.Length > 0)
                runs.Add((DecodeEntities(txt), stack.Peek().color, stack.Peek().bold));
            pos = t.Index + t.Length;
            if (t.Groups[4].Value == "/") continue;             // self-closing
            if (t.Groups[1].Value == "/") { if (stack.Count > 1) stack.Pop(); continue; }
            var st = AttrValue(t.Value, "style") ?? "";
            var col = Regex.Match(st, @"(?<![-\w])color\s*:\s*([^;]+)") is { Success: true } cm2
                ? ParseCssColor(cm2.Groups[1].Value.Trim()) ?? stack.Peek().color
                : stack.Peek().color;
            var bold = Regex.IsMatch(st, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase)
                || (stack.Peek().bold
                    && !Regex.IsMatch(st, @"font-weight\s*:\s*normal", RegexOptions.IgnoreCase));
            stack.Push((col, bold));
        }
        var tail = leafInner[pos..];
        if (tail.Length > 0)
            runs.Add((DecodeEntities(tail), stack.Peek().color, stack.Peek().bold));

        // Whitespace: collapse interior runs, trim the line's two ends.
        var x = lineX;
        for (var i = 0; i < runs.Count; i++)
        {
            var text = Regex.Replace(runs[i].text, @"[ \t\r\n\f]+", " ");
            if (i == 0) text = text.TrimStart();
            if (i == runs.Count - 1) text = text.TrimEnd();
            if (text.Length == 0) continue;
            Run(xv, x, xv.y, text, runs[i].color, runs[i].bold);
            x += MeasureFaceText(runs[i].bold ? xv.face + " Bold" : xv.face, text, xv.fs);
        }
        xv.y += xv.lineBox;
        _ = leafTag;
    }

    // Recursive container walk: a div deepens the indent by the `*` padding;
    // a leaf anchor/span renders one line.
    private static void Walk(XmlViewerState xv, string inner, double indent)
    {
        var pos = 0;
        while (true)
        {
            var t = Regex.Match(inner[pos..], @"<(/?)(\w+)([^>]*?)(/?)>|<!--[\s\S]*?-->");
            if (!t.Success) return;
            var abs = pos + t.Index;
            if (t.Value.StartsWith("<!--", StringComparison.Ordinal))
            {
                pos = abs + t.Length;
                continue;
            }
            var closing = t.Groups[1].Value == "/";
            var tag = t.Groups[2].Value.ToLowerInvariant();
            var self = t.Groups[4].Value == "/";
            pos = abs + t.Length;
            if (closing || self) continue;
            if (tag == "div")
            {
                var innerDiv = BalancedInner(inner, pos, "div");
                if (innerDiv is null) return;
                Walk(xv, innerDiv, indent + xv.starPad);
                pos += innerDiv.Length + tag.Length + 3;
            }
            else if (tag is "a" or "span")
            {
                var innerLeaf = BalancedInner(inner, pos, tag);
                if (innerLeaf is null) return;
                EmitLine(xv, tag, t.Groups[3].Value, innerLeaf, indent);
                pos += innerLeaf.Length + tag.Length + 3;
            }
            else if (tag is "style" or "script")
            {
                var end = inner.IndexOf("</" + tag, pos, StringComparison.OrdinalIgnoreCase);
                if (end < 0) return;
                pos = inner.IndexOf('>', end) + 1;
            }
        }
    }
}
