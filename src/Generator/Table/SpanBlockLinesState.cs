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
private sealed class SpanBlockLinesState
{
    public Cell cell = null!;
    public Row row = null!;
    public MarginInfo? padding;
    public double dp;
    public double padLeft;
    public double padRight;
    public double width;
    public double availWidth;
    public Aspose.Pdf.Text.TextState? textState;
    public double defaultFontSize;
    public HorizontalAlignment cellAlign;
    public double maxLine;
    public double tight;
    public SpanBlock block = default!;
    public double[] colWidths = default!;
    public string? text;
    public double fragFontSize;
    public Color? color;
    public bool fragBold;
    public HorizontalAlignment fragAlign;
    // A fragment turned a quarter turn advances along the cell's HEIGHT, so the
    // width-derived extent above describes the wrong axis for it: a tall narrow
    // column would break such a run after every character and report each one as
    // its own fragment. The run stays whole; its own axis is what bounds it.
    public bool quarterTurned;
    public int fragFirstLine;
    // The leading and line box this paragraph declared, stamped onto its lines.
    public double fragLeading;
    public bool fragHangingBreakSpace;
    public (double AscentEm, double DescentEm) fragLineBox;
}
}
