using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static readonly string[] ListStyleTypeWords =
    {
        "none", "disc", "circle", "square", "decimal",
        "lower-alpha", "upper-alpha", "lower-latin", "upper-latin", "lower-roman", "upper-roman",
    };

    /// <summary>The list-style-type keyword a <c>list-style-type</c> or <c>list-style</c> value names, the latin
    /// spellings read as alpha; null when it names none.</summary>
    private static string? ListStyleKeyword(string value)
    {
        foreach (var tok in value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = tok.Trim().ToLowerInvariant();
            if (Array.IndexOf(ListStyleTypeWords, word) >= 0) return word.Replace("latin", "alpha");
        }
        return null;
    }

    /// <summary>The list-style-type of one rule or declaration block: the longhand over the shorthand.</summary>
    private static string? RuleListStyle(IReadOnlyDictionary<string, string>? rule)
    {
        if (rule is null) return null;
        if (rule.TryGetValue("list-style-type", out var t) && ListStyleKeyword(t) is { } typed) return typed;
        return rule.TryGetValue("list-style", out var s) ? ListStyleKeyword(s) : null;
    }

    /// <summary>The list-style-type a list or list item declares for itself (empty = none declared, the list's
    /// or the UA default applies): its legacy type attribute, then the sheet's tag, class, descendant and id
    /// rules, then its style attribute, each overriding the one before.</summary>
    private static string ListStyleTypeFor(ParseBlocksState pb, Dictionary<string, string>? attrs, string tag)
    {
        string? type = null;
        if (attrs is not null && attrs.TryGetValue("type", out var attr) && attr is not null)
            type = attr.Trim() switch
            {
                "A" => "upper-alpha", "a" => "lower-alpha",
                "I" => "upper-roman", "i" => "lower-roman",
                "1" => "decimal",
                var word => ListStyleKeyword(word) is "disc" or "circle" or "square" ? word.ToLowerInvariant() : null,
            };
        if (pb.css is { } css)
        {
            var name = tag.ToLowerInvariant();
            var hooks = new List<string>();
            if (attrs is not null && attrs.TryGetValue("class", out var cls) && cls is not null)
                foreach (var c in cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    hooks.Add("." + c);
                    hooks.Add(name + "." + c);
                }
            var selectors = new List<string> { name };
            selectors.AddRange(hooks);
            // (the open ancestors, the element itself last on the stacks, hook "ancestor element" rules)
            for (var i = 0; i < pb.divTagStack.Count - 1; i++)
            {
                var anc = new List<string> { pb.divTagStack[i] };
                if (i < pb.divClassStack.Count)
                    foreach (var c in pb.divClassStack[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        anc.Add("." + c);
                        // (…and the ancestor's tag-compound hook, `div.ls ul`)
                        anc.Add(pb.divTagStack[i] + "." + c);
                    }
                foreach (var a in anc)
                {
                    selectors.Add(a + " " + name);
                    selectors.AddRange(hooks.Where(h => h[0] == '.').Select(h => a + " " + h));
                }
            }
            if (attrs is not null && attrs.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id))
                selectors.Add("#" + id.Trim());
            foreach (var sel in selectors)
                if (css.TryGetValue(sel, out var rule) && RuleListStyle(rule) is { } ruled) type = ruled;
        }
        if (attrs is not null && attrs.TryGetValue("style", out var style) && !string.IsNullOrEmpty(style))
        {
            var own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var groups in SafeRegex.Matches(StyleDeclRx, style).Select(m => m.Groups))
                own[groups[1].Value] = groups[2].Value.Trim();
            if (RuleListStyle(own) is { } inline) type = inline;
        }
        return type ?? "";
    }

    /// <summary>The marker of a list item from its computed list-style-type (its own, else its list's): none
    /// draws no marker, a bullet keyword its glyph, an ordinal type the next count; with neither declared, the
    /// list's kind decides (the UA ordinal or depth bullet). An ordered list counts every item, marked or not.</summary>
    private static string? ListItemMarker(ParseBlocksState pb, Dictionary<string, string>? attrs)
    {
        var type = pb.style.ListStyleType.Length > 0 ? pb.style.ListStyleType : pb.parent.ListStyleType;
        // (a none list whose items generate their counter in a ::before rule still shows the count: the
        // generated number replaces the marker the list style removed)
        if (type == "none" && CounterBeforeSelects(pb, attrs)) type = "decimal";
        var ordinal = type is "decimal" or "lower-alpha" or "upper-alpha" or "lower-roman" or "upper-roman";
        var count = pb.parent.ListKind == 1 || ordinal ? ++pb.parent.ListCounter : 0;
        return type switch
        {
            "none" => null,
            "disc" => "•",
            "circle" => "◦",
            // (the CSS-box flow's square is the medium small square, as its UA depth bullet)
            "square" => pb.cssBoxBody ? "◾" : "▪",
            _ when ordinal || pb.parent.ListKind == 1 => FormatListOrdinal(count, type) + ".",
            _ => UaBulletMarker(pb),
        };
    }

    /// <summary>The list-marker rules of the document's sheets: the colour an unscoped <c>li::marker</c> /
    /// <c>::marker</c> rule gives every marker (null when there is none), and the selectors - as their chains of
    /// tag and class compounds - of the <c>::before</c> rules whose content is a <c>counter()</c> or
    /// <c>counters()</c>.</summary>
    private static void ScanSheetMarkerRules(ConvertState cv)
    {
        foreach (Match block in SafeRegex.Matches(cv.html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var css = SafeRegex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
            foreach (var groups in SafeRegex.Matches(css, @"([^{}]+)\{([^{}]*)\}").Select(rule => rule.Groups))
            {
                var selectors = groups[1].Value.Split(',').Select(sel => sel.Trim().ToLowerInvariant()).ToList();
                var marker = selectors.Any(sel => sel is "li::marker" or "::marker" or "*::marker");
                var counted = selectors.Any(sel => sel.EndsWith("before", StringComparison.Ordinal))
                    && groups[2].Value.IndexOf("counter", StringComparison.OrdinalIgnoreCase) >= 0;
                // (only a marker rule or a counted ::before rule has declarations to read)
                if (!marker && !counted) continue;
                foreach (var decl in SafeRegex.Matches(StyleDeclRx, groups[2].Value).Select(m => m.Groups))
                {
                    var prop = decl[1].Value;
                    if (marker && prop.Equals("color", StringComparison.OrdinalIgnoreCase))
                        cv.markerColor = ParseCssColor(decl[2].Value.Trim()) ?? cv.markerColor;
                    else if (counted && prop.Equals("content", StringComparison.OrdinalIgnoreCase)
                        && SafeRegex.IsMatch(decl[2].Value, @"\bcounters?\(", RegexOptions.IgnoreCase))
                        foreach (var sel in selectors)
                            if (SafeRegex.Match(sel, @"^([\w.\s>+~-]+?)::?before$") is { Success: true } chain)
                                (cv.counterMarkers ??= new()).Add(chain.Groups[1].Value
                                    .Split(new[] { ' ', '\t', '\r', '\n', '>', '+', '~' }, StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }
    }

    /// <summary>Whether a counter-content ::before rule selects this list item: the selector's last compound
    /// names an li (or no tag) and classes the item carries, and each compound before it an open ancestor
    /// (every combinator read as a descendant one).</summary>
    private static bool CounterBeforeSelects(ParseBlocksState pb, Dictionary<string, string>? attrs)
    {
        if (pb.cv?.counterMarkers is not { } chains) return false;
        var own = attrs is not null && attrs.TryGetValue("class", out var cls) ? cls : null;
        return chains.Any(chain => CompoundMatches(chain[chain.Length - 1], "li", own)
            && chain.Take(chain.Length - 1).All(anc => OpenAncestorMatches(pb, anc)));
    }

    private static bool OpenAncestorMatches(ParseBlocksState pb, string compound)
    {
        // (the open ancestors, the element itself last on the stacks)
        for (var i = 0; i < pb.divTagStack.Count - 1; i++)
            if (CompoundMatches(compound, pb.divTagStack[i], i < pb.divClassStack.Count ? pb.divClassStack[i] : null))
                return true;
        return false;
    }

    /// <summary>Whether a <c>tag.class.class</c> compound (tag optional) selects an element.</summary>
    private static bool CompoundMatches(string compound, string tag, string? classAttr)
    {
        var parts = compound.Split('.');
        if (parts[0].Length > 0 && !parts[0].Equals(tag, StringComparison.OrdinalIgnoreCase)) return false;
        var classes = (classAttr ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Skip(1).All(name => classes.Contains(name, StringComparer.OrdinalIgnoreCase));
    }
}
