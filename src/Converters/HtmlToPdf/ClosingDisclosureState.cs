using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ClosingDisclosureState
{
    public byte[] calibri = null!;
    public byte[] arial = null!;
    public double marginLeft;
    public double marginTop;
    public double marginBottom;
    public double contentW;
    public double pageWidth;
    public double pageHeight;
    public double flowLeft;
    public double tableLeft;
    public double tableW;
    public Document doc = null!;
    public List<Aspose.Pdf.Page> pages = null!;
    public List<(int Sheet, int Layer, int Seq, string Text)> ops = null!;
    public int seq;
    public System.Globalization.CultureInfo invc = null!;
    // ── the header block ────────────────────────────────────────────────
    public string body = null!;
    public double labelLeft;
    public List<string> headings = null!;
    // the section's own heading opens its label list, and the File No pair
    // is a right float rather than a row of its own
    public List<string> floated = null!;
    public double y;
    public string[] parties = null!;
    // ── the two cost grids ──────────────────────────────────────────────
    public double[] colX = null!;
    // ── the summaries pair ──────────────────────────────────────────────
    public double halfW;
    public double leftX;
    public double rightX;
    public double payoffSplit;
    public double contactW;
    public double contactRight;
    public List<string> labels = null!;
    public double cy;
    public int sheet;
    public string html = null!;
    public byte[] calibriB = null!;
    public byte[] arialB = null!;
}
}
