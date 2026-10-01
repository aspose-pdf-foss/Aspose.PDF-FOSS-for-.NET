using System.Text;
using System.Text.RegularExpressions;
namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Every opening tag the metric parse understands, by name: the table structure, the cells, the blocks and the inline runs.</summary>
    private static void OpenMetricTokenTag(MetricTableState mt, Token tok, string tag)
    {
        switch (tag)
        {
            case "table" when mt.mps.sawTable:
                OpenMetricTable(mt);
                break;
            case "table" when !mt.mps.sawTable:
                HandleMetricTableOpen(mt, tok);
                break;
            case "tr":
                OpenMetricRow(mt, tok);
                break;
            case "td":
            case "th":
                HandleMetricCellOpen(mt, tok, tag);
                break;
            case "b":
            case "strong":
                mt.mps.boldDepth++;
                if (mt.mps.cell is not null) mt.mps.cellBoldMarks.Add((mt.text.Length - mt.mps.textMarkCount, true));
                // (a UA cell: the tag's class colour inks the run - `strong.clinical { color: #3300FF }`
                // on the lab report - and its close restores the cell's)
                if (mt.stdSerif && mt.mps.cell is not null && !tok.IsSelfClosing)
                {
                    mt.mps.strongSaves.Push(mt.mps.cell.Fore);
                    if (tok.Attributes is { } bca && bca.TryGetValue("class", out var bcls) && bcls is not null)
                        foreach (var bc in bcls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            if ((mt.css.TryGetValue(tag + "." + bc, out var bRule) || mt.css.TryGetValue("." + bc, out bRule)
                                    || (bRule = CommentWrappedRule(mt.css, tag + "." + bc) ?? CommentWrappedRule(mt.css, "." + bc)) is not null)
                                && bRule.TryGetValue("color", out var bCol) && ParseCssColor(bCol.Trim()) is { } bColV)
                                mt.mps.cell.Fore = bColV;
                }
                // report cells account bold per RUN (boldDepth); the
                // whole-cell flag stays a legacy-flow behaviour
                // (a UA cell keeps the RUN - its bold ends where the tag closes, see the run marks;
                // the other flows keep the whole-cell flag)
                if (mt.mps.cell is not null && !mt.reportCells && !mt.stdSerif) mt.mps.cell.Bold = true;
                break;
            case "hr":
                // An <hr> inside a cell is drawn content, not a block break:
                // the grid gives it a line box and strokes the rule in it.
                if (mt.mps.cell is not null)
                {
                    mt.mps.cell.HrRule = true;
                    ReadMetricHrBox(mt.mps.cell, tok);
                }
                break;
            default: OpenMetricControlTag(mt, tok, tag); break;
        }
    }
}
