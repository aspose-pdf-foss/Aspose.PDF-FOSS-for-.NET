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
private sealed class RowPlanState
{
    public Table.RowPlan plan = null!;
    public MarginInfo? defaultPad;
    public double maxLineHeight;
    // Tight (no-leading) height of whatever set maxLineHeight. A block of n lines occupies
    // (n-1)·LineHeight + TightLine, so a single text line takes its glyph height (≈ FontSize)
    // rather than a full 1.2× leading slot — matching the generator's row height.
    // Non-text content (images, control glyphs) keeps its full height as the tight value.
    public double tightForMax;
    public double maxVertPad;
    public double maxTopPad;
    public List<(double padV, int lineCount, double tight, double exact, double ownStack)> cellTotals = null!;
    // Row height = MAX over cells of (its own padding + its own content),
    // not max-content + max-padding: a title row with
    // a 14 pt/pad-8 title cell next to a 10 pt/pad-11 number cell sizes at 22 pt
    // (the title cell's total), not 25. Expressed as effective padding on
    // top of the row's content grid.
    public double rowContentH;
    public double maxCellTotal;
    public bool anyExactCell;
    public double maxOwnTotal;
}
}
