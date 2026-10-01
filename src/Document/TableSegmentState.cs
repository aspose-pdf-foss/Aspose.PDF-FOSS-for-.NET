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
private sealed class TableSegmentState
{
    public double cfs;
    public double kfs;
    public double gap;
    public double totalH;
    public double tx0;
    public double txR;
    public double topY;
    public Content.ContentStreamBuilder tb2 = null!;
    // cell fills go down BEFORE the grid and the text, so the
    // rules stay visible and the text sits on top of its fill
    public double bgY;
    // header text, middle-aligned per column
    public string thRes = null!;
    public double hx;
    // cells
    public double rowTop;
    public StepRowState sr = default!;
    public FlowLayout flow = default!;
    public Converters.HtmlToPdfConverter.StepTable pt = default!;
    public List<List<string>> headLines = default!;
    public double headH = 0;
    public List<(double h, List<(List<double> lhs, List<(int li, double x, Converters.HtmlToPdfConverter.StepSeg? seg, string? txt)> pieces)>)> laidRows = default!;
    public double[] declared = default!;
    public int firstRow = 0;
}
}
