using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FontFaceSheetState
{
    // ── fingerprint ──
    public System.Text.StringBuilder styleText = null!;
    public string css = null!;
    public System.Text.RegularExpressions.Match bodyW = null!;
    // ── own faces, with their weight/style declarations ──
    public Dictionary<string, List<Aspose.Pdf.Converters.HtmlToPdfConverter.SheetFace>> faces = null!;
    // ── class rules (selector → declarations), attribute selectors matched
    //    against the element chain the document actually has ──
    public List<(string Selector, string Body)> rules = null!;
    // ── body blocks: paragraphs inside optional div wrappers ──
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string bodyInner = null!;
    public List<(string Text, string PClass, string DivClasses, bool BoldTag)> paras = null!;
    // The `body > :first-child { margin-top }` rule — a candidate margin the
    // first block collapses with.
    public double firstChildMt;
    public System.Text.RegularExpressions.Match fcm = null!;
    // ── layout ──
    public double contentW;
    public double pageW;
    public double pageH;
    public double left;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    public Page pg = null!;
    public System.Text.StringBuilder sb = null!;
    public System.Globalization.CultureInfo inv = null!;
    public double flowTop;
    public double pendingMargin;
    public string html = default!;
    public HtmlLoadOptions? options = null;
    public double defaultPageHeight = 0;
}
}
