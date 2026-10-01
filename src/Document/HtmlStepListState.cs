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
private sealed class HtmlStepListState
{
    public byte[] regTtf = null!;
    public byte[] boldTtf = null!;
    public Text.TrueTypeParser tp = null!;
    public Text.GlyphOutlineParser gpReg = null!;
    public Text.GlyphOutlineParser gpBold = null!;
    public double upm;
    public double winAsc;
    public double winDesc;
    public int hheaSum;
    public double em;
    // ---- Lay the whole list out (top-down distances from the flow cursor) ----
    public double pageW;
    public double ulMargin;
    public double liLeft;
    public List<(double yDown, double x, string text, bool bold, double size)> runsOut = null!;
    public List<(double yDown, double x)> bulletsOut = null!;
    public double yDown;
    public double pendingMargin;
    public double totalH;
    // ---- Emit: bullet markers + text runs as embedded Type0 serif ----
    public Content.ContentStreamBuilder csb = null!;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    /// <summary>Resource name the serif is registered under (a name of its own keeps
    /// it clear of the Helvetica an overflow page registers as F1).</summary>
    public string? resNameHint;
    public List<Converters.HtmlToPdfConverter.StepListItem> items = default!;
    public FlowLayout flow = default!;
    public double marginLeft = 0;
    public double marginRight = 0;
    public Color? htmlColor = null;
}
}
