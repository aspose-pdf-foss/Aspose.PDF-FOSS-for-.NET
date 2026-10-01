using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ColumnWidthState
{
    // AutoFitToContent OVERRIDES declared widths — the content sizing IS the
    // adjustment, not a fallback for a missing ColumnWidths: a table declaring
    // "50 50 50" still lays every cell on one line.
    // A column-paginating table is exempt; there the declared widths drive the
    // slice packing.
    public bool contentOverridesWidths;
    // Accept space, tab, comma, or semicolon as column-width separators —
    // callers in the wild write ColumnWidths as "100 200 100" or "100, 200, 100"
    // interchangeably. A trailing '%' makes the value a percentage of the table's
    // available width (e.g. "3% 97%"); resolved here so percentage columns fill the
    // content area instead of collapsing to the 100pt parse-failure fallback.
    // A string with whitespace separators may carry decimal COMMAS ("91,3 323,8"
    // from string.Format under a comma-decimal culture like de-DE) — split those
    // on whitespace only and read the comma as a decimal point, so the
    // authored widths survive a culture-formatted round trip.
    public bool hasSpaceSep;
    public string[] parts = null!;
    public double[] widths = null!;
    // The share a column actually DECLARED, resolved against the real box — the
    // ceiling on how far it may grow once the resolver below re-fits the grid.
    // 0 = this column declared nothing (its emitted share is the synthetic
    // leftover the builder spreads over the auto columns).
    public double[] declShare = null!;
    public bool[]? declMask;
    // A row may carry more cells than the specified column widths; the table is padded
    // to the widest row rather than dropping the surplus cells. Append auto-width
    // columns (reusing the last specified width) so every cell is laid out.
    public int neededCols;
}
}
