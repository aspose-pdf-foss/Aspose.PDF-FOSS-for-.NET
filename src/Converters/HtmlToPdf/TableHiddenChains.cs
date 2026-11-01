namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static readonly List<(string Context, string Target, string Prop)> NoHidingChains = new();

    /// <summary>Per parsed sheet, its two-part descendant rules that hide their target.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyDictionary<string, Dictionary<string, string>>, List<(string Context, string Target, string Prop)>> HidingChainsBySheet = new();

    /// <summary>The sheet's descendant rules that hide what they reach (`a.tip span { visibility: hidden }`, a
    /// tooltip shown only on hover): the flat hidden check matches an element's own selectors, so a walker
    /// that knows the open ancestors matches these. Read once per sheet.</summary>
    private static List<(string Context, string Target, string Prop)> HidingDescendantRules(IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        if (css is not { Count: > 0 }) return NoHidingChains;
        return HidingChainsBySheet.GetValue(css, sheet =>
        {
            var chains = new List<(string Context, string Target, string Prop)>();
            foreach (var kv in sheet)
            {
                var space = kv.Key.IndexOf(' ');
                if (space <= 0 || kv.Key.IndexOf(":not(", StringComparison.Ordinal) >= 0) continue;
                foreach (var prop in VisibilityProperties)
                    if (kv.Value.TryGetValue(prop, out var value) && HidesBy(prop, value))
                    {
                        chains.Add((kv.Key.Substring(0, space), kv.Key.Substring(space + 1), prop));
                        break;
                    }
            }
            return chains.Count == 0 ? NoHidingChains : chains;
        });
    }

    private static readonly List<List<CssChainSeg>> NoHidingSegs = new();

    /// <summary>Per sheet's hiding descendant rules, each as its parsed chain.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<List<(string Context, string Target, string Prop)>, List<List<CssChainSeg>>> HidingSegsByRules = new();

    /// <summary>The sheet's hiding descendant rules as chains a CSS element chain is matched against: the context
    /// may be a compound (`div.nav ul`), which the hook lookups do not key. Parsed once per sheet.</summary>
    private static List<List<CssChainSeg>> HidingDescendantChains(IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var rules = HidingDescendantRules(css);
        if (rules.Count == 0) return NoHidingSegs;
        return HidingSegsByRules.GetValue(rules, list =>
        {
            var chains = new List<List<CssChainSeg>>();
            foreach (var (context, target, _) in list)
                if (ParseChainSelector(context + " " + target).result is { } segs) chains.Add(segs);
            return chains;
        });
    }

    /// <summary>The hiding descendant rules of the table's own sheet and of the document's.</summary>
    private static List<(string Context, string Target, string Prop)> HidingDescendantRules(
        IReadOnlyDictionary<string, Dictionary<string, string>>? own, IReadOnlyDictionary<string, Dictionary<string, string>>? doc)
    {
        var a = HidingDescendantRules(own);
        var b = HidingDescendantRules(doc);
        if (b.Count == 0 || ReferenceEquals(a, b)) return a;
        if (a.Count == 0) return b;
        var both = new List<(string Context, string Target, string Prop)>(a);
        both.AddRange(b);
        return both;
    }

    /// <summary>Tracks the elements open inside the current cell and reports an opening tag that a hiding
    /// descendant rule reaches through one of them; its subtree is then skipped like any hidden element.
    /// An inline style declaring the property the rule hides by decides instead.</summary>
    private static bool HiddenUnderOpenAncestor(TableParseState ps, List<(string Context, string Target, string Prop)> chains, Token tok, string tag)
    {
        var open = ps.cellOpenElems ??= new List<HtmlNode>();
        if (tok.IsClose)
        {
            var at = open.FindLastIndex(n => n.Tag == tag);
            if (at >= 0) open.RemoveRange(at, open.Count - at);
            return false;
        }
        var isCell = tag is "td" or "th";
        if (isCell) open.Clear();
        else if (ps.cell is null) return false;
        var node = new HtmlNode { Tag = tag, Attrs = tok.Attributes };
        var container = !tok.IsSelfClosing && !VoidTags.Contains(tag);
        if (!isCell && ChainReaches(node, open, chains, tok.Attributes))
        {
            if (container) { ps.hiddenSubTag = tag; ps.hiddenSubDepth = 1; }
            return true;
        }
        if (container) open.Add(node);
        return false;
    }

    private static bool ChainReaches(HtmlNode node, List<HtmlNode> ancestors, List<(string Context, string Target, string Prop)> chains, Dictionary<string, string>? attrs)
    {
        foreach (var (context, target, prop) in chains)
        {
            if (DeclaresOwn(attrs, prop) || !SimpleSelectorMatches(target, node)) continue;
            foreach (var a in ancestors)
                if (SimpleSelectorMatches(context, a)) return true;
        }
        return false;
    }

    private static bool DeclaresOwn(Dictionary<string, string>? attrs, string prop) =>
        attrs is not null && attrs.TryGetValue("style", out var style) && style is not null
        && SafeRegex.IsMatch(style, @"(?<![-\w])" + prop + @"\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
