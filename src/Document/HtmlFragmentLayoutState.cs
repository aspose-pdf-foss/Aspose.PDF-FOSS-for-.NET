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
private sealed class HtmlFragmentLayoutState
{
    public string htmlContent = null!;
    public Color? htmlColor;
    // One Link annotation per hyperlinked HtmlFragment (see below).
    public bool htmlFragmentLinkEmitted;
    // Left border+padding of every framed block currently open around
    // the content being rendered (see the frame bookkeeping below).
    public double htmlFrameIndent;
    // Framed blocks: a block element whose CSS declares a border draws
    // a box round everything it contains — its own text and any table
    // inside it — over as many pages as that content takes. The spans
    // are in the SOURCE's coordinates, so the chunked render below can
    // say when each frame opens and closes by where the chunk sits.
    public List<(int Start, int End, double BorderWidthPt, Aspose.Pdf.Color BorderColor, double PadTopPt)> htmlFrames = null!;
    // The frames open so far, each with the slot and Y it opened at.
    public List<(int Index, int Slot, double Top)> htmlOpenFrames = null!;
    // The fragment layout inputs, captured from the method parameters.
    public HtmlFragment html = null!;
    public FlowLayout flow = null!;
    public Page page = null!;
    public Text.TextBuilder tb = null!;
    public HashSet<Table> renderedTables = null!;
    public List<(byte[] content, double width, double height)> overflowPages = null!;
    public Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages = null!;
    public double marginLeft;
    public double marginRight;
    public double marginTop;
    public double marginBottom;
    // How far through the fragment's html the table segmentation has reached.
    public int chunkAt;
    /// <summary>Every prose piece of this mixed fragment lays out on the UA flow, so its
    /// sheet-styled tables take the UA table model too.</summary>
    public bool uaFlowProse;
}
}
