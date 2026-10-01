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
private sealed class LayoutTableRenderState
{
    public double originLeft;
    // The layout table's own cell padding sits above and below every
    // block it places — the markup's `cellpadding` nests, so each level
    // of table inset adds its own.
    public double lpadTop;
    public double lpadBottom;
    public double y;
    public Table lt = default!;
    public double originX = 0;
    public double boxW = 0;
    public double startY = 0;
    public FlowLayout flow = default!;
    public double marginLeft = 0;
    public HashSet<Table> renderedTables = default!;
    public bool measureOnly = false;
    public int cellCount;
    public double[] widths = null!;
    public double declared;
    public int undeclared;
    public double rowTop;
    public double rowAdvance;
    public double cx;
}
}
