using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PositionedSpansState
{
    // ── Parse the page divs, their spans and link overlays ──
    // Two source dialects: the older pdf-page shape (inline pt styles) and the
    // stl_ class scheme (page_N containers, left/top in em, 1 em = 12 pt,
    // appearance via stylesheet classes). Both reflow identically from here on.
    public System.Text.RegularExpressions.MatchCollection pageDivs = null!;
    public bool stlDialect;
    public Dictionary<string, double>? stlFontSizes;
    // Two stl_ shapes with distinct behaviour: a raster-background
    // page (<img class="stl_04">) follows the reflow below; an
    // object/svg-background page keeps the legacy flow (the two differ
    // structurally - a raster img occupies a flow slot, an object
    // does not).
    public bool stlImgBg;
    public PageInfo? pageInfo;
    public double pageH;
    public double srcDivW;
    // The page div's declared height, when its stylesheet gives one. Together with the
    // width it says whether the export's own page box IS the sheet this reflow targets.
    public double srcDivH;
    // Parse every stl_ page's line divs once: the flow consumes them in document
    // order and the sheet width is measured from them.
    public List<List<Aspose.Pdf.Converters.HtmlToPdfConverter.StlPara>> stlPages = null!;
    // stl_ sheet width rides the CONTENT: the longest unbreakable unit — words
    // glued by a nbsp, or a word glued to the leader run after it — measured in
    // raw font units at the reflow size, plus both side margins, when that
    // outgrows the A4 sheet. The pdf-page dialect keeps its source-box-plus-pad
    // rule.
    public double pageW;
    // The export's page box IS the sheet only when it already matches the one this reflow
    // targets. An export of a differently-sized page (612x792) reads as a content box and
    // keeps the historic pad — its shipped expected output is that shape, and chasing the
    // current behaviour against a shipped template is how a calibrated gate gets broken.
    public bool stlBoxIsSheet;
    public double contentW;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public Dictionary<string, (int objNum, string embedName)> fontFileCache = null!;
    public Core.PdfIndirectRef? serifFontRef;
    public List<(Aspose.Pdf.Page page, Aspose.Pdf.Rectangle rect, string url)> pendingLinks = null!;
    public Page? page;
    public Core.PdfIndirectRef? placeholderIconRef;
    public double baselineY;
}
}
