using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MonoReportRenderState
{
    public System.Text.RegularExpressions.Match bqM = null!;
    public string body = null!;
    public string face = null!;
    public double adv;
    // ── the header lines, then the table's rows ───────────────────────
    public int tIdx;
    public string headMarkup = null!;
    public List<string> header = null!;
    public List<List<(string Text, Aspose.Pdf.Color? Bg)>> rows = null!;
    // ── the character grid ────────────────────────────────────────────
    public int nCols;
    public double[] colW = null!;
    public double contentLeft;
    public double widest;
    public double gridW;
    public double neededPage;
    public Document doc = null!;
    public Page page = null!;
    // the report's own face, as a WinAnsi Type1 resource on this page
    public string res = null!;
    // ── draw ──────────────────────────────────────────────────────────
    public System.Globalization.CultureInfo invc = null!;
    // The body's own background paints the CONTENT BOX — page margin to
    // page margin, top margin to bottom (measured: 90..1006.64 × 72..770).
    public System.Text.RegularExpressions.Match bodyBgM = null!;
    public double y;
    public double tableTop;
    // The first and last rows are their own groups under `rules=groups`:
    // each is one rule taller, and a rule opens the last one.
    public double rowTop;
    public int lastRi;
    // `rules=groups` draws a rule between row GROUPS only — here the frame's
    // own top and bottom plus the two that fence the first and last rows.
    public System.Text.StringBuilder sb = null!;
    public string html = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}
