using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BlockTextState
{
    public int lineIdx;
    // An icon-bearing line already spent its extra height ABOVE the
    // baseline — only the descent still needs room below (such a
    // line keeps its baseline 4.7 pt over the margin).
    public double lineNeedBelow;
    public string fontRes = null!;
    public System.Globalization.CultureInfo invc = null!;
    // dir=rtl documents lay flow lines out right-aligned: the line's right
    // edge sits on the right content margin (measured with real advances,
    // not the wrap estimate, so the anchor edge is exact).
    public double lineXPos;
    public string rtlFace = null!;
    // UA-flow float: the block is a shrink-to-fit box on this line — a
    // left float sits on the left content edge, a right float against
    // the right one (pageWidth − marginLeft, the frame symmetric to the
    // flow's left content origin). Its background fills exactly the
    // measured advance (see the fill branch below).
    public double uaFloatW;
    public string lnX = null!;
    public string lnY = null!;
    // CSS color: set the fill colour for this line's text (and its list marker)
    // from the block's resolved foreground colour, emitted as its own content
    // stream so it applies across whichever text-emit branch below runs; reset
    // to black afterwards so later content is unaffected. Layout-neutral — only
    // the drawn ink changes.
    public Color? lineForeColor;
    // Non-WinAnsi line (CJK, RTL, Cyrillic/Greek/Armenian, mixed-script): the
    // Standard-14 WinAnsi Tf/Tj path collapses these to '?'. Embed a covering
    // Unicode face as a Type0/CID font (deduped once per page) and emit hex glyph
    // ids. A pure Arabic/Hebrew line is written in VISUAL order (shaped Arabic /
    // reversed Hebrew) so it displays right-to-left; a mixed LTR+RTL line gets its
    // RTL segments visualized in place; the absorber logicalizes presentation forms
    // and pure-Hebrew runs back to logical reading order. When no single installed
    // face covers the whole line (e.g. Arabic + CJK on one line), the line is split
    // into per-script segments emitted as consecutive Tf/Tj runs.
    public bool isRtlLine;
    public string uniSource = null!;
    // Redline: a line whose only non-WinAnsi chars are the symbol
    // PUA box glyphs stays in the face writer, which draws those
    // sub-runs with the symbol face itself.
    public Aspose.Pdf.Text.Font? cjkFont;
    public byte[]? cjkTtf;
    public string cjkName = null!;
    // The block inputs, captured from the method parameters.
    public Block block = null!;
    public HtmlFlowCursor flow = null!;
    public HtmlDocProfile profile = null!;
    public HtmlBlockMetrics metrics = null!;
    public Document doc = null!;
    public Core.PdfDictionary docFontDict = null!;
    public StringBuilder sb = null!;
    public Dictionary<string, (Page page, double y)> anchorTargets = null!;
    public List<(Page page, Aspose.Pdf.Rectangle rect, string url, string? text)> pendingLinks = null!;
    public Dictionary<string, (string resName, Core.PdfIndirectRef fontRef)> embeddedFonts = null!;
    public Dictionary<string, (int objNum, string embedName)> fontFileCache = null!;
    public Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack = null!;
    public bool articleFlow;
    public bool uaFlow;
    public double marginBottom;
    public double marginLeft;
    public double marginRight;
    public double marginTop;
    public double pageHeight;
    public double pageWidth;
}
}
