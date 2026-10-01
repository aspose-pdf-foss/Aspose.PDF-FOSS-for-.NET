using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the RTL SVG table block: the cell and placeholder helpers, one data row, the legend.
    private static List<HtmlNode> Cells(HtmlNode tr)
    {
        var cells = new List<HtmlNode>();
        foreach (var c in tr.Children)
            if (c.Tag is "td" or "th") cells.Add(c);
        return cells;
    }

    private static (int idx, double w, double h)? SvgPlaceholder(HtmlNode scope)
    {
        foreach (var d in scope.Descendants())
        {
            if (d.Tag != "img" || d.Attrs is null) continue;
            if (!d.Attrs.TryGetValue("src", out var src)
                || !src.StartsWith("inline-svg:", StringComparison.Ordinal)) continue;
            if (!int.TryParse(src.Substring("inline-svg:".Length), out var idx)) continue;
            double w = 0, h = 0;
            if (d.Attrs.TryGetValue("width", out var ws))
                double.TryParse(ws, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out w);
            if (d.Attrs.TryGetValue("height", out var hs))
                double.TryParse(hs, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out h);
            return (idx, w, h);
        }
        return null;
    }

    /// <summary>x</summary>
    private static void ReadRtlSvgLegend(RtlSvgTableState rt)
    {
        var n = rt.dt.Legend.Count;
        rt.dt.LegendXFrac = new double[n];
        rt.dt.LegendWFrac = new double[n];
        rt.dt.LegendLabelRightFrac = new double[n];
        rt.dt.MidLabelRightFrac = new double[n];
        for (var i = 0; i < n; i++)
        {
            rt.dt.LegendWFrac[i] = 1.0 / n;
            rt.dt.LegendXFrac[i] = 1.0 - (i + 1.0) / n;
            rt.dt.LegendLabelRightFrac[i] = 1.0 - (double)i / n - 0.01;
            rt.dt.MidLabelRightFrac[i] = rt.dt.LegendLabelRightFrac[i];
        }
    }

    /// <summary>x</summary>
    private static void ReadRtlSvgDataRow(RtlSvgTableState rt, int ri)
    {
        if (SvgPlaceholder(rt.rows[ri]) is not null) return;
        var cells = Cells(rt.rows[ri]);
        var texts = new List<(string Text, int Col)>();
        for (var ci = 0; ci < cells.Count; ci++)
        {
            var t = DomText(cells[ci], rt.css);
            if (t.Length > 0) texts.Add((t, ci));
        }
        if (texts.Count == 0) return;
        if (rt.dt.TitleText is null && texts.Count == 1 && ri == 0)
            rt.dt.TitleText = texts[0].Text;
        else
            rt.dt.MidLabels.AddRange(texts);
    }
}
