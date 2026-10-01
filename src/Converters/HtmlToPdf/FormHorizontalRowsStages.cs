using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the form horizontal-row transform: the class, style, span, pixel and column-width reads, and one row group.
    private static string FhClassOf(HtmlNode n)
    => n.Attrs is not null && n.Attrs.TryGetValue("class", out var c) ? c : "";

    private static string FhStyleOf(HtmlNode n)
    => n.Attrs is not null && n.Attrs.TryGetValue("style", out var s2) ? s2 : "";

    private static double FhPxOf(string style, string prop)
    {
        var m = Regex.Match(style, prop + @"\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static double FhColWidth(HtmlNode n, double containerPx)
    {
        for (var p2 = n.Parent; p2 is not null; p2 = p2.Parent)
        {
            var st = FhStyleOf(p2);
            if (p2.Tag == "div" && FhPxOf(st, "width") > 0
                && Regex.IsMatch(st, @"float\s*:\s*left", RegexOptions.IgnoreCase))
                return FhPxOf(st, "width");
        }
        return containerPx > 0 ? containerPx : 780.0;
    }

    /// <summary></summary>
    private static bool TransformFormRowGroup(HtmlNode g, double containerPx, string html, System.Globalization.CultureInfo inv, string rowStyle, List<(int start, int end, string repl)> repls)
    {
        if (g.Tag != "div" || !FhClassOf(g).Contains("control-group", StringComparison.OrdinalIgnoreCase))
            return true;
        HtmlNode? label = null, controls = null;
        foreach (var d in g.Descendants())
        {
            if (label is null && d.Tag == "label") label = d;
            if (controls is null && d.Tag == "div"
                && FhClassOf(d).Contains("controls", StringComparison.OrdinalIgnoreCase)) controls = d;
            if (label is not null && controls is not null) break;
        }
        if (label is null || controls is null) return true;
        var labStyle = FhStyleOf(label);
        var wLab = FhPxOf(labStyle, "width");
        var mVal = FhPxOf(FhStyleOf(controls), "margin-left");
        if (wLab <= 0 || mVal <= wLab
            || !Regex.IsMatch(labStyle, @"float\s*:\s*left", RegexOptions.IgnoreCase)) return true;
        var wVal = Math.Max(60, FhColWidth(g, containerPx) - mVal);
        // A value span carrying its own CSS width sets the value cell's width —
        // it may overflow the enclosing float column exactly as the float does
        // in a browser (`width:210px` inside the 380px column runs past 380).
        foreach (var d in controls.Descendants())
            if (d.Tag == "span")
            {
                var sw = FhPxOf(FhStyleOf(d), "width");
                if (sw > 0) wVal = sw;
                break;
            }
        // The cell's inner text box loses a few px to the cell inset; pad the
        // value cell so its wrap width matches the span's CSS content width
        // ("…catheters," stays on the 190px line; the inset-
        // narrowed cell broke it a word early).
        wVal += 6;
        var bold = Regex.IsMatch(labStyle, @"font-weight\s*:\s*(700|bold)", RegexOptions.IgnoreCase);
        var labText = FhContentSpan(label) is { } ls2 ? html[ls2.start..ls2.end].Trim() : "";
        var valHtml = FhContentSpan(controls) is { } cs2 ? html[cs2.start..cs2.end].Trim() : "";
        // class=fh-row marks the synthesized row for the layout pass (per-row CSS
        // rhythm); data-fhw carries its natural width so a row wider than its
        // float column keeps that width instead of being squeezed to fit.
        var row = "<table class=\"fh-row\" data-fhw=\"" + (wLab + (mVal - wLab) + wVal).ToString(inv)
            + "\"" + rowStyle + "><tr><td style=\"width:" + wLab.ToString(inv)
            + "px;text-align:right;\">" + (bold ? "<b>" : "") + labText + (bold ? "</b>" : "")
            + "</td><td style=\"width:" + (mVal - wLab).ToString(inv)
            + "px\"></td><td style=\"width:" + wVal.ToString(inv)
            + "px\">" + valHtml + "</td></tr></table>";
        repls.Add((g.SrcIndex, g.SrcEnd, row));
        return true;
    }
    private static (int start, int end)? FhContentSpan(HtmlNode n) =>
        n.Children.Count > 0 ? (n.Children[0].SrcIndex, n.Children[^1].SrcEnd) : null;
}
