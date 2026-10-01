using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Spread page elements: one inner element of a spread placed.</summary>
    private static bool RenderSpreadElement(SpreadPagesState sp, SpreadRenderState rs, Match el)
    {
        var se = new SpreadElementState();
        se.tag = el.Groups["tag"].Value.ToLowerInvariant();
        se.attrs = el.Groups["attrs"].Value;
        se.elBody = el.Groups["body"].Value;
        // an element nested inside a float div is drawn by the float walk
        if (Regex.IsMatch(se.attrs, "class\\s*=\\s*[\"'][^\"']*float", RegexOptions.IgnoreCase))
            se.tag = "float:" + (se.attrs.Contains("floatright", System.StringComparison.OrdinalIgnoreCase)
                ? "right" : "left");
        else if (el.Index > 0 && Regex.IsMatch(
                     rs.inner[..el.Index], "<div\\b[^>]*float[^>]*>(?:(?!</div>)[\\s\\S])*$",
                     RegexOptions.IgnoreCase))
            return true;

        switch (se.tag)
        {
            case "h1":
                PlaceHeading(sp, rs, se, el, se.tag);
                break;
            case "p":
                PlaceParagraph(sp, rs, se, el, se.tag);
                break;
            case "float:right": case "float:left":
                PlaceFloat(sp, rs, se, el, se.tag);
                break;
        }
        return true;
    }
}
