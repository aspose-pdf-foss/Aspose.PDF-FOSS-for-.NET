using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RegionTableDetectState
{
    // 1. Cluster edge positions into row/column boundary values
    public List<double> rowBounds = null!;
    public List<double> colBounds = null!;
    public double rulesRight;
    public double rulesLeft;
    public double rulesTop;
    public double rulesBottom;
    // The EDGE-derived boundaries, before any text-cluster inference below.
    // Cell-side validation snaps to these: a text-inferred boundary has no
    // border of its own, so a sub-cell it creates borrows the sides of the
    // enclosing REAL cell instead of failing the side count and severing
    // the flood-fill at a borderless column.
    public List<double> realColBounds = null!;
    public List<double> realRowBounds = null!;
    public int nRows;
    public int nCols;
    // 2. Mark each grid position as a valid cell if it has >= 3 bounding sides
    public bool[,] valid = null!;
    public int validCount;
    // Fallback: progressively lower the side threshold when less than half the
    // grid cells are valid. Tables with full-width H-edges but segmented V-edges
    // (interior columns having only top+bottom borders) need minSides=2.
    public int totalCells;
    // 3. Collect all valid cells, then use flood-fill to find connected components
    public bool[,] visited = null!;
    public List<Aspose.Pdf.Text.AbsorbedTable> tables = null!;
    public List<TextRun> runs = default!;
    public List<HEdge> hEdges = default!;
    public List<VEdge> vEdges = default!;
}
}
