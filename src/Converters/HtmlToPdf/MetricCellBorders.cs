using System.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The border declarations a cell's sheet rules (tag, class, tag.class, id) and style attribute give
    /// it, in cascade order; empty when none.</summary>
    private static string CellBorderDecls(IReadOnlyDictionary<string, Dictionary<string, string>>? css, string tag,
        Dictionary<string, string>? attrs)
    {
        var decls = new StringBuilder();
        if (css is not null)
            foreach (var sel in ControlSelectors(tag, attrs))
                if (css.TryGetValue(sel, out var rule))
                    foreach (var kv in rule.Where(kv => kv.Key.StartsWith("border", StringComparison.OrdinalIgnoreCase)
                                 && !kv.Key.Equals("border-collapse", StringComparison.OrdinalIgnoreCase)
                                 && !kv.Key.Equals("border-spacing", StringComparison.OrdinalIgnoreCase)))
                        decls.Append(kv.Key).Append(':').Append(kv.Value).Append(';');
        if (attrs is not null && attrs.TryGetValue("style", out var style) && !string.IsNullOrEmpty(style))
            decls.Append(style).Append(';');
        return decls.ToString();
    }

    /// <summary>A grid cell that no dialect gave a border takes its CSS border sides after the cascade, each side in
    /// its own width and colour (a width-less style is CSS medium).</summary>
    private static void ApplyCellCascadeBorders(CellOpenState co)
    {
        if (co.ps.cell is not { Border: null } cell) return;
        var decls = CellBorderDecls(co.css, co.tag, co.tok.Attributes);
        if (decls.Length == 0) return;
        var sides = CssBorderSides(decls);
        if (!sides.Any(sd => sd.W > 0)) return;
        var border = new BorderInfo(BorderSide.None, sides.Max(sd => sd.W), sides.First(sd => sd.W > 0).Col ?? Color.Black);
        GraphInfo Side(CssBorderSide sd) => new() { LineWidth = (float)sd.W, Color = sd.Col ?? Color.Black };
        if (sides[0].W > 0) border.Top = Side(sides[0]);
        if (sides[1].W > 0) border.Right = Side(sides[1]);
        if (sides[2].W > 0) border.Bottom = Side(sides[2]);
        if (sides[3].W > 0) border.Left = Side(sides[3]);
        cell.Border = border;
    }

    /// <summary>The cell's CSS border sides after the cascade - the sheet's tag, class, tag.class and id rules, then
    /// its style attribute, shorthands and longhands alike (a width-less style is CSS medium) - stroke the sides the
    /// per-side readers left bare. An attribute-bordered grid draws its own frame and keeps it.</summary>
    private static void ApplyMetricCellCascadeBorders(MetricTableState mt, MetricCell cell, string tag,
        Dictionary<string, string>? attrs)
    {
        if (mt.mps.bordered) return;
        var decls = CellBorderDecls(mt.css, tag, attrs);
        if (decls.Length == 0) return;
        var sides = CssBorderSides(decls);
        if (cell.BorderTopW <= 0 && sides[0].W > 0) { cell.BorderTopW = sides[0].W; cell.BorderTopCol = sides[0].Col ?? cell.BorderTopCol; }
        if (cell.BorderRightW <= 0 && sides[1].W > 0) { cell.BorderRightW = sides[1].W; cell.BorderRightCol = sides[1].Col ?? cell.BorderRightCol; }
        if (cell.BorderBottomW <= 0 && sides[2].W > 0) { cell.BorderBottomW = sides[2].W; cell.BorderBottomCol = sides[2].Col ?? cell.BorderBottomCol; }
        if (cell.BorderLeftW <= 0 && sides[3].W > 0) { cell.BorderLeftW = sides[3].W; cell.BorderLeftCol = sides[3].Col ?? cell.BorderLeftCol; }
    }
}
