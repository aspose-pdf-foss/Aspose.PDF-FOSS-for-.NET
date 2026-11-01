using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A form control's em: its UA font of 10 pt, in CSS px.</summary>
    private const double ControlEmPx = 10 / PxPt;

    /// <summary>The CSS root em in px, the base of a control's rem lengths.</summary>
    private const double ControlRemPx = 16;

    /// <summary>CSS px per inch.</summary>
    private const double CssPxPerInch = 96;

    /// <summary>The sheet selectors that reach one control, weakest first: its element name, each class
    /// (bare and element-qualified), then its id.</summary>
    private static List<string> ControlSelectors(string tag, Dictionary<string, string>? attrs)
    {
        var name = tag.ToLowerInvariant();
        var selectors = new List<string> { name };
        if (attrs is not null && attrs.TryGetValue("class", out var classes))
            foreach (var c in classes.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                selectors.Add("." + c);
                selectors.Add(name + "." + c);
            }
        if (attrs is not null && attrs.TryGetValue("id", out var id) && id.Length > 0)
        {
            selectors.Add("#" + id);
            selectors.Add(name + "#" + id);
        }
        return selectors;
    }

    /// <summary>A control's length declaration in CSS px, any absolute unit or em; 0 when absent.</summary>
    private static double ControlLengthPx(string style, string prop)
    {
        var m = SafeRegex.Match(style, @"(?:^|[;\s])" + prop + @"\s*:\s*([\d.]+)\s*(px|pt|in|cm|mm|rem|em)\b",
            RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return 0;
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "px" => n,
            "pt" => n / PxPt,
            "in" => n * CssPxPerInch,
            "cm" => n * CssPxPerInch / 2.54,
            "mm" => n * CssPxPerInch / 25.4,
            "rem" => n * ControlRemPx,
            _ => n * ControlEmPx,
        };
    }

    /// <summary>The border and background declarations a control's sheet rules and style attribute give it,
    /// in cascade order, as one declaration string; null when neither speaks of them.</summary>
    private static string? ControlChromeDecls(IReadOnlyDictionary<string, Dictionary<string, string>>? sheet,
        string? tag, Dictionary<string, string>? attrs, string? styleAttr)
    {
        var decls = new StringBuilder();
        if (sheet is not null && !string.IsNullOrEmpty(tag))
            foreach (var sel in ControlSelectors(tag!, attrs))
            {
                if (!sheet.TryGetValue(sel, out var rule)) continue;
                foreach (var kv in rule.Where(kv => IsChromeProperty(kv.Key)))
                    decls.Append(kv.Key).Append(':').Append(kv.Value).Append(';');
            }
        if (!string.IsNullOrEmpty(styleAttr))
            foreach (var groups in SafeRegex.Matches(StyleDeclRx, styleAttr!).Select(dm => dm.Groups))
                if (IsChromeProperty(groups[1].Value))
                    decls.Append(groups[1].Value).Append(':').Append(groups[2].Value.Trim()).Append(';');
        return decls.Length > 0 ? decls.ToString() : null;
    }

    private static bool IsChromeProperty(string prop) =>
        prop.StartsWith("border", StringComparison.OrdinalIgnoreCase)
        || prop.StartsWith("background", StringComparison.OrdinalIgnoreCase);

    /// <summary>Paints a control box its own CSS: the background colour under the box, then the declared border
    /// sides in place of the UA frame. False when the declarations name no border, so the caller keeps the UA
    /// frame (over the background drawn here).</summary>
    private static bool PaintControlChrome(Page page, string decls, double lx, double bottom, double w, double h)
    {
        Color? fill = null;
        foreach (var groups in SafeRegex.Matches(StyleDeclRx, decls).Select(dm => dm.Groups))
        {
            var prop = groups[1].Value.ToLowerInvariant();
            if (prop is not ("background" or "background-color")) continue;
            fill = groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(tok => ParseCssColor(tok)).LastOrDefault(c => c is not null);
        }
        DrawBox(page, lx, bottom, w, h, border: null, borderWidth: 0, fill: fill);
        if (!SafeRegex.IsMatch(decls, @"(?:^|;)\s*border", RegexOptions.IgnoreCase)) return false;
        var sides = CssBorderSides(decls);
        if (sides.Any(s => s.W > 0))
            EmitCssSideFrames(page, sides, lx, lx + w, bottom + h, bottom, CultureInfo.InvariantCulture);
        return true;
    }
}
