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
private sealed class FloatingBoxParagraphState
{
    // Flow-positioned, no-size FloatingBox is indistinguishable from the
    // ambient paragraph flow. Inline its child paragraphs into the shared
    // cursor so long content paginates via the surrounding FlowLayout.
    // Absolutely-positioned (Left/Top set) boxes still render through
    // AddFloatingBox since they don't participate in the flow.
    // A box that paints a background/border or carries a background
    // image is meant to render as a visible box (e.g. a coloured header
    // band), not be dissolved into the transparent paragraph flow — route
    // it through AddFloatingBox so its fill, border and child images draw.
    public bool fboxIsVisibleBox;
    // A box that paints chrome but declares NO SIZE still dissolves: it
    // flows its children exactly as a chrome-less box does (top and left margins
    // ignored, the bottom one acting) and strokes the border round the region the
    // children ended up occupying — the content width, from the cursor where the
    // box opened down to where it closed. Routing it through the absolute renderer
    // instead drew a degenerate 1x1 border and charged the box's Margin.Top.
    public bool fboxChromeOnFlow;
    public FloatingBox fbox = null!;
    public FlowLayout flow = null!;
    public Page page = null!;
    public System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries = null!;
    public PageLayoutState pl = null!;
    public Dictionary<int, int> headingAutoCounters = null!;
    public List<(byte[] content, double width, double height)> overflowPages = null!;
    public double marginLeft = 0;
    public double marginRight = 0;
    public double marginBottom = 0;
    public double marginTop = 0;
}
}
