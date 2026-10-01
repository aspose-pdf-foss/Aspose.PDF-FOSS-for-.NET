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
private sealed class HtmlBlockRenderState
{
    // IsParagraphHasMargin: block margins collapse between neighbours (the larger of
    // a block's bottom margin and the next block's top margin is spent, once) and
    // the last block's bottom margin is not spent at all - the paragraph after the
    // fragment seats straight below its last line (probed against the reference).
    public bool paragraphMargins;
    public double pendingMarginBottom;
    // A list box carries 1.12 em above and below (probed: 13.4 pt at 12 pt, entering and
    // leaving the list, no margin between its items); the previous block tells which.
    public bool prevWasListItem;
    // A FontSize the caller set on the fragment is the HTML body size:
    // it seeds the parser's root style, so unsized blocks inherit it
    // while explicit heading/inline sizes still win.
    public double bodyFs;
    // …and when the caller set none, the document's OWN `body { }` rule
    // is the base type — a fragment that ships a stylesheet sets in the
    // size and face it declares, not the 11 pt Standard-14 default.
    // The caller's TextState still wins; this only fills the gap.
    public (double SizePt, string? Face, Aspose.Pdf.Color? BgColor, double LineHeightPt) bodyCss;
    public Aspose.Pdf.Text.Font? bodyCssFace;
    /// <summary>The UA serif a full-document fragment that names no face and no size draws in (see HtmlBlockRender).</summary>
    public Aspose.Pdf.Text.Font? uaDefaultFace;
    // Inline <strong>/<u> runs are tracked as RANGES here: a
    // paragraph that emphasises only some of its words draws
    // those words bold/underlined instead of promoting the
    // whole block's face.
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.Block> blocks = null!;
    /// <summary>The block being rendered, so a block can look at the one that follows it.</summary>
    public int blockIndex;
    // The body's own background paints the printed content box, on every page
    // the fragment runs over — a browser paints the body box under everything.
    public int bodyBgStartSlot;
    // The blocks are authored CSS boxes, so they page-break the way a browser
    // prints them: a block leaves no fewer than the CSS default two of its own
    // lines behind, which is why a two-line paragraph moves whole.
    public int savedMinLines;
    // Legacy-font dialect (summernote / Word-paste HTML): every text run
    // is wrapped in <font face size> with a resolvable embedded face and
    // an explicit colour. It renders faithfully — embedded
    // face at the <font size> point size, CSS colour, on a 1.25×em line
    // grid — instead of the Standard-14 legacy flow. Gated tightly so no
    // other page-level HtmlFragment changes.
    public Text.Font? legacyFace;
    public bool legacyDialect;
    // Whether the fragment's single Link annotation has been emitted; sticky once true.
    public bool htmlFragmentLinkEmitted;
    public string chunk = default!;
    public HtmlFragment html = default!;
    public FlowLayout flow = default!;
    public Page page = default!;
    public Text.TextBuilder tb = default!;
    public Color? htmlColor = null;
    public List<byte[]> inlineSvgs = default!;
    public double htmlFrameIndent = 0;
    public double marginLeft = 0;
    public double marginRight = 0;
    public double marginTop = 0;
}
}
