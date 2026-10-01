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
private sealed class HtmlBlockState
{
    // Page-level emphasis title (e.g. <p style="font-family:X"><b><i>):
    // the named face draws in its bold-italic variant at
    // the browser <p> default size, on the font's natural line height.
    // Gated on combined bold+italic + a resolvable styled face so ordinary
    // page HTML keeps the Standard-14 flow.
    public Text.Font? styledFace;
    // Emphasis title uses the browser <p> default 12pt (the body
    // default is 11; the styled path uses 12).
    public double fontSize;
    // Faithful line grid for the dialect: pitch = 1.25×em.
    public double legacyLead;
    // List items carry a top margin (the common
    // `li { margin: .5em 0 }` rule) so the vertical rhythm
    // tracks a browser/CSS layout rather than packing tight.
    public double topMargin;
    public Aspose.Pdf.Text.TextFragment bf = null!;
    // HTML renders text on roughly a 1.2x line pitch; the legacy-font
    // dialect uses a 1.25×em grid. A LineSpacing the CALLER set on the
    // fragment overrides that pitch, and it carries the same meaning it
    // does on every other TextState: POINTS of extra leading over the
    // font size, so LineSpacing 1.5 at 12 pt steps 13.5 pt per line.
    public double htmlCallerLs;
    public double htmlBlockLead;
    // A face the CALLER set on the fragment IS the fragment's body font:
    // an unstyled block draws in it (and its ascent/descent then size the
    // link boxes over the block's anchors). The block's own declared face
    // still wins, as does the legacy dialect's embedded one.
    // …and with no face on the fragment, the page's (then the document's)
    // DefaultTextState face is the body font the HTML inherits.
    public Aspose.Pdf.Text.Font? callerBodyFace;
    // The document's own body face draws the block, on the CSS
    // `line-height: normal` box that face's own metrics define
    // (pixel-quantized, so the pitch steps in 0.75 pt).
    public bool cssLineBox;
    // A block that declares a background paints a band across the content
    // width: its own line boxes plus the box's padding above and below, with
    // the text inset by the padding on the left. The box announces its chrome
    // and first line box before it opens, so it never starts on a page that
    // cannot hold them — a browser moves such a box whole.
    public Color? bandColor;
    public int bandStartSlot;
    public double bandTop;
    // A block whose <strong>/<u> runs cover only part of it
    // sets those runs in their own style; the base face of
    // the line stays regular. A block emphasised throughout
    // keeps the whole-block promotion above.
    public List<(int Start, int Length, bool Bold, bool Italic, bool Underline, Aspose.Pdf.Hyperlink? Link)>? emphRuns = null!;
    public bool wrote;
}
}
