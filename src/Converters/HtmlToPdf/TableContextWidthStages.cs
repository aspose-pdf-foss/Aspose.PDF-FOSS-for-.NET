using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The nested grids lift out of the markup, the col elements seed the column model, and the table's declared width settles against the box it must fit.</summary>
    private static void ExtractNestedGridsAndTableWidth(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, string html)
    {
        cfg.nestedHtml = new List<string>();
        if (cfg.uaCellBoxes && cfg.liftNestedTables) html = UaPaintedDivsToGrids(html);
        if (cfg.liftNestedTables) html = BlockRowsAsGrids(html);
        if (ps.sheetTdBoxRule) html = HrRowsAsBands(DropEmptyRows(html));
        cfg.scanHtml = cfg.liftNestedTables ? ExtractNestedTables(html, cfg.nestedHtml) : html;
        foreach (var value in SafeRegex.Matches(cfg.scanHtml, @"<col\b[^>]*>", RegexOptions.IgnoreCase).Select(cm2 => cm2.Value))
        {
            double wpx = 0;
            var wa = SafeRegex.Match(value, @"width\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (wa.Success) double.TryParse(wa.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out wpx);
            else
            {
                var ws = SafeRegex.Match(value, @"style\s*=\s*[""'][^""']*width\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase);
                if (ws.Success) double.TryParse(ws.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out wpx);
            }
            (colModel.colGroupPt ??= new List<double>()).Add(wpx * PxToPt);
        }


        // The cascade on the table box: its inline style, then its own CLASS rules
        // (`table.AdvTbl { width: 800px }`), then the sheet's type rule.
        string? twVal;
        if (cfg.tblStyle.TryGetValue("width", out var itw)) twVal = itw;
        else
            twVal = TableClassRuleValue(cfg, "width")
                ?? (cfg.css.TryGetValue("table", out var tw2) && tw2.TryGetValue("width", out var tw) ? tw : null);
        if (twVal is null && cfg.docElementGrid && cfg.docCss is not null
            && cfg.docCss.TryGetValue("table", out var dtw2) && dtw2.TryGetValue("width", out var dtw))
        {
            twVal = dtw;
            colModel.tableWidthFromDocRule = true;
        }
        if (twVal is null && cfg.tblChainDecls is not null
            && cfg.tblChainDecls.TryGetValue("width", out var twChain))
        {
            twVal = twChain;
            colModel.tableWidthPctOfBox = true;
        }
        // …and so does the presentational ATTRIBUTE: `<table width="290">` is HTML4's
        // spelling of `width: 290px` and `width="100%"` of `width: 100%`. That pair is
        // the only width an email template gives its inner boxes; without it such a grid
        // was "undeclared" and its columns fell to min-content — a headline column one
        // letter wide.
        // (`width="600px"` - the unit spelt into the attribute - declares the same box: the jobs
        // board sizes every ad grid so, and its sheet follows the declared 600)
        if (twVal is null && cfg.liftNestedTables && cfg.tblTag.Success
            && SafeRegex.Match(cfg.tblTag.Value, @"\bwidth\s*=\s*[""']?\s*(\d+(?:\.\d+)?)\s*(%?)(?:px)?\s*[""'\s>]",
                RegexOptions.IgnoreCase) is { Success: true } twAttr)
        {
            var twAttrPct = twAttr.Groups[2].Value.Length > 0;
            twVal = twAttr.Groups[1].Value + (twAttrPct ? "%" : "px");
            colModel.tableWidthPctOfBox = twAttrPct;
        }
        if (twVal is not null && twVal.EndsWith("%", StringComparison.Ordinal)
            && double.TryParse(twVal.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var twp))
        {
            colModel.tableWidthFrac = Compat.Clamp(twp / 100.0, 0.05, 1.0);
            colModel.tableWidthDeclared = true;
        }
        // An absolute declared width ("9.75in"), capped by a type-rule max-width
        // ("table { max-width: 6.25in }" — the CSS constraint the fixed width must
        // respect), pins the table box inside the available width.
        else if (twVal is not null && cfg.availWidthPt > 0 && TryParseLength(twVal) is { } twAbs && twAbs > 0)
        {
            string? maxWv = null;
            if (cfg.tblStyle.TryGetValue("max-width", out var imw)) maxWv = imw;
            else if (cfg.css.TryGetValue("table", out var mt) && mt.TryGetValue("max-width", out var mw1)) maxWv = mw1;
            else if (cfg.docCss is not null && cfg.docCss.TryGetValue("table", out var mdt) && mdt.TryGetValue("max-width", out var mw2)) maxWv = mw2;
            if (maxWv is not null && TryParseLength(maxWv) is { } mwPt && mwPt > 0)
                twAbs = Math.Min(twAbs, mwPt);
            colModel.tableWidthFrac = Compat.Clamp(twAbs / cfg.availWidthPt, 0.05, 1.0);
            colModel.tableWidthDeclared = true;
            colModel.tableWidthDeclaredAbs = true;
            colModel.tableWidthDeclAbsPt = twAbs;
        }
    }
}
