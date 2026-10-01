using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the div-structure segmentation: the effective style, the column-scope test, and one div hit.
    // Print-grid mode (classCss set): a div's effective style folds its CLASS
    // rules' box declarations under the inline style, so class-styled grids
    // (.col-xs-N width%, .infobox borders) segment like inline-styled ones.
    private static string DivEffStyle(DivSegmentState dv, string openTag)
    {
        var st = DivStyleOf(openTag);
        if (dv.classCss is null) return st;
        var clm = Regex.Match(openTag, @"class\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        if (!clm.Success) return st;
        var sb = new StringBuilder(st);
        foreach (var c in clm.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (dv.classCss.TryGetValue("." + c, out var d))
                foreach (var kv in d)
                    if (kv.Key is "border" or "padding" or "padding-top" or "padding-bottom"
                        or "padding-left" or "padding-right" or "margin-bottom" or "width")
                    { sb.Append(';').Append(kv.Key).Append(':').Append(kv.Value); }
        return sb.ToString();
    }

    private static bool IsDivColScopeStyle(DivSegmentState dv, string st) =>
        dv.classCss is not null
        && !Regex.IsMatch(st, @"float\s*:\s*left", RegexOptions.IgnoreCase)
        && !Regex.IsMatch(st, @"border\s*:", RegexOptions.IgnoreCase)
        && StylePct(st, "width") is > 0 and < 100;

    /// <summary></summary>
    private static bool SegmentNextDiv(DivSegmentState dv)
    {
        dv.hit = null; string style = "";
        for (var m = dv.divRx.Match(dv.html, dv.pos); m.Success; m = m.NextMatch())
        {
            var st = DivEffStyle(dv, m.Value);
            if (IsFloatColStyle(st, dv.allowPxCols) || IsBorderBoxStyle(st) || IsDivColScopeStyle(dv, st)) { dv.hit = m; style = st; break; }
        }
        if (dv.hit is null) return false;
        dv.afterOpen = dv.hit.Index + dv.hit.Length;
        (dv.end, var contentEnd) = FindDivEnd(dv.html, dv.afterOpen);
        if (dv.end < 0) return false;
        if (dv.hit.Index > dv.pos)
            dv.segs.Add(new DivSeg { Kind = DivSeg.Flow, Html = dv.html[dv.pos..dv.hit.Index] });
        if (IsDivColScopeStyle(dv, style) && !IsFloatColStyle(style, dv.allowPxCols) && !IsBorderBoxStyle(style))
        {
            dv.segs.Add(new DivSeg
            {
                Kind = DivSeg.Col,
                Html = dv.html[dv.afterOpen..contentEnd],
                WidthFrac = StylePct(style, "width") / 100.0,
                ColPadPt = StyleLenPt(style, "padding-left") + StyleLenPt(style, "padding-right"),
            });
            dv.pos = dv.end;
            return true;
        }
        return SegmentColumnOrPlainDiv(dv, style, contentEnd);
    }
}
