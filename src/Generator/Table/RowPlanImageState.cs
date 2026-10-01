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
private sealed class RowPlanImageState
{
    public byte[]? rawBytes;
    public bool svgSource;
    public bool svgData;
    public double imgXOffset;
    // Height of the box a letterboxed picture reserves, when that box is
    // larger than the picture drawn inside it (0 = picture IS the box).
    public double imgBoxHeight;
    public double dispW;
    public double dispH;
    // A picture is clamped to the cell's own BOX, not to its text box:
    // the LAST column's box keeps the pitched width that overhangs the
    // content band and the picture fills it, while only the TEXT is
    // clipped at the margin (an image draws 295 wide in a 296 pt box
    // whose caption wraps in 291.5).
    public double imgAvailWidth;
    public byte[] imgBytes = null!;
    // The reserve covers the BOX, which a letterboxed picture is smaller than.
    public double imgBoxH;
    public double imgLineH;
    public int imgLines;
    // The reserve must sum to the image's OWN height: pricing each line at
    // the table's default font size while counting them at the line BOX
    // left every tall image short by the difference (a 112.5 pt image
    // reserved 100), so a column of them overlapped and the last one ran
    // past the section it sits in. The lifted render sizes the stack
    // exactly; the legacy grid keeps its calibrated quantisation.
    public double imgLinePt;
}
}
