using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A sheet rule that names a face, read per selector of its list: the selector as written, the family stack
    /// it declares, and the element the selector styles (see <see cref="FaceRuleSubject"/>; null reaches every element).</summary>
    internal sealed class SheetFamilyRule
    {
        public string Selector = "";
        public string Family = "";
        public CssChainSeg? Subject;
    }

    /// <summary>The rules of a parsed sheet a check asks whole, kept as written while the flat map is built: the flat map
    /// keeps only the selectors it can express, under keys of its own, so a check that looks keys up misses every
    /// spelling it did not think of. A rule is kept as its selector list and value; its selectors are read once, when a
    /// check first asks, so the several reads of a page's sheet pay for no more than the list.</summary>
    internal sealed class KeptSheetRules
    {
        /// <summary>The rules that name a face: their selector lists and the font-family or `font` shorthand value.</summary>
        public readonly List<(string Selectors, string Value, bool Shorthand)> FamilyDecls = new();
        /// <summary>The page's body element: its id, classes and own face.</summary>
        public CssElem Body = null!;
        private List<SheetFamilyRule>? _families;

        /// <summary>The face rules, one per selector, read on first use.</summary>
        public List<SheetFamilyRule> Families => LazyInitializer.EnsureInitialized(ref _families, () => ReadFamilyRules(FamilyDecls))!;
    }

    /// <summary>Per parsed sheet, the rules it keeps whole (see <see cref="KeptSheetRules"/>).</summary>
    private static readonly ConditionalWeakTable<Dictionary<string, Dictionary<string, string>>, KeptSheetRules> KeptRulesTable = new();

    /// <summary>One stylesheet rule's face added to the sheet's kept rules (created when the first is kept; the caller sets
    /// the body element once the sheet is read).</summary>
    private static KeptSheetRules? KeepRulesAsWritten(KeptSheetRules? kept, string selectors, Dictionary<string, string> decls)
    {
        var family = decls.TryGetValue("font-family", out var declared) ? declared : null;
        var shorthand = decls.TryGetValue("font", out var font) ? font : null;
        if (family is null && shorthand is null) return kept;
        kept ??= new KeptSheetRules();
        if (family is not null) kept.FamilyDecls.Add((selectors, family, false));
        if (shorthand is not null) kept.FamilyDecls.Add((selectors, shorthand, true));
        return kept;
    }

    /// <summary>The kept face rules read per selector: the family each names (a shorthand's family part) and the element
    /// its selector styles.</summary>
    private static List<SheetFamilyRule> ReadFamilyRules(List<(string Selectors, string Value, bool Shorthand)> decls)
    {
        var rules = new List<SheetFamilyRule>();
        foreach (var (selectors, value, shorthand) in decls)
        {
            var family = shorthand ? FontShorthandFamily(value) : value;
            foreach (var selector in SelectorsAsWritten(selectors))
                rules.Add(new SheetFamilyRule { Selector = selector, Family = family, Subject = FaceRuleSubject(selector) });
        }
        return rules;
    }

    /// <summary>The selectors of a rule's list, a legacy comment wrapper dropped; an at-rule's block (the @font-face that
    /// defines a family) styles no element and gives none.</summary>
    private static IEnumerable<string> SelectorsAsWritten(string selectors)
    {
        foreach (var raw in selectors.Split(','))
        {
            var selector = raw.Replace("<!--", " ").Replace("-->", " ").Trim();
            if (selector.Length > 0 && selector[0] != '@') yield return selector;
        }
    }

    /// <summary>The family a `font` shorthand ends in, after its size and line height; the whole value where no size
    /// opens a family (`inherit`, a system font).</summary>
    private static string FontShorthandFamily(string value)
    {
        var m = SafeRegex.Match(FontShorthandSizeRx, value);
        return m.Success ? m.Groups["family"].Value.Trim() : value.Trim();
    }

    private static readonly Regex FontShorthandSizeRx = new Regex(
        @"(?<![\w.-])(?<size>\d*\.?\d+(?:px|pt|pc|em|rem|ex|ch|%|cm|mm|in|q|vw|vh|vmin|vmax)|0|x*-?small|x*-?large|medium|smaller|larger)(?:\s*/\s*[^\s,]+)?\s+(?<family>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);

    /// <summary>The face and the size a `font` shorthand states: the first family of its stack and the size in
    /// points (a keyword size at the UA mapping; 0 where the size cannot be read); null where no size opens a family.</summary>
    private static (string Family, double SizePt)? FontShorthandParts(string value)
    {
        var m = SafeRegex.Match(FontShorthandSizeRx, value);
        if (!m.Success || FirstFontFamily(m.Groups["family"].Value) is not { } family) return null;
        return (family, TryParseCssFontSize(m.Groups["size"].Value) ?? 0);
    }

    /// <summary>The element a selector styles - the compound it ends in - as a segment of its tag and its class and id
    /// hooks. The selector's other compounds, and the pseudo-classes, pseudo-elements and attribute tests of its last,
    /// are taken to hold: they only narrow the elements it reaches. Null when the compound cannot be read, which
    /// reaches every element.</summary>
    private static CssChainSeg? FaceRuleSubject(string selector)
    {
        var s = selector;
        if (s.IndexOf('[') >= 0) s = SafeRegex.Replace(AttributeTestRx, s, "[]");
        if (s.IndexOf('(') >= 0)
            for (var bare = ""; bare != s;)
            {
                bare = s;
                s = SafeRegex.Replace(PseudoArgumentRx, s, "()");
            }
        s = s.TrimEnd(SelectorCombinatorChars);
        return ReadCompound(s[(s.LastIndexOfAny(SelectorCombinatorChars) + 1)..]);
    }

    private static readonly Regex AttributeTestRx = new Regex(@"\[[^\[\]]*\]", RegexOptions.Compiled, RegexLimits.Timeout);
    private static readonly Regex PseudoArgumentRx = new Regex(@"\([^()]*\)", RegexOptions.Compiled, RegexLimits.Timeout);
    private static readonly char[] SelectorCombinatorChars = { ' ', '\t', '\r', '\n', '>', '+', '~' };

    /// <summary>A compound selector read as a segment: a tag or `*`, then class and id hooks, pseudo-classes and
    /// pseudo-elements (their arguments emptied) and emptied attribute tests; null for anything else.</summary>
    private static CssChainSeg? ReadCompound(string c)
    {
        if (c.Length == 0) return null;
        var seg = new CssChainSeg();
        var i = c[0] == '*' ? 1 : IdentEnd(c, 0);
        if (i > 0 && c[0] != '*') seg.Tag = c[..i].ToLowerInvariant();
        while (i < c.Length)
        {
            if (c[i] is '[' && i + 1 < c.Length && c[i + 1] == ']') { i += 2; continue; }
            var mark = c[i];
            if (mark is not ('.' or '#' or ':')) return null;
            var start = mark == ':' && i + 1 < c.Length && c[i + 1] == ':' ? i + 2 : i + 1;
            var end = IdentEnd(c, start);
            if (end == start) return null;
            var name = c[start..end];
            if (mark == '.') (seg.Classes ??= new List<string>()).Add(name);
            else if (mark == '#') seg.Id = name;
            // (`:root` is the html element, whose face the body rule overrides)
            else if (name.Equals("root", StringComparison.OrdinalIgnoreCase)) seg.Tag = "html";
            i = mark == ':' && end + 1 < c.Length && c[end] == '(' && c[end + 1] == ')' ? end + 2 : end;
        }
        return seg;
    }

    /// <summary>Where the identifier starting at <paramref name="i"/> ends (letters, digits, `-`, `_`).</summary>
    private static int IdentEnd(string c, int i)
    {
        while (i < c.Length && (char.IsLetterOrDigit(c[i]) || c[i] is '-' or '_')) i++;
        return i;
    }

    /// <summary>The page's body element - its id, classes and own face - for the rules matched against it; a bare body
    /// where the page carries no body tag.</summary>
    private static CssElem SheetBodyElement(string html) =>
        SafeRegex.Match(BodyOpenTagRx, html) is { Success: true } tag ? ElementOfOpenTag("body", tag.Value) : new CssElem { Tag = "body" };

    private static readonly Regex BodyOpenTagRx = new Regex(@"<body\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);

    /// <summary>The element an open tag starts: its tag, the id and classes a selector hooks on, and the face its own
    /// markup names (the first of each attribute counts, as a parser keeps it).</summary>
    private static CssElem ElementOfOpenTag(string tag, string openTag)
    {
        var e = new CssElem { Tag = tag.ToLowerInvariant() };
        if (openTag.IndexOf('=') < 0) return e;
        string? style = null, face = null;
        foreach (Match m in SafeRegex.Matches(HookAttributeRx, openTag))
        {
            var value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "id": e.Id ??= value.Trim(); break;
                case "class": e.Classes ??= value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } cls ? cls : null; break;
                case "style": style ??= value; break;
                default: face ??= value; break;
            }
        }
        e.OwnFamily = OwnFamily(style, face);
        return e;
    }

    private static readonly Regex HookAttributeRx = new Regex(
        @"(?<![\w-])(id|class|style|face)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>""']+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);

    /// <summary>The face an element's own markup names: the last font-family or `font` family of its style attribute,
    /// else its face attribute; null where it names none.</summary>
    private static string? OwnFamily(string? style, string? face)
    {
        string? family = null;
        if (style is not null && style.IndexOf("font", StringComparison.OrdinalIgnoreCase) >= 0)
            foreach (var groups in SafeRegex.Matches(StyleDeclRx, style).Select(d => d.Groups))
            {
                var prop = groups[1].Value;
                if (prop.Equals("font-family", StringComparison.OrdinalIgnoreCase)) family = groups[2].Value.Trim();
                else if (prop.Equals("font", StringComparison.OrdinalIgnoreCase)) family = FontShorthandFamily(groups[2].Value);
            }
        return family ?? face;
    }

    /// <summary>The elements a grid's own markup opens - its table, sections, rows and cells and what they hold, the
    /// grids nested in its cells and their wrappers included - one per distinct open tag, with the tags, classes and
    /// ids among them: a subject that asks for one they lack reaches none, which rules most rules out unread.</summary>
    internal sealed class GridMarkup
    {
        public readonly List<CssElem> Elements = new();
        private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _classes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);

        public GridMarkup(string html)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in SafeRegex.Matches(MarkupOpenTagRx, html))
            {
                if (!seen.Add(m.Value)) continue;
                var e = ElementOfOpenTag(m.Groups[1].Value, m.Value);
                Elements.Add(e);
                _tags.Add(e.Tag);
                if (e.Id is { } id) _ids.Add(id);
                if (e.Classes is { } classes) _classes.UnionWith(classes);
            }
        }

        /// <summary>True when an element of the markup is one the segment styles.</summary>
        public bool Holds(CssChainSeg s) =>
            (s.Tag is null || _tags.Contains(s.Tag)) && (s.Id is null || _ids.Contains(s.Id))
            && (s.Classes is null || s.Classes.TrueForAll(_classes.Contains)) && Elements.Exists(e => ChainSegMatches(s, e));
    }

    private static readonly Regex MarkupOpenTagRx = new Regex(@"<([a-zA-Z][\w:-]*)\b[^>]*>", RegexOptions.Compiled, RegexLimits.Timeout);
}
