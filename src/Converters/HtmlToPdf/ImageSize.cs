using System.Globalization;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static readonly Regex CssLengthValueRx = new(
        @"^\s*([\d.]+)\s*(px|pt|in|cm|mm|rem|em)?\s*(?:!important)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);

    /// <summary>A CSS length in px, any absolute unit or em of <paramref name="emPx"/> (a unitless number is quirks
    /// px); null for anything else (auto, a percentage, a calc).</summary>
    private static double? CssLengthPx(string value, double emPx)
    {
        var m = SafeRegex.Match(CssLengthValueRx, value);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return null;
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "pt" => n / PxPt,
            "in" => n * CssPxPerInch,
            "cm" => n * CssPxPerInch / 2.54,
            "mm" => n * CssPxPerInch / 25.4,
            "rem" => n * ControlRemPx,
            "em" => n * emPx,
            _ => n,
        };
    }

    // A selector's specificity as one number: ids, then classes, then type names (none reaches ten).
    private const int SpecificityIdWeight = 100;
    private const int SpecificityClassWeight = 10;

    /// <summary>The specificity of a selector of type, class and id simple selectors joined by descent.</summary>
    private static int CssSpecificity(string selector)
    {
        var specificity = 0;
        foreach (var compound in selector.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            foreach (var simple in SafeRegex.Split(compound, @"(?=[.#])"))
            {
                if (simple.Length == 0) continue;
                specificity += simple[0] == '#' ? SpecificityIdWeight : simple[0] == '.' ? SpecificityClassWeight : 1;
            }
        return specificity;
    }

    /// <summary>The declaration blocks that size an image, weakest first: the sheet's rules the image matches - its
    /// type rule, the descendant rules its open ancestors satisfy, its class rules (bare, element-qualified and
    /// under an ancestor) and its id rule - in the order of their specificity, then its style attribute.</summary>
    private static IEnumerable<IReadOnlyDictionary<string, string>> ImageSizeRules(ParseBlocksState pb,
        Dictionary<string, string> attrs)
    {
        var classes = attrs.TryGetValue("class", out var cls) && cls is not null
            ? cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        if (pb.css is { } css)
        {
            var hooks = new List<string>();
            for (var i = 0; i < pb.divTagStack.Count; i++)
            {
                hooks.Add(pb.divTagStack[i]);
                if (i < pb.divClassStack.Count)
                    hooks.AddRange(pb.divClassStack[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(c => "." + c));
            }
            var selectors = new List<string> { "img" };
            selectors.AddRange(hooks.Select(h => h + " img"));
            foreach (var c in classes)
            {
                selectors.Add("." + c);
                selectors.Add("img." + c);
                selectors.AddRange(hooks.Select(h => h + " ." + c));
            }
            if (attrs.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id)) selectors.Add("#" + id.Trim());
            foreach (var sel in selectors.OrderBy(CssSpecificity))
                if (css.TryGetValue(sel, out var rule)) yield return rule;
        }
        if (attrs.TryGetValue("style", out var style) && !string.IsNullOrEmpty(style))
        {
            var own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var groups in SafeRegex.Matches(StyleDeclRx, style).Select(m => m.Groups))
                own[groups[1].Value] = groups[2].Value.Trim();
            yield return own;
        }
    }

    /// <summary>The image's CSS width and height from the cascade over its attributes, each converted from its own
    /// unit to px; a percentage width is a share of the containing block, resolved at layout, and <c>auto</c> from
    /// a rule that wins over a sized one gives the side back to the picture.</summary>
    private static void ReadImageCssSize(ParseBlocksState pb, Dictionary<string, string> attrs)
    {
        var emPx = pb.styleStack.Peek().FontSize / PxPt;
        foreach (var rule in ImageSizeRules(pb, attrs))
        {
            if (rule.TryGetValue("width", out var w))
            {
                if (ReadPercentImageWidth(pb, w)) pb.iw = 0;
                else if (CssLengthPx(w, emPx) is { } wPx) { pb.iw = wPx; pb.imgWFrac = 0; }
                else if (IsCssAuto(w)) { pb.iw = 0; pb.imgWFrac = 0; }
            }
            if (rule.TryGetValue("height", out var h))
            {
                if (CssLengthPx(h, emPx) is { } hPx) pb.ih = hPx;
                else if (IsCssAuto(h)) pb.ih = 0;
            }
        }
    }

    private static bool IsCssAuto(string value) =>
        value.Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);
}
