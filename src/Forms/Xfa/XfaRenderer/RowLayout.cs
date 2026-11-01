using System.Globalization;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>Lays out the cells of a table row from (<paramref name="cx"/>,
    /// <paramref name="cy"/>). A table's columnWidths override the cells' own widths: cell k
    /// sits at the cumulative column x and spans colSpan columns (Designer cells often carry
    /// stale w attributes narrower or wider than their column). Every cell is drawn as tall
    /// as the tallest one, so the boxes of a row whose one cell wrapped its text end on one
    /// line under that text.</summary>
    private static void LayoutRow(Ctx ctx, XmlElement row, List<XmlElement> cells, double cx, double cy, string path)
    {
        var colWidths = (row.ParentNode as XmlElement)?.GetAttribute("columnWidths")
            ?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Len(s, 0)).Where(v => v > 0).ToList();
        var widths = new List<double>(cells.Count);
        int col = 0;
        foreach (var c in cells)
        {
            double cw;
            if (colWidths is { Count: > 0 } && col < colWidths.Count)
            {
                // colSpan="-1" spans all remaining columns (XFA spec).
                int span = 1;
                if (int.TryParse(c.GetAttribute("colSpan"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sp) && sp != 0)
                    span = sp < 0 ? Math.Max(1, colWidths.Count - col) : sp;
                cw = 0;
                for (int s2 = 0; s2 < span && col + s2 < colWidths.Count; s2++) cw += colWidths[col + s2];
                col += span;
                c.SetAttribute("w", cw.ToString("0.###", CultureInfo.InvariantCulture) + "pt");
            }
            else cw = BoxW(c);
            widths.Add(cw);
        }
        double rowHeight = 0;
        for (int k = 0; k < cells.Count; k++)
            if (!Hidden(cells[k])) rowHeight = Math.Max(rowHeight, Height(ctx, cells[k], widths[k]));
        double xx = cx;
        for (int k = 0; k < cells.Count; k++)
        {
            Place(ctx, cells[k], xx, cy, path, widths[k], rowHeight);
            xx += widths[k];
        }
    }
}
