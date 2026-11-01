using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static readonly Regex FragmentTagRx = new(
        @"<(/?)([a-zA-Z][\w-]*)((?:[^>""']|""[^""]*""|'[^']*')*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexLimits.Timeout);

    private static readonly Regex StyleAttrRx = new(
        @"\sstyle\s*=\s*(?:""([^""]*)""|'([^']*)')",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);

    /// <summary>Elements a sheet rule never reaches through the inline style: the document scaffolding (whose body
    /// rule the fragment flows read themselves) and the non-rendered elements.</summary>
    private static readonly HashSet<string> SheetInlineSkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "head", "body", "style", "script", "meta", "link", "title", "br", "hr",
    };

    /// <summary>An HtmlFragment's own &lt;style&gt; rules cascaded onto its elements: each element takes the
    /// declarations of the rules its tag, classes and id select - also under one open ancestor, for the two-part
    /// descendant rules - weakest first, written ahead of its own style attribute, which still wins. The fragment
    /// layouts read inline styles; a fragment without a sheet comes back unchanged.</summary>
    internal static string InlineFragmentSheet(string html)
    {
        if (html.IndexOf("<style", StringComparison.OrdinalIgnoreCase) < 0) return html;
        var sheet = ParseStyleSheet(html);
        if (!sheet.Keys.Any(k => !k.Equals("body", StringComparison.OrdinalIgnoreCase)
                && !k.Equals("html", StringComparison.OrdinalIgnoreCase)))
            return html;
        var open = new List<(string Tag, List<string> Hooks)>();
        var inStyle = false;
        return SafeRegex.Replace(FragmentTagRx, html, m =>
        {
            var tag = m.Groups[2].Value.ToLowerInvariant();
            if (tag == "style") { inStyle = m.Groups[1].Length == 0; return m.Value; }
            if (inStyle) return m.Value;
            if (m.Groups[1].Length > 0)
            {
                var at = open.FindLastIndex(o => o.Tag == tag);
                if (at >= 0) open.RemoveRange(at, open.Count - at);
                return m.Value;
            }
            var attrs = m.Groups[3].Value;
            var hooks = SelectorHooks(tag, attrs);
            var selfClosing = attrs.EndsWith("/", StringComparison.Ordinal) || VoidTags.Contains(tag);
            var result = SheetInlineSkipTags.Contains(tag) ? m.Value : WithSheetDeclarations(m.Value, sheet, tag, hooks, open);
            if (!selfClosing) open.Add((tag, hooks));
            return result;
        });
    }

    /// <summary>The simple selectors one element answers to: its tag, each class bare and tag-qualified, its id.</summary>
    private static List<string> SelectorHooks(string tag, string attrs)
    {
        var hooks = new List<string> { tag };
        if (SafeRegex.Match(attrs, @"\sclass\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>""']+))", RegexOptions.IgnoreCase) is { Success: true } cm)
            foreach (var c in (cm.Groups[1].Success ? cm.Groups[1].Value : cm.Groups[2].Success ? cm.Groups[2].Value : cm.Groups[3].Value)
                         .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                hooks.Add("." + c);
                hooks.Add(tag + "." + c);
            }
        if (SafeRegex.Match(attrs, @"\sid\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>""']+))", RegexOptions.IgnoreCase) is { Success: true } im)
            hooks.Add("#" + (im.Groups[1].Success ? im.Groups[1].Value : im.Groups[2].Success ? im.Groups[2].Value : im.Groups[3].Value));
        return hooks;
    }

    /// <summary>The start tag with the declarations of every rule that selects it, in specificity order (simple
    /// selectors, then descendant rules under its open ancestors), ahead of its own style attribute.</summary>
    private static string WithSheetDeclarations(string startTag, Dictionary<string, Dictionary<string, string>> sheet,
        string tag, List<string> hooks, List<(string Tag, List<string> Hooks)> open)
    {
        var decls = new StringBuilder();
        void Take(string selector)
        {
            if (!sheet.TryGetValue(selector, out var rule)) return;
            foreach (var kv in rule) decls.Append(kv.Key).Append(':').Append(kv.Value).Append(';');
        }
        foreach (var hook in hooks.Where(h => h[0] != '#')) Take(hook);
        foreach (var anc in open)
            foreach (var ah in anc.Hooks)
                foreach (var hook in hooks)
                    Take(ah + " " + hook);
        foreach (var hook in hooks.Where(h => h[0] == '#')) Take(hook);
        if (decls.Length == 0) return startTag;
        var own = SafeRegex.Match(StyleAttrRx, startTag);
        if (!own.Success)
        {
            var end = startTag.EndsWith("/>", StringComparison.Ordinal) ? startTag.Length - 2 : startTag.Length - 1;
            return startTag[..end] + " style=\"" + decls.Replace("\"", "'") + "\"" + startTag[end..];
        }
        var ownValue = own.Groups[1].Success ? own.Groups[1].Value : own.Groups[2].Value;
        var quote = own.Groups[1].Success ? '"' : '\'';
        var merged = decls.Replace(quote.ToString(), quote == '"' ? "'" : "\"") + ownValue;
        return startTag[..own.Index] + " style=" + quote + merged + quote + startTag[(own.Index + own.Length)..];
    }
}
