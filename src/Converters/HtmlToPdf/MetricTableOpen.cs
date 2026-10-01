using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleMetricTableOpen(MetricTableState mt, Token tok)
    {
        mt.mps.sawTable = true;
        // A sheet whose td/th rule borders the cells (bare, or through the grid's own chain:
        // `table tr th, td { border: 1px solid black }`) draws the grid bordered, attributes or
        // none (probed: every cell of the 20-column grid strokes its 0.75 box).
        if (mt.stdSerif && !mt.mps.bordered && SheetBordersCells(mt.css))
        {
            mt.mps.bordered = true;
            mt.mps.borderHugs = true;
        }
        if (!(tok.Attributes is { } ta)) return;
        ReadMetricLegacyGrid(mt, ta);
        ReadMetricTableStyleWidth(mt, ta);
        ReadMetricTableChrome(mt, ta);
        ReadMetricTableSideFrames(mt, ta);
        ReadMetricTableClass(mt, ta);
        CascadePtFormInlineSides(mt, ta);
        // (the cells' class paddings replace this attribute padding in the pt form grid)
        mt.mps.cellPadPt = mt.p;
        ReadMetricTableWidth(mt, ta);
        // The table's own inline font-size sizes every cell that declares none, the way a
        // table class's typography does (probed: `style="font-size:14px"` draws its rows
        // at 10.5 where the body is 12).
        if (ta.TryGetValue("style", out var tfst) && tfst is not null
            && Regex.Match(tfst, @"(?<![-\w])font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } tfsm
            && TryParseCssFontSize(tfsm.Groups[1].Value.Trim()) is { } tfs && tfs > 0)
        {
            mt.mps.fontSize = tfs; mt.mps.tableClassFont = true;
            if (CssSizeIsAbsolute(tfsm.Groups[1].Value)) mt.mps.tableClassAbsoluteSize = true;
        }
        // …and its inline font-family is the face of every cell that names no closer one, as a
        // class face is (measured: a wrapper `<table style="font-family: Arial">` draws Arial).
        if (ta.TryGetValue("style", out var tffSt) && tffSt is not null
            && Regex.Match(tffSt, @"(?<![-\w])font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } tffm
            && FirstFontFamily(tffm.Groups[1].Value) is { Length: > 0 } tfam
            && (!mt.stdSerif || SourceEngineFaces.Contains(tfam)))
            mt.mps.tableClassFace = tfam;
        ReadMetricTableInlineBox(mt, ta);
    }

    /// <summary>The table's own inline bold and left padding: its cells draw bold, and its
    /// columns start that much inside its left frame.</summary>
    private static void ReadMetricTableInlineBox(MetricTableState mt, Dictionary<string, string> ta)
    {
        // (the pt form's table class `padding: 2px` insets the columns from the frame on every side -
        //  probed: the first cell box opens border + 1.5 + spacing inside the box edge)
        if (mt.mps.ptFormCells && ta.TryGetValue("class", out var tcls) && tcls is not null)
            foreach (var cn in tcls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if ((mt.css.TryGetValue("." + cn, out var tRule) || mt.css.TryGetValue("table." + cn, out tRule))
                    && tRule.TryGetValue("padding", out var tPad) && CssSideValues(tPad.Trim()) is { } tPads)
                {
                    if (TryParseLength(tPads[3]) is { } tpl && tpl > 0) mt.mps.tablePadLeftPt = tpl;
                    if (TryParseLength(tPads[0]) is { } tpt && tpt > 0) mt.mps.tablePadTopPt = tpt;
                    if (TryParseLength(tPads[2]) is { } tpb && tpb > 0) mt.mps.tablePadBottomPt = tpb;
                }
        if (!ta.TryGetValue("style", out var st) || st is null) return;
        // A table's inline `font-weight: bold` is inherited typography: every cell
        // under it draws bold unless it re-declares the weight.
        if (Regex.IsMatch(st, @"(?<![-\w])font-weight\s*:\s*bold", RegexOptions.IgnoreCase))
            mt.mps.tableBold = true;
        // …and its `padding-left` insets the columns from its frame. A UNITLESS value is
        // CSS pixels here (the dunning letter's `padding-left : 10` seats them 7.5 in).
        var padM = Regex.Match(st, @"(?<![-\w])padding-left\s*:\s*([\d.]+)\s*(px|pt)?",
            RegexOptions.IgnoreCase);
        if (padM.Success && double.TryParse(padM.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var padV) && padV > 0)
            mt.mps.tablePadLeftPt = padM.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase)
                ? padV : padV * PxPt;
        // ...and its vertical padding is band space inside the frame (probed on the e-mail cards:
        // a `padding-bottom: 15px` card closes 15 px under its last row; a `padding: 5px` card
        // opens 5 px under its frame)
        // (…an inline style that states no padding leaves the class padding standing)
        if (mt.stdSerif && (!mt.mps.ptFormCells || Regex.IsMatch(st, @"(?<![-\w])padding", RegexOptions.IgnoreCase)))
        {
            var (tpT, _, tpB, _) = CssPaddingSidesPt(st, UaDefaultFontPt);
            mt.mps.tablePadTopPt = tpT;
            mt.mps.tablePadBottomPt = tpB;
        }
    }
}
