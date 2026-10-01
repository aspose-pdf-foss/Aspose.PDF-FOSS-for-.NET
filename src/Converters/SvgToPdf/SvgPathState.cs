using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class SvgToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SvgPathState
{
    public System.Text.RegularExpressions.MatchCollection tokens = null!;
    public double cx;
    public double cy;
    public double sx;
    public double sy;
    public double pcx;
    public double pcy;
    public double pqx;
    public double pqy;
    // The previous path command letter; a smooth curve mirrors its control point.
    public char prevCmd;
    // The command letter being applied.
    public char cmd;
    public List<double> nums = null!;
}
}
