using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ImageBlockState
{
    public double natW;
    public double natH;
    public double w;
    public double h;
    public double availW;
    public bool rtlOverflow;
    public double padTop;
    public double padBottom;
    // A RIGHT-floated image hangs off the right content edge, inset by
    // its own margin, instead of starting at the flow cursor.
    public double imgX;
    // Two floats that do not fit side by side do not overlap: the later
    // one drops to below the earlier one and takes its own edge there.
    // Measured on the certificate page - a 168.75 pt logo floated left
    // reaches 317.25 and the 131.25 pt logo floated right would start at
    // 315.25, so the second seats at y = the first's bottom
    // rather than beside it.
    public double floatDropY;
    public double imgFlowY;
}
}
