using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A metric span's float and its own style attribute: a floated span makes no line box in its cell, and a styled one sizes and colours the run it opens.</summary>
    private static void ApplyMetricSpanStyleAttribute(MetricTableState mt, Token tok)
    {
        // A floated span makes no line box in its cell: the cell's own strut does not
        // apply to a row whose text all floats (its rows pitch on the floats' boxes).
        var sFloat = tok.Attributes is { } sfa && sfa.TryGetValue("style", out var sfst)
            && Regex.IsMatch(sfst, @"float\s*:\s*(left|right)\b", RegexOptions.IgnoreCase);
        if (!tok.IsSelfClosing) { mt.floatSpans.Push(sFloat); if (sFloat) mt.floatDepth++; }
        if (mt.mps.cell is not null && tok.Attributes is { } sa
            && sa.TryGetValue("style", out var sst))
        {
        // quote entities decode BEFORE the property scan — the ';'
        // inside &quot; would otherwise truncate a value mid-entity
        // (font-family: &quot;Arial&quot; parsed as the face '&quot')
        if (sst.IndexOf('&') >= 0)
            sst = sst.Replace("&quot;", "\"").Replace("&#34;", "\"")
                     .Replace("&apos;", "'").Replace("&#39;", "'");
        // WIDTH:Npx; DISPLAY:inline-table — the span fixes its column's
        // content width and grows the line box.
        var wm = Regex.Match(sst, @"width\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase);
        if (wm.Success && Regex.IsMatch(sst, @"display\s*:\s*inline-table", RegexOptions.IgnoreCase))
        {
            mt.mps.cell.HasSpan = true;
            mt.mps.cell.SpanW = Math.Max(mt.mps.cell.SpanW, double.Parse(wm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * PxPt);
        }
        // Inline span typography styles the rest of its cell — the
        // legacy corpus wraps whole cell contents in one styled span.
        var sfm = Regex.Match(sst, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (sfm.Success && FirstFontFamily(sfm.Groups[1].Value) is { Length: > 0 } sfam)
            mt.mps.cell.Face = CanonicalStandardFaceName(sfam);
        // An inline span's margin-left indents its cell's text, as an in-cell paragraph's does.
        if (mt.stdSerif && Regex.Match(sst, @"margin-left\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } smlM
            && TryParseLength(smlM.Groups[1].Value.Trim()) is { } smlPt && smlPt > 0)
            mt.mps.cell.PadLeft = Math.Max(mt.mps.cell.PadLeft, smlPt);
        var ssm = Regex.Match(sst, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        // font-size: larger is RELATIVE — 1.2 x the cell's current
        // size (13px title → 15.6px = 11.7 pt, measured), so it must
        // beat the keyword table's fixed UA-base mapping.
        if (ssm.Success && ssm.Groups[1].Value.Trim()
                .Equals("larger", StringComparison.OrdinalIgnoreCase))
            mt.mps.cell.FontSize = HtmlLargerStepPt(mt.mps.cell.FontSize ?? mt.mps.fontSize);
        else if (ssm.Success && TryParseCssFontSize(ssm.Groups[1].Value.Trim()) is { } sfs)
            mt.mps.cell.FontSize = sfs;
        if (Regex.IsMatch(sst, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
            mt.mps.cell.Italic = true;
        if (Regex.IsMatch(sst, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase))
            mt.mps.cell.Bold = true;
        // (a UA cell's inline box carries its own left padding: the pen advances by it before the
        // box's ink - probed on the cheque: a `padding-left: 5px` span seats 3.75 past the run before)
        if (mt.stdSerif && !mt.reportCells && Regex.Match(sst, @"(?<![-\w])padding-left\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } splM
            && TryParseLength(splM.Groups[1].Value.Trim()) is { } splPt && splPt > 0)
            mt.mps.pendingRunPadLeft += splPt;
        var scm = Regex.Match(sst, @"(?<![-\w])color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (scm.Success && ParseCssColor(scm.Groups[1].Value.Trim()) is { } scol
            && (scol.R != 255 || scol.G != 255 || scol.B != 255))
            mt.mps.cell.Fore = scol;
        }
    }
}
