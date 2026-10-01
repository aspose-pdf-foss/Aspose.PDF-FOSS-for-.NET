using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class UaRowState
{
    public double declH;
    public System.Text.RegularExpressions.Match dhm = null!;
    public List<(string Text, double Fs, string? Family, Aspose.Pdf.Color? Bg, Aspose.Pdf.Color? EdgeColor, bool[] Solid, List<string> Lines, List<double> Boxes)> cells = null!;
    // wrap each cell and size its line boxes
    public double rowContentH;
    public bool edged;
    public double rowH;
    // paint: fills, then edges, then text
    public double cellL;
}
}
