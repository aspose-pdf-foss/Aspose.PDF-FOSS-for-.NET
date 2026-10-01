using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BinaryTextState
{
    public byte[] ttf = null!;
    public Text.GlyphOutlineParser gp = null!;
    public int upm;
    // ---- HTML5-style tokenize: keep text, swallow tags/comments.
    public System.Text.StringBuilder textBuf = null!;
    public string text = null!;
    // ---- Item stream: (advance, glyph-or-null). Paragraphs split at FF/VT.
    // An item list per unbreakable segment; a segment boundary is a collapsed
    // whitespace run, a position before '\', or one after '-'.
    public List<List<(double w, List<(double adv, char? ch)> items)>> paragraphs = null!;
    public List<(double, List<(double, char?)>)> curPara = null!;
    public List<(double adv, char? ch)> curSeg = null!;
    public double curSegW;
    public bool pendingSpace;
    // ---- Min-content width and page size.
    public double maxSeg;
    public double left;
    public double right;
    public double pageW;
    public double pageH;
    public double limit;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    public Page page = null!;
    // ---- Greedy wrap + emission. First baseline 88.91 from the page top,
    // then a constant 13.5 pt pitch (per-line strut quirks stay within
    // rendering tolerance).
    public System.Globalization.CultureInfo invc = null!;
    public double baseline;
    public System.Text.StringBuilder sb = null!;
    public string html = default!;
}
}
