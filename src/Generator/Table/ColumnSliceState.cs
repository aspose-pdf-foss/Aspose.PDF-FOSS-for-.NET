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
private sealed class ColumnSliceState
{
    public Table band = null!;
    public System.Text.StringBuilder widthTokens = null!;
    // A row's height is a property of the WHOLE row, not of one slice: a
    // cell that wraps in the narrow far columns makes the row taller in
    // EVERY slice (the two-digit report rows wrap in their 55.8 pt columns
    // and the first slice's rows grow to two lines with the text seated at
    // the row top). Measure each row against the FULL grid once and stamp
    // the height on the slice rows as a floor.
    public int[] fullMap = null!;
    public double[] colWidths = default!;
    public int repeat = 0;
    public int colStart = 0;
    public int colEnd = 0;
}
}
