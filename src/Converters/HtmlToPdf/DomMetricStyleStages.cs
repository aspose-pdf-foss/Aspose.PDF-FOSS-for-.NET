using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The sheet's own table rules set the plain-serif grid's face, size and borders.</summary>
    private static void ApplySerifTableRuleTypography(MetricTableState mt)
    {
        if (mt.stdSerif && mt.css.TryGetValue("table", out var tblFontRule)
            && tblFontRule.TryGetValue("font", out var tblFontV))
        {
            var tfsh = Regex.Match(tblFontV, @"([\d.]+)\s*(pt|px)\s+(.+)$", RegexOptions.IgnoreCase);
            if (tfsh.Success && double.TryParse(tfsh.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var tfshV) && tfshV > 0)
            {
                mt.mps.fontSize = tfsh.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)
                    ? tfshV * 0.75 : tfshV;
                if (FirstFontFamily(tfsh.Groups[3].Value) is { Length: > 0 } tfshFam
                    && WinMetricsFor(tfshFam) is { } tfshFm)
                { mt.face = tfshFam; mt.fm = tfshFm; mt.tableRuleFace = true; }
            }
        }

        mt.fmSum = mt.fm.sum <= 1.0 ? 1.2 : mt.fm.sum;
        mt.lineH = MetricLineHeight(mt.mps.fontSize, mt.fmSum);
        mt.boldFace = mt.face + "-Bold";

        mt.mps.bordered = false;
        mt.mps.borderColor = Color.FromArgb(0, 0, 0);
        mt.bw = 0.75;
        if (mt.stdSerif && mt.css.TryGetValue("table", out var tblRule)
            && tblRule.TryGetValue("border", out var tblBv)
            && tblBv.Contains("solid", StringComparison.OrdinalIgnoreCase)
            && !(tblRule.TryGetValue("border-collapse", out var tblBc)
                 && tblBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)))
        {
            mt.mps.bordered = true;
            if (ParseCssColor(tblBv) is { } tblBcol) mt.mps.borderColor = tblBcol;
        }
        mt.collapseBoxW = 0.0;
        if (mt.stdSerif
            && mt.css.TryGetValue("table", out var cbRule)
            && cbRule.TryGetValue("border-collapse", out var cbC)
            && cbC.Contains("collapse", StringComparison.OrdinalIgnoreCase)
            && cbRule.TryGetValue("border-style", out var cbS)
            && cbS.Contains("solid", StringComparison.OrdinalIgnoreCase)
            && !(mt.css.TryGetValue("td", out var cbTd)
                 && (cbTd.ContainsKey("border") || cbTd.ContainsKey("border-style"))))
        {
            mt.collapseBoxW = cbRule.TryGetValue("border-width", out var cbW)
                && TryParseLength(cbW.Trim()) is { } cbWPt && cbWPt > 0 ? cbWPt : 0.75;
            if (cbRule.TryGetValue("border-color", out var cbCol)
                && ParseCssColor(cbCol) is { } cbColV) mt.mps.borderColor = cbColV;
        }
    }

    /// <summary>The row's box width comes off the collapsed grid, or off the wrapper stack's own table rule.</summary>
    private static void ResolveMetricRowBoxWidth(MetricTableState mt)
    {
        if (mt.stdSerif && mt.collapseBoxW == 0
            && ElementRule(mt.css, "td") is { } egTd
            && ((egTd.TryGetValue("border-collapse", out var egBc)
                    && egBc.Contains("collapse", StringComparison.OrdinalIgnoreCase))
                || (mt.css.TryGetValue("table", out var egTbl) && egTbl.TryGetValue("border-collapse", out var egTbc)
                    && egTbc.Contains("collapse", StringComparison.OrdinalIgnoreCase)
                    // (the CLASS-scoped shape only - `table.list { border-collapse }` folded by ScopedTableCss;
                    // a bare `table { border-collapse }` over a `table tr td { border }` chain keeps its
                    // calibrated separate grid: the div-list document draws at the 1.5 spacing)
                    && Regex.IsMatch(Regex.Match(mt.tableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase).Value, @"\bclass\s*=", RegexOptions.IgnoreCase)))
            && egTd.TryGetValue("border", out var egB)
            && egB.Contains("solid", StringComparison.OrdinalIgnoreCase))
        {
            mt.mps.collapsedGrid = true;
            mt.elemCollapseGrid = true;
            mt.s = 0;
            if (ParseCssColor(egB) is { } egCol) mt.mps.collapsedCol = egCol;
        }
        // pt-report sheets (non-serif wrapper mode): the TABLE rule's
        // border-collapse zeroes the spacing and its padding: 0 the cell
        // padding — cell/table attributes still win below.
        if (!mt.stdSerif && mt.wrapperStacks && mt.css.TryGetValue("table", out var ptTblRule))
        {
            if (ptTblRule.TryGetValue("border-collapse", out var ptBc)
                && ptBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)) mt.s = 0;
            if (ptTblRule.TryGetValue("padding", out var ptPad)
                && Regex.IsMatch(ptPad.Trim(), @"^0(px)?$")) mt.p = 0;
        }
    }
}
