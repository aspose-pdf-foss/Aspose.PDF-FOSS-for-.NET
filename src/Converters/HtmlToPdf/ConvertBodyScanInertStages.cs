using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Properties that cannot pull the document onto authored geometry: tints, transforms and filters, display, gradient stop selectors, inert borders and relative positions, and vendor-mangled debris no engine would honour.</summary>
    private static bool PropertyCannotPullFlowOntoGeometry(KeyValuePair<string, Dictionary<string, string>> kv, string prop)
    {
        // Properties that cannot pull the document onto authored geometry keep
        // it UA-default: tints; `transform`/`filter` (transform is applied to
        // the element it decorates, never to the flow); `display` (none is
        // suppressed and a block-span breaks its line in this flow — both
        // UA-level behaviours, not authored geometry); and vendor-mangled
        // debris (a leading dash or an embedded space — "-webkit - transform")
        // that no engine would honour.
        if (prop is "color" or "background-color" or "background"
            // text-decoration is a tint on the glyphs it decorates (a strike or an
            // underline rides the run's own baseline) - it moves nothing.
            or "text-decoration"
            or "transform" or "filter" or "display"
            // font-family cannot drive LAYOUT by itself; whether a
            // declared face disqualifies the UA flow is the separate
            // resolvable-family check below.
            or "font-family"
            // …and the metric flow HONOURS class typography (font-size,
            // weight, centring) and page breaks — a class styled this
            // way is rendered, not a reason to abandon the flow. clear
            // only matters to float layouts, which are opt-in.
            or "font" or "font-size" or "font-weight" or "font-style"
            or "text-align" or "white-space"
            // box-sizing switches a model neither flow implements —
            // inert either way
            or "box-sizing"
            or "page-break-after" or "page-break-before" or "clear"
            // height on a class = a spacer the flow already honours
            // through ExplicitHeight (the clear-both float terminator).
            or "height" or "min-height" or "vertical-align"
            // …and the screen-only or no-op declarations: `zoom` (an IE hasLayout
            // trigger), `overflow` (nothing scrolls on paper), `layout-grid` (an IE
            // East-Asian grid no engine here honours), `opacity`, `cursor`, `outline`
            // and `resize` - measured on the parked bucket, each was the ONLY rule
            // keeping a document off the flow the reference renders it in.
            or "zoom" or "overflow" or "overflow-x" or "overflow-y"
            or "layout-grid" or "layout-grid-mode" or "layout-grid-line" or "layout-grid-char"
            or "opacity" or "cursor" or "outline" or "resize"
            // …a word-wrap that only breaks a word too long for its line, a table's own border
            // spacing (grid chrome), and generated `content` (the flow renders the text it
            // states or nothing) - none of them lays the flow out
            or "word-wrap" or "overflow-wrap" or "border-spacing" or "content"
            // (…and a keep-together pagination hint, like the page-break directives above)
            or "page-break-inside") return true;
        // a width floor on a pseudo-class rule (`span:first-child.narrow { min-width: 8% }`) is the
        // label column dialect's, which the flow renders where it renders it at all
        if (prop is "min-width" && kv.Key.Contains(':')) return true;
        // a ZERO border (`border: 0`, `border: 0px`, `* { border: 0px }`) draws nothing - inert
        if (prop.StartsWith("border", StringComparison.Ordinal)
            && Regex.IsMatch(kv.Value[prop].Trim(), @"^0(?:px|pt|em|in|cm|mm)?(?:\s+\S+)*$", RegexOptions.IgnoreCase)) return true;
        // `position: relative` with no offset in the rule moves nothing
        if (prop == "position" && kv.Value[prop].Trim().Equals("relative", StringComparison.OrdinalIgnoreCase)
            && !kv.Value.ContainsKey("top") && !kv.Value.ContainsKey("left")
            && !kv.Value.ContainsKey("right") && !kv.Value.ContainsKey("bottom")) return true;
        // a border declared NONE draws nothing — inert
        if (prop is "border" or "border-style"
            && kv.Value[prop].Contains("none", StringComparison.OrdinalIgnoreCase)) return true;
        if (prop.Length == 0 || prop[0] == '-' || prop.Contains(' ')) return true;
        return false;
    }
}
