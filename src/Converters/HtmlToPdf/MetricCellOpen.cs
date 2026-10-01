using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleMetricCellOpen(MetricTableState mt, Token tok, string tag)
    {
        CloseCell(mt.mps, mt.text, mt.reportCells, mt.stdSerif);
        mt.mps.row ??= new List<MetricCell>();
        mt.mps.cell = new MetricCell { Bold = tag == "th" || mt.mps.tableBold, Tag = tag };
        if (mt.mps.tableClassFace is { } tcf) mt.mps.cell.Face = tcf;
        mt.mps.inlineWrapDepth = 0;
        if (mt.mps.pendingNestSpan > 1) { mt.mps.cell.ColSpan = mt.mps.pendingNestSpan; mt.mps.pendingNestSpan = 0; }
        // Browser UA default: <th> content is centered.
        if (mt.stdSerif && tag == "th") mt.mps.cell.Align = HorizontalAlignment.Center;
        // …and a `.cls td { line-height }` rule of the table's own class paces the cell's line (the
        // land-register order's `.ListTable TD { LINE-HEIGHT: 18px }` rows band 13.5, which the
        // 12 pt strut used to coincide with)
        if (mt.stdSerif && mt.mps.cell.LineHeightPt <= 0)
            foreach (var tc in mt.mps.tableClassNames)
                if ((mt.css.TryGetValue("." + tc + " " + tag, out var tcRule) || mt.css.TryGetValue("table." + tc + " " + tag, out tcRule))
                    && tcRule.TryGetValue("line-height", out var tcLh)
                    && Regex.Match(tcLh, @"^\s*([\d.]+)\s*(px|pt)\s*$", RegexOptions.IgnoreCase) is { Success: true } tcLhM)
                {
                    mt.mps.cell.LineHeightPt = DtpNum(tcLhM.Groups[1].Value)
                        * (tcLhM.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? 1 : PxPt);
                    break;
                }
        // …and where the sheet pads the td cells alone, each td carries that padding as its own
        // extra over the table's UA padding (see ReadMetricTableChrome)
        if (mt.stdSerif && tag == "td" && mt.mps.tdPadExtraPt > 0)
        {
            mt.mps.cell.PadLeft = mt.p + mt.mps.tdPadExtraPt;
            mt.mps.cell.PadRight = mt.p + mt.mps.tdPadExtraPt;
            mt.mps.cell.PadTopPt = mt.mps.tdPadExtraPt;
            mt.mps.cell.PadBottomPt = mt.mps.tdPadExtraPt;
        }
        // The table's own white-space inherits into the cell.
        if (mt.mps.tableNoWrap) mt.mps.cell.NoWrap = true;
        // The sheet's own th/td element rule styles the cell (the
        // order-ticket th { font-size: 80%; text-align: left }).
        if (mt.stdSerif && mt.css.TryGetValue(tag, out var cellTagRule))
            ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, mt.mps.cell, cellTagRule);
        // …and the element rule's line-height paces the cell's line box: a length as it stands, a
        // percent, number or em of the cell's size (MEASURED, the evaluation form: `TD { font-size:
        // .75em; line-height: 150% }` rows pitch 13.5 = 1.5 x 9, its blank <br> rows included)
        if (mt.stdSerif && mt.mps.cell.LineHeightPt <= 0 && ElementRule(mt.css, tag) is { } lhTagRule
            && lhTagRule.TryGetValue("line-height", out var tagLh)
            && CellRuleLineHeightPt(tagLh, mt.mps.cell.FontSize ?? mt.mps.fontSize) is { } tagLhPt)
            mt.mps.cell.LineHeightPt = tagLhPt;
        // …and the element rule's `white-space: nowrap` keeps the cell on one line the way the
        // table's own does (a class nowrap stays calibrated; the tag rule reaches every cell, the
        // legacy comment-wrapped sheet's first rule included).
        if (mt.stdSerif && ElementRule(mt.css, tag) is { } wsTagRule
            && wsTagRule.TryGetValue("white-space", out var tagWs)
            && Regex.IsMatch(tagWs, @"^\s*(nowrap|pre(?![-\w]))", RegexOptions.IgnoreCase))
            mt.mps.cell.NoWrap = true;
        // …and so does a chain rule that ends in the tag through the grid's own structure
        // (`table tr th { border; width; text-align }` reaches every th).
        if (mt.stdSerif)
            foreach (var chainRule in CellChainRules(mt.css, tag, anyCell: mt.mps.sheetSpacingZero))
                ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, mt.mps.cell, chainRule);
        // (…and the row's FIRST cell takes the sheet's `:first-child` rule for its tag - the left rule
        //  the change-control grid gives its first column)
        if (mt.stdSerif && mt.mps.sheetSpacingZero && mt.mps.row.Count == 0)
            foreach (var fcKey in new[] { tag + ":first-child", "table " + tag + ":first-child" })
                if (mt.css.TryGetValue(fcKey, out var fcRule))
                    ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, mt.mps.cell, fcRule);
        if (mt.mps.rowFs is { } rfs) { mt.mps.cell.FontSize = rfs; if (mt.mps.rowFsFromClass) mt.mps.cell.FontFromClass = true; }
        if (mt.mps.rowAlign is { } ra) mt.mps.cell.Align = ra;
        if (mt.mps.rowBg is { } rbg) mt.mps.cell.Bg = rbg;
        if (mt.mps.rowFace is { } rfc) mt.mps.cell.Face = rfc;
        mt.mps.cell.RowInlineTypo = mt.mps.rowInlineTypo;
        if (mt.mps.rowBold) mt.mps.cell.Bold = true;
        if (mt.mps.rowFore is { } rfo) mt.mps.cell.Fore = rfo;
        if (mt.mps.rowVTop) mt.mps.cell.VAlignTop = true;
        if (mt.mps.rowVBottom) mt.mps.cell.VAlignBottom = true;
        if (mt.mps.rowTdBags is not null)
            foreach (var tb in mt.mps.rowTdBags) ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, mt.mps.cell, tb);
        ApplyMetricSiblingCellRules(mt, mt.mps.cell, tag, tok);
        if (tok.Attributes is { } ca)
        {
            // NoWrap layout is part of the modern-nesting model;
            // the dead-css greens stay on their calibrated wrap.
            if (mt.wrapperStacks && ca.ContainsKey("nowrap")) mt.mps.cell.NoWrap = true;
            if (ca.TryGetValue("colspan", out var csp)
                && int.TryParse(csp.Trim(), out var cspN) && cspN > 1)
                mt.mps.cell.ColSpan = cspN;
            if (ca.TryGetValue("rowspan", out var rsp)
                && int.TryParse(rsp.Trim(), out var rspN) && rspN > 1)
                mt.mps.cell.RowSpan = rspN;
            if (ca.TryGetValue("bgcolor", out var tdbg)
                && AttrColor(tdbg) is { } tdbgc)
                mt.mps.cell.Bg = tdbgc;
            if (ca.TryGetValue("class", out var tdcls))
            {
                ApplyMetricCellClasses(mt, mt.mps.cell, tdcls);
            }
            if (ca.TryGetValue("style", out var tdst))
            {
                ApplyMetricCellStyle(mt, mt.mps.cell, tdst);
            }
            ApplyMetricCellAttributes(mt, mt.mps.cell, ca);
        }
    }

    /// <summary>The sheet's descendant-chain rules that end in the cell's tag and run only through the
    /// grid's own structure (`table tr th`): they address every such cell. Head cells only - the
    /// probed shape; a `table td` chain stays with the calibrated grid its greens were measured on.</summary>
    private static IEnumerable<Dictionary<string, string>> CellChainRules(IReadOnlyDictionary<string, Dictionary<string, string>> css, string tag, bool anyCell = false)
    {
        // (a sheet that zeroes the grid's border-spacing authors its td boxes too - the change-control
        //  page's `table td { padding: 0.2em; border-bottom; border-right }`)
        if (!anyCell && !tag.Equals("th", StringComparison.OrdinalIgnoreCase)) yield break;
        foreach (var kv in css)
        {
            var segs = kv.Key.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length < 2 || !segs[^1].Equals(tag, StringComparison.OrdinalIgnoreCase)) continue;
            var structural = true;
            for (var i = 0; i < segs.Length - 1 && structural; i++)
                structural = segs[i].ToLowerInvariant() is "table" or "tbody" or "thead" or "tfoot" or "tr";
            if (structural) yield return kv.Value;
        }
    }

    /// <summary>The sheet's adjacent-sibling rules that dress this cell because of the
    /// cell that closed immediately before it (<c>.label + td { padding-left: .5em }</c>
    /// insets the report family's value columns). Applied after the element and row
    /// rules and before the cell's own class and inline style, which outrank it.</summary>
    private static void ApplyMetricSiblingCellRules(MetricTableState mt, MetricCell cell, string tag, Token tok)
    {
        if (mt.siblingCellRules is null || mt.mps.row is not { Count: > 0 } row) return;
        var prev = row[^1];
        var left = new CssElem { Tag = prev.Tag, Classes = prev.ClassNames?.ToArray() };
        var right = new CssElem
        {
            Tag = tag,
            Classes = tok.Attributes is { } a && a.TryGetValue("class", out var cls)
                ? cls.Split(' ', StringSplitOptions.RemoveEmptyEntries) : null,
        };
        if (MatchSiblingCellDecls(mt.siblingCellRules, left, right) is { } decls)
            ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, cell, decls);
    }
}
