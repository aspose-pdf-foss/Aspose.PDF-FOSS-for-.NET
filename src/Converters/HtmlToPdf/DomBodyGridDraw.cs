using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Body grid table: 
    private static void FlushRun(GridTableState gd)
    {
        if (gd.cell is null || gd.text.Length == 0) { gd.text.Clear(); return; }
        var t = DecodeEntities(gd.text.ToString());
        gd.text.Clear();
        if (t.Length == 0) return;
        var b = gd.boldDepth > 0; var it = gd.italDepth > 0;
        if (gd.cell.Runs.Count > 0 && gd.cell.Runs[^1].Bold == b && gd.cell.Runs[^1].Italic == it)
            gd.cell.Runs[^1] = (gd.cell.Runs[^1].Text + t, b, it);
        else gd.cell.Runs.Add((t, b, it));
    }

    // a cell boundary resets emphasis: the sheet leaves a stray unclosed <b>
    // at a cell's end, and the following cells draw regular
    private static void CloseCell(GridTableState gd) { FlushRun(gd); if (gd.cell is not null) gd.row!.Add(gd.cell); gd.cell = null; gd.boldDepth = 0; gd.italDepth = 0; }

    private static void CloseRow(GridTableState gd) { CloseCell(gd); if (gd.row is { Count: > 0 }) gd.rows.Add(gd.row); gd.row = null; }

    private static string RunFace(GridTableState gd, bool b, bool it) => b ? gd.face + " Bold" : it ? gd.face + " Italic" : gd.face;

    private static void EnsureGridFonts(GridTableState gd, Page p2)
    {
        EnsureFont(p2, gd.faceRes, "F8");
        EnsureFont(p2, gd.faceRes + "Bold", "F9");
        EnsureFont(p2, gd.faceRes + "Italic", "F10");
    }

    private static void HLine(GridTableState gd, double yTd)
        => gd.bops.Append(Compat.Format(gd.invc,
            $"{gd.marginLeft:F2} {gd.pageHeight - yTd:F2} m {gd.marginLeft + gd.contentWidth:F2} {gd.pageHeight - yTd:F2} l S "));

    private static void VLine(GridTableState gd, double x, double y0Td, double y1Td)
        => gd.bops.Append(Compat.Format(gd.invc,
            $"{x:F2} {gd.pageHeight - y0Td:F2} m {x:F2} {gd.pageHeight - y1Td:F2} l S "));

    private static void FlushBorders(GridTableState gd, Page p2)
    {
        if (gd.bops.Length == 0) return;
        p2.AddContentStream(Encoding.ASCII.GetBytes(
            Compat.Format(gd.invc, $"q 0 0 0 RG {GridBorderPt:0.##} w {gd.bops}Q\n")));
        gd.bops.Clear();
    }

    // Vertical border strengths for a row: outer edges always stroke; an
    // interior boundary strokes unless BOTH neighbouring cell sides zero it
    // (border-left:0 beside border-right:0 collapses to nothing); a boundary
    // inside a colspan has no border at all.
    private static bool[] RowEdges(GridTableState gd, List<GridCell> r)
    {
        var on = new bool[gd.nCols + 1];
        on[0] = on[gd.nCols] = true;
        for (var i = 0; i < r.Count; i++)
        {
            var c = r[i];
            if (i > 0)
            {
                var left = r[i - 1];
                on[c.Col] = !left.BorderRightZero || !c.BorderLeftZero;
            }
        }
        return on;
    }
}
