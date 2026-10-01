using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TableComponentBuildState
{
    public List<int> rowIndices = null!;
    // Row-span support: a cell extends DOWN through the next grid row when the
    // boundary between them carries no border across this cell's column span
    // (cells are built from the drawn rules - an uncovered interior
    // boundary means one tall cell, not stacked cells with an invented rule).
    public HashSet<(int r, int c)> compSet = null!;
    public HashSet<(int r, int c)> consumed = null!;
    // Build rows top-to-bottom: in PDF coords, larger y = higher on page,
    // so reverse row index order to get visual top-to-bottom.
    public List<Aspose.Pdf.Text.AbsorbedRow> allRows = null!;
    // A ruled grid is one table: its blank rows are rows (a five-row grid
    // whose middle three rows hold only a space reports all five), and
    // a row of fewer cells is a spanning row, not a separator.
    public List<List<Aspose.Pdf.Text.AbsorbedRow>> sections = null!;
    public List<Aspose.Pdf.Text.AbsorbedTable> result = null!;
    public List<(int r, int c)> component = default!;
    public List<double> rowBounds = default!;
    public List<double> colBounds = default!;
    public List<TextRun> runs = default!;
    public List<HEdge>? hEdges = null;
    public List<VEdge>? vEdges = null;
    public double yBot;
    public double yTop;
    public List<int> colsInRow = null!;
    // A colspan row (an HTML caption/summary row spanning the whole grid) registers
    // only its two OUTERMOST grid cells as valid: the interior positions have just
    // top+bottom edges (no interior verticals cross the band). Left as-is, its text
    // (which starts near the row's left edge and runs across the interior) would
    // match neither narrow outer cell by centre-X, the row would read as empty, and
    // the separator-row split would drop it — shifting every row index below it.
    // Rebuild exactly that signature — the leftmost and rightmost columns of the
    // component's grid, nothing in between — as the ONE spanning cell it visually
    // is. Rows that are merely sparse (some interior cells valid, or not anchored
    // to both grid edges) keep their per-cell layout.
    public int gridMinCol;
    public bool spanning;
    public List<(int cFrom, int cTo)> cellSpans = null!;
    public List<Aspose.Pdf.Text.AbsorbedCell> cells = null!;
}
}
