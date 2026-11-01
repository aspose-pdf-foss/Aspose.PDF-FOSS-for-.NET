using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A rule addressed to the cell's ROW by its position (`#grid table tr:nth-child(2n+1) { background }`):
    /// the row's fill shows through every cell that paints none of its own, which is how a browser paints a
    /// striped grid. Read after the cell's own rules, which outrank it.</summary>
    private static void ApplyChainRowBackground(CellOpenState co)
    {
        if (co.chainRules is null || co.ps.cell!.BackgroundColor is not null || co.ps.rowOrdinal <= 0) return;
        co.ps.rowPositionRules ??= co.chainRules.Any(r => r.Segs[^1].Tag == "tr" && r.Segs[^1].Nth is not null);
        if (co.ps.rowPositionRules != true) return;
        // (the row stands under the same ancestors as its cells: the chain dialect's base, or the UA-boxed
        //  grid's ancestors and table)
        List<CssElem> rowChain;
        if (co.chainBase is not null) rowChain = new List<CssElem>(co.chainBase);
        else if (co.ps.uaSheetGrid) { rowChain = UaCellChain(co.cssAncestors, co.ps); rowChain.RemoveAt(rowChain.Count - 1); }
        else return;
        rowChain.Add(new CssElem { Tag = "tr", Ordinal = co.ps.rowOrdinal });
        if (MatchChainDecls(co.chainRules, rowChain) is not { } rowDecls) return;
        if ((rowDecls.TryGetValue("background-color", out var bg) || rowDecls.TryGetValue("background", out bg))
            && ParseCssColor(bg) is { } fill)
            co.ps.cell.BackgroundColor = fill;
    }
}
