using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StlPositionedState
{
    public string stlCss = null!;
    public Dictionary<string, Aspose.Pdf.Converters.HtmlToPdfConverter.StlClassProps> classProps = null!;
    // The em-compensation dialect keeps every letter-spacing on a 0.01 em grid
    // (the word-spacing absorbs the rounding residue), and its WIDTH BUDGET
    // drops letter-spacing entirely: in such a
    // round trip, two adjacent justified lines carrying word-spacings of
    // opposite SIGN (+0.05 and -0.01 em) both solve to one right edge under
    // glyph advances + word-spacing alone, and the derived sheet runs 13 pt
    // past the page's drawn ink - consistent only with the letter-spacing
    // excluded. The grid is the dialect's signature: the default dialect
    // solves letter-spacings at four decimals.
    public bool emCompensationGrid;
    public Dictionary<string, Aspose.Pdf.Converters.HtmlToPdfConverter.StlFontFace> htmlFaces = null!;
    // Constant content inset inside the margins (0.5em of the 12pt root):
    // every line and the background raster render 6pt right and 6pt
    // down of (ML, MT), and the same 6pt is each line box's width-budget tail.
    public double stlContentPad;
    public PageInfo? pageInfo;
    public double pageW0;
    public double pageH;
    public MarginInfo? pageMargin;
    public bool marginsExplicit;
    public double ml;
    public double mr;
    public double mt;
    public double mb;
    public double band;
    public System.Text.RegularExpressions.MatchCollection pageDivs = null!;
    // ── Harvest every page's runs and background image first: the page WIDTH is
    // document-wide (the widest line anywhere), so layout needs the full sweep. ──
    public List<List<Aspose.Pdf.Converters.HtmlToPdfConverter.StlRun>> pagesRuns = null!;
    public List<(byte[] bytes, double wPt, double hPt)?> pagesImage = null!;
    public double maxRight;
    public double pageW;
    // ── Emit ──
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public System.Globalization.CultureInfo inv = null!;
    public int segStart;
    public int segEnd;
    public string seg = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.StlRun> runs = null!;
    // The page container's own box (width em × 12): text overflowing the
    // fixed box never widens the sheet — a custom-encoded run whose measured
    // advance overshoots still yields the box-bound width.
    public double boxW;
    public System.Text.RegularExpressions.Match pdCls = null!;
    // Background raster (PNG page background / embedded data URI): sized by the
    // page-box class (width/height em × 12).
    public (byte[], double, double)? bg;
    public System.Text.RegularExpressions.Match img = null!;
    public int kMax;
    public Page[] outPages = null!;
    public double leftPt;
    public double topPt;
    public double x;
    public double sentinelAdv;
    public bool lineHasBox;
    public double lsBudget;
    // The em-compensation dialect's sheet budget is a rule of its own
    // (see gridBudget below).
    public double gridBudget;
    public System.Text.RegularExpressions.MatchCollection spanMatches = null!;
}
}
