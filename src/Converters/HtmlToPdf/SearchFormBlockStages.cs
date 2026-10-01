using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The stages of the search-form block: the descendant scan, the text input's class and inline style, and the buttons.</summary>
    private static void ReadSearchFormButtons(SearchFormBlockState fb)
    {
        if (fb.buttons.Count > 0 && fb.css is not null)
        {
            foreach (var kv in fb.css)
            {
                if (kv.Key is ".lsbb")
                {
                    if (kv.Value.TryGetValue("height", out var bh))
                    {
                        var v = ParsePxValue(bh);
                        if (v > 0) fb.sf.ButtonHeightPx = v + 2;
                    }
                    if (kv.Value.TryGetValue("background", out var bg))
                    {
                        var c = ParseCssColor(bg);
                        if (c is not null) fb.sf.ButtonBg = c;
                    }
                }
                else if (kv.Key is ".lsb")
                {
                    if (kv.Value.TryGetValue("font", out var bf))
                    {
                        var m = Regex.Match(bf, @"([\d.]+)\s*px");
                        if (m.Success) fb.sf.ButtonFontPx = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    if (kv.Value.TryGetValue("color", out var bc))
                    {
                        var c = ParseCssColor(bc);
                        if (c is not null) fb.sf.ButtonFg = c;
                    }
                }
            }
        }
    }

    /// <summary></summary>
    private static void ReadSearchInputStyle(SearchFormBlockState fb)
    {
        if (fb.textInput!.Attrs is not null && fb.textInput.Attrs.TryGetValue("class", out var icls)
            && fb.css is not null)
        {
            foreach (var c in icls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!fb.css.TryGetValue("." + c, out var decls)) continue;
                if (decls.TryGetValue("width", out var wv))
                {
                    var wpx = ParsePxValue(wv);
                    if (wpx > fb.cellW) fb.cellW = wpx;
                    if (wpx > 0 && (fb.inputContentW == 0 || wpx < fb.inputContentW)) fb.inputContentW = wpx;
                }
                if (decls.TryGetValue("height", out var hv))
                {
                    var hpx = ParsePxValue(hv);
                    if (hpx > 0) fb.inputH = hpx;
                }
            }
        }
        if (fb.cellW <= 0) fb.cellW = 496;
        if (fb.inputContentW <= 0) fb.inputContentW = fb.cellW;
        fb.padL = 6;
        fb.padR = 8;
        if (fb.textInput.Attrs is not null && fb.textInput.Attrs.TryGetValue("style", out var istyle))
        {
            var pm = Regex.Match(istyle, @"(?<![\w-])padding\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (pm.Success)
            {
                var parts = pm.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (parts.Length)
                {
                    case 1: fb.padL = fb.padR = ParsePxValue(parts[0]); break;
                    case 2: case 3: fb.padL = fb.padR = ParsePxValue(parts[1]); break;
                    case 4: fb.padR = ParsePxValue(parts[1]); fb.padL = ParsePxValue(parts[3]); break;
                }
            }
        }
    }

    /// <summary></summary>
    private static void ScanSearchFormDescendants(SearchFormBlockState fb)
    {
        foreach (var d in fb.form.Descendants())
        {
            if (d.Tag.Length == 0) continue;
            if (IsHiddenElement(d.Tag, d.Attrs, fb.css)) continue;
            if (d.Tag == "input")
            {
                string? type = null;
                d.Attrs?.TryGetValue("type", out type);
                type = string.IsNullOrEmpty(type) ? "text" : type.ToLowerInvariant();
                if (type is "text" or "search" && fb.textInput is null) fb.textInput = d;
                else if (type == "submit")
                {
                    string? v = null, nm = null;
                    d.Attrs?.TryGetValue("value", out v);
                    d.Attrs?.TryGetValue("name", out nm);
                    if (!string.IsNullOrEmpty(v)) fb.buttons.Add((DecodeEntities(v!), nm ?? ""));
                }
            }
            else if (d.Tag == "img" && fb.icon is null && fb.textInput is not null
                     && d.Attrs is not null && d.Attrs.TryGetValue("style", out var ist)
                     && ist.Contains("absolute", StringComparison.OrdinalIgnoreCase))
                fb.icon = d;
            else if (d.Tag == "a" && fb.link is null && fb.textInput is not null
                     && d.Attrs?.ContainsKey("href") == true)
                fb.link = d;
        }
    }
}
