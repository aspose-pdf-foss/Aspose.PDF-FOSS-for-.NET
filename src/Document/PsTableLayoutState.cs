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
private sealed class PsTableLayoutState
{
    // the table's own text size, and the factor its line
    // metrics scale by against the form's 12 pt
    public double cfs;
    public double kfs;
    // an author's table stacks its cell lines on the form's
    // own pitch; a widget grid keeps the tighter one it was
    // built to
    // an author's grid stacks its cell lines on the box the
    // cell's own size asks for; a widget grid keeps the tighter
    // one it was built to
    public double baseLine;
    public double[] declared = null!;
    public double[] visible = null!;
    public double used;
    public List<List<string>> headLines = null!;
    public double headH;
    public List<(double h, List<(List<double> lhs, List<(int li, double x, Aspose.Pdf.Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)> laidRows = null!;
    public double psTblX;
    // the spacing runs down the table's own edges too, so a
    // grid of separate cells is one gap taller than its rows
    public double totalH;
    public Page page = default!;
    public StepRowState sr = default!;
    public Converters.HtmlToPdfConverter.StepTable pt = default!;
    public double rowH;
    public double colX;
    public List<(List<double>, List<(int, double, Aspose.Pdf.Converters.HtmlToPdfConverter.StepSeg?, string?)>)> cellsOut = null!;
    public double rowFloor;
    // an author's cell wraps inside its own box, a
    // widget grid's against the column it declared
    public double colWc;
    public double iw;
    public List<double> lhs = null!;
    public List<(int, double, Aspose.Pdf.Converters.HtmlToPdfConverter.StepSeg?, string?)> pieces = null!;
    public double ccx;
    public double lineH;
    public bool lineDirty;
    public double blockBlankW;
    // the block's own margin above, spent on the
    // first line box it opens and no other
    public double clMargin;
    // with separate borders the cell stands its own
    // box, one rule above its lines and one below
    public double cellH;
}
}
